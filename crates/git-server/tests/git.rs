use git_client::Client;
use serde_json::{Value, json};
use std::{collections::BTreeSet, fs, path::Path, process::Command};
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
        let client = Client::connect(Path::new(env!("CARGO_BIN_EXE_project-launch-git-server")))
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

async fn change_index(client: &Client, repo: &str, stage: bool, group: &str, paths: &[&str]) {
    assert_eq!(
        request(
            client,
            if stage { "git.stage" } else { "git.unstage" },
            json!({"repoId":repo,"target":{"kind":group},"paths":paths})
        )
        .await,
        true
    );
}

async fn commit_params(client: &Client, repo: &str, message: &str) -> Value {
    let status = request(client, "git.status", json!({"repoId":repo})).await;
    json!({
        "repoId": repo,
        "operationId": uuid::Uuid::new_v4().to_string(),
        "message": message,
        "expectedHead": status["head"],
        "expectedBranch": status["branch"],
    })
}

#[cfg(unix)]
#[tokio::test]
async fn agent_messages_use_staged_or_all_diffs_without_mutating_repository() {
    use std::os::unix::fs::PermissionsExt;
    let f = Fixture::new();
    f.write("partial", "base\n");
    let head = f.commit("Base");
    f.write("partial", "staged-version\n");
    f.git(&["add", "partial"]);
    f.write("partial", "working-version\n");
    f.write("new file", "untracked-version\n");
    f.write("binary.bin", [0, 1, 255]);
    let index = f.git(&["write-tree"]);
    let bin = TempDir::new().unwrap();
    let capture = bin.path().join("prompt");
    let mode = bin.path().join("mode");
    let script = r#"#!/bin/sh
cat > "$PROJLAUNCH_TEST_PROMPT"
printf '%s\n' "$@" > "$PROJLAUNCH_TEST_ARGS"
# Reproduce a CLI echoing a large prompt/progress stream before its final result.
printf 'diagnostic-prefix-to-discard\n' >&2
head -c 196608 /dev/zero | tr '\000' x >&2
printf '\n' >&2
case "$(cat "$PROJLAUNCH_TEST_MODE")" in
    fail) echo 'Sign in to the agent first' >&2; exit 1 ;;
    slow) exec sleep 20 ;;
    invalid) printf '{"result":"","is_error":false}'; exit 0 ;;
    change) printf 'external-change\n' > "$PROJLAUNCH_TEST_REPO/partial" ;;
esac
if [ "$1" = exec ]; then
    while [ "$1" != --output-last-message ]; do shift; done
    shift
    printf 'Describe staged changes\n\nGenerated body\n' > "$1"
else
    printf '{"result":"Describe all changes","is_error":false}'
