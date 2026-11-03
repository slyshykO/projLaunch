// Runs the platform build entry point (build.bat on Windows, build.sh elsewhere).
import { spawnSync } from "node:child_process";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const windows = process.platform === "win32";
const script = join(root, windows ? "build.bat" : "build.sh");
const result = spawnSync(script, process.argv.slice(2), {
    cwd: root,
    stdio: "inherit",
    shell: windows,
});
if (result.error) {
    console.error(result.error.message);
    process.exit(1);
}
process.exit(result.status ?? 1);
