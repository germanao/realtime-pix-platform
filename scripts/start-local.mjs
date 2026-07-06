import { spawn } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(scriptDir, "..");
const args = new Set(process.argv.slice(2));
const logDir = path.join(root, "work", "runtime-logs");
const eventBusDir = path.join(root, "work", "local-bus");
const dotnet = process.env.DOTNET_EXE ?? "C:\\Program Files\\dotnet\\dotnet.exe";

fs.mkdirSync(logDir, { recursive: true });

function start(name, command, commandArgs, cwd = root, options = {}) {
  const stdout = fs.openSync(path.join(logDir, `${name}.out.log`), "a");
  const stderr = fs.openSync(path.join(logDir, `${name}.err.log`), "a");
  const child = spawn(command, commandArgs, {
    cwd,
    detached: true,
    env: {
      ...process.env,
      EventBus__Directory: eventBusDir
    },
    stdio: ["ignore", stdout, stderr],
    windowsHide: true,
    shell: options.shell ?? false
  });

  child.unref();
  console.log(`${name} pid=${child.pid}`);
}

if (!args.has("--skip-infra") && !args.has("--frontend-only")) {
  start("docker-compose", "docker", ["compose", "-f", path.join(root, "infra", "docker-compose.yml"), "up", "-d"]);
}

const services = [
  ["identity-presence-service", "services/identity-presence-service/IdentityPresenceService.csproj"],
  ["wallet-ledger-service", "services/wallet-ledger-service/WalletLedgerService.csproj"],
  ["transaction-service", "services/transaction-service/TransactionService.csproj"],
  ["realtime-events-service", "services/realtime-events-service/RealtimeEventsService.csproj"],
  ["bot-service", "services/bot-service/BotService.csproj"],
  ["api-gateway", "services/api-gateway/ApiGateway.csproj"]
];

if (!args.has("--frontend-only")) {
  for (const [name, project] of services) {
    start(name, dotnet, ["run", "--project", path.join(root, project), "--configuration", "Release", "--no-build"]);
  }

  console.log("Backend services are starting. API Gateway: http://localhost:5100");
}

if (args.has("--frontend") || args.has("--frontend-only")) {
  const webRoot = path.join(root, "apps", "web");
  start("web", process.execPath, [path.join(webRoot, "node_modules", "next", "dist", "bin", "next"), "dev", "--port", "3000"], webRoot);
  console.log("Frontend is starting. Web: http://localhost:3000");
}
