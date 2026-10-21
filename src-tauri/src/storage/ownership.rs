use std::{
    fs::{File, OpenOptions, TryLockError},
    path::Path,
};

use anyhow::{Context, Result};
use rusqlite::{Connection, OptionalExtension, TransactionBehavior, params};
use serde::Serialize;
use sysinfo::{Pid, ProcessRefreshKind, ProcessesToUpdate, System, UpdateKind};

/// A reader keeps its mode for its entire lifetime. Restart to compete for ownership.
#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct Access {
    pub can_write: bool,
    pub owner_pid: Option<u32>,
}

#[derive(Debug)]
struct Identity {
    pid: u32,
    started: i64,
    executable: String,
}

fn inspect(pid: u32) -> Option<Identity> {
    let mut system = System::new();
    let process_id = Pid::from_u32(pid);
    system.refresh_processes_specifics(
        ProcessesToUpdate::Some(&[process_id]),
        true,
        ProcessRefreshKind::nothing().with_exe(UpdateKind::Always),
    );
    let process = system.process(process_id)?;
    Some(Identity {
        pid,
        started: i64::try_from(process.start_time()).ok()?,
        executable: process.exe()?.to_string_lossy().into_owned(),
    })
}

/// Never delete this file: unlinking a locked file can create two independent locks.
/// The OS releases the handle's lock even after kill/abort. PID reuse or an
/// inaccessible process therefore cannot leave the database permanently locked.
pub(super) fn try_lock(directory: &Path) -> Result<Option<File>> {
    let file = OpenOptions::new()
        .create(true)
        .truncate(false)
        .read(true)
        .write(true)
        .open(directory.join("projlaunch.writer.lock"))
        .context("Cannot open database ownership lock")?;
    match file.try_lock() {
        Ok(()) => Ok(Some(file)),
        Err(TryLockError::WouldBlock) => Ok(None),
        Err(TryLockError::Error(error)) => Err(error).context("Cannot lock database ownership"),
    }
}

pub(super) fn owner_pid(connection: &Connection) -> Result<Option<u32>> {
    let owner = connection
        .query_row(
            "SELECT process_id, process_started, executable FROM database_owner WHERE id = 1",
            [],
            |row| {
                Ok(Identity {
                    pid: row.get(0)?,
                    started: row.get(1)?,
                    executable: row.get(2)?,
                })
            },
        )
        .optional()?;
    if let Some(owner) = &owner {
        let verified = inspect(owner.pid).is_some_and(|live| {
            live.started == owner.started && live.executable == owner.executable
        });
        tracing::info!(
            pid = owner.pid,
            verified,
            "Database is owned by another instance; OS lock is held"
        );
    }
    Ok(owner.map(|owner| owner.pid))
}

/// Called only while holding the OS lock; the short SQL transaction publishes
/// the owner atomically. The token protects cleanup from deleting another lease.
pub(super) fn claim(connection: &mut Connection) -> Result<String> {
    let tx = connection.transaction_with_behavior(TransactionBehavior::Immediate)?;
    let previous = tx
        .query_row(
            "SELECT process_id, process_started, executable FROM database_owner WHERE id = 1",
            [],
            |row| {
                Ok(Identity {
                    pid: row.get(0)?,
                    started: row.get(1)?,
                    executable: row.get(2)?,
                })
            },
        )
        .optional()?;
    if let Some(previous) = previous {
        let same_process = inspect(previous.pid).is_some_and(|live| {
            live.started == previous.started && live.executable == previous.executable
        });
        // Even a still-running PID may have relinquished storage, or been reused.
        // An actual owner would still hold the OS lock, so taking it is decisive.
        tracing::info!(
            pid = previous.pid,
            same_process,
            "Recovering released database ownership record"
        );
    }
    let pid = std::process::id();
    let current = inspect(pid);
    let executable = match &current {
        Some(identity) => identity.executable.clone(),
        None => std::env::current_exe()
            .context("Cannot identify database owner executable")?
            .to_string_lossy()
            .into_owned(),
    };
    let token: String = tx.query_row("SELECT lower(hex(randomblob(16)))", [], |row| row.get(0))?;
    tx.execute(
        "INSERT INTO database_owner(id, process_id, process_started, executable, instance_token)
         VALUES(1, ?1, ?2, ?3, ?4) ON CONFLICT(id) DO UPDATE SET
         process_id = excluded.process_id, process_started = excluded.process_started,
         executable = excluded.executable, instance_token = excluded.instance_token",
        params![pid, current.map_or(0, |p| p.started), executable, token],
    )?;
    tx.commit()?;
    Ok(token)
}
