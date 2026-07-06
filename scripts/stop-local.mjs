import { execFileSync } from "node:child_process";

const ports = new Set(["3000", "5100", "5101", "5102", "5103", "5104", "5105"]);
const output = execFileSync("netstat", ["-ano"], { encoding: "utf8" });
const pids = new Set();

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

for (const pid of pids) {
  try {
    execFileSync("taskkill", ["/PID", pid, "/F"], { stdio: "ignore" });
    console.log(`stopped pid=${pid}`);
  } catch {
    console.log(`pid=${pid} was already stopped`);
  }
}

if (pids.size === 0) {
  console.log("No local realtime-pix processes were listening on the configured ports.");
}

