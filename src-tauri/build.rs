fn main() {
    let target = std::env::var("TARGET").unwrap();
    println!("cargo:rustc-env=PROJLAUNCH_TARGET={target}");
    println!(
        "cargo:rustc-env=PROJLAUNCH_EXE_SUFFIX={}",
        if target.contains("windows") {
            ".exe"
        } else {
            ""
        }
    );
    tauri_build::build()
}
