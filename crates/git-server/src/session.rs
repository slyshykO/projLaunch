//! Session scoping and RPC profile carried over from jsonrpc-axum-probe.
use crate::{
    api::GitService,
    git::{Cursor, Repo},
};
use anyhow::{Context, Result, ensure};
pub use git_protocol::MAX_FRAME;
use json_rpc::JsonRpc;
use serde_json::{Value, json};
use std::{
    collections::HashMap,
    sync::{Arc, Mutex},
    time::Duration,
};
use tokio::sync::mpsc;
use tokio_util::sync::CancellationToken;
pub const MAX_IN_FLIGHT: usize = 32;

pub struct Session {
    pub service: Arc<GitService>,
    pub closed: CancellationToken,
    outgoing: Option<mpsc::Sender<String>>,
    pub repositories: Mutex<HashMap<String, Arc<Repo>>>,
    pub cursors: Mutex<HashMap<String, Cursor>>,
    operations: Mutex<HashMap<String, CancellationToken>>,
}

pub struct Operation {
    session: Arc<Session>,
    id: String,
    pub cancel: CancellationToken,
}
impl Drop for Operation {
    fn drop(&mut self) {
        self.session.operations.lock().unwrap().remove(&self.id);
    }
}

impl Session {
    pub fn new(service: Arc<GitService>, outgoing: Option<mpsc::Sender<String>>) -> Arc<Self> {
        Arc::new(Self {
            service,
            outgoing,
            closed: CancellationToken::new(),
            repositories: Mutex::default(),
            cursors: Mutex::default(),
            operations: Mutex::default(),
        })
    }
    pub fn operation(self: &Arc<Self>, id: &str) -> Result<Operation> {
        ensure!(
            !id.is_empty() && id.len() <= 128,
            "operationId must contain 1–128 bytes"
        );
        let mut operations = self.operations.lock().unwrap();
        ensure!(!operations.contains_key(id), "Duplicate operationId");
        let cancel = self.closed.child_token();
        operations.insert(id.into(), cancel.clone());
        Ok(Operation {
            session: self.clone(),
            id: id.into(),
            cancel,
        })
    }
    pub fn cancel(&self, id: &str) -> bool {
        if let Some(token) = self.operations.lock().unwrap().get(id) {
            token.cancel();
            true
        } else {
            false
        }
    }
    pub fn repo(&self, id: &str) -> Result<Arc<Repo>> {
        self.repositories
            .lock()
            .unwrap()
            .get(id)
            .cloned()
            .context("Unknown repoId; reopen the repository after reconnect")
    }
    pub fn cursor(&self, id: &str, repo_id: &str) -> Result<Cursor> {
        let mut cursors = self.cursors.lock().unwrap();
        cursors.retain(|_, cursor| cursor.history.created.elapsed() < Duration::from_secs(300));
        let cursor = cursors
            .get(id)
            .context("History cursor expired; refresh history")?;
        ensure!(
            cursor.history.repo_id == repo_id,
            "History cursor belongs to a different repository"
        );
        Ok(cursor.clone())
    }
    pub fn save_cursor(&self, cursor: Cursor) -> String {
        let mut cursors = self.cursors.lock().unwrap();
        if cursors.len() >= 128
            && let Some(oldest) = cursors
                .iter()
                .min_by_key(|(_, c)| c.history.created)
                .map(|(k, _)| k.clone())
        {
            cursors.remove(&oldest);
        }
        let id = uuid::Uuid::new_v4().to_string();
        cursors.insert(id.clone(), cursor);
        id
    }
    pub async fn dispatch(self: &Arc<Self>, rpc: &JsonRpc, line: &str) -> Result<()> {
        if let Some(response) = call(rpc, line).await {
            let response = if response.len() > MAX_FRAME {
                let id = serde_json::from_str::<Value>(line)?["id"].clone();
                json!({"jsonrpc":"2.0","id":id,"error":{"code":-32004,"message":"Response too large; narrow the request"}}).to_string()
            } else {
                response
            };
            tokio::select! {
                _ = self.closed.cancelled() => {},
                result = self.outgoing.as_ref().context("Missing transport")?.send(response) => { result?; },
            }
        }
        Ok(())
    }
    pub async fn shutdown(&self) {
        self.closed.cancel();
        self.cursors.lock().unwrap().clear();
        self.repositories.lock().unwrap().clear();
    }
}

/// The prototype's deliberate profile: no batches or notification side effects.
pub async fn call(rpc: &JsonRpc, line: &str) -> Option<String> {
    if let Ok(value) = serde_json::from_str::<Value>(line) {
        if value.is_array() {
            return Some(json!({"jsonrpc":"2.0","id":null,"error":{"code":-32005,"message":"Batches not supported"}}).to_string());
        }
        if value.get("id").is_none()
            && matches!(
                json_rpc::Message::from_json(value),
                Ok(json_rpc::Message::Notification(_))
            )
        {
            return None;
        }
    }
    rpc.call(line).await
}
