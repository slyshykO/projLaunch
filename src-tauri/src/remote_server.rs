//! Linux sidecar installation shared by WSL and SSH transports.
use anyhow::{Context, Result, ensure};
use git_protocol::{PROTOCOL_VERSION, ServerInfo};
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
pub(crate) const BOOTSTRAP: &str = include_str!("remote/bootstrap.sh");

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

pub(crate) fn find_binary(executable: &Path, development: &Path) -> Result<PathBuf> {
    let candidates = [
        executable.with_file_name("binaries").join(FILENAME),
        development.join("binaries").join(FILENAME),
    ];
    candidates.iter().find(|p| p.is_file()).cloned().with_context(|| format!(
        "Linux Git server is unavailable (checked {}). Build it with build.bat build-git-server ubuntu22 --release",
        candidates.iter().map(|p| p.display().to_string()).collect::<Vec<_>>().join(", ")
    ))
}

pub(crate) async fn run(
    mut command: Command,
    label: &str,
    binary: Option<&[u8]>,
) -> Result<std::process::Output> {
    command
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped());
    let mut child = command
        .spawn()
        .with_context(|| format!("Cannot start {label}"))?;
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
    .with_context(|| format!("Timed out preparing Git server on {label}"))?
}

pub(crate) async fn prepare(
    binary: &Path,
    base: &str,
    label: &str,
    command: impl Fn(&str, &str, &str) -> Command,
) -> Result<(String, ServerInfo)> {
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
    let mut output = run(command("probe", &digest, base), label, None).await?;
    if output.status.code() == Some(10) {
        tracing::info!(label, "Installing bundled Linux Git server");
        output = run(command("install", &digest, base), label, Some(&bytes)).await?;
    }
    ensure!(
        output.status.success(),
        "Cannot prepare Git server on {label}: {} {}",
        diagnostic(&output.stderr).trim(),
        diagnostic(&output.stdout).trim()
    );
    let text = String::from_utf8(output.stdout).context("Invalid remote bootstrap output")?;
    let (path, json) = text
        .trim_end()
        .split_once('\n')
        .context("Missing Linux Git server version. Rebuild the Linux server")?;
    ensure!(
        path.starts_with('/') && !path.contains('\r'),
        "Invalid Linux Git server path"
    );
    let info: ServerInfo =
        serde_json::from_str(json).context("Invalid Linux Git server version")?;
    ensure!(
        info.protocol_version == PROTOCOL_VERSION
            && info.target == TARGET
            && !info.server_version.is_empty(),
        "Incompatible Linux Git server: version {}, protocol {}, target {}",
        info.server_version,
        info.protocol_version,
        info.target
    );
    Ok((path.to_owned(), info))
}
