//! One bounded, concurrent session pump for both stdio and WebSocket.
use crate::{
    api::{GitService, git_api},
    session::{MAX_FRAME, MAX_IN_FLIGHT, Session},
};
use futures_util::{Sink, SinkExt, Stream, StreamExt};
use std::{sync::Arc, time::Duration};
use tokio::{
    sync::{Semaphore, mpsc},
    task::JoinSet,
};
use tokio_util::{
    codec::{FramedRead, FramedWrite, LinesCodec},
    sync::CancellationToken,
};

pub async fn serve<I, O, E, F>(
    mut input: I,
    mut output: O,
    service: Arc<GitService>,
    stop: CancellationToken,
) -> anyhow::Result<()>
where
    I: Stream<Item = Result<String, E>> + Unpin + Send + 'static,
    O: Sink<String, Error = F> + Unpin + Send + 'static,
    E: std::error::Error + Send + Sync + 'static,
    F: std::error::Error + Send + Sync + 'static,
{
    let (tx, mut rx) = mpsc::channel::<String>(16);
    let session = Session::new(service, Some(tx));
    let rpc = Arc::new(git_api(session.clone()));
    let mut writer = tokio::spawn(async move {
        while let Some(message) = rx.recv().await {
            tokio::time::timeout(Duration::from_secs(5), output.send(message)).await??;
        }
        anyhow::Ok(())
    });
    let slots = Arc::new(Semaphore::new(MAX_IN_FLIGHT));
    let controls = Arc::new(Semaphore::new(4));
    let mut calls = JoinSet::new();
    let mut writer_completed = false;
    let result = loop {
        tokio::select! {
            _ = stop.cancelled() => break Ok(()),
            result = &mut writer => { writer_completed = true; break result.map_err(anyhow::Error::from).and_then(|r|r); },
            Some(result) = calls.join_next(), if !calls.is_empty() => {
                match result { Ok(Ok(()))=>{}, Ok(Err(e))=>break Err(e), Err(e)=>break Err(e.into()) }
            },
            message = input.next() => match message {
                None => break Ok(()),
                Some(Err(e)) => break Err(e.into()),
                Some(Ok(message)) => {
                    if message.len()>MAX_FRAME {break Err(anyhow::anyhow!("frame too large"));}
                    let control = serde_json::from_str::<serde_json::Value>(&message).ok().is_some_and(|v| v["method"] == "git.cancel");
                    let permits = if control { &controls } else { &slots };
                    let permit = match permits.clone().try_acquire_owned() {
                        Ok(permit)=>permit, Err(_)=>break Err(anyhow::anyhow!("too many concurrent requests")),
                    };
                    let (session,rpc) = (session.clone(),rpc.clone());
                    calls.spawn(async move {let _permit=permit; session.dispatch(&rpc,&message).await});
                }
            }
        }
    };
    session.closed.cancel();
    // Let GitRunner kill and WAIT for children. Aborting tasks skips async cleanup.
    while calls.join_next().await.is_some() {}
    session.shutdown().await;
    if !writer_completed {
        writer.abort();
        let _ = writer.await;
    }
    result
}

pub async fn stdio<R, W>(reader: R, writer: W, service: Arc<GitService>) -> anyhow::Result<()>
where
    R: tokio::io::AsyncRead + Unpin + Send + 'static,
    W: tokio::io::AsyncWrite + Unpin + Send + 'static,
{
    serve(
        FramedRead::new(reader, LinesCodec::new_with_max_length(MAX_FRAME)),
        FramedWrite::new(writer, LinesCodec::new_with_max_length(MAX_FRAME)),
        service,
        CancellationToken::new(),
    )
    .await
}