fi
"#;
    for name in ["codex", "claude"] {
        let path = bin.path().join(name);
        fs::write(&path, script).unwrap();
        fs::set_permissions(path, fs::Permissions::from_mode(0o755)).unwrap();
    }
    fs::write(&mode, "ok").unwrap();
    let mut command = tokio::process::Command::new(env!("CARGO_BIN_EXE_project-launch-git-server"));
    let path = std::env::join_paths(
        std::iter::once(bin.path().to_path_buf())
            .chain(std::env::split_paths(&std::env::var_os("PATH").unwrap())),
    )
    .unwrap();
    command
        .arg("stdio")
        .env("PATH", path)
        .env("PROJLAUNCH_TEST_PROMPT", &capture)
        .env("PROJLAUNCH_TEST_ARGS", bin.path().join("args"))
        .env("PROJLAUNCH_TEST_MODE", &mode)
        .env("PROJLAUNCH_TEST_REPO", f.path());
    let client = Client::connect_command(command).await.unwrap();
    let repo = request(&client, "repo.open", json!({"path":f.path()})).await;
    let params = json!({"repoId":repo["repoId"],"agent":"codex"});
    let generated = request(&client, "git.generateCommitMessage", params.clone()).await;
    assert_eq!(generated["scope"], "staged");
    assert_eq!(
        generated["message"],
        "Describe staged changes\n\nGenerated body"
    );
    let prompt = fs::read_to_string(&capture).unwrap();
    assert!(prompt.contains("staged-version"));
    assert!(!prompt.contains("working-version"));
    assert!(!prompt.contains("untracked-version"));
    assert!(prompt.contains("ONLY their staged diff") && prompt.contains("ALL working changes"));
    let args = fs::read_to_string(bin.path().join("args")).unwrap();
    assert!(args.contains("--sandbox\nread-only") && args.contains("--ignore-user-config"));
    assert_eq!(f.git(&["write-tree"]), index);
    assert_eq!(f.git(&["rev-parse", "HEAD"]), head);
    assert_eq!(
        fs::read_to_string(f.path().join("partial")).unwrap(),
        "working-version\n"
    );
    f.git(&["reset", "--", "partial"]);
    let params = json!({"repoId":repo["repoId"],"agent":"claude"});
    let generated = request(&client, "git.generateCommitMessage", params.clone()).await;
    assert_eq!(generated["scope"], "all");
    assert_eq!(generated["message"], "Describe all changes");
    let prompt = fs::read_to_string(&capture).unwrap();
    assert!(prompt.contains("working-version") && prompt.contains("untracked-version"));
    assert!(prompt.contains("Binary file; contents omitted"));
    assert!(!prompt.contains("staged-version"));
    assert!(
        fs::read_to_string(bin.path().join("args"))
            .unwrap()
            .contains("--tools\n\n--strict-mcp-config")
    );
    for mode_value in ["fail", "invalid"] {
        fs::write(&mode, mode_value).unwrap();
        let mut params = params.clone();
        params["operationId"] = json!(mode_value);
        let error = client
            .call::<_, Value>("git.generateCommitMessage", &params)
            .await
            .unwrap_err()
            .to_string();
        assert!(
            error.contains(if mode_value == "fail" {
                "Sign in to the agent first"
            } else {
                "empty or invalid"
            }),
            "{error}"
        );
        if mode_value == "fail" {
            assert!(error.contains("Earlier stderr omitted"), "{error}");
            assert!(!error.contains("diagnostic-prefix-to-discard"));
            assert!(
                error.len() < 66000,
                "Diagnostic memory/output must stay bounded"
            );
        }
    }
    fs::write(&mode, "change").unwrap();
    let error = client
        .call::<_, Value>(
            "git.generateCommitMessage",
            &json!({"repoId":repo["repoId"],"agent":"claude","operationId":"change"}),
        )
        .await
        .unwrap_err()
        .to_string();
    assert!(error.contains("changed during generation"), "{error}");
    fs::remove_file(&capture).unwrap();
    fs::write(&mode, "slow").unwrap();
    let operation = async {
        client
            .call::<_, Value>(
                "git.generateCommitMessage",
                &json!({"repoId":repo["repoId"],"agent":"claude","operationId":"slow"}),
            )
            .await
    };
    let cancel = async {
        tokio::time::timeout(std::time::Duration::from_secs(5), async {
            while !capture.exists() {
                tokio::time::sleep(std::time::Duration::from_millis(10)).await;
            }
        })
        .await
        .unwrap();
        // Change content while the agent is running, then cancel without losing Git state.
        f.write("partial", "changed-again\n");
        client
            .call::<_, bool>("git.cancel", &json!({"operationId":"slow"}))
            .await
            .unwrap();
    };
    let (result, _) = tokio::join!(operation, cancel);
    assert!(result.unwrap_err().to_string().contains("cancelled"));
    assert_eq!(f.git(&["rev-parse", "HEAD"]), head);
    assert!(f.git(&["diff", "--cached", "--name-only"]).is_empty());
    client.shutdown().await;
}

