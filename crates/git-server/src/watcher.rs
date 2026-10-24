//! Bounded, debounced invalidations. Watch the Git directories as well as the
//! worktree: linked worktrees keep HEAD/index elsewhere and share refs.
use crate::git::Repo;
use anyhow::Result;
use git_protocol::RepositoryChanged;
use notify::{Event, EventKind, RecommendedWatcher, RecursiveMode, Watcher};
use std::{
    path::Path,
    sync::{
        Arc,
        atomic::{AtomicU8, Ordering},
    },
    time::Duration,
};
use tokio::{sync::mpsc, task::JoinHandle};
use tokio_util::sync::CancellationToken;

const WORKTREE: u8 = 1;
const HISTORY: u8 = 2;
const FAILED: u8 = 4;

pub struct Subscription {
    _watcher: RecommendedWatcher,
    stop: CancellationToken,
    task: JoinHandle<()>,
}

impl Subscription {
    pub async fn close(self) {
        self.stop.cancel();
        let _ = self.task.await;
    }
}

fn metadata_change(path: &Path) -> u8 {
    if path
        .file_name()
        .and_then(|name| name.to_str())
        .is_some_and(|name| name.ends_with(".lock"))
    {
        return 0;
    }
    let first = path.components().next().map(|part| part.as_os_str());
    match first.and_then(|part| part.to_str()) {
        Some("index" | "sharedindex" | "info") => WORKTREE,
        Some(
            "HEAD" | "refs" | "packed-refs" | "config" | "config.worktree" | "shallow" | "reftable",
        ) => HISTORY | WORKTREE,
        Some(name) if name.starts_with("sharedindex.") => WORKTREE,
        _ => 0, // Git reads, object writes, reflogs and temporary lock files.
    }
}

fn classify(event: &Event, repo: &Repo) -> u8 {
    if event.need_rescan() {
        return HISTORY | WORKTREE;
    }
    if matches!(event.kind, EventKind::Access(_)) {
        return 0;
    }
    if event.paths.is_empty() {
        return HISTORY | WORKTREE;
    }
    event.paths.iter().fold(0, |flags, path| {
        let mut change = WORKTREE;
        for directory in [&repo.info.git_dir, &repo.info.common_dir] {
            if let Ok(relative) = path.strip_prefix(directory) {
                // ReadDirectoryChangesW can report directory timestamps on
                // enumeration. Children carry actual ref/index changes; directory
                // renames/removals still invalidate the snapshot.
                if event.kind.is_modify()
                    && !matches!(
                        event.kind,
                        EventKind::Modify(notify::event::ModifyKind::Name(_))
                    )
                    && path.is_dir()
                {
                    return flags;
                }
                change = if relative.as_os_str().is_empty() {
                    if event.kind.is_remove()
                        || matches!(
                            event.kind,
                            EventKind::Modify(notify::event::ModifyKind::Name(_))
                        )
                    {
                        FAILED
                    } else {
                        // Windows also reports directory timestamps when Git
                        // creates/removes a lock. Observe the children instead.
                        0
                    }
                } else {
                    metadata_change(relative)
                };
                return flags | change;
            }
        }
        if path == &repo.root
            && (event.kind.is_remove()
                || matches!(
                    event.kind,
                    EventKind::Modify(notify::event::ModifyKind::Name(_))
                ))
        {
            change |= FAILED;
        }
        // A linked worktree's .git file can change its repository binding.
        if path == &repo.root.join(".git") {
            change |= HISTORY | FAILED;
        }
        flags | change
    })
}

pub async fn subscribe(
    repo: Arc<Repo>,
    subscription_id: String,
    outgoing: mpsc::Sender<String>,
    stop: CancellationToken,
) -> Result<Subscription> {
    let flags = Arc::new(AtomicU8::new(0));
    let (wake, mut events) = mpsc::channel(1);
    let observed = repo.clone();
    let pending = flags.clone();
    let repo_id = repo.info.repo_id.clone();
    let watcher = tokio::task::spawn_blocking(move || {
        let mut watcher = notify::recommended_watcher(move |event: notify::Result<Event>| {
            let changes = match event {
                Ok(event) => classify(&event, &observed),
                Err(_) => FAILED | HISTORY | WORKTREE,
            };
            if changes != 0 {
                pending.fetch_or(changes, Ordering::Relaxed);
                let _ = wake.try_send(());
            }
        })?;
        // Avoid overlapping registrations, which duplicate events and can fail
        // with some native backends. Paths from repo.open are absolute.
        let mut roots = vec![
            repo.root.clone(),
            repo.info.git_dir.clone().into(),
            repo.info.common_dir.clone().into(),
        ];
        roots.sort();
        roots.dedup();
        for root in &roots {
            if !roots
                .iter()
                .any(|parent| parent != root && root.starts_with(parent))
            {
                watcher.watch(root, RecursiveMode::Recursive)?;
            }
        }
        anyhow::Ok(watcher)
    })
    .await??;
    let cancelled = stop.clone();
    let task = tokio::spawn(async move {
        loop {
            tokio::select! {
                _ = cancelled.cancelled() => break,
                event = events.recv() => if event.is_none() { break; },
            }
            // Fixed window: even a continuous build cannot postpone updates forever.
            tokio::select! {
                _ = cancelled.cancelled() => break,
                _ = tokio::time::sleep(Duration::from_millis(300)) => {},
            }
            while events.try_recv().is_ok() {}
            let changes = flags.swap(0, Ordering::Relaxed);
            if changes == 0 {
                continue;
            }
            let event = RepositoryChanged {
                subscription_id: subscription_id.clone(),
                repo_id: repo_id.clone(),
                history: changes & HISTORY != 0,
                working_tree: changes & WORKTREE != 0,
                error: (changes & FAILED != 0)
                    .then(|| "Filesystem observation was interrupted; reconnecting".into()),
            };
            let line =
                serde_json::json!({"jsonrpc":"2.0", "method":"repo.changed", "params":event})
                    .to_string();
            tokio::select! {
                _ = cancelled.cancelled() => break,
                result = outgoing.send(line) => if result.is_err() { break; },
            }
            if changes & FAILED != 0 {
                break;
            }
        }
    });
    Ok(Subscription {
        _watcher: watcher,
        stop,
        task,
    })
}
