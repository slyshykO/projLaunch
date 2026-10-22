pub use git_discovery::discover;
use serde::Serialize;

#[derive(Debug, Default, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ProjectGitStatus {
    pub repositories: Vec<RepositoryStatus>,
    pub warnings: Vec<String>,
    pub unsupported: bool,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct RepositoryStatus {
    pub endpoint: Option<crate::endpoint::Endpoint>,
    pub root: String,
    pub name: String,
    pub branch: Option<String>,
    // None is unknown; a failed check must never be displayed as clean.
    pub dirty: Option<bool>,
    pub error: Option<String>,
}
