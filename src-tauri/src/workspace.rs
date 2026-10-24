use crate::endpoint::Endpoint;
use std::collections::HashSet;

use anyhow::{Result, ensure};
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct Workspace {
    pub version: u32,
    pub tabs: Vec<RepositoryTab>,
    pub active_root: Option<String>,
    #[serde(default)]
    pub active_endpoint: Option<Endpoint>,
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn history_state_accepts_legacy_snapshots_and_validates_new_positions() {
        let mut value = json!({
            "version": 1, "tabs": [{ "root": "/repo", "endpoint": {"kind": "wsl", "distribution": "Ubuntu"},
                "name": "repo", "filesWidth": 40, "filesHeight": 40 }]
        });
        for version in [1, 2, 3] {
            value["version"] = json!(version);
            assert!(
                Workspace::from_json(&value.to_string()).unwrap().tabs[0]
                    .history
                    .is_none()
            );
        }
        let history = json!({"count": 300, "scroll": {
            "top": 1245.5, "left": 80.0, "anchor": "a".repeat(40), "offset": -12.5
        }});
        value["tabs"][0]["history"] = history.clone();
        let restored = Workspace::from_json(&value.to_string()).unwrap();
        assert_eq!(
            serde_json::to_value(&restored.tabs[0].history).unwrap(),
            history
        );
        for (field, invalid) in [
            ("top", json!(-1)),
            ("left", json!(-1)),
            ("anchor", json!("not-an-oid")),
            ("offset", json!("invalid")),
        ] {
            let mut invalid_value = value.clone();
            invalid_value["tabs"][0]["history"]["scroll"][field] = invalid;
            assert!(Workspace::from_json(&invalid_value.to_string()).is_err());
        }
        for count in [json!(-1), json!(1.5), json!(u32::MAX)] {
            let mut invalid_value = value.clone();
            invalid_value["tabs"][0]["history"]["count"] = count;
            assert!(Workspace::from_json(&invalid_value.to_string()).is_err());
        }
        let mut invalid = restored.clone();
        invalid.tabs[0].history.as_mut().unwrap().scroll.top = f64::NAN;
        assert!(invalid.validate().is_err());
        invalid.tabs[0].history.as_mut().unwrap().scroll.top = 0.0;
        invalid.tabs[0].history.as_mut().unwrap().scroll.offset = f64::INFINITY;
        assert!(invalid.validate().is_err());
    }

    #[test]
    fn ssh_tabs_keep_host_user_and_config_alias_on_roundtrip() {
        let endpoint =
            |username: &str| json!({"kind": "ssh", "host": "work-alias", "username": username});
        let tab = |username: &str| json!({"endpoint": endpoint(username), "root": "/repo", "name": "repo", "filesWidth": 40, "filesHeight": 40});
        let value = json!({"version": 2, "activeRoot": "/repo", "activeEndpoint": endpoint("bob"), "tabs": [tab(""), tab("alice"), tab("bob")]});
        let saved = Workspace::from_json(&value.to_string()).unwrap();
        let restored = Workspace::from_json(&serde_json::to_string(&saved).unwrap()).unwrap();
        assert_eq!(restored.tabs.len(), 3);
        assert_eq!(
            restored.active_endpoint,
            Some(Endpoint::Ssh {
                host: "work-alias".into(),
                username: "bob".into()
            })
        );
        let mut invalid = value;
        invalid["activeEndpoint"]["username"] = json!("missing");
        assert!(Workspace::from_json(&invalid.to_string()).is_err());
    }

    #[test]
    fn endpoints_distinguish_identical_paths_and_restore_active_distribution() {
        let tab = |distribution: &str| {
            json!({
                "root": "/home/user/project", "name": "project",
                "endpoint": { "kind": "wsl", "distribution": distribution },
                "filesWidth": 40, "filesHeight": 40
            })
        };
        let mut value = json!({
            "version": 2, "tabs": [tab("Ubuntu"), tab("Debian")],
            "activeRoot": "/home/user/project",
            "activeEndpoint": { "kind": "wsl", "distribution": "Debian" }
        });
        let workspace = Workspace::from_json(&value.to_string()).unwrap();
        assert!(
            serde_json::to_string(&workspace)
                .unwrap()
                .contains("Debian")
        );
        value["activeEndpoint"]["distribution"] = json!("Missing");
        assert!(Workspace::from_json(&value.to_string()).is_err());
        value["activeEndpoint"]["distribution"] = json!("Ubuntu");
        value["tabs"][1] = tab("Ubuntu");
        assert!(Workspace::from_json(&value.to_string()).is_err());
        value["tabs"] = json!([tab("")]);
        assert!(Workspace::from_json(&value.to_string()).is_err());
        value["tabs"] = json!([tab("Ubuntu")]);
        value["tabs"][0]["root"] = json!("relative/path");
        assert!(Workspace::from_json(&value.to_string()).is_err());
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct RepositoryTab {
    pub root: String,
    #[serde(default)]
    pub endpoint: Option<Endpoint>,
    pub name: String,
    pub files_width: f64,
    pub files_height: f64,
    pub selection: Option<Selection>,
    #[serde(default)]
    pub history: Option<History>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub(crate) struct History {
    pub count: u32,
    pub scroll: Scroll,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub(crate) struct Scroll {
    pub top: f64,
    pub left: f64,
    pub anchor: Option<String>,
    pub offset: f64,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct Selection {
    pub commit: Option<git_protocol::Commit>,
    pub parent: Option<usize>,
    pub file: Option<SelectedFile>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct SelectedFile {
    pub target: String,
    pub path: String,
}

impl Workspace {
    pub(crate) fn validate(&self) -> Result<()> {
        // Workspace JSON version, independent of the SQLite schema version.
        // Keep the upper bound in sync with Workspace.CurrentVersion in
        // src/src/domain/Workspace.fs; versions 1 and 2 remain readable.
        ensure!(
            matches!(self.version, 1..=3),
            "Unsupported workspace state version {}",
            self.version
        );
        ensure!(self.tabs.len() <= 128, "Too many saved repository tabs");
        self.active_endpoint
            .clone()
            .unwrap_or_default()
            .validate()?;
        let mut roots = HashSet::new();
        for tab in &self.tabs {
            let endpoint = tab.endpoint.clone().unwrap_or_default();
            endpoint.validate()?;
            ensure!(
                endpoint.absolute_path(&tab.root),
                "Saved repository path must be absolute"
            );
            ensure!(
                roots.insert((endpoint, tab.root.as_str())),
                "Duplicate saved repository tab"
            );
            ensure!(
                (20.0..=70.0).contains(&tab.files_width)
                    && (20.0..=70.0).contains(&tab.files_height),
                "Saved panel sizes must be between 20 and 70 percent"
            );
            if let Some(history) = &tab.history {
                let scroll = &history.scroll;
                ensure!(
                    history.count <= i32::MAX as u32,
                    "Invalid saved history depth"
                );
                ensure!(
                    scroll.top.is_finite()
                        && scroll.top >= 0.0
                        && scroll.left.is_finite()
                        && scroll.left >= 0.0
                        && scroll.offset.is_finite(),
                    "Invalid saved history scroll position"
                );
                ensure!(
                    scroll
                        .anchor
                        .as_ref()
                        .is_none_or(|oid| matches!(oid.len(), 40 | 64)
                            && oid.bytes().all(|c| c.is_ascii_hexdigit())),
                    "Invalid saved history anchor"
                );
            }
            if let Some(selection) = &tab.selection {
                if let Some(commit) = &selection.commit {
                    let valid_oid = |oid: &str| {
                        matches!(oid.len(), 40 | 64) && oid.bytes().all(|c| c.is_ascii_hexdigit())
                    };
                    ensure!(
                        valid_oid(&commit.oid) && commit.parents.iter().all(|oid| valid_oid(oid)),
                        "Invalid saved commit ID"
                    );
                    ensure!(
                        if commit.parents.is_empty() {
                            selection.parent.is_none()
                        } else {
                            selection
                                .parent
                                .is_some_and(|parent| (1..=commit.parents.len()).contains(&parent))
                        },
                        "Invalid saved commit parent"
                    );
                } else {
                    ensure!(
                        selection.parent.is_none(),
                        "Working tree has no selected parent"
                    );
                }
                if let Some(file) = &selection.file {
                    ensure!(!file.path.is_empty(), "Saved diff path cannot be empty");
                    ensure!(
                        if selection.commit.is_some() {
                            file.target == "commit"
                        } else {
                            matches!(file.target.as_str(), "staged" | "unstaged" | "untracked")
                        },
                        "Invalid saved diff target"
                    );
                }
            }
        }
        ensure!(
            self.active_root
                .as_deref()
                .is_none_or(|root| roots
                    .contains(&(self.active_endpoint.clone().unwrap_or_default(), root))),
            "Active repository is not in saved tabs"
        );
        Ok(())
    }

    pub(crate) fn from_json(content: &str) -> Result<Self> {
        ensure!(
            content.len() <= 1024 * 1024,
            "Workspace state exceeds 1 MiB"
        );
        let workspace: Self = serde_json::from_str(content)?;
        workspace.validate()?;
        Ok(workspace)
    }
}
