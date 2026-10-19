use git_client::Client;
use serde_json::{Value, json};
use std::{fs, path::Path, process::Command};
use tempfile::TempDir;

struct Fixture(TempDir);
impl Fixture {
    fn new() -> Self {
        let fixture = Self(TempDir::new().unwrap());
        fixture.git(&["init", "-b", "main"]);
        fixture.git(&["config", "user.name", "Тест Author"]);
        fixture.git(&["config", "user.email", "test@example.invalid"]);
        fixture.git(&["config", "core.autocrlf", "false"]);
        fixture.git(&["config", "commit.gpgsign", "false"]);
        fixture
    }
    fn path(&self) -> &Path {
        self.0.path()
    }
    fn git(&self, args: &[&str]) -> String {
        let output = Command::new("git")
            .current_dir(self.path())
            .args(args)
            .env("GIT_CONFIG_NOSYSTEM", "1")
            .env(
                "GIT_CONFIG_GLOBAL",
                if cfg!(windows) { "NUL" } else { "/dev/null" },
            )
            .output()
            .unwrap();
        assert!(
            output.status.success(),
            "git {args:?}: {}",
            String::from_utf8_lossy(&output.stderr)
        );
        String::from_utf8(output.stdout).unwrap().trim().into()
    }
    fn write(&self, name: &str, text: impl AsRef<[u8]>) {
        fs::write(self.path().join(name), text).unwrap();
    }
    fn commit(&self, message: &str) -> String {
        self.git(&["add", "-A"]);
        self.git(&["commit", "-m", message]);
        self.git(&["rev-parse", "HEAD"])
    }
    async fn connect(&self) -> (Client, String) {
        let client = Client::connect(Path::new(env!("CARGO_BIN_EXE_git-server")))
            .await
            .unwrap();
        let repo = request(&client, "repo.open", json!({"path":self.path()})).await;
        (client, repo["repoId"].as_str().unwrap().into())
    }
}

async fn request(client: &Client, method: &str, mut params: Value) -> Value {
    params["operationId"] = json!(uuid::Uuid::new_v4().to_string());
    client.call(method, &params).await.unwrap()
}

#[tokio::test]
async fn empty_repository_and_unborn_staged_diff() {
    let fixture = Fixture::new();
    let (client, repo) = fixture.connect().await;
    let hello: Value = client.call("server.hello", &json!({})).await.unwrap();
    assert_eq!(hello["protocolVersion"], 1);
    assert!(
        hello["gitVersion"]
            .as_str()
            .unwrap()
            .starts_with("git version")
    );
    let status = request(&client, "git.status", json!({"repoId":repo})).await;
    assert_eq!(status["branch"], "main");
    assert!(status["head"].is_null());
    let log = request(&client, "git.log", json!({"repoId":repo})).await;
    assert_eq!(log["commits"], json!([]));
    fixture.write("first.txt", "first\n");
    fixture.git(&["add", "."]);
    let patch = request(
        &client,
        "git.diff",
        json!({"repoId":repo,"target":{"kind":"staged"},"path":"first.txt"}),
    )
    .await;
    assert!(patch["text"].as_str().unwrap().contains("+first"));
    client.shutdown().await;
}

#[tokio::test]
async fn status_separates_index_worktree_renames_untracked_and_unicode() {
    let fixture = Fixture::new();
    fixture.write("tracked.txt", "base\n");
    fixture.write("old name.txt", "rename\n");
    fixture.commit("root");
    fixture.write("tracked.txt", "staged\n");
    fixture.git(&["add", "tracked.txt"]);
    fixture.write("tracked.txt", "worktree\n");
    fixture.git(&["mv", "old name.txt", "нове ім’я.txt"]);
    fixture.write("--literal [x].txt", "new\n");
    let (client, repo) = fixture.connect().await;
    let status = request(&client, "git.status", json!({"repoId":repo})).await;
    let entries = status["entries"].as_array().unwrap();
    let tracked = entries.iter().find(|e| e["path"] == "tracked.txt").unwrap();
    assert_eq!(tracked["index"], "M");
    assert_eq!(tracked["worktree"], "M");
    let renamed = entries
        .iter()
        .find(|e| e["path"] == "нове ім’я.txt")
        .unwrap();
    assert_eq!(renamed["originalPath"], "old name.txt");
    assert!(
        entries
            .iter()
            .any(|e| e["kind"] == "untracked" && e["path"] == "--literal [x].txt")
    );
    let staged = request(
        &client,
        "git.diff",
        json!({"repoId":repo,"target":{"kind":"staged"},"path":"tracked.txt"}),
    )
    .await;
    assert!(staged["text"].as_str().unwrap().contains("+staged"));
    assert!(!staged["text"].as_str().unwrap().contains("+worktree"));
    let unstaged = request(
        &client,
        "git.diff",
        json!({"repoId":repo,"target":{"kind":"unstaged"},"path":"tracked.txt"}),
    )
    .await;
    assert!(unstaged["text"].as_str().unwrap().contains("-staged"));
    assert!(unstaged["text"].as_str().unwrap().contains("+worktree"));
    let files = request(
        &client,
        "git.diffFiles",
        json!({"repoId":repo,"target":{"kind":"staged"}}),
    )
    .await;
    assert!(
        files
            .as_array()
            .unwrap()
            .iter()
            .any(|f| f["originalPath"] == "old name.txt")
    );
    let rename = request(
        &client,
        "git.diff",
        json!({"repoId":repo,"target":{"kind":"staged"},"path":"нове ім’я.txt"}),
    )
    .await;
    assert!(
        rename["text"]
            .as_str()
            .unwrap()
            .contains("rename from old name.txt")
    );
    client.shutdown().await;
}

