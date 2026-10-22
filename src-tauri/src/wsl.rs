use anyhow::{Context, Result, ensure};
use git_client::Client;
use git_protocol::{Hello, PROTOCOL_VERSION, ServerInfo};
use sha2::{Digest, Sha256};
use std::{
    fmt::Write as _,
    path::{Path, PathBuf},
    process::Stdio,
    time::Duration,
};
use tokio::{io::AsyncWriteExt, process::Command};

const TARGET: &str = "x86_64-unknown-linux-gnu";
const FILENAME: &str = "project-launch-git-server-x86_64-unknown-linux-gnu";
const BOOTSTRAP: &str = include_str!("wsl/bootstrap.sh");

// wsl.exe's own errors can be UTF-16LE; Linux process output is UTF-8.
fn diagnostic(bytes: &[u8]) -> String {
    if bytes.starts_with(&[0xff, 0xfe])
        || bytes
            .as_chunks::<2>()
            .0
            .iter()
            .take(32)
            .any(|pair| pair[1] == 0)
    {
        let bytes = bytes.strip_prefix(&[0xff, 0xfe]).unwrap_or(bytes);
        String::from_utf16_lossy(
            &bytes
                .as_chunks::<2>()
                .0
                .iter()
                .map(|b| u16::from_le_bytes([b[0], b[1]]))
                .collect::<Vec<_>>(),
        )
    } else {
        String::from_utf8_lossy(bytes).into_owned()
    }
}

pub async fn connect(distribution: &str) -> Result<Client> {
    ensure!(cfg!(windows), "WSL endpoints are supported on Windows only");
    let executable = std::env::current_exe()?;
    let binary = find_binary(&executable, Path::new(env!("CARGO_MANIFEST_DIR")))?;
    let (path, info) = prepare(distribution, &binary, "").await?;
    let mut command = command(distribution);
    command.args([&path, "stdio"]);
    let client = Client::connect_command(command).await
        .with_context(|| format!("Cannot connect to Git server in WSL '{distribution}'. Check that Git is installed in this distribution"))?;
    let hello: Hello = client.call("server.hello", &serde_json::json!({})).await?;
    ensure!(
        hello.server_version == info.server_version,
        "WSL Git server version changed during startup"
    );
    ensure!(
        hello.capabilities.iter().any(|v| v == "project.discover"),
        "WSL Git server does not support project discovery; rebuild the Linux server"
    );
    tracing::info!(distribution, version = %info.server_version, "WSL Git server connected");
    Ok(client)
}

fn find_binary(executable: &Path, development: &Path) -> Result<PathBuf> {
    let candidates = [
        executable.with_file_name("binaries").join(FILENAME),
        development.join("binaries").join(FILENAME),
    ];
    candidates.iter().find(|p| p.is_file()).cloned().with_context(|| format!(
        "Linux Git server is unavailable (checked {}). Build it with build.bat build-git-server ubuntu22 --release",
        candidates.iter().map(|p| p.display().to_string()).collect::<Vec<_>>().join(", ")
    ))
}

fn command(distribution: &str) -> Command {
    let mut command = Command::new("wsl.exe");
    command.args(["--distribution", distribution, "--cd", "~", "--exec"]);
    command.kill_on_drop(true);
    #[cfg(windows)]
    command.creation_flags(0x08000000);
    command
}

async fn bootstrap(
    distribution: &str,
    digest: &str,
    base: &str,
    binary: Option<&[u8]>,
) -> Result<std::process::Output> {
    let mut command = command(distribution);
    command.args([
        "sh",
        "-c",
        BOOTSTRAP,
        "projlaunch-bootstrap",
        if binary.is_some() { "install" } else { "probe" },
        digest,
        base,
    ]);
    command
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped());
    let mut child = command
        .spawn()
        .with_context(|| format!("Cannot start WSL '{distribution}'"))?;
    let mut stdin = child.stdin.take().unwrap();
    tokio::time::timeout(Duration::from_secs(60), async move {
        // Drain output concurrently with upload to avoid deadlocking on startup errors.
        let upload = async move {
            if let Some(bytes) = binary {
                stdin.write_all(bytes).await?;
            }
            drop(stdin);
            Ok::<_, std::io::Error>(())
        };
        let (upload, output) = tokio::join!(upload, child.wait_with_output());
        let output = output?;
        if output.status.success() {
            upload?;
        }
        Ok(output)
    })
    .await
    .with_context(|| format!("Timed out preparing Git server in WSL '{distribution}'"))?
}

