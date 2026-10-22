pub fn server_info() -> git_protocol::ServerInfo {
    git_protocol::ServerInfo {
        server_version: env!("CARGO_PKG_VERSION").into(),
        protocol_version: git_protocol::PROTOCOL_VERSION,
        target: env!("GIT_SERVER_TARGET").into(),
    }
}

pub mod api;
pub mod git;
#[cfg(feature = "network")]
pub mod network;
pub mod session;
pub mod transport;
