use crate::{endpoint::Endpoint, remote_server};
use anyhow::{Context, Result, ensure};
use git_client::Client;
use git_protocol::{Hello, ServerInfo};
use std::path::Path;
use tokio::process::Command;

/// Authentication, port, host aliases and jump hosts belong to OpenSSH configuration.
struct Ssh<'a> {
    host: &'a str,
    username: &'a str,
    config: Option<&'a Path>,
}

// OpenSSH joins the remote command and runs it in a shell. Local argv quoting alone
// is insufficient, including for paths returned by the remote installer.
fn quote(value: &str) -> String {
    format!("'{}'", value.replace('\'', "'\"'\"'"))
}

impl Ssh<'_> {
    fn command(&self, script: &str) -> Command {
        let mut command = Command::new("ssh");
        command.arg("-T");
        for option in [
            "BatchMode=yes",
            "StrictHostKeyChecking=yes",
            "ConnectTimeout=10",
            "ConnectionAttempts=1",
            "ServerAliveInterval=15",
            "ServerAliveCountMax=2",
            "ClearAllForwardings=yes",
            "ForwardAgent=no",
            "ForwardX11=no",
            "ControlMaster=no",
            "ControlPath=none",
            "RemoteCommand=none",
            "RequestTTY=no",
            "StdinNull=no",
            "ForkAfterAuthentication=no",
            "SessionType=default",
            "PermitLocalCommand=no",
        ] {
            command.args(["-o", option]);
        }
        if let Some(config) = self.config {
            command.arg("-F").arg(config);
        }
        if !self.username.is_empty() {
            command.args(["-l", self.username]);
        }
        command
            .arg("--")
            .arg(self.host)
            .arg(script)
            .kill_on_drop(true);
        #[cfg(windows)]
        command.creation_flags(0x08000000);
        command
    }

    async fn prepare(&self, binary: &Path, base: &str) -> Result<(String, ServerInfo)> {
        remote_server::prepare(binary, base, &format!("SSH '{}'", self.host), |mode, digest, base| {
            self.command(&format!("sh -c {} projlaunch-bootstrap {} {} {}",
                quote(remote_server::BOOTSTRAP), quote(mode), quote(digest), quote(base)))
        }).await.with_context(|| format!(
            "Cannot prepare SSH Git server on '{}'. Check the host/config alias, key or ssh-agent authentication and trusted host key (known_hosts)", self.host))
    }

    async fn connect(&self, binary: &Path, base: &str) -> Result<Client> {
        Endpoint::Ssh {
            host: self.host.into(),
            username: self.username.into(),
        }
        .validate()?;
        let (path, info) = self.prepare(binary, base).await?;
        let client = Client::connect_command(self.command(&format!("exec {} stdio", quote(&path))))
            .await.with_context(|| format!("Cannot connect to SSH Git server on '{}'; Git must be installed in its non-interactive PATH", self.host))?;
        let hello: Hello = client.call("server.hello", &serde_json::json!({})).await?;
        ensure!(
            hello.server_version == info.server_version,
            "SSH Git server version changed during startup"
        );
        ensure!(
            hello
                .capabilities
                .iter()
                .any(|value| value == "project.discover"),
            "SSH Git server does not support project discovery; rebuild the Linux server"
        );
        tracing::info!(host = self.host, version = %info.server_version, "SSH Git server connected");
        Ok(client)
    }
}

pub async fn connect(host: &str, username: &str) -> Result<Client> {
    let binary = remote_server::find_binary(
        &std::env::current_exe()?,
        Path::new(env!("CARGO_MANIFEST_DIR")),
    )?;
    Ssh {
        host,
        username,
        config: None,
    }
    .connect(&binary, "")
    .await
}

#[cfg(test)]
mod tests {
    use super::*;
    use git_protocol::*;
    use std::path::PathBuf;