#[tokio::test]
async fn history_snapshot_survives_new_commits_and_branch_movement() {
    let fixture = Fixture::new();
    fixture.write("one", "1");
    let root = fixture.commit("Корінь");
    fixture.git(&["tag", "-a", "v1", "-m", "version"]);
    fixture.write("one", "2");
    let second = fixture.commit("second");
    fixture.write("one", "3");
    let third = fixture.commit("third");
    let (client, repo) = fixture.connect().await;
    let first = request(&client, "git.log", json!({"repoId":repo,"limit":1})).await;
    assert_eq!(first["commits"][0]["oid"], third);
    assert_eq!(first["commits"][0]["parents"], json!([second]));
    fixture.write("one", "4");
    fixture.commit("not in snapshot");
    let page = request(
        &client,
        "git.log",
        json!({"repoId":repo,"limit":1,"cursor":first["nextCursor"]}),
    )
    .await;
    assert_eq!(page["commits"][0]["oid"], second);
    let last = request(
        &client,
        "git.log",
        json!({"repoId":repo,"limit":1,"cursor":page["nextCursor"]}),
    )
    .await;
    assert_eq!(last["commits"][0]["oid"], root);
    assert_eq!(last["commits"][0]["subject"], "Корінь");
    assert!(
        last["commits"][0]["refs"]
            .as_array()
            .unwrap()
            .contains(&json!("refs/tags/v1"))
    );
    assert!(last["nextCursor"].is_null());
    let patch = request(
        &client,
        "git.diff",
        json!({"repoId":repo,"target":{"kind":"commit","oid":root},"path":"one"}),
    )
    .await;
    assert!(patch["text"].as_str().unwrap().contains("+1"));
    client.shutdown().await;
}

#[tokio::test]
async fn merge_requires_parent_and_history_is_topological() {
    let fixture = Fixture::new();
    fixture.write("base", "base");
    let root = fixture.commit("base");
    fixture.git(&["checkout", "-b", "side"]);
    fixture.write("side", "side");
    let side = fixture.commit("side");
    fixture.git(&["checkout", "main"]);
    fixture.write("main", "main");
    let main = fixture.commit("main");
    fixture.git(&["merge", "--no-ff", "side", "-m", "merge"]);
    let merge = fixture.git(&["rev-parse", "HEAD"]);
    let (client, repo) = fixture.connect().await;
    let error = client
        .call::<_, Value>(
            "git.diffFiles",
            &json!({"repoId":repo,"operationId":"merge","target":{"kind":"commit","oid":merge}}),
        )
        .await
        .unwrap_err();
    assert!(error.to_string().contains("explicit"));
    let files = request(
        &client,
        "git.diffFiles",
        json!({"repoId":repo,"target":{"kind":"commit","oid":merge,"parent":1}}),
    )
    .await;
    assert_eq!(files[0]["path"], "side");
    let files = request(
        &client,
        "git.diffFiles",
        json!({"repoId":repo,"target":{"kind":"commit","oid":merge,"parent":2}}),
    )
    .await;
    assert_eq!(files[0]["path"], "main");
    let page = request(&client, "git.log", json!({"repoId":repo})).await;
    let commits = page["commits"].as_array().unwrap();
    assert_eq!(commits[0]["oid"], merge);
    assert_eq!(commits[0]["parents"], json!([main, side]));
    assert_eq!(commits.last().unwrap()["oid"], root);
    client.shutdown().await;
}

