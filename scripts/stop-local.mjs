import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(scriptDir, "..");
const pidFile = path.join(root, "work", "runtime-pids.json");
const args = new Set(process.argv.slice(2));
const pids = new Set();

if (fs.existsSync(pidFile)) {
  for (const processInfo of JSON.parse(fs.readFileSync(pidFile, "utf8"))) {
    if (Number.isInteger(processInfo.pid) && processInfo.pid > 0) {
      pids.add(String(processInfo.pid));
    }
  }
}

if (process.platform === "win32") {
  const ports = new Set(["3000", "5100", "5101", "5102", "5103", "5104", "5105", "5106"]);
  const output = execFileSync("netstat", ["-ano"], { encoding: "utf8" });

  for (const line of output.split(/\r?\n/)) {
    if (!line.includes("LISTENING")) {
      continue;
    }

    const parts = line.trim().split(/\s+/);
    const localAddress = parts[1] ?? "";
    const pid = parts[parts.length - 1];
    const port = localAddress.split(":").pop();

    if (port && ports.has(port) && pid && /^\d+$/.test(pid)) {
      pids.add(pid);
    }
  }
}

for (const pid of pids) {
  try {
    if (process.platform === "win32") {
      execFileSync("taskkill", ["/PID", pid, "/T", "/F"], { stdio: "ignore" });
    } else {
      process.kill(-Number(pid), "SIGTERM");
    }
    console.log(`stopped pid=${pid}`);
  } catch {
    console.log(`pid=${pid} was already stopped`);
  }
}

fs.rmSync(pidFile, { force: true });

if (pids.size === 0) {
  console.log("No local realtime-pix processes were listening on the configured ports.");
}

if (args.has("--with-postgres")) {
  execFileSync(
    "docker",
    ["compose", "-f", path.join(root, "infra", "docker-compose.yml"), "stop"],
    { cwd: root, stdio: "inherit" }
  );
}
