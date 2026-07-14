import { spawn, spawnSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(scriptDir, "..");
const args = new Set(process.argv.slice(2));
const logDir = path.join(root, "work", "runtime-logs");
const eventBusDir = path.join(root, "work", "local-bus");
const pidFile = path.join(root, "work", "runtime-pids.json");
const dotnet = process.env.DOTNET_EXE ?? "dotnet";
const launchedProcesses = [];

fs.mkdirSync(logDir, { recursive: true });

function start(name, command, commandArgs, cwd = root, options = {}) {
  const stdout = fs.openSync(path.join(logDir, `${name}.out.log`), "a");
  const stderr = fs.openSync(path.join(logDir, `${name}.err.log`), "a");
  const child = spawn(command, commandArgs, {
    cwd,
    detached: true,
    env: {
      ...process.env,
      EventBus__Directory: eventBusDir,
      ...(options.env ?? {})
    },
    stdio: ["ignore", stdout, stderr],
    windowsHide: true,
    shell: options.shell ?? false
  });

  fs.closeSync(stdout);
  fs.closeSync(stderr);
  child.unref();
  launchedProcesses.push({ name, pid: child.pid });
  fs.writeFileSync(pidFile, `${JSON.stringify(launchedProcesses, null, 2)}\n`);
  console.log(`${name} pid=${child.pid}`);
}

function postgresConnection(port, database, username, password) {
  return [
    "Host=localhost",
    `Port=${port}`,
    `Database=${database}`,
    `Username=${username}`,
    ["Password", password].join("=")
  ].join(";");
}

const postgresConnections = {
  identity: postgresConnection(5433, "identity_presence_db", "identity_app", "identity_dev_password"),
  bankA: postgresConnection(5434, "bank_a_ledger_db", "bank_a_app", "bank_a_dev_password"),
  bankB: postgresConnection(5435, "bank_b_ledger_db", "bank_b_app", "bank_b_dev_password"),
  transaction: postgresConnection(5436, "transaction_db", "transaction_app", "transaction_dev_password"),
  realtime: postgresConnection(5437, "realtime_projection_db", "realtime_app", "realtime_dev_password")
};

function run(command, commandArgs, options = {}) {
  const result = spawnSync(command, commandArgs, {
    cwd: root,
    env: { ...process.env, ...(options.env ?? {}) },
    encoding: "utf8",
    shell: false,
    stdio: "inherit"
  });
  if (result.status !== 0) {
    throw new Error(`${command} ${commandArgs.join(" ")} failed with exit code ${result.status}.`);
  }
}

function migrate(project, connectionString, environmentVariable) {
  run(
    dotnet,
    [
      "tool", "run", "dotnet-ef", "database", "update",
      "--project", path.join(root, project),
      "--configuration", "Release",
      "--no-build",
      "--connection", connectionString
    ],
    { env: { [environmentVariable]: connectionString } }
  );
}

if (args.has("--with-postgres") && !args.has("--frontend-only")) {
  run("docker", ["compose", "-f", path.join(root, "infra", "docker-compose.yml"), "up", "-d", "--wait"]);
  run(dotnet, ["tool", "restore"]);
  migrate("services/identity-presence-service/IdentityPresence.Infrastructure/IdentityPresence.Infrastructure.csproj", postgresConnections.identity, "IDENTITY_PRESENCE_MIGRATIONS_CONNECTION");
  migrate("services/bank-ledger-service/BankLedger.Infrastructure/BankLedger.Infrastructure.csproj", postgresConnections.bankA, "BANK_LEDGER_MIGRATIONS_CONNECTION");
  migrate("services/bank-ledger-service/BankLedger.Infrastructure/BankLedger.Infrastructure.csproj", postgresConnections.bankB, "BANK_LEDGER_MIGRATIONS_CONNECTION");
  migrate("services/transaction-service/Transaction.Infrastructure/Transaction.Infrastructure.csproj", postgresConnections.transaction, "TRANSACTION_MIGRATIONS_CONNECTION");
  migrate("services/realtime-events-service/RealtimeEvents.Infrastructure/RealtimeEvents.Infrastructure.csproj", postgresConnections.realtime, "REALTIME_PROJECTION_MIGRATIONS_CONNECTION");
}

const services = [
  ["identity-presence-service", "services/identity-presence-service/IdentityPresence.Api/IdentityPresence.Api.csproj", 5101,
    args.has("--with-postgres") ? { ConnectionStrings__Default: postgresConnections.identity } : {}],
  ["bank-a-ledger-service", "services/bank-ledger-service/BankLedger.Api/BankLedger.Api.csproj", 5102, {
    Bank__Id: "bank-a",
    Bank__Name: "Bank A",
    Bank__WelcomeBalance: "10000",
    EventBus__QueueName: "bank-a-commands",
    EventBus__ServiceBus__QueueName: "bank-a-commands",
    ...(args.has("--with-postgres") ? { ConnectionStrings__Default: postgresConnections.bankA } : {})
  }],
  ["bank-b-ledger-service", "services/bank-ledger-service/BankLedger.Api/BankLedger.Api.csproj", 5106, {
    Bank__Id: "bank-b",
    Bank__Name: "Bank B",
    Bank__WelcomeBalance: "0",
    EventBus__QueueName: "bank-b-commands",
    EventBus__ServiceBus__QueueName: "bank-b-commands",
    ...(args.has("--with-postgres") ? { ConnectionStrings__Default: postgresConnections.bankB } : {})
  }],
  ["transaction-service", "services/transaction-service/Transaction.Api/Transaction.Api.csproj", 5103,
    args.has("--with-postgres") ? { ConnectionStrings__Default: postgresConnections.transaction } : {}],
  ["realtime-events-service", "services/realtime-events-service/RealtimeEvents.Api/RealtimeEvents.Api.csproj", 5104,
    args.has("--with-postgres") ? { ConnectionStrings__Default: postgresConnections.realtime } : {}],
  ["bot-service", "services/bot-service/Bot.Worker/Bot.Worker.csproj", 5105],
  ["api-gateway", "services/api-gateway/ApiGateway.Api/ApiGateway.Api.csproj", 5100]
];

if (!args.has("--frontend-only")) {
  for (const [name, project, port, serviceEnv = {}] of services) {
    start(name, dotnet, ["run", "--project", path.join(root, project), "--configuration", "Release", "--no-build"], root, {
      env: {
        ASPNETCORE_URLS: `http://localhost:${port}`,
        ...serviceEnv
      }
    });
  }

  console.log("Backend services are starting. API Gateway: http://localhost:5100");
}

if (args.has("--frontend") || args.has("--frontend-only")) {
  const webRoot = path.join(root, "apps", "web");
  start("web", process.execPath, [path.join(webRoot, "node_modules", "next", "dist", "bin", "next"), "dev", "--port", "3000"], webRoot);
  console.log("Frontend is starting. Web: http://localhost:3000");
}
