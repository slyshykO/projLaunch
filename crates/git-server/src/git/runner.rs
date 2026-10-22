use anyhow::{Context, Result, bail};
use std::{path::Path, process::Stdio, sync::Arc, time::Duration};
use tokio::{
    io::{AsyncReadExt, AsyncWriteExt},
    process::Command,
    sync::Semaphore,
};
use tokio_util::sync::CancellationToken;

pub const OUTPUT_LIMIT: usize = 8 * 1024 * 1024;

#[derive(Debug)]
pub struct Output {
    pub bytes: Vec<u8>,
    pub truncated: bool,
    pub success: bool,
}

#[derive(Clone)]
pub struct GitRunner {
    slots: Arc<Semaphore>,
    pub timeout: Duration,
}

impl Default for GitRunner {
    fn default() -> Self {
        Self {
            slots: Arc::new(Semaphore::new(8)),
            timeout: Duration::from_secs(30),
        }
    }
}

impl GitRunner {
    pub async fn run(
        &self,
        root: &Path,
        args: &[String],
        cancel: &CancellationToken,
    ) -> Result<Vec<u8>> {
        Ok(self
            .output(root, args, cancel, OUTPUT_LIMIT, false, false)
            .await?
            .bytes)
    }

    pub async fn output(
        &self,
        root: &Path,
        args: &[String],
        cancel: &CancellationToken,
        limit: usize,
        truncate: bool,
        allow_failure: bool,
    ) -> Result<Output> {
        let _slot = tokio::select! {
            biased;
            _ = cancel.cancelled() => bail!("Operation cancelled"),
            permit = self.slots.acquire() => permit?,
        };
        self.execute(
            Self::command(root, args),
            None,
            cancel,
            limit,
            truncate,
            allow_failure,
        )
        .await
    }

    pub async fn run_with_input(
        &self,
        root: &Path,
        args: &[String],
        input: &[u8],
        cancel: &CancellationToken,
    ) -> Result<Vec<u8>> {
        let _slot = tokio::select! {
            biased;
            _ = cancel.cancelled() => bail!("Operation cancelled"),
            permit = self.slots.acquire() => permit?,
        };
        let mut command = Self::command(root, args);
        command.stdin(Stdio::piped());
        Ok(self
            .execute(command, Some(input), cancel, OUTPUT_LIMIT, false, false)
            .await?
            .bytes)
    }

    fn command(root: &Path, args: &[String]) -> Command {
        let mut command = Command::new("git");
        command
            .current_dir(root)
            .args([
                "--no-pager",
                "--literal-pathspecs",
                "-c",
                "color.ui=false",
                "-c",
                "core.quotepath=false",
                "-c",
                "core.fsmonitor=false",
            ])
            .args(args)
            .env("LC_ALL", "C")
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .kill_on_drop(true);
        // An IDE or parent Git process must not redirect operations to its own repository.
        for (key, _) in std::env::vars_os() {
            if key.to_string_lossy().starts_with("GIT_") {
                command.env_remove(key);
            }
        }
        command
            .env("GIT_TERMINAL_PROMPT", "0")
            .env("GIT_OPTIONAL_LOCKS", "0")
            .env("GIT_NO_REPLACE_OBJECTS", "1");
        #[cfg(windows)]
        command.creation_flags(0x08000000);
        command
    }

