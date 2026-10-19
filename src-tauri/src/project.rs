use std::collections::HashMap;

use anyhow::{Context, Result, ensure};
use serde::{Deserialize, Deserializer, Serialize};

#[derive(Debug, Clone, PartialEq, Eq, Deserialize, Serialize)]
pub(crate) enum Remote {
    Ssh { host: String, username: String },
    Wsl(String),
}

#[derive(Debug, Clone, PartialEq, Eq, Deserialize, Serialize)]
pub(crate) struct ProjectData {
    pub id: String,
    pub name: String,
    #[serde(rename = "lastOpened")]
    pub last_opened: String,
    pub description: String,
    pub path: String,
    pub ide: String,
    pub environment: HashMap<String, String>,
    #[serde(default, deserialize_with = "deserialize_remote")]
    pub remote: Option<Remote>,
}

impl ProjectData {
    pub fn from_json(content: &str) -> Result<Self> {
        // Some older Windows editors write UTF-8 JSON with a BOM.
        let project: Self = serde_json::from_str(content.trim_start_matches('\u{feff}'))
            .context("Invalid project JSON")?;
        ensure!(
            !project.id.trim().is_empty(),
            "Project ID must not be empty"
        );
        Ok(project)
    }
}

fn deserialize_remote<'de, D>(deserializer: D) -> Result<Option<Remote>, D::Error>
where
    D: Deserializer<'de>,
{
    let value = Option::<serde_json::Value>::deserialize(deserializer)?;
    value
        .map(|value| {
            // Previous Thoth.Json encoders used arrays; current encoders use
            // serde's externally tagged objects. Both remain valid input.
            let normalized = match &value {
                serde_json::Value::Array(parts) => match parts.as_slice() {
                    [tag, host, username] if tag.as_str() == Some("Ssh") => {
                        serde_json::json!({"Ssh": {"host": host, "username": username}})
                    }
                    [tag, distro] if tag.as_str() == Some("Wsl") => {
                        serde_json::json!({"Wsl": distro})
                    }
                    _ => return Err(serde::de::Error::custom("Invalid legacy remote value")),
                },
                _ => value,
            };
            serde_json::from_value(normalized).map_err(serde::de::Error::custom)
        })
        .transpose()
}