#[tokio::test]
async fn commit_records_entire_index_and_preserves_unstaged_and_untracked_files() {
    let f = Fixture::new();
    f.write("partial", "base\n");
    f.write("removed", "gone\n");
    f.write("original", "rename\n");
    let parent = f.commit("base");
    f.write("partial", "staged\n");
    f.git(&["add", "partial"]);
    f.write("partial", "unstaged\n");
    f.git(&["rm", "removed"]);
    f.git(&["mv", "original", "renamed"]);
    f.write("--новый [1].bin", [0, 1, 255]);
    f.git(&["add", "--", "--новый [1].bin"]);
    f.write("untracked", "leave alone");
    let index = f.git(&["write-tree"]);
    let (client, repo) = f.connect().await;
    let message = "Unicode коммит\n\n# literal comment\n--amend $(no-shell) `literal`\n";
    let params = commit_params(&client, &repo, message).await;
    let result: Value = client.call("git.commit", &params).await.unwrap();
    assert_eq!(result["oid"], f.git(&["rev-parse", "HEAD"]));
    assert_eq!(f.git(&["rev-parse", "HEAD^"]), parent);
    assert_eq!(f.git(&["rev-parse", "HEAD^{tree}"]), index);
    assert_eq!(f.git(&["show", "HEAD:partial"]), "staged");
    assert_eq!(f.git(&["log", "-1", "--format=%B"]), message.trim());
    assert_eq!(
        f.git(&["log", "-1", "--format=%an <%ae>"]),
        "Тест Author <test@example.invalid>"
    );
    assert_eq!(
        fs::read_to_string(f.path().join("partial")).unwrap(),
        "unstaged\n"
    );
    assert_eq!(
        fs::read_to_string(f.path().join("untracked")).unwrap(),
        "leave alone"
    );
    assert_eq!(f.git(&["write-tree"]), index);
    assert!(f.git(&["diff", "--cached", "--name-only"]).is_empty());
    client.shutdown().await;
}

#[tokio::test]
async fn commit_supports_unborn_and_detached_head() {
    let f = Fixture::new();
    f.write("first", "one");
    f.git(&["add", "first"]);
    let (client, repo) = f.connect().await;
    let params = commit_params(&client, &repo, "First").await;
    assert!(params["expectedHead"].is_null());
    let first: Value = client.call("git.commit", &params).await.unwrap();
    assert_eq!(f.git(&["log", "-1", "--format=%P"]), "");
    f.git(&["checkout", "--detach"]);
    f.write("first", "two");
    f.git(&["add", "first"]);
    let params = commit_params(&client, &repo, "Detached").await;
    assert!(params["expectedBranch"].is_null());
    let detached: Value = client.call("git.commit", &params).await.unwrap();
    assert_ne!(first["oid"], detached["oid"]);
    assert_eq!(first["oid"], f.git(&["rev-parse", "main"]));
    assert_eq!(first["oid"], f.git(&["rev-parse", "HEAD^"]));
    assert_eq!(detached["oid"], f.git(&["rev-parse", "HEAD"]));
    client.shutdown().await;
}

#[tokio::test]
async fn commit_rejects_invalid_message_empty_index_stale_head_and_busy_repository() {
    let f = Fixture::new();
    f.write("file", "base");
    let head = f.commit("Base");
    let (client, repo) = f.connect().await;
    let params = commit_params(&client, &repo, "Nothing").await;
    let error = client
        .call::<_, Value>("git.commit", &params)
        .await
        .unwrap_err()
        .to_string();
    assert!(error.contains("No staged changes"), "{error}");
    f.write("file", "next");
    f.git(&["add", "file"]);
    let index = f.git(&["write-tree"]);
    for message in [
        " \n\t".to_string(),
        "nul\0message".to_string(),
        "я".repeat(32769),
    ] {
        let params = commit_params(&client, &repo, &message).await;
        let error = client
            .call::<_, Value>("git.commit", &params)
            .await
            .unwrap_err()
            .to_string();
        assert!(error.contains("Commit message"), "{error}");
    }
    let params = commit_params(&client, &repo, "Stale branch").await;
    f.git(&["checkout", "-b", "other"]);
    let error = client
        .call::<_, Value>("git.commit", &params)
        .await
        .unwrap_err()
        .to_string();
    assert!(error.contains("HEAD or branch changed"), "{error}");
    let mut params = commit_params(&client, &repo, "Stale HEAD").await;
    params["expectedHead"] = json!("0".repeat(40));
    assert!(
        client
            .call::<_, Value>("git.commit", &params)
            .await
            .unwrap_err()
            .to_string()
            .contains("HEAD or branch changed")
    );
    for marker in [
        "MERGE_HEAD",
        "CHERRY_PICK_HEAD",
        "REVERT_HEAD",
        "rebase-merge",
        "rebase-apply",
        "sequencer",
        "index.lock",
    ] {
        f.write(&format!(".git/{marker}"), &head);
        let params = commit_params(&client, &repo, "Blocked").await;
        let error = client
            .call::<_, Value>("git.commit", &params)
            .await
            .unwrap_err()
            .to_string();
        assert!(error.contains(marker), "{error}");
        assert_eq!(
            fs::read_to_string(f.path().join(format!(".git/{marker}"))).unwrap(),
            head
        );
        fs::remove_file(f.path().join(format!(".git/{marker}"))).unwrap();
    }
    assert_eq!(f.git(&["rev-parse", "HEAD"]), head);
    assert_eq!(f.git(&["write-tree"]), index);
    assert_eq!(fs::read_to_string(f.path().join("file")).unwrap(), "next");
    client.shutdown().await;
}