    async fn execute(
        &self,
        mut command: Command,
        input: Option<&[u8]>,
        cancel: &CancellationToken,
        limit: usize,
        truncate: bool,
        allow_failure: bool,
    ) -> Result<Output> {
        let directory = command
            .as_std()
            .get_current_dir()
            .unwrap_or(Path::new("."))
            .display()
            .to_string();
        let mut child = command.spawn().with_context(|| {
            format!(
                "Cannot start Git in '{directory}'; check Git on PATH and the working directory"
            )
        })?;
        let mut stdout = child.stdout.take().unwrap();
        let mut stderr = child.stderr.take().unwrap();
        let stdin = child.stdin.take();
        let mut input_done = input.is_none();
        let write_input = async move {
            if let Some(input) = input {
                let mut stdin = stdin.context("Git stdin is not piped")?;
                stdin
                    .write_all(input)
                    .await
                    .context("Cannot write Git stdin")?;
                // Dropping the pipe signals EOF so Git can finish reading revisions.
            }
            Ok::<_, anyhow::Error>(())
        };
        tokio::pin!(write_input);
        let mut bytes = Vec::new();
        let mut errors = Vec::new();
        let mut out = [0u8; 8192];
        let mut err = [0u8; 4096];
        let (mut out_done, mut err_done) = (false, false);
        let deadline = tokio::time::sleep(self.timeout);
        tokio::pin!(deadline);
        let result: Result<bool> = async {
            while !input_done || !out_done || !err_done {
                tokio::select! {
                    biased;
                    _ = cancel.cancelled() => bail!("Operation cancelled"),
                    _ = &mut deadline => bail!("Git command timed out"),
                    result = &mut write_input, if !input_done => {
                        result?;
                        input_done = true;
                    },
                    n = stdout.read(&mut out), if !out_done => {
                        let n = n?;
                        out_done = n == 0;
                        let room = limit.saturating_sub(bytes.len());
                        bytes.extend_from_slice(&out[..n.min(room)]);
                        if n > room {
                            if truncate { return Ok(true); }
                            bail!("Git output exceeds {limit} bytes; narrow the request");
                        }
                    },
                    n = stderr.read(&mut err), if !err_done => {
                        let n = n?;
                        err_done = n == 0;
                        let room = 65536usize.saturating_sub(errors.len());
                        errors.extend_from_slice(&err[..n.min(room)]);
                        if n > room { bail!("Git stderr exceeds 65536 bytes"); }
                    },
                }
            }
            Ok(false)
        }
        .await;
        let truncated = match result {
            Ok(truncated) => truncated,
            Err(error) => {
                let _ = child.start_kill();
                let _ = child.wait().await;
                return Err(error);
            }
        };
        if truncated {
            let _ = child.start_kill();
        }
        let status = tokio::select! {
            biased;
            _ = cancel.cancelled() => {
                let _ = child.start_kill(); let _ = child.wait().await;
                bail!("Operation cancelled");
            },
            _ = &mut deadline => {
                let _ = child.start_kill(); let _ = child.wait().await;
                bail!("Git command timed out");
            },
            status = child.wait() => status?,
        };
        if !status.success() && !truncated && !allow_failure {
            bail!(
                "Git failed ({}) in '{}': {}",
                status,
                directory,
                String::from_utf8_lossy(&errors).trim()
            );
        }
        Ok(Output {
            bytes,
            truncated,
            success: status.success(),
        })
    }
}

