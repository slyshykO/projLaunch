//! Multiplexed NDJSON client. Owns and reaps only its own server process.
use anyhow::{Context, Result, bail, ensure};
use futures_util::{SinkExt, StreamExt};
use git_protocol::{Hello, MAX_FRAME, PROTOCOL_VERSION};
use serde::{Serialize, de::DeserializeOwned};
use serde_json::{Value, json};
use std::{
    collections::HashMap,
    path::Path,
    process::Stdio,
    sync::atomic::{AtomicU64, Ordering},
    time::Duration,
};
use tokio::{
    process::Command,
    sync::{mpsc, oneshot, watch},
};
use tokio_util::{
    codec::{FramedRead, FramedWrite, LinesCodec},
    sync::CancellationToken,
};

type Reply = oneshot::Sender<Result<Value, String>>;
struct Request {
    id: u64,
    line: String,
    reply: Reply,
}

pub struct Client {
    requests: mpsc::Sender<Request>,
    stop: CancellationToken,
    finished: watch::Receiver<bool>,
    next_id: AtomicU64,
}

impl Drop for Client {
    fn drop(&mut self) {
        self.stop.cancel();
    }
}

impl Client {
    pub async fn connect(executable: &Path) -> Result<Self> {
        let mut command = Command::new(executable);
        command.arg("stdio");
        Self::connect_command(command).await
    }

    /// Connect through a transport process such as wsl.exe, retaining ownership of its lifetime.
    pub async fn connect_command(mut command: Command) -> Result<Self> {
        command
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::inherit())
            .kill_on_drop(true);
        #[cfg(windows)]
        command.creation_flags(0x08000000);
        let mut child = command
            .spawn()
            .context("Cannot start Git server transport")?;
        let mut output = FramedWrite::new(
            child.stdin.take().unwrap(),
            LinesCodec::new_with_max_length(MAX_FRAME),
        );
        let mut input = FramedRead::new(
            child.stdout.take().unwrap(),
            LinesCodec::new_with_max_length(MAX_FRAME),
        );
        let (requests, mut incoming) = mpsc::channel::<Request>(32);
        let stop = CancellationToken::new();
        let stopped = stop.clone();
        let (done, finished) = watch::channel(false);
        tokio::spawn(async move {
            let mut pending: HashMap<u64, Reply> = HashMap::new();
            let result: Result<()> = async {
                loop {
                    tokio::select! {
                        biased;
                        _ = stopped.cancelled() => break,
                        status = child.wait() => bail!("Git server exited: {}", status?),
                        response = input.next() => {
                            let line = response.context("Git server closed stdout")??;
                            let value: Value = serde_json::from_str(&line)?;
                            ensure!(value["jsonrpc"] == "2.0", "Invalid Git server protocol");
                            let id = value["id"].as_u64().context("Invalid Git response ID")?;
                            let reply = pending.remove(&id).context("Unknown Git response ID")?;
                            let response = if let Some(error) = value.get("error") {
                                Err(format!("Git RPC {}: {}", error["code"], error["message"].as_str().unwrap_or("Unknown error")))
                            } else { value.get("result").cloned().ok_or_else(|| "Missing Git result".into()) };
                            let _ = reply.send(response);
                        },
                        request = incoming.recv() => match request {
                            Some(request) => {
                                pending.insert(request.id, request.reply);
                                tokio::time::timeout(Duration::from_secs(5), output.send(request.line)).await??;
                            },
                            None => break,
                        }
                    }
                }
                Ok(())
            }.await;
            stopped.cancel();
            let error = result
                .err()
                .map(|e| format!("{e:#}"))
                .unwrap_or_else(|| "Git server disconnected; reopen repositories".into());
            incoming.close();
            for (_, reply) in pending {
                let _ = reply.send(Err(error.clone()));
            }
            while let Some(request) = incoming.recv().await {
                let _ = request.reply.send(Err(error.clone()));
            }
            // EOF asks the server to cancel operations and reap Git children before exiting.
            drop(output);
            if tokio::time::timeout(Duration::from_secs(35), child.wait())
                .await
                .is_err()
            {
                let _ = child.start_kill();
                let _ = child.wait().await;
            }
            let _ = done.send(true);
        });
        let client = Self {
            requests,
            stop,
            finished,
            next_id: AtomicU64::new(1),
        };
        let hello: Hello = client.call("server.hello", &json!({})).await?;
        ensure!(
            hello.protocol_version == PROTOCOL_VERSION,
            "Incompatible Git server protocol"
        );
        Ok(client)
    }

    pub fn is_closed(&self) -> bool {
        self.stop.is_cancelled()
    }
    pub fn stop(&self) {
        self.stop.cancel();
    }

    pub async fn shutdown(&self) {
        self.stop.cancel();
        let mut finished = self.finished.clone();
        while !*finished.borrow_and_update() {
            if finished.changed().await.is_err() {
                break;
            }
        }
    }

    pub async fn call<P: Serialize + ?Sized, R: DeserializeOwned>(
        &self,
        method: &str,
        params: &P,
    ) -> Result<R> {
        ensure!(
            !self.is_closed(),
            "Git server disconnected; reopen repositories"
        );
        let id = self.next_id.fetch_add(1, Ordering::Relaxed);
        let line = serde_json::to_string(
            &json!({"jsonrpc":"2.0","id":id,"method":method,"params":params}),
        )?;
        ensure!(line.len() <= MAX_FRAME, "Git request exceeds frame limit");
        let (reply, result) = oneshot::channel();
        let operation = async {
            self.requests
                .send(Request { id, line, reply })
                .await
                .context("Git server disconnected")?;
            let value = result
                .await
                .context("Git server disconnected")?
                .map_err(anyhow::Error::msg)?;
            Ok(serde_json::from_value(value)?)
        };
        match tokio::time::timeout(Duration::from_secs(90), operation).await {
            Ok(result) => result,
            Err(_) => {
                self.stop.cancel();
                bail!("Git request timed out; connection closed, reopen repositories");
            }
        }
    }
}