#[cfg(unix)]
#[tokio::test]
async fn commit_honors_hooks_identity_and_signing_failures() {
    use std::os::unix::fs::PermissionsExt;
    let f = Fixture::new();
    f.write("file", "base");
    let head = f.commit("Base");
    f.write("file", "next");
    f.git(&["add", "file"]);
    let index = f.git(&["write-tree"]);
    let (client, repo) = f.connect().await;
    f.git(&["config", "core.hooksPath", ".git/hooks"]);
    for hook in ["pre-commit", "commit-msg"] {
        let name = format!(".git/hooks/{hook}");
        f.write(&name, "#!/bin/sh\necho rejected-by-hook >&2\nexit 1\n");
        fs::set_permissions(f.path().join(&name), fs::Permissions::from_mode(0o755)).unwrap();
        let params = commit_params(&client, &repo, "Rejected").await;
        let error = client
            .call::<_, Value>("git.commit", &params)
            .await
            .unwrap_err()
            .to_string();
        assert!(error.contains("rejected-by-hook"), "{error}");
        fs::remove_file(f.path().join(&name)).unwrap();
    }
    f.git(&["config", "user.name", ""]);
    let params = commit_params(&client, &repo, "Identity").await;
    assert!(
        client
            .call::<_, Value>("git.commit", &params)
            .await
            .is_err()
    );
    f.git(&["config", "user.name", "Author"]);
    f.git(&["config", "commit.gpgsign", "true"]);
    f.git(&["config", "gpg.format", "openpgp"]);
    f.git(&[
        "config",
        "gpg.program",
        "/nonexistent-projlaunch-test-signer",
    ]);
    let params = commit_params(&client, &repo, "Signing").await;
    assert!(
        client
            .call::<_, Value>("git.commit", &params)
            .await
            .is_err()
    );
    assert_eq!(f.git(&["rev-parse", "HEAD"]), head);
    assert_eq!(f.git(&["write-tree"]), index);
    f.git(&["config", "commit.gpgsign", "false"]);
    f.write(
        ".git/hooks/commit-msg",
        "#!/bin/sh\nprintf '\\nHook trailer\\n' >> \"$1\"\n",
    );
    fs::set_permissions(
        f.path().join(".git/hooks/commit-msg"),
        fs::Permissions::from_mode(0o755),
    )
    .unwrap();
    let params = commit_params(&client, &repo, "Accepted").await;
    let _: Value = client.call("git.commit", &params).await.unwrap();
    assert_eq!(
        f.git(&["log", "-1", "--format=%B"]),
        "Accepted\n\nHook trailer"
    );
    client.shutdown().await;
}

#[tokio::test]
async fn index_changes_preserve_worktree_head_and_unselected_partial_changes() {
    let f = Fixture::new();
    f.write("partial", "base\n");
    f.write("deleted", "remove\n");
    f.write("other", "base\n");
    let head = f.commit("base");
    f.write("partial", "index\n");
    f.git(&["add", "partial"]);
    f.write("partial", "worktree\n");
    f.write("other", "unselected\n");
    f.write("--новый [1].bin", [0, 1, 2, 255]);
    fs::remove_file(f.path().join("deleted")).unwrap();
    let (client, repo) = f.connect().await;
    change_index(&client, &repo, true, "unstaged", &["partial", "deleted"]).await;
    assert_eq!(f.git(&["show", ":partial"]), "worktree");
    assert_eq!(f.git(&["show", ":other"]), "base");
    change_index(&client, &repo, true, "untracked", &["--новый [1].bin"]).await;
    change_index(
        &client,
        &repo,
        false,
        "staged",
        &["partial", "deleted", "--новый [1].bin"],
    )
    .await;
    assert!(f.git(&["diff", "--cached", "--name-only"]).is_empty());
    assert_eq!(f.git(&["rev-parse", "HEAD"]), head);
    assert_eq!(fs::read(f.path().join("partial")).unwrap(), b"worktree\n");
    assert!(!f.path().join("deleted").exists());
    assert_eq!(
        fs::read(f.path().join("--новый [1].bin")).unwrap(),
        [0, 1, 2, 255]
    );
    client.shutdown().await;
}

