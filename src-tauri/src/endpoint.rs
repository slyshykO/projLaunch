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
        Ok(())
    }

    pub fn absolute_path(&self, path: &str) -> bool {
        !path.contains('\0')
            && match self {
                Self::Local => std::path::Path::new(path).is_absolute(),
                Self::Wsl { .. } => path.starts_with('/'),
            }
    }
}
