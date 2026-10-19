//! Discover only explicitly associated project locations, never scan child trees.
use anyhow::{Context, Result, bail, ensure};
use quick_xml::{Reader, XmlVersion, events::Event};
use serde::Serialize;
use std::{
    collections::BTreeSet,
    fs,
    io::Read,
    path::{Path, PathBuf},
};

const MAX_DEFINITION_BYTES: u64 = 2 * 1024 * 1024;
const MAX_LOCATIONS: usize = 512;
const MAX_REPOSITORIES: usize = 64;

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
    pub root: String,
    pub name: String,
    pub branch: Option<String>,
    // None is unknown; a failed check must never be displayed as clean.
    pub dirty: Option<bool>,
    pub error: Option<String>,
}

#[derive(Debug, Default)]
pub struct Discovery {
    pub roots: BTreeSet<PathBuf>,
    pub warnings: Vec<String>,
}

impl Discovery {
    fn warn(&mut self, error: impl std::fmt::Display) {
        if self.warnings.len() < 8 {
            self.warnings.push(error.to_string());
        }
    }

    fn add_location(&mut self, path: &Path) {
        match find_root(path) {
            Ok(Some(root)) if self.roots.len() < MAX_REPOSITORIES => {
                self.roots.insert(root);
            }
            Ok(Some(root)) if self.roots.contains(&root) => {}
            Ok(Some(_)) => {
                self.warn("Repository limit reached (64); some repositories were not checked.")
            }
            Ok(None) => {}
            Err(error) => self.warn(format!("{}: {error:#}", path.display())),
        }
    }
}

fn find_root(path: &Path) -> Result<Option<PathBuf>> {
    let path = path
        .canonicalize()
        .context("Project location is unavailable")?;
    let directory = if path.is_dir() {
        path.as_path()
    } else {
        path.parent().context("Missing parent directory")?
    };
    for ancestor in directory.ancestors() {
        match fs::metadata(ancestor.join(".git")) {
            Ok(metadata) if metadata.is_dir() || metadata.is_file() => {
                return Ok(Some(ancestor.to_owned()));
            }
            Ok(_) => bail!("Invalid .git entry"),
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => {}
            Err(error) => return Err(error).context("Cannot inspect .git"),
        }
    }
    Ok(None)
}

fn read_definition(path: &Path) -> Result<String> {
    let mut bytes = Vec::new();
    fs::File::open(path)?
        .take(MAX_DEFINITION_BYTES + 1)
        .read_to_end(&mut bytes)?;
    ensure!(
        bytes.len() as u64 <= MAX_DEFINITION_BYTES,
        "Project definition exceeds 2 MiB"
    );
    if bytes.starts_with(&[0xff, 0xfe]) || bytes.starts_with(&[0xfe, 0xff]) {
        ensure!(bytes.len() % 2 == 0, "Invalid UTF-16 project definition");
        let little_endian = bytes[0] == 0xff;
        let units: Vec<_> = bytes[2..]
            .as_chunks::<2>()
            .0
            .iter()
            .map(|chunk| {
                if little_endian {
                    u16::from_le_bytes([chunk[0], chunk[1]])
                } else {
                    u16::from_be_bytes([chunk[0], chunk[1]])
                }
            })
            .collect();
        return String::from_utf16(&units).context("Invalid UTF-16 project definition");
    }
    Ok(String::from_utf8(bytes)?
        .trim_start_matches('\u{feff}')
        .to_owned())
}

fn relative_location(base: &Path, location: &str) -> Result<PathBuf> {
    ensure!(!location.trim().is_empty(), "Empty project location");
    // Solution files use Windows separators even when read on another platform.
    let location = if cfg!(windows) {
        location.to_owned()
    } else {
        location.replace('\\', "/")
    };
    Ok(base.join(location))
}

