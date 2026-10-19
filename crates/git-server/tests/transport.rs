//! Regression coverage for the NDJSON pump carried over from the prototype.
use futures_util::{SinkExt, StreamExt};
use git_protocol::MAX_FRAME;
use git_server::transport;
use serde_json::{Value, json};
use std::time::Duration;
use tokio::{
    io::{DuplexStream, ReadHalf, WriteHalf},
    task::JoinHandle,
    time::timeout,
};
use tokio_util::codec::{FramedRead, FramedWrite, LinesCodec};

struct Peer {
    input: FramedRead<ReadHalf<DuplexStream>, LinesCodec>,
    output: FramedWrite<WriteHalf<DuplexStream>, LinesCodec>,
    task: JoinHandle<anyhow::Result<()>>,
}
impl Peer {
    fn new() -> Self {
        let (client, server) = tokio::io::duplex(64 * 1024);
        let (read, write) = tokio::io::split(server);
        let task = tokio::spawn(transport::stdio(read, write, Default::default()));
        let (read, write) = tokio::io::split(client);
        Self {
            input: FramedRead::new(read, LinesCodec::new_with_max_length(MAX_FRAME)),
            output: FramedWrite::new(write, LinesCodec::new()),
            task,
        }
    }
    async fn send(&mut self, value: Value) {
        self.output.send(value.to_string()).await.unwrap();
    }
    async fn next(&mut self) -> Value {
        serde_json::from_str(
            &timeout(Duration::from_secs(5), self.input.next())
                .await
                .unwrap()
                .unwrap()
                .unwrap(),
        )
        .unwrap()
    }
    async fn close(mut self) {
        SinkExt::<String>::close(&mut self.output).await.unwrap();
        timeout(Duration::from_secs(5), self.task)
            .await
            .unwrap()
            .unwrap()
            .unwrap();
    }
}

#[tokio::test]
async fn rpc_errors_ids_batches_and_notifications() {
    let mut peer = Peer::new();
    peer.output.send("{".to_owned()).await.unwrap();
    assert_eq!(peer.next().await["error"]["code"], -32700);
    peer.send(
        json!({"jsonrpc":"2.0","id":"wrong params","method":"git.status","params":{"repoId":123}}),
    )
    .await;
    assert_eq!(peer.next().await["error"]["code"], -32602);
    peer.send(json!({"jsonrpc":"2.0","id":null,"method":"missing","params":{}}))
        .await;
    let unknown = peer.next().await;
    assert!(unknown["id"].is_null());
    assert_eq!(unknown["error"]["code"], -32601);
    peer.send(json!([])).await;
    assert_eq!(peer.next().await["error"]["code"], -32005);
    peer.send(json!({"jsonrpc":"1.0","id":2,"method":"server.hello","params":{}}))
        .await;
    assert_eq!(peer.next().await["error"]["code"], -32600);
    peer.send(json!({"jsonrpc":"2.0","method":"git.cancel","params":{"operationId":"ignored notification"}})).await;
    peer.send(json!({"jsonrpc":"2.0","id":"next","method":"git.cancel","params":{"operationId":"unknown"}})).await;
    let next = peer.next().await;
    assert_eq!(next["id"], "next");
    assert_eq!(next["result"], false);
    peer.close().await;
}

#[tokio::test]
async fn concurrent_responses_keep_their_request_ids() {
    let mut peer = Peer::new();
    for id in 0..16 {
        peer.send(json!({"jsonrpc":"2.0","id":id,"method":"server.hello","params":{}}))
            .await;
    }
    let mut ids = std::collections::BTreeSet::new();
    for _ in 0..16 {
        let value = peer.next().await;
        assert_eq!(value["result"]["protocolVersion"], 1);
        assert!(ids.insert(value["id"].as_u64().unwrap()));
    }
    assert_eq!(ids.len(), 16);
    peer.close().await;
}

#[tokio::test]
async fn oversized_frame_closes_connection() {
    let mut peer = Peer::new();
    let _ = peer.output.send("x".repeat(MAX_FRAME + 1)).await;
    assert!(
        timeout(Duration::from_secs(5), peer.task)
            .await
            .unwrap()
            .unwrap()
            .is_err()
    );
}

#[tokio::test]
async fn eof_during_requests_shuts_down_cleanly() {
    let mut peer = Peer::new();
    for id in 0..16 {
        peer.send(json!({"jsonrpc":"2.0","id":id,"method":"server.hello","params":{}}))
            .await;
    }
    peer.close().await;
}