#[tokio::test]
async fn unborn_index_can_stage_and_unstage_selected_literal_files() {
    let f = Fixture::new();
    for name in ["one", "--two [x]", "two x"] {
        f.write(name, "new");
    }
    let (client, repo) = f.connect().await;
    change_index(&client, &repo, true, "untracked", &["one", "--two [x]"]).await;
    assert_eq!(f.git(&["ls-files"]), "--two [x]\none");
    change_index(&client, &repo, false, "staged", &["--two [x]"]).await;
    assert_eq!(f.git(&["ls-files"]), "one");
    change_index(&client, &repo, false, "staged", &["one"]).await;
    assert!(f.git(&["ls-files"]).is_empty());
    assert_eq!(fs::read(f.path().join("one")).unwrap(), b"new");
    client.shutdown().await;
}

#[tokio::test]
async fn unstage_rename_restores_both_index_paths_without_renaming_working_file() {
    let f = Fixture::new();
    f.write("old", "unchanged content\n");
    f.commit("base");
    f.git(&["mv", "old", "new"]);
    let (client, repo) = f.connect().await;
    change_index(&client, &repo, false, "staged", &["new"]).await;
    assert_eq!(f.git(&["ls-files"]), "old");
    assert!(f.git(&["diff", "--cached", "--name-only"]).is_empty());
    assert!(!f.path().join("old").exists());
    assert_eq!(
        fs::read(f.path().join("new")).unwrap(),
        b"unchanged content\n"
    );
    client.shutdown().await;
}

#[tokio::test]
async fn invalid_or_stale_selection_and_index_lock_do_not_stage_other_files() {
    let f = Fixture::new();
    f.write("tracked", "base");
    f.commit("base");
    f.write("tracked", "changed");
    f.write("new", "new");
    let (client, repo) = f.connect().await;
    for (method, group, paths) in [
        ("git.stage", "unstaged", vec![]),
        ("git.stage", "unstaged", vec!["tracked", "stale"]),
        ("git.stage", "untracked", vec!["../outside"]),
        ("git.stage", "untracked", vec!["."]),
        ("git.stage", "untracked", vec!["*"]),
        ("git.stage", "staged", vec!["tracked"]),
        ("git.unstage", "unstaged", vec!["tracked"]),
    ] {
        assert!(client.call::<_, Value>(method, &json!({"repoId":repo,"operationId":"invalid","target":{"kind":group},"paths":paths})).await.is_err());
        assert!(f.git(&["diff", "--cached", "--name-only"]).is_empty());
    }
    f.write(".git/index.lock", "external lock");
    assert!(client.call::<_, Value>("git.stage", &json!({"repoId":repo,"operationId":"locked","target":{"kind":"unstaged"},"paths":["tracked"]})).await.is_err());
    assert_eq!(
        fs::read(f.path().join(".git/index.lock")).unwrap(),
        b"external lock"
    );
    assert!(f.git(&["diff", "--cached", "--name-only"]).is_empty());
    client.shutdown().await;
}

#[tokio::test]
async fn file_replaced_by_directory_does_not_stage_unselected_descendants() {
    let f = Fixture::new();
    f.write("item", "file");
    f.commit("base");
    fs::remove_file(f.path().join("item")).unwrap();
    fs::create_dir(f.path().join("item")).unwrap();
    f.write("item/new", "unselected");
    let (client, repo) = f.connect().await;
    let result = client.call::<_, Value>("git.stage", &json!({"repoId":repo,"operationId":"directory","target":{"kind":"unstaged"},"paths":["item"]})).await;
    assert!(result.is_err());
    assert_eq!(f.git(&["show", ":item"]), "file");
    assert!(f.git(&["diff", "--cached", "--name-only"]).is_empty());
    client.shutdown().await;
}

#[tokio::test]
async fn client_bounds_a_workspace_burst_below_the_server_request_limit() {
    let fixture = Fixture::new();
    let (client, _) = fixture.connect().await;
    let replies = futures_util::future::join_all(
        (0..64).map(|_| async { client.call::<_, Value>("server.hello", &json!({})).await }),
    )
    .await;
    assert!(replies.iter().all(Result::is_ok), "{replies:?}");
    assert!(!client.is_closed());
    client.shutdown().await;
    let mut late = client.events();
    assert!(matches!(
        late.try_recv().unwrap(),
        git_client::Event::Disconnected(_)
    ));
}

