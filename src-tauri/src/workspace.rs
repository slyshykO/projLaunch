use std::{collections::HashSet, path::Path};

use anyhow::{Result, ensure};
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct Workspace {
    pub version: u32,
    pub tabs: Vec<RepositoryTab>,
    pub active_root: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct RepositoryTab {
    pub root: String,
    pub name: String,
    pub files_width: f64,
    pub files_height: f64,
    pub selection: Option<Selection>,
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
        ensure!(
            self.version == 1,
            "Unsupported workspace state version {}",
            self.version
        );
        ensure!(self.tabs.len() <= 128, "Too many saved repository tabs");
        let mut roots = HashSet::new();
        for tab in &self.tabs {
            ensure!(
                Path::new(&tab.root).is_absolute(),
                "Saved repository path must be absolute"
            );
            ensure!(
                roots.insert(tab.root.as_str()),
                "Duplicate saved repository tab"
            );
            ensure!(
                (20.0..=70.0).contains(&tab.files_width)
                    && (20.0..=70.0).contains(&tab.files_height),
                "Saved panel sizes must be between 20 and 70 percent"
            );
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
                .is_none_or(|root| roots.contains(root)),
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
