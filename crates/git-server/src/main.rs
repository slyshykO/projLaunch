#[tokio::main]
async fn main() -> anyhow::Result<()> {
    match std::env::args().nth(1).as_deref() {
        Some("--version-json") => {
            println!("{}", serde_json::to_string(&git_server::server_info())?);
            Ok(())
        }
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
        _ => anyhow::bail!("usage: project-launch-git-server stdio"),
    }
}
