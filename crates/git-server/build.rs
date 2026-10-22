fn main() {
    println!(
        "cargo:rustc-env=GIT_SERVER_TARGET={}",
        std::env::var("TARGET").unwrap()
    );
}
