use std::{path::Path, sync::Mutex};

use anyhow::{Context, Result};
use serde::Deserialize;
use tracing_appender::{
    non_blocking::{NonBlocking, NonBlockingBuilder, WorkerGuard},
    rolling::{Builder, Rotation},
};
use tracing_subscriber::{EnvFilter, layer::SubscriberExt, util::SubscriberInitExt};

const MAX_BATCH: usize = 32;
const MAX_MESSAGE_BYTES: usize = 16 * 1024;

pub struct Logging {
    guard: Mutex<Option<WorkerGuard>>,
    error: Option<String>,
}

fn file_writer(directory: &Path) -> Result<(NonBlocking, WorkerGuard)> {
    let file = Builder::new()
        .rotation(Rotation::DAILY)
        .filename_prefix("projlaunch")
        .filename_suffix("log")
        .max_log_files(7)
        .build(directory)
        .with_context(|| {
            format!(
                "Cannot create the application log in '{}'",
                directory.display()
            )
        })?;
    Ok(NonBlockingBuilder::default()
        .buffered_lines_limit(4096)
        .finish(file))
}

fn filter() -> EnvFilter {
    EnvFilter::try_from_env("PROJ_LAUNCHER_LOG").unwrap_or_else(|_| EnvFilter::new("info"))
}

impl Logging {
    pub fn start(directory: Result<&Path, &str>) -> Self {
        let setup = (|| -> Result<WorkerGuard> {
            let directory = directory.map_err(|error| anyhow::anyhow!("{error}"))?;
            let (writer, guard) = file_writer(directory)?;
            tracing_subscriber::registry()
                .with(filter())
                .with(
                    tracing_subscriber::fmt::layer()
                        .with_ansi(false)
                        .with_writer(writer),
                )
                .with(
                    tracing_subscriber::fmt::layer()
                        .with_ansi(false)
                        .with_writer(std::io::stderr),
                )
                .try_init()
                .map_err(|error| anyhow::anyhow!("{error}"))?;
            Ok(guard)
        })();
        match setup {
            Ok(guard) => Self {
                guard: Mutex::new(Some(guard)),
                error: None,
            },
            Err(error) => {
                // A read-only/unavailable log directory must not disable the launcher.
                let message = format!("File logging unavailable: {error:#}");
                let _ = tracing_subscriber::fmt()
                    .with_env_filter(filter())
                    .with_ansi(false)
                    .with_writer(std::io::stderr)
                    .try_init();
                eprintln!("{message}");
                Self {
                    guard: Mutex::new(None),
                    error: Some(message),
                }
            }
        }
    }

    pub fn shutdown(&self) {
        // Tauri may exit the process without unwinding run(); flush explicitly on Exit.
        if let Ok(mut guard) = self.guard.lock() {
            drop(guard.take());
        }
    }
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum Level {
    Trace,
    Debug,
    Info,
    Warn,
    Error,
}

#[derive(Debug, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct FrontendEntry {
    level: Level,
    message: String,
}

fn record(entries: &[FrontendEntry]) -> Result<(), String> {
    if entries.len() > MAX_BATCH {
        return Err("A log batch must contain at most 32 entries".into());
    }
    if entries
        .iter()
        .any(|entry| entry.message.len() > MAX_MESSAGE_BYTES)
    {
        return Err("A log message must not exceed 16 KiB".into());
    }
    // Validate the whole batch before emitting any entries. Debug-format strings so
    // newlines/control characters cannot impersonate additional timestamped records.
    for entry in entries {
        match entry.level {
            Level::Trace => tracing::trace!(target: "frontend", message = ?entry.message),
            Level::Debug => tracing::debug!(target: "frontend", message = ?entry.message),
            Level::Info => tracing::info!(target: "frontend", message = ?entry.message),
            Level::Warn => tracing::warn!(target: "frontend", message = ?entry.message),
            Level::Error => tracing::error!(target: "frontend", message = ?entry.message),
        }
    }
    Ok(())
}