async fn prepare(distribution: &str, binary: &Path, base: &str) -> Result<(String, ServerInfo)> {
    let bytes = tokio::fs::read(binary)
        .await
        .with_context(|| format!("Cannot read '{}'", binary.display()))?;
    ensure!(
        bytes.len() <= 64 * 1024 * 1024,
        "Linux Git server exceeds 64 MiB"
    );
    let mut digest = String::with_capacity(64);
    for byte in Sha256::digest(&bytes) {
        write!(digest, "{byte:02x}").expect("Writing to a String cannot fail");
    }
    let mut output = bootstrap(distribution, &digest, base, None).await?;
    if output.status.code() == Some(10) {
        tracing::info!(distribution, "Installing bundled Git server in WSL");
        output = bootstrap(distribution, &digest, base, Some(&bytes)).await?;
    }
    ensure!(
        output.status.success(),
        "Cannot prepare Git server in WSL '{distribution}': {} {}",
        diagnostic(&output.stderr).trim(),
        diagnostic(&output.stdout).trim()
    );
    let text = String::from_utf8(output.stdout).context("Invalid WSL bootstrap output")?;
    let (path, json) = text
        .trim_end()
        .split_once('\n')
        .context("Missing WSL Git server version. Rebuild the Linux server")?;
    ensure!(
        path.starts_with('/') && !path.contains('\r'),
        "Invalid WSL Git server path"
    );
    let info: ServerInfo = serde_json::from_str(json).context("Invalid WSL Git server version")?;
    ensure!(
        info.protocol_version == PROTOCOL_VERSION
            && info.target == TARGET
            && !info.server_version.is_empty(),
        "Incompatible WSL Git server: version {}, protocol {}, target {}",
        info.server_version,
        info.protocol_version,
        info.target
    );
    Ok((path.to_owned(), info))
}

#[cfg(all(test, windows))]
mod tests {
    use super::*;
    use git_protocol::*;

    async fn shell(distribution: &str, script: &str, args: &[&str]) -> Result<String> {
        let output = command(distribution)
            .args(["sh", "-c", script, "fixture"])
            .args(args)
            .output()
            .await?;
        ensure!(
            output.status.success(),
            "WSL fixture: {}",
            String::from_utf8_lossy(&output.stderr)
        );
        Ok(String::from_utf8(output.stdout)?.trim_end().to_owned())
    }

    #[tokio::test]
    #[ignore = "Requires WSL and a built Linux sidecar; set PROJLAUNCH_TEST_WSL_DISTRO"]
    async fn wsl_install_reuse_repair_rpc_and_shutdown() -> Result<()> {
        let distro = std::env::var("PROJLAUNCH_TEST_WSL_DISTRO")
            .context("Set PROJLAUNCH_TEST_WSL_DISTRO")?;
        let base = shell(&distro, "mktemp -d /tmp/projlaunch-wsl-test-XXXXXXXX", &[]).await?;
        ensure!(
            base.starts_with("/tmp/projlaunch-wsl-test-") && !base.contains('\n'),
            "Unexpected fixture path"
        );
        let result = exercise(&distro, &base).await;
        // Only the exact mktemp-owned directory above is removed, even after a failed assertion.
        let cleanup = shell(&distro, "rm -rf -- \"$1\"", &[&base]).await;
        result?;
        cleanup?;
        Ok(())
    }