fn locations(path: &Path, text: &str) -> Result<Vec<PathBuf>> {
    let base = path.parent().context("Missing project directory")?;
    let extension = path
        .extension()
        .and_then(|ext| ext.to_str())
        .unwrap_or("")
        .to_ascii_lowercase();
    let mut paths = Vec::new();
    match extension.as_str() {
        "code-workspace" => {
            let options = jsonc_parser::ParseOptions {
                allow_comments: true,
                allow_trailing_commas: true,
                allow_loose_object_property_names: false,
                allow_missing_commas: false,
                allow_single_quoted_strings: false,
                allow_hexadecimal_numbers: false,
                allow_unary_plus_numbers: false,
                allow_bare_decimal_point_numbers: false,
                allow_non_finite_numbers: false,
                allow_extended_string_escapes: false,
            };
            let value: serde_json::Value = jsonc_parser::parse_to_serde_value(text, &options)?;
            let folders = value
                .get("folders")
                .and_then(|value| value.as_array())
                .context("Workspace must contain a folders array")?;
            ensure!(
                folders.len() <= MAX_LOCATIONS,
                "Too many workspace folders (maximum 512)"
            );
            for folder in folders {
                if let Some(location) = folder.get("path").and_then(|value| value.as_str()) {
                    paths.push(relative_location(base, location)?);
                } else if let Some(uri) = folder.get("uri").and_then(|value| value.as_str()) {
                    paths.push(tauri::Url::parse(uri)?.to_file_path().map_err(|_| {
                        anyhow::anyhow!("Only local file:// workspace folders are supported")
                    })?);
                } else {
                    bail!("Workspace folder must contain a path or local file URI");
                }
            }
        }
        "sln" => {
            ensure!(
                text.contains("Microsoft Visual Studio Solution File"),
                "Invalid solution header"
            );
            for line in text
                .lines()
                .map(str::trim)
                .filter(|line| line.starts_with("Project("))
            {
                let quoted: Vec<_> = line.split('"').collect();
                ensure!(quoted.len() >= 9, "Invalid solution project entry");
                // Solution folders are logical groups, not filesystem locations.
                if quoted[1].eq_ignore_ascii_case("{66A26720-8FB5-11D2-AA7E-00C04F688DDE}")
                    || quoted[1].eq_ignore_ascii_case("{2150E333-8FDC-42A3-9474-1A3956D46DE8}")
                {
                    continue;
                }
                paths.push(relative_location(base, quoted[5])?);
                ensure!(
                    paths.len() <= MAX_LOCATIONS,
                    "Too many solution projects (maximum 512)"
                );
            }
        }
        "slnx" => {
            let mut reader = Reader::from_str(text);
            let mut has_solution = false;
            loop {
                match reader.read_event()? {
                    Event::Start(element) | Event::Empty(element) => {
                        if element.name().as_ref() == "Solution" {
                            has_solution = true;
                        }
                        if element.name().as_ref() == "Project" {
                            for attribute in element.attributes() {
                                let attribute = attribute?;
                                if attribute.key.as_ref() == "Path" {
                                    let value =
                                        attribute.normalized_value(XmlVersion::Implicit1_0)?;
                                    paths.push(relative_location(base, &value)?);
                                    ensure!(
                                        paths.len() <= MAX_LOCATIONS,
                                        "Too many solution projects (maximum 512)"
                                    );
                                }
                            }
                        }
                    }
                    Event::DocType(_) => bail!("XML document types are not supported"),
                    Event::Eof => break,
                    _ => {}
                }
            }
            ensure!(has_solution, "Invalid XML solution");
        }
        _ => {}
    }
    Ok(paths)
}

