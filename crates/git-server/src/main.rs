#[tokio::main]
async fn main() -> anyhow::Result<()> {
    match std::env::args().nth(1).as_deref() {
        Some("stdio") => {
            git_server::transport::stdio(
                tokio::io::stdin(),
                tokio::io::stdout(),
                Default::default(),
            )
            .await
        }
        #[cfg(feature = "network")]
        Some("network") => {
            let (addr, server) = git_server::network::network(Default::default()).await?;
            eprintln!("Development HTTP/WS listening on {addr}");
            tokio::signal::ctrl_c().await?;
            server.stop()?;
            server.stopped().await;
            Ok(())
        }
        _ => anyhow::bail!("usage: git-server stdio"),
    }
}
