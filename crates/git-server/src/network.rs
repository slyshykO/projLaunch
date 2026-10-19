use crate::{
    api::{GitService, git_api},
    session, transport,
};
use axum::{
    Router,
    extract::{DefaultBodyLimit, State, WebSocketUpgrade, ws::Message},
    http::{StatusCode, header},
    response::{IntoResponse, Response},
    routing::get,
};
use futures_util::{SinkExt, StreamExt};
use session::{MAX_FRAME, Session};
use std::{net::SocketAddr, sync::Arc};
use tokio::{net::TcpListener, task::JoinHandle};
use tokio_util::sync::CancellationToken;

#[derive(Clone)]
struct App {
    service: Arc<GitService>,
    http: Arc<json_rpc::JsonRpc>,
    stop: CancellationToken,
}
pub struct Server {
    stop: CancellationToken,
    task: JoinHandle<std::io::Result<()>>,
}
impl Server {
    pub fn stop(&self) -> anyhow::Result<()> {
        self.stop.cancel();
        Ok(())
    }
    pub async fn stopped(self) {
        self.task.await.unwrap().unwrap();
    }
}
pub async fn network(service: Arc<GitService>) -> anyhow::Result<(SocketAddr, Server)> {
    let stop = CancellationToken::new();
    // HTTP is a single demo principal; production must resolve an authenticated scope.
    let http = Arc::new(git_api(Session::new(service.clone(), None)));
    let app = App {
        service,
        http,
        stop: stop.clone(),
    };
    let router = Router::new()
        .route("/", get(ws).post(http_call))
        .layer(DefaultBodyLimit::max(MAX_FRAME))
        .with_state(app);
    let listener = TcpListener::bind("127.0.0.1:0").await?;
    let addr = listener.local_addr()?;
    let shutdown = stop.clone();
    let task = tokio::spawn(async move {
        axum::serve(listener, router)
            .with_graceful_shutdown(shutdown.cancelled_owned())
            .await
    });
    Ok((addr, Server { stop, task }))
}
async fn http_call(State(app): State<App>, body: String) -> Response {
    match session::call(&app.http, &body).await {
        Some(response) if response.len() <= MAX_FRAME => {
            ([(header::CONTENT_TYPE, "application/json")], response).into_response()
        }
        Some(_) => StatusCode::INTERNAL_SERVER_ERROR.into_response(),
        None => StatusCode::NO_CONTENT.into_response(),
    }
}
async fn ws(State(app): State<App>, upgrade: WebSocketUpgrade) -> Response {
    upgrade
        .max_message_size(MAX_FRAME)
        .max_frame_size(MAX_FRAME)
        .on_upgrade(move |socket| async move {
            let (output, input) = socket.split();
            let input = input
                .take_while(|message| std::future::ready(!matches!(message, Ok(Message::Close(_)))))
                .filter_map(|message| {
                    std::future::ready(match message {
                        Ok(Message::Text(text)) => Some(Ok(text.to_string())),
                        Ok(Message::Binary(_)) => {
                            Some(Err(std::io::Error::other("text frames required")))
                        }
                        Ok(_) => None,
                        Err(e) => Some(Err(std::io::Error::other(e))),
                    })
                });
            let output = output.with(|text: String| {
                std::future::ready(Ok::<_, axum::Error>(Message::Text(text.into())))
            });
            if let Err(error) = transport::serve(input, output, app.service, app.stop).await {
                eprintln!("WebSocket closed: {error}");
            }
        })
}