async fn changed(
    events: &mut tokio::sync::broadcast::Receiver<git_client::Event>,
    history: bool,
) -> git_protocol::RepositoryChanged {
    tokio::time::timeout(std::time::Duration::from_secs(8), async {
        loop {
            match events.recv().await.unwrap() {
                git_client::Event::Changed(event) => {
                    assert!(event.error.is_none(), "{event:?}");
                    if !history || event.history {
                        return event;
                    }
                }
                event => panic!("Unexpected {event:?}"),
            }
        }
    })
    .await
    .expect("Expected filesystem invalidation")
}

async fn drain_events(events: &mut tokio::sync::broadcast::Receiver<git_client::Event>) {
    while tokio::time::timeout(std::time::Duration::from_millis(600), events.recv())
        .await
        .is_ok()
    {}
}

#[tokio::test]
async fn subscriptions_observe_content_index_refs_and_stop_without_read_feedback() {
    let fixture = Fixture::new();
    fixture.write("tracked.txt", "initial");
    fixture.commit("Initial");
    let (client, repo) = fixture.connect().await;
    let mut events = client.events();
    let params = json!({"repoId":repo,"subscriptionId":"watch"});
    assert!(
        client
            .call::<_, bool>("repo.subscribe", &params)
            .await
            .unwrap()
    );
    assert!(
        client
            .call::<_, bool>("repo.subscribe", &params)
            .await
            .is_err()
    );
    fixture.write("new.txt", "untracked");
    let event = changed(&mut events, false).await;
    assert_eq!(event.subscription_id, "watch");
    assert_eq!(event.repo_id, repo);
    assert!(event.working_tree);
    assert!(!event.history);
    fixture.write("tracked.txt", "modified");
    changed(&mut events, false).await;
    // Another edit to an already dirty file must invalidate an open diff too.
    fixture.write("tracked.txt", "modified again");
    changed(&mut events, false).await;
    fixture.git(&["add", "tracked.txt"]);
    assert!(changed(&mut events, false).await.working_tree);
    fixture.commit("Second");
    assert!(changed(&mut events, true).await.history);
    fixture.git(&["branch", "another"]);
    changed(&mut events, true).await;
    fixture.git(&["pack-refs", "--all"]);
    changed(&mut events, true).await;
    drain_events(&mut events).await;
    request(&client, "git.status", json!({"repoId":repo})).await;
    request(&client, "git.log", json!({"repoId":repo})).await;
    request(&client, "git.branches", json!({"repoId":repo})).await;
    let unexpected =
        tokio::time::timeout(std::time::Duration::from_millis(800), events.recv()).await;
    assert!(
        unexpected.is_err(),
        "Reads must not trigger a refresh loop: {unexpected:?}"
    );
    assert!(
        client
            .call::<_, bool>("repo.unsubscribe", &json!({"subscriptionId":"watch"}))
            .await
            .unwrap()
    );
    assert!(
        !client
            .call::<_, bool>("repo.unsubscribe", &json!({"subscriptionId":"watch"}))
            .await
            .unwrap()
    );
    fixture.write("tracked.txt", "after unsubscribe");
    assert!(
        tokio::time::timeout(std::time::Duration::from_millis(800), events.recv())
            .await
            .is_err()
    );
    client.shutdown().await;
}