pub fn args(items: &[&str]) -> Vec<String> {
    items.iter().map(|s| (*s).to_owned()).collect()
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::{fs, io::Write};

    // A real child process exercises the same capture/kill/wait path as Git.
    // No global environment mutation or external sleep executable is needed.
    #[test]
    fn child_fixture() {
        let Ok(marker) = std::env::var("PROJLAUNCH_TEST_CHILD") else {
            return;
        };
        let mode = std::env::var("PROJLAUNCH_TEST_MODE").unwrap();
        let mut heartbeat = fs::File::create(marker).unwrap();
        for _ in 0..3000 {
            heartbeat.write_all(b".").unwrap();
            heartbeat.flush().unwrap();
            match mode.as_str() {
                "stdout" => {
                    std::io::stdout().write_all(&[b'x'; 8192]).unwrap();
                    std::io::stdout().flush().unwrap();
                }
                "stderr" => {
                    std::io::stderr().write_all(&[b'x'; 8192]).unwrap();
                }
                _ => {}
            }
            std::thread::sleep(Duration::from_millis(5));
        }
    }

    fn fixture(marker: &Path, mode: &str) -> Command {
        let mut command = Command::new(std::env::current_exe().unwrap());
        command
            .args([
                "--exact",
                "git::runner::tests::child_fixture",
                "--nocapture",
            ])
            .env("PROJLAUNCH_TEST_CHILD", marker)
            .env("PROJLAUNCH_TEST_MODE", mode)
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .kill_on_drop(true);
        #[cfg(windows)]
        command.creation_flags(0x08000000);
        command
    }

    async fn wait_started(marker: &Path) {
        tokio::time::timeout(Duration::from_secs(5), async {
            while !fs::metadata(marker).is_ok_and(|m| m.len() > 0) {
                tokio::time::sleep(Duration::from_millis(5)).await;
            }
        })
        .await
        .unwrap();
    }

    async fn assert_stopped(marker: &Path) {
        let length = fs::metadata(marker).unwrap().len();
        tokio::time::sleep(Duration::from_millis(50)).await;
        assert_eq!(
            fs::metadata(marker).unwrap().len(),
            length,
            "child is still running"
        );
    }

    #[tokio::test]
    async fn rpc_cancellation_is_session_scoped_and_reaps_child() {
        let dir = tempfile::TempDir::new().unwrap();
        let marker = dir.path().join("heartbeat");
        let service = Arc::new(crate::api::GitService::default());
        let session = crate::session::Session::new(service.clone(), None);
        let other = crate::session::Session::new(service, None);
        let operation = session.operation("slow").unwrap();
        let cancel = operation.cancel.clone();
        let mut command = fixture(&marker, "sleep");
        command.stdin(Stdio::piped());
        let task = tokio::spawn(async move {
            // A child that never reads stdin must still be cancellable.
            let input = vec![b'x'; 1024 * 1024];
            GitRunner::default()
                .execute(command, Some(&input), &cancel, 8192, false, false)
                .await
        });
        wait_started(&marker).await;
        let request =
            r#"{"jsonrpc":"2.0","id":1,"method":"git.cancel","params":{"operationId":"slow"}}"#;
        let foreign: serde_json::Value =
            serde_json::from_str(&crate::api::git_api(other).call(request).await.unwrap()).unwrap();
        assert_eq!(foreign["result"], false);
        assert!(!task.is_finished());
        let own: serde_json::Value =
            serde_json::from_str(&crate::api::git_api(session).call(request).await.unwrap())
                .unwrap();
        assert_eq!(own["result"], true);
        assert!(
            task.await
                .unwrap()
                .unwrap_err()
                .to_string()
                .contains("cancelled")
        );
        assert_stopped(&marker).await;
    }

    #[tokio::test]
    async fn timeout_and_output_limits_reap_children() {
        for mode in ["sleep", "stdout", "stderr", "stdin"] {
            let dir = tempfile::TempDir::new().unwrap();
            let marker = dir.path().join("heartbeat");
            let runner = GitRunner {
                timeout: Duration::from_millis(500),
                ..GitRunner::default()
            };
            let mut command = fixture(&marker, mode);
            let input = vec![b'x'; 1024 * 1024];
            let input = if mode == "stdin" {
                command.stdin(Stdio::piped());
                Some(input.as_slice())
            } else {
                None
            };
            let error = runner
                .execute(
                    command,
                    input,
                    &CancellationToken::new(),
                    4096,
                    false,
                    false,
                )
                .await
                .unwrap_err();
            assert!(
                error
                    .to_string()
                    .contains(if matches!(mode, "sleep" | "stdin") {
                        "timed out"
                    } else {
                        "exceeds"
                    }),
                "{error}"
            );
            assert_stopped(&marker).await;
        }
    }

    #[tokio::test]
    async fn disconnect_cancels_and_reaps_before_session_shutdown() {
        let dir = tempfile::TempDir::new().unwrap();
        let marker = dir.path().join("heartbeat");
        let session = crate::session::Session::new(Default::default(), None);
        let operation = session.operation("disconnect").unwrap();
        let command = fixture(&marker, "sleep");
        let task = tokio::spawn(async move {
            GitRunner::default()
                .execute(command, None, &operation.cancel, 8192, false, false)
                .await
        });
        wait_started(&marker).await;
        session.closed.cancel();
        assert!(task.await.unwrap().is_err());
        session.shutdown().await;
        assert_stopped(&marker).await;
    }
}