pub fn discover(project_path: &str) -> Result<Discovery> {
    let path = Path::new(project_path);
    ensure!(
        path.is_absolute(),
        "Set an absolute local project path to check Git"
    );
    let metadata = fs::metadata(path).context("Project path is unavailable")?;
    let mut result = Discovery::default();
    result.add_location(path);
    if metadata.is_file()
        && matches!(
            path.extension()
                .and_then(|ext| ext.to_str())
                .unwrap_or("")
                .to_ascii_lowercase()
                .as_str(),
            "code-workspace" | "sln" | "slnx"
        )
    {
        match read_definition(path).and_then(|text| locations(path, &text)) {
            Ok(locations) => {
                for location in locations {
                    result.add_location(&location);
                }
            }
            Err(error) => result.warn(format!("{}: {error:#}", path.display())),
        }
    }
    Ok(result)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn repo(base: &Path, name: &str) -> PathBuf {
        let root = base.join(name);
        fs::create_dir_all(root.join(".git")).unwrap();
        root.canonicalize().unwrap()
    }

    #[test]
    fn directory_and_project_file_use_containing_repo_without_scanning_children() {
        let temp = tempfile::tempdir().unwrap();
        let root = repo(temp.path(), "main");
        repo(&root, "nested");
        let project = root.join("src/app.fsproj");
        fs::create_dir_all(project.parent().unwrap()).unwrap();
        fs::write(&project, "<Project />").unwrap();
        for path in [&root, &project] {
            let found = discover(path.to_str().unwrap()).unwrap();
            assert_eq!(found.roots, BTreeSet::from([root.clone()]));
            assert!(found.warnings.is_empty());
        }
    }

    #[test]
    fn workspace_jsonc_deduplicates_roots_and_keeps_valid_paths_when_one_is_missing() {
        let temp = tempfile::tempdir().unwrap();
        let first = repo(temp.path(), "first repo");
        let second = repo(temp.path(), "другий");
        fs::create_dir_all(first.join("src")).unwrap();
        let workspace = temp.path().join("demo.code-workspace");
        fs::write(
            &workspace,
            r#"{
            // Repeated paths in one worktree should produce one badge.
            "folders": [
                {"path": "first repo"}, {"path": "first repo/src"},
                {"path": "другий"}, {"path": "missing"},
            ],
        }"#,
        )
        .unwrap();
        let found = discover(workspace.to_str().unwrap()).unwrap();
        assert_eq!(found.roots, BTreeSet::from([first, second]));
        assert_eq!(found.warnings.len(), 1);
    }

    #[test]
    fn local_uri_and_git_file_worktrees_are_supported() {
        let temp = tempfile::tempdir().unwrap();
        let root = temp.path().join("linked worktree");
        fs::create_dir(&root).unwrap();
        fs::write(root.join(".git"), "gitdir: ../main/.git/worktrees/linked").unwrap();
        let workspace = temp.path().join("demo.code-workspace");
        let uri = tauri::Url::from_directory_path(&root).unwrap().to_string();
        fs::write(
            &workspace,
            serde_json::json!({"folders": [{"uri": uri}]}).to_string(),
        )
        .unwrap();
        assert_eq!(
            discover(workspace.to_str().unwrap()).unwrap().roots,
            BTreeSet::from([root.canonicalize().unwrap()])
        );
    }

    #[test]
    fn solution_finds_external_projects_and_ignores_virtual_folders() {
        let temp = tempfile::tempdir().unwrap();
        let first = repo(temp.path(), "first");
        let second = repo(temp.path(), "second");
        fs::write(second.join("app.csproj"), "<Project />").unwrap();
        let solution = first.join("demo.sln");
        fs::write(&solution, concat!(
            "Microsoft Visual Studio Solution File, Format Version 12.00\n",
            "Project(\"{2150E333-8FDC-42A3-9474-1A3956D46DE8}\") = \"Folder\", \"Virtual\", \"{id}\"\nEndProject\n",
            "Project(\"{project-type}\") = \"App\", \"..\\second\\app.csproj\", \"{id}\"\nEndProject\n",
        )).unwrap();
        let found = discover(solution.to_str().unwrap()).unwrap();
        assert_eq!(found.roots, BTreeSet::from([first, second]));
        assert!(found.warnings.is_empty());
    }

    #[test]
    fn xml_solution_decodes_paths_and_utf16_solutions_work() {
        let temp = tempfile::tempdir().unwrap();
        let root = repo(temp.path(), "A&B");
        fs::write(root.join("app.fsproj"), "<Project />").unwrap();
        let solution = temp.path().join("demo.slnx");
        fs::write(&solution, "<Solution><Folder Name=\"/Apps/\"><Project Path=\"A&amp;B/app.fsproj\" /></Folder></Solution>").unwrap();
        assert_eq!(
            discover(solution.to_str().unwrap()).unwrap().roots,
            BTreeSet::from([root.clone()])
        );
        let solution = temp.path().join("demo.sln");
        let text = "Microsoft Visual Studio Solution File\nProject(\"{type}\") = \"App\", \"A&B/app.fsproj\", \"{id}\"\nEndProject\n";
        let bytes: Vec<_> = [0xff, 0xfe]
            .into_iter()
            .chain(text.encode_utf16().flat_map(u16::to_le_bytes))
            .collect();
        fs::write(&solution, bytes).unwrap();
        assert_eq!(
            discover(solution.to_str().unwrap()).unwrap().roots,
            BTreeSet::from([root])
        );
    }

    #[test]
    fn malformed_and_remote_definitions_warn_instead_of_claiming_no_repository() {
        let temp = tempfile::tempdir().unwrap();
        let path = temp.path().join("demo.code-workspace");
        for contents in [
            "{bad",
            r#"{"folders": [{"uri": "vscode-remote://ssh-remote+host/work"}]}"#,
        ] {
            fs::write(&path, contents).unwrap();
            let found = discover(path.to_str().unwrap()).unwrap();
            assert!(!found.warnings.is_empty());
        }
        assert!(discover("relative/path").is_err());
        assert!(discover(temp.path().join("missing").to_str().unwrap()).is_err());
        let found = discover(temp.path().to_str().unwrap()).unwrap();
        assert!(found.roots.is_empty() && found.warnings.is_empty());
    }

    #[test]
    fn oversized_definitions_are_bounded() {
        let temp = tempfile::tempdir().unwrap();
        let path = temp.path().join("huge.code-workspace");
        fs::File::create(&path)
            .unwrap()
            .set_len(MAX_DEFINITION_BYTES + 1)
            .unwrap();
        assert!(discover(path.to_str().unwrap()).unwrap().warnings[0].contains("2 MiB"));
    }
}