#[tokio::test]
async fn linked_worktree_subscriptions_watch_shared_refs_and_are_session_scoped() {
    let fixture = Fixture::new();
    fixture.write("tracked.txt", "initial");
    fixture.commit("Initial");
    let linked = tempfile::tempdir().unwrap();
    let root = linked.path().join("linked worktree");
    fixture.git(&["worktree", "add", "-b", "linked", root.to_str().unwrap()]);
    let (first, main_repo) = fixture.connect().await;
    let (second, _) = fixture.connect().await;
    let other = request(&second, "repo.open", json!({"path":root})).await;
    let mut main_events = first.events();
    let mut linked_events = second.events();
    first
        .call::<_, bool>(
            "repo.subscribe",
            &json!({"repoId":main_repo,"subscriptionId":"same-id"}),
        )
        .await
        .unwrap();
    second
        .call::<_, bool>(
            "repo.subscribe",
            &json!({"repoId":other["repoId"],"subscriptionId":"same-id"}),
        )
        .await
        .unwrap();
    fs::write(root.join("tracked.txt"), "linked edit").unwrap();
    assert!(changed(&mut linked_events, false).await.working_tree);
    assert!(
        tokio::time::timeout(std::time::Duration::from_millis(600), main_events.recv())
            .await
            .is_err()
    );
    fixture.git(&["tag", "shared-tag"]);
    changed(&mut linked_events, true).await;
    changed(&mut main_events, true).await;
    first.shutdown().await;
    fixture.git(&["branch", "after-other-session-closed"]);
    changed(&mut linked_events, true).await;
    second.shutdown().await;
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
    let branches = request(&client, "git.branches", json!({"repoId":repo})).await;
    assert_eq!(branches["branches"], json!([]));
    assert_eq!(branches["remotes"], json!([]));
    assert_eq!(branches["currentBranch"], "main");
    assert!(branches["head"].is_null());
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
async fn branches_include_unloaded_tips_tracking_unicode_and_detached_head() {
    let fixture = Fixture::new();
    fixture.write("file.txt", "root\n");
    let root = fixture.commit("root");
    fixture.git(&["branch", "feature/старе"]);
    fixture.git(&["update-ref", "refs/remotes/origin/main", &root]);
    fixture.git(&[
        "symbolic-ref",
        "refs/remotes/origin/HEAD",
        "refs/remotes/origin/main",
    ]);
    fixture.git(&[
        "remote",
        "add",
        "origin",
        "https://example.invalid/repo.git",
    ]);
    fixture.git(&["config", "branch.main.remote", "origin"]);
    fixture.git(&["config", "branch.main.merge", "refs/heads/main"]);
    fixture.git(&["tag", "not-a-branch"]);
    fixture.write("file.txt", "tip\n");
    let tip = fixture.commit("tip");
    let (client, repo) = fixture.connect().await;
    let page = request(&client, "git.log", json!({"repoId":repo, "limit":1})).await;
    assert_eq!(page["commits"].as_array().unwrap().len(), 1);
    assert_eq!(page["commits"][0]["oid"], tip);
    let list = request(&client, "git.branches", json!({"repoId":repo})).await;
    assert_eq!(
        list["remotes"],
        json!([{"name":"origin", "url":"https://example.invalid/repo.git"}])
    );
    assert_eq!(list["currentBranch"], "main");
    assert_eq!(list["head"], tip);
    let branches = list["branches"].as_array().unwrap();
    assert_eq!(
        branches.len(),
        3,
        "Tags and symbolic remote HEAD are excluded"
    );
    let old = branches
        .iter()
        .find(|b| b["name"] == "feature/старе")
        .unwrap();
    assert_eq!(old["oid"], root);
    assert_eq!(old["refName"], "refs/heads/feature/старе");
    assert_eq!(old["isRemote"], false);
    let main = branches.iter().find(|b| b["name"] == "main").unwrap();
    assert_eq!(main["isCurrent"], true);
    assert_eq!(main["upstream"], "origin/main");
    assert_eq!(main["tracking"], "ahead 1");
    let remote = branches
        .iter()
        .find(|b| b["name"] == "origin/main")
        .unwrap();
    assert_eq!(remote["isRemote"], true);
    assert_eq!(remote["isCurrent"], false);
    fixture.git(&["checkout", "--detach", &root]);
    let detached = request(&client, "git.branches", json!({"repoId":repo})).await;
    assert!(detached["currentBranch"].is_null());
    assert_eq!(detached["head"], root);
    assert!(
        detached["branches"]
            .as_array()
            .unwrap()
            .iter()
            .all(|b| b["isCurrent"] == false)
    );
    fixture.git(&["branch", "-D", "feature/старе"]);
    let refreshed = request(&client, "git.branches", json!({"repoId":repo})).await;
    assert_eq!(refreshed["branches"].as_array().unwrap().len(), 2);
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
async fn history_paginates_more_than_1024_distinct_roots() {
    let fixture = Fixture::new();
    // Fast-import creates independent tips in one process. 2048 SHA-1 roots
    // exceed both the former root cap and Windows' command-line limit.
    let mut input = String::new();
    for index in 0..2048 {
        let message = format!("root {index}");
        input.push_str(&format!(
            "commit refs/heads/root-{index}\ncommitter Test <test@example.invalid> 1700000000 +0000\ndata {}\n{message}\n\n",
            message.len()
        ));
    }
    let import = fixture.path().join("import.txt");
    fs::write(&import, input).unwrap();
    let output = Command::new("git")
        .current_dir(fixture.path())
        .args(["fast-import", "--quiet"])
        .stdin(fs::File::open(import).unwrap())
        .output()
        .unwrap();
    assert!(
        output.status.success(),
        "{}",
        String::from_utf8_lossy(&output.stderr)
    );
    fixture.git(&["update-ref", "refs/heads/main", "refs/heads/root-0"]);
    fixture.git(&["tag", "duplicate-tip", "refs/heads/root-0"]);
    let expected: BTreeSet<_> = fixture
        .git(&["rev-list", "--all"])
        .lines()
        .map(str::to_owned)
        .collect();
    assert_eq!(expected.len(), 2048);

    let (client, repo) = fixture.connect().await;
    let mut cursor = Value::Null;
    let mut seen = BTreeSet::new();
    loop {
        let page = request(
            &client,
            "git.log",
            json!({"repoId":repo,"limit":200,"cursor":cursor}),
        )
        .await;
        for commit in page["commits"].as_array().unwrap() {
            assert!(
                seen.insert(commit["oid"].as_str().unwrap().to_owned()),
                "duplicate commit across pages"
            );
        }
        cursor = page["nextCursor"].clone();
        if cursor.is_null() {
            break;
        }
    }
    assert_eq!(seen, expected);

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
    let params = commit_params(&client, &repo, "Do not commit conflicts").await;
    let merge_head = fs::read(fixture.path().join(".git/MERGE_HEAD")).unwrap();
    fs::remove_file(fixture.path().join(".git/MERGE_HEAD")).unwrap();
    // An unmerged index can exist without an integration-operation marker.
    let error = client
        .call::<_, Value>("git.commit", &params)
        .await
        .unwrap_err()
        .to_string();
    assert!(error.contains("Resolve all conflicts"), "{error}");
    assert_eq!(fixture.git(&["rev-parse", "HEAD"]), head);
    fixture.write(".git/MERGE_HEAD", merge_head);
    fixture.write("conflict", "resolved\n");
    change_index(&client, &repo, true, "unstaged", &["conflict"]).await;
    assert!(fixture.git(&["ls-files", "--unmerged"]).is_empty());
    assert_eq!(fixture.git(&["show", ":conflict"]), "resolved");
    assert_eq!(fixture.git(&["rev-parse", "HEAD"]), head);
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
    // A file can disappear between listing and opening its diff. The RPC error
    // must identify it even after crossing the sidecar/client boundary.
    let info = request(&client, "repo.open", json!({"path":fixture.path()})).await;
    fs::remove_file(fixture.path().join("--literal [x].txt")).unwrap();
    let error = client.call::<_, Value>("git.diff", &json!({"repoId":repo,"operationId":"missing-file","target":{"kind":"untracked"},"path":"--literal [x].txt"})).await.unwrap_err().to_string();
    let missing = Path::new(info["root"].as_str().unwrap()).join("--literal [x].txt");
    assert!(error.contains(missing.to_str().unwrap()), "{error}");
    assert!(error.contains("not in the selected diff"), "{error}");
    let error = client.call::<_, Value>("git.diff", &json!({"repoId":repo,"operationId":"traversal","target":{"kind":"untracked"},"path":"../outside"})).await.unwrap_err();
    assert!(error.to_string().contains("relative"));
    client.shutdown().await;
}

#[tokio::test]
async fn repository_errors_identify_the_directory_across_rpc() {
    let directory = TempDir::new().unwrap();
    let missing = directory.path().join("missing репозиторий");
    let client = Client::connect(Path::new(env!("CARGO_BIN_EXE_project-launch-git-server")))
        .await
        .unwrap();
    // Cover both failure to spawn in a missing directory and Git rejecting an
    // existing directory that is not a repository.
    for path in [&missing, directory.path()] {
        let error = client
            .call::<_, Value>(
                "repo.open",
                &json!({"path":path,"operationId":"unavailable-repository"}),
            )
            .await
            .unwrap_err()
            .to_string();
        assert!(error.contains(path.to_str().unwrap()), "{error}");
        assert!(error.contains("Git RPC -32000"), "{error}");
    }
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