    #[test]
    fn address_and_username_are_separate_and_alias_uses_config_user() {
        for (host, username) in [
            ("192.0.2.1", "alice"),
            ("work-server", ""),
            ("2001:db8::1", "bob"),
        ] {
            Endpoint::Ssh {
                host: host.into(),
                username: username.into(),
            }
            .validate()
            .unwrap();
            let ssh = Ssh {
                host,
                username,
                config: None,
            };
            let command = ssh.command("printf test");
            let args: Vec<_> = command
                .as_std()
                .get_args()
                .map(|v| v.to_string_lossy().into_owned())
                .collect();
            assert_eq!(&args[args.len() - 3..], &["--", host, "printf test"]);
            assert_eq!(args.iter().any(|value| value == "-l"), !username.is_empty());
            if !username.is_empty() {
                assert!(args.windows(2).any(|values| values == ["-l", username]));
            }
            assert!(args.iter().any(|value| value == "BatchMode=yes"));
            assert!(
                args.iter()
                    .any(|value| value == "StrictHostKeyChecking=yes")
            );
        }
        for host in [
            "",
            "-oProxyCommand=bad",
            "host;touch bad",
            "user@host",
            "host\nother",
        ] {
            assert!(
                Endpoint::Ssh {
                    host: host.into(),
                    username: String::new()
                }
                .validate()
                .is_err()
            );
        }
        assert!(
            Endpoint::Ssh {
                host: "host".into(),
                username: "$(bad)".into()
            }
            .validate()
            .is_err()
        );
        assert_eq!(quote("a'b $(touch file)"), "'a'\"'\"'b $(touch file)'");
    }

    async fn shell(ssh: &Ssh<'_>, script: &str) -> Result<String> {
        let output = remote_server::run(ssh.command(script), "SSH fixture", None).await?;
        ensure!(
            output.status.success(),
            "SSH fixture: {}",
            String::from_utf8_lossy(&output.stderr)
        );
        Ok(String::from_utf8(output.stdout)?.trim_end().to_owned())
    }

    #[tokio::test]
    #[ignore = "Set PROJLAUNCH_TEST_SSH_CONFIG and PROJLAUNCH_TEST_SSH_HOST for a disposable test host"]
    async fn ssh_install_reuse_repair_rpc_and_shutdown() -> Result<()> {
        let config = PathBuf::from(std::env::var("PROJLAUNCH_TEST_SSH_CONFIG")?);
        let host = std::env::var("PROJLAUNCH_TEST_SSH_HOST")?;
        let username = std::env::var("PROJLAUNCH_TEST_SSH_USER").unwrap_or_default();
        let ssh = Ssh {
            host: &host,
            username: &username,
            config: Some(&config),
        };
        let base = shell(&ssh, "mktemp -d /tmp/projlaunch-ssh-test-XXXXXXXX").await?;
        ensure!(
            base.starts_with("/tmp/projlaunch-ssh-test-") && !base.contains('\n'),
            "Unexpected fixture directory"
        );
        let result = exercise(&ssh, &base).await;
        let cleanup = shell(&ssh, &format!("rm -rf -- {}", quote(&base))).await;
        result?;
        cleanup?;
        Ok(())
    }