#[tokio::test]
async fn conflict_and_detached_head_are_explicit() {
    let fixture = Fixture::new();
    fixture.write("conflict", "base\n");
    fixture.commit("base");
    fixture.git(&["checkout", "-b", "side"]);
    fixture.write("conflict", "side\n");
    fixture.commit("side");
    fixture.git(&["checkout", "main"]);
    fixture.write("conflict", "main\n");
    let head = fixture.commit("main");
    let output = Command::new("git")
        .current_dir(fixture.path())
        .args(["merge", "side"])
        .output()
        .unwrap();
    assert!(!output.status.success());
    let (client, repo) = fixture.connect().await;
    let status = request(&client, "git.status", json!({"repoId":repo})).await;
    assert_eq!(status["entries"][0]["kind"], "conflict");
    assert_eq!(status["entries"][0]["index"], "U");
    assert_eq!(status["entries"][0]["worktree"], "U");
    fixture.git(&["merge", "--abort"]);
    fixture.git(&["checkout", "--detach", &head]);
    let status = request(&client, "git.status", json!({"repoId":repo})).await;
    assert!(status["branch"].is_null());
    assert_eq!(status["head"], head);
    client.shutdown().await;
}

#[tokio::test]
async fn binary_preview_limits_and_literal_paths() {
    let fixture = Fixture::new();
    fixture.write("binary.dat", [0, 1, 2]);
    fixture.write("large", "old\n");
    fixture.commit("base");
    fixture.write("binary.dat", [0, 3, 4]);
    fixture.write("large", "0123456789abcdef\n".repeat(20000));
    fixture.write("new.bin", [0, 4, 5]);
    fixture.write("--literal [x].txt", "preview\n");
    let (client, repo) = fixture.connect().await;
    let binary = request(
        &client,
        "git.diff",
        json!({"repoId":repo,"target":{"kind":"unstaged"},"path":"binary.dat"}),
    )
    .await;
    assert_eq!(binary["binary"], true);
    let large = request(
        &client,
        "git.diff",
        json!({"repoId":repo,"target":{"kind":"unstaged"},"path":"large"}),
    )
    .await;
    assert_eq!(large["truncated"], true);
    let preview = request(
        &client,
        "git.diff",
        json!({"repoId":repo,"target":{"kind":"untracked"},"path":"new.bin"}),
    )
    .await;
    assert_eq!(preview["binary"], true);
    assert!(preview["text"].is_null());
    let preview = request(
        &client,
        "git.diff",
        json!({"repoId":repo,"target":{"kind":"untracked"},"path":"--literal [x].txt"}),
    )
    .await;
    assert_eq!(preview["text"], "preview\n");
    assert_eq!(preview["untracked"], true);
    let error = client.call::<_, Value>("git.diff", &json!({"repoId":repo,"operationId":"traversal","target":{"kind":"untracked"},"path":"../outside"})).await.unwrap_err();
    assert!(error.to_string().contains("relative"));
    client.shutdown().await;
}

#[tokio::test]
async fn multiple_repositories_worktrees_and_reconnect_handles() {
    let fixture = Fixture::new();
    fixture.write("base", "base");
    fixture.commit("base");
    let linked_parent = TempDir::new().unwrap();
    let linked = linked_parent.path().join("linked");
    fixture.git(&["worktree", "add", "-b", "linked", linked.to_str().unwrap()]);
    let other = Fixture::new();
    let (client, repo) = fixture.connect().await;
    let worktree = request(&client, "repo.open", json!({"path":linked})).await;
    assert_ne!(worktree["repoId"], repo);
    assert_ne!(worktree["gitDir"], worktree["commonDir"]);
    let second = request(&client, "repo.open", json!({"path":other.path()})).await;
    let (a, b) = tokio::join!(
        request(&client, "git.status", json!({"repoId":repo})),
        request(&client, "git.status", json!({"repoId":second["repoId"]}))
    );
    assert!(!a["head"].is_null());
    assert!(b["head"].is_null());
    client.shutdown().await;
    let (new_client, new_repo) = fixture.connect().await;
    assert_ne!(new_repo, repo);
    assert!(
        new_client
            .call::<_, Value>("git.status", &json!({"repoId":repo,"operationId":"old"}))
            .await
            .is_err()
    );
    new_client.shutdown().await;
}

#[cfg(unix)]
#[tokio::test]
async fn newline_tab_paths_and_external_symlink_preview() {
    let fixture = Fixture::new();
    let name = "line\nwith\ttab";
    fixture.write(name, "first\n");
    fixture.commit("root");
    fixture.write(name, "second\n");
    std::os::unix::fs::symlink("/etc/passwd", fixture.path().join("link")).unwrap();
    let (client, repo) = fixture.connect().await;
    let status = request(&client, "git.status", json!({"repoId":repo})).await;
    assert!(
        status["entries"]
            .as_array()
            .unwrap()
            .iter()
            .any(|e| e["path"] == name)
    );
    let patch = request(
        &client,
        "git.diff",
        json!({"repoId":repo,"target":{"kind":"unstaged"},"path":name}),
    )
    .await;
    assert!(patch["text"].as_str().unwrap().contains("+second"));
    let preview = request(
        &client,
        "git.diff",
        json!({"repoId":repo,"target":{"kind":"untracked"},"path":"link"}),
    )
    .await;
    assert_eq!(preview["text"], "/etc/passwd");
    client.shutdown().await;
}