#[tauri::command]
pub fn frontend_log(
    logging: tauri::State<'_, Logging>,
    entries: Vec<FrontendEntry>,
) -> Result<(), String> {
    if let Some(error) = &logging.error {
        return Err(error.clone());
    }
    record(&entries)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn native_and_frontend_logs_share_a_file_and_flush_on_shutdown() {
        let temp = tempfile::tempdir().unwrap();
        // The same directory can hold the database; logging must not touch it.
        std::fs::write(temp.path().join("projlaunch.db"), b"database fixture").unwrap();
        let (writer, guard) = file_writer(temp.path()).unwrap();
        let subscriber = tracing_subscriber::fmt()
            .with_ansi(false)
            .with_max_level(tracing::Level::TRACE)
            .with_writer(writer)
            .finish();
        tracing::subscriber::with_default(subscriber, || {
            tracing::info!("native startup");
            record(&[
                FrontendEntry {
                    level: Level::Info,
                    message: "UI ready: Привет".into(),
                },
                FrontendEntry {
                    level: Level::Error,
                    message: "failure\nwith details".into(),
                },
            ])
            .unwrap();
        });
        let logging = Logging {
            guard: Mutex::new(Some(guard)),
            error: None,
        };
        let path = std::fs::read_dir(temp.path())
            .unwrap()
            .map(|entry| entry.unwrap().path())
            .find(|path| path.extension().is_some_and(|ext| ext == "log"))
            .unwrap();
        // The worker must write during runtime, while its guard is still alive.
        let deadline = std::time::Instant::now() + std::time::Duration::from_secs(2);
        while !std::fs::read_to_string(&path)
            .unwrap()
            .contains(r"failure\nwith details")
        {
            assert!(
                std::time::Instant::now() < deadline,
                "Log was not written during runtime"
            );
            std::thread::sleep(std::time::Duration::from_millis(10));
        }
        logging.shutdown();
        logging.shutdown();
        assert!(
            path.file_name()
                .unwrap()
                .to_str()
                .unwrap()
                .starts_with("projlaunch.")
        );
        let text = std::fs::read_to_string(path).unwrap();
        assert!(text.contains("native startup"));
        assert!(text.contains("INFO") && text.contains("ERROR") && text.contains("frontend"));
        assert!(text.contains("Привет"));
        assert!(text.contains(r"failure\nwith details"));
        assert_eq!(text.lines().count(), 3);
        assert!(!text.contains('\u{1b}'));
        assert_eq!(
            std::fs::read(temp.path().join("projlaunch.db")).unwrap(),
            b"database fixture"
        );
    }

    #[test]
    fn frontend_payloads_are_bounded_and_levels_are_validated() {
        assert!(record(&[]).is_ok());
        assert!(
            record(&[FrontendEntry {
                level: Level::Warn,
                message: "x".repeat(MAX_MESSAGE_BYTES + 1)
            }])
            .is_err()
        );
        let entries = (0..33)
            .map(|_| FrontendEntry {
                level: Level::Info,
                message: "test".into(),
            })
            .collect::<Vec<_>>();
        assert!(record(&entries).is_err());
        assert!(
            serde_json::from_str::<FrontendEntry>(r#"{"level":"fatal","message":"test"}"#).is_err()
        );
        assert!(
            serde_json::from_str::<FrontendEntry>(
                r#"{"level":"info","message":"test","path":"elsewhere"}"#
            )
            .is_err()
        );
    }

    #[test]
    fn unavailable_directory_returns_an_error() {
        let temp = tempfile::tempdir().unwrap();
        let file = temp.path().join("not-a-directory");
        std::fs::write(&file, "fixture").unwrap();
        let error = file_writer(&file).err().unwrap();
        assert!(format!("{error:#}").contains(file.to_str().unwrap()));
    }
}