    async fn exercise(ssh: &Ssh<'_>, base: &str) -> Result<()> {
        let binary = remote_server::find_binary(
            &std::env::current_exe()?,
            Path::new(env!("CARGO_MANIFEST_DIR")),
        )?;
        let cache = format!("{base}/cache ' Привіт $(false)");
        let fixture = tempfile::tempdir()?;
        let untrusted_config = fixture.path().join("untrusted-config");
        let empty_hosts = fixture.path().join("empty-known-hosts");
        std::fs::write(&empty_hosts, "")?;
        std::fs::write(
            &untrusted_config,
            format!(
                "UserKnownHostsFile \"{}\"\nGlobalKnownHostsFile none\nStrictHostKeyChecking no\n{}",
                empty_hosts.display().to_string().replace('\\', "/"),
                std::fs::read_to_string(ssh.config.unwrap())?,
            ),
        )?;
        let untrusted = Ssh {
            config: Some(&untrusted_config),
            ..*ssh
        };
        let error = untrusted.prepare(&binary, &cache).await.unwrap_err();
        ensure!(
            format!("{error:#}").contains("Host key verification failed"),
            "Unknown host key was accepted or unexpected error: {error:#}"
        );
        let unauthorized = Ssh {
            username: "projlaunch-fixture-no-such-user",
            ..*ssh
        };
        let error = unauthorized.prepare(&binary, &cache).await.unwrap_err();
        ensure!(
            format!("{error:#}").contains("Permission denied"),
            "Authentication failure was not surfaced: {error:#}"
        );
        let (path, _) = ssh.prepare(&binary, &cache).await?;
        let inode = shell(ssh, &format!("stat -c %i -- {}", quote(&path))).await?;
        ensure!(
            ssh.prepare(&binary, &cache).await?.0 == path,
            "Unchanged install path changed"
        );
        ensure!(
            shell(ssh, &format!("stat -c %i -- {}", quote(&path))).await? == inode,
            "Unchanged binary copied again"
        );
        shell(ssh, &format!("printf broken > {}", quote(&path))).await?;
        ssh.prepare(&binary, &cache).await?;

        let old = fixture.path().join("old-server");
        std::fs::write(
            &old,
            format!(
                "#!/bin/sh\nprintf '%s\\n' '{{\"serverVersion\":\"old\",\"protocolVersion\":{PROTOCOL_VERSION},\"target\":\"x86_64-unknown-linux-gnu\"}}'\n"
            ),
        )?;
        let (old_path, old_info) = ssh.prepare(&old, &cache).await?;
        ensure!(
            old_info.server_version == "old" && old_path != path,
            "Versions were not isolated"
        );
        std::fs::write(
            &old,
            "#!/bin/sh\nprintf '%s\\n' '{\"serverVersion\":\"future\",\"protocolVersion\":999,\"target\":\"x86_64-unknown-linux-gnu\"}'\n",
        )?;
        ensure!(
            format!("{:#}", ssh.prepare(&old, &cache).await.unwrap_err()).contains("Incompatible"),
            "Incompatible server accepted"
        );

        let root = format!("{base}/repo ' Привіт $(false)");
        shell(ssh, &format!("set -eu; mkdir -p -- {root}; cd -- {root}; git init -q; printf 'before\\n' > file.txt; git add .; git -c user.name=Fixture -c user.email=fixture@example.test commit -qm Initial; printf 'after\\n' > file.txt", root = quote(&root))).await?;
        let client = ssh.connect(&binary, &cache).await?;
        let discovery: git_discovery::Discovery = client
            .call(
                "project.discover",
                &OpenParams {
                    path: root.clone(),
                    operation_id: "discover".into(),
                },
            )
            .await?;
        ensure!(
            discovery.roots.contains(&PathBuf::from(&root)),
            "Remote discovery failed"
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
        ensure!(status.entries.len() == 1, "Wrong SSH status");
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
            "Wrong SSH history"
        );
        let patch: Patch = client
            .call(
                "git.diff",
                &PatchParams {
                    repo_id: repo.repo_id,
                    operation_id: "diff".into(),
                    target: DiffTarget::Unstaged,
                    path: "file.txt".into(),
                },
            )
            .await?;
        ensure!(
            patch
                .text
                .as_deref()
                .is_some_and(|text| text.contains("+after")),
            "Wrong SSH diff"
        );
        client.shutdown().await;
        // Record the actual remote PID to verify EOF exits the server, not just the ssh transport.
        let pid_file = format!("{base}/server.pid");
        let client = Client::connect_command(ssh.command(&format!(
            "echo $$ > {}; exec {} stdio",
            quote(&pid_file),
            quote(&path)
        )))
        .await?;
        let _: Repository = client
            .call(
                "repo.open",
                &OpenParams {
                    path: root,
                    operation_id: "reopen".into(),
                },
            )
            .await?;
        client.shutdown().await;
        shell(
            ssh,
            &format!("! kill -0 \"$(cat -- {})\" 2>/dev/null", quote(&pid_file)),
        )
        .await?;
        Ok(())
    }
}
