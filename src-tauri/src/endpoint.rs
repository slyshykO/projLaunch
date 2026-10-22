use anyhow::{Result, ensure};
use serde::{Deserialize, Serialize};

/// Execution environment; repository paths are interpreted inside this environment.
#[derive(Debug, Clone, Default, PartialEq, Eq, Hash, Serialize, Deserialize)]
#[serde(tag = "kind", rename_all = "camelCase", deny_unknown_fields)]
pub enum Endpoint {
    #[default]
    Local,
    Wsl {
        distribution: String,
    },
    Ssh {
        host: String,
        #[serde(default)]
        username: String,
    },
}

impl Endpoint {
    pub fn validate(&self) -> Result<()> {
        if let Self::Wsl { distribution } = self {
            ensure!(
                !distribution.trim().is_empty()
                    && distribution.len() <= 256
                    && !distribution.chars().any(char::is_control),
                "Invalid WSL distribution name"
            );
        }
        if let Self::Ssh { host, username } = self {
            ensure!(
                !host.is_empty()
                    && host.len() <= 256
                    && !host.starts_with('-')
                    && host
                        .bytes()
                        .all(|b| b.is_ascii_alphanumeric() || b"._-:[]%".contains(&b)),
                "Enter an SSH host/IP or config alias (without user@, spaces or command-line options); use the separate username field"
            );
            ensure!(
                username.len() <= 256
                    && !username.starts_with('-')
                    && username
                        .bytes()
                        .all(|b| b.is_ascii_alphanumeric() || b"._-@".contains(&b)),
                "Invalid SSH username; leave it empty to use SSH config"
            );
        }
        Ok(())
    }

    pub fn absolute_path(&self, path: &str) -> bool {
        !path.contains('\0')
            && match self {
                Self::Local => std::path::Path::new(path).is_absolute(),
                Self::Wsl { .. } | Self::Ssh { .. } => path.starts_with('/'),
            }
    }
}
