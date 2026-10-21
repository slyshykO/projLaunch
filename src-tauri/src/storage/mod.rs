mod database;
mod legacy_import;
mod migrations;

#[cfg(test)]
mod tests;

use std::{path::PathBuf, sync::Arc, thread};

use tokio::sync::{mpsc, oneshot};

use crate::project::ProjectData;
use crate::workspace::Workspace;
use database::Database;

type Reply<T> = oneshot::Sender<Result<T, String>>;

enum Request {
    List(Reply<Vec<String>>),
    Get(String, Reply<ProjectData>),
    Save(String, String, Reply<()>),
    LoadWorkspace(Reply<Option<Workspace>>),
    SaveWorkspace(Workspace, Reply<()>),
}

struct Worker {
    sender: Option<mpsc::Sender<Request>>,
    thread: Option<thread::JoinHandle<()>>,
}

impl Drop for Worker {
    fn drop(&mut self) {
        // Finish accepted operations and close SQLite before releasing the worker.
        self.sender.take();
        if let Some(thread) = self.thread.take() {
            let _ = thread.join();
        }
    }
}

#[derive(Clone)]
pub(crate) struct Storage {
    worker: Result<Arc<Worker>, String>,
}

impl Storage {
    pub(crate) fn start(app_data_dir: Result<PathBuf, String>) -> Self {
        let worker = app_data_dir.and_then(|directory| {
            let (sender, mut receiver) = mpsc::channel(32);
            let thread = thread::Builder::new()
                .name("project-storage".into())
                .spawn(move || {
                    // Initialization is queued ahead of all commands. Errors remain
                    // visible to the UI, with writes disabled until the next launch.
                    let mut database = Database::open(&directory).map_err(|error| {
                        tracing::error!(error = %format!("{error:#}"), "Project storage initialization failed");
                        format!(
                            "Project storage could not be initialized: {error:#}. \
                             Existing project files have not been changed. \
                             Fix the reported problem and restart ProjLaunch."
                        )
                    });
                    if database.is_ok() {
                        tracing::info!("Project storage ready");
                    }
                    while let Some(request) = receiver.blocking_recv() {
                        match request {
                            Request::LoadWorkspace(reply) => {
                                let _ = reply.send(run(&mut database, |db| db.load_workspace()));
                            }
                            Request::SaveWorkspace(workspace, reply) => {
                                let _ = reply.send(run(&mut database, |db| db.save_workspace(&workspace)));
                            }
                            Request::List(reply) => {
                                let _ = reply.send(run(&mut database, |db| db.list()));
                            }
                            Request::Get(id, reply) => {
                                let _ = reply.send(run(&mut database, |db| db.get(&id)));
                            }
                            Request::Save(id, content, reply) => {
                                let _ = reply.send(run(&mut database, |db| db.save(&id, &content)));
                            }
                        }
                    }
                })
                .map_err(|error| format!("Cannot start project storage: {error}"))?;
            Ok(Arc::new(Worker {
                sender: Some(sender),
                thread: Some(thread),
            }))
        });
        Self { worker }
    }

    async fn request<T>(&self, make: impl FnOnce(Reply<T>) -> Request) -> Result<T, String> {
        let worker = self.worker.as_ref().map_err(Clone::clone)?;
        let (reply, result) = oneshot::channel();
        worker
            .sender
            .as_ref()
            .expect("Live storage always has a sender")
            .send(make(reply))
            .await
            .map_err(|_| "Project storage worker stopped".to_owned())?;
        result
            .await
            .map_err(|_| "Project storage worker stopped before replying".to_owned())?
    }

    pub(crate) async fn list(&self) -> Result<Vec<String>, String> {
        self.request(Request::List).await
    }

    pub(crate) async fn load_workspace(&self) -> Result<Option<Workspace>, String> {
        self.request(Request::LoadWorkspace).await
    }

    pub(crate) async fn save_workspace(&self, workspace: Workspace) -> Result<(), String> {
        self.request(|reply| Request::SaveWorkspace(workspace, reply))
            .await
    }

    pub(crate) async fn get(&self, id: String) -> Result<ProjectData, String> {
        self.request(|reply| Request::Get(id, reply)).await
    }

    pub(crate) async fn save(&self, id: String, content: String) -> Result<(), String> {
        self.request(|reply| Request::Save(id, content, reply))
            .await
    }
}

fn run<T>(
    database: &mut Result<Database, String>,
    operation: impl FnOnce(&mut Database) -> anyhow::Result<T>,
) -> Result<T, String> {
    match database {
        Ok(database) => operation(database).map_err(|error| {
            tracing::error!(error = %format!("{error:#}"), "Project storage operation failed");
            format!("{error:#}")
        }),
        Err(error) => Err(error.clone()),
    }
}