    async fn exercise(distro: &str, base: &str) -> Result<()> {
        let binary = find_binary(
            &std::env::current_exe()?,
            Path::new(env!("CARGO_MANIFEST_DIR")),
        )?;
        let cache = format!("{base}/cache with spaces ' Юнікод $literal");
        let (path, info) = prepare(distro, &binary, &cache).await?;
        ensure!(info.protocol_version == PROTOCOL_VERSION, "Wrong protocol");
        let inode = shell(distro, "stat -c %i -- \"$1\"", &[&path]).await?;
        let (reused, _) = prepare(distro, &binary, &cache).await?;
        ensure!(
            reused == path && inode == shell(distro, "stat -c %i -- \"$1\"", &[&path]).await?,
            "Unchanged server was copied again"
        );
        shell(distro, "printf broken > \"$1\"", &[&path]).await?;
        let (repaired, _) = prepare(distro, &binary, &cache).await?;
        ensure!(repaired == path, "Repair installed the wrong binary");

        // An older installed version is left intact, while the bundled version gets its own path.
        let fixture = tempfile::tempdir()?;
        let old = fixture.path().join("old-server");
        std::fs::write(
            &old,
            format!(
                "#!/bin/sh\nprintf '%s\\n' '{{\"serverVersion\":\"0.0.1\",\"protocolVersion\":{PROTOCOL_VERSION},\"target\":\"{TARGET}\"}}'\n"
            ),
        )?;
        let (old_path, old_info) = prepare(distro, &old, &cache).await?;
        ensure!(
            old_info.server_version == "0.0.1" && old_path != path,
            "Versions share an installation path"
        );
        ensure!(
            prepare(distro, &binary, &cache).await?.0 == path,
            "Bundled version was not selected"
        );
        std::fs::write(
            &old,
            format!(
                "#!/bin/sh\nprintf '%s\\n' '{{\"serverVersion\":\"99\",\"protocolVersion\":999,\"target\":\"{TARGET}\"}}'\n"
            ),
        )?;
        ensure!(
            prepare(distro, &old, &cache)
                .await
                .unwrap_err()
                .to_string()
                .contains("Incompatible"),
            "Incompatible protocol was accepted"
        );

        let root = format!("{base}/repo ' Привіт $literal");
        shell(
            distro,
            r#"
            set -eu
            mkdir -p -- "$1"
            cd -- "$1"
            git init -q
            printf 'before\n' > 'hello світ.txt'
            git add .
            git -c user.name=Fixture -c user.email=fixture@example.test commit -qm Initial
            printf 'after\n' > 'hello світ.txt'
        "#,
            &[&root],
        )
        .await?;
        let workspace = format!("{base}/fixture.code-workspace");
        let definition = serde_json::json!({ "folders": [{ "path": root }] }).to_string();
        shell(
            distro,
            "printf '%s' \"$2\" > \"$1\"",
            &[&workspace, &definition],
        )
        .await?;
        let pid_file = format!("{base}/server.pid");
        let mut cmd = command(distro);
        cmd.args([
            "sh",
            "-c",
            "echo $$ > \"$2\"; exec \"$1\" stdio",
            "fixture",
            &path,
            &pid_file,
        ]);
        let client = Client::connect_command(cmd).await?;
        let discovery: git_discovery::Discovery = client
            .call(
                "project.discover",
                &OpenParams {
                    path: workspace,
                    operation_id: "discover".into(),
                },
            )
            .await?;
        ensure!(
            discovery.roots.contains(&PathBuf::from(&root)) && discovery.warnings.is_empty(),
            "WSL workspace discovery failed"
        );
        let repo: Repository = client
            .call(
                "repo.open",
                &OpenParams {
                    path: root.clone(),
                    operation_id: "open".into(),
                },
            )
            .await?;
        let status: Status = client
            .call(
                "git.status",
                &RepoParams {
                    repo_id: repo.repo_id.clone(),
                    operation_id: "status".into(),
                },
            )
            .await?;
        ensure!(status.entries.len() == 1, "Wrong WSL status");
        let log: LogPage = client
            .call(
                "git.log",
                &LogParams {
                    repo_id: repo.repo_id.clone(),
                    operation_id: "log".into(),
                    cursor: None,
                    limit: Some(10),
                },
            )
            .await?;
        ensure!(
            log.commits.len() == 1 && log.commits[0].subject == "Initial",
            "Wrong WSL history"
        );
        let patch: Patch = client
            .call(
                "git.diff",
                &PatchParams {
                    repo_id: repo.repo_id,
                    operation_id: "diff".into(),
                    target: DiffTarget::Unstaged,
                    path: "hello світ.txt".into(),
                },
            )
            .await?;
        ensure!(
            patch
                .text
                .as_deref()
                .is_some_and(|text| text.contains("+after")),
            "Wrong WSL diff"
        );
        client.shutdown().await;
        shell(
            distro,
            "! kill -0 \"$(cat -- \"$1\")\" 2>/dev/null",
            &[&pid_file],
        )
        .await?;
        let mut cmd = command(distro);
        cmd.args([&path, "stdio"]);
        let reconnected = Client::connect_command(cmd).await?;
        let _: Repository = reconnected
            .call(
                "repo.open",
                &OpenParams {
                    path: root,
                    operation_id: "reopen".into(),
                },
            )
            .await?;
        reconnected.shutdown().await;
        Ok(())
    }
}
