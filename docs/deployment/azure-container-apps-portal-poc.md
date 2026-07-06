# Azure Container Apps Portal Proof of Concept

This guide creates the Real-Time PIX backend manually through the Azure Portal.
It is designed for learning Azure Container Apps and validating the hosting
model before introducing Terraform and CI/CD.

Last verified against Microsoft documentation: June 18, 2026.

## 1. Scope and Expected Result

You will create:

- One Azure resource group.
- One Azure Container Apps environment using the Consumption workload profile.
- Six independently deployed Container Apps.
- External HTTPS endpoints for the API gateway and the two current SignalR
  services.
- Internal-only HTTP endpoints for wallet and transaction services.
- One worker app with ingress disabled for the bots.
- Health probes, scale limits, environment variables, logs, and a cost budget.

At the end of the hosting proof of concept:

- All six `/health` endpoints can run successfully.
- The API gateway can forward HTTP requests to internal services.
- Browser clients can reach the two SignalR hubs.
- Container Apps revisions, logs, scale-to-zero, and service discovery are
  visible in the Portal.

The current repository will **not** complete the distributed PIX event workflow
because its JSONL event bus is local to a filesystem. See
[Current limitations](#2-current-repository-limitations).

## 2. Current Repository Limitations

Read this section before provisioning anything.

### 2.1 Local event bus

The services call `AddRealtimePixFileEventBus` and write to:

```text
work/local-bus/events.jsonl
```

Separate Container Apps do not share this file. A transfer accepted by
`transaction-service` will remain in `requested` state because
`wallet-ledger-service` cannot read the event.

Do not solve this by mounting one Azure Files share into every service. The
current named mutex only coordinates processes on one operating system host; it
does not provide a distributed lock across replicas.

The correct solution is a network event backbone such as Azure Event Grid with
service webhook consumers, or a broker adapter designed for the service
contract.

### 2.2 In-memory state

Wallets, transfers, presence, and realtime projections currently live in
process memory. Any restart, new revision, or scale-to-zero cycle clears them.

For this reason:

- Set every service to a maximum of one replica.
- Treat all data as disposable.
- Do not use this deployment as evidence that PostgreSQL persistence works.

### 2.3 Direct browser SignalR endpoints

The frontend currently connects directly to:

- `identity-presence-service/presence/hub`
- `realtime-events-service/events/hub`

Those two services require external ingress for the current frontend. A later
version should use Azure SignalR Service for managed scale-out.

### 2.4 No Dockerfiles

The repository currently has no production Dockerfiles. Complete the image
preparation section before opening the Azure Container Apps creation wizard.

## 3. Naming Worksheet

Azure names must be lowercase where indicated. Replace `germano` with a short,
unique identifier if these example names are already used.

| Purpose | Suggested value |
| --- | --- |
| Subscription | Your Azure subscription |
| Region | `Brazil South`, if all selected services are available; otherwise `East US` |
| Resource group | `rg-realtime-pix-poc` |
| Container Apps environment | `cae-realtime-pix-poc` |
| API gateway | `pix-api-gateway` |
| Identity/presence | `pix-identity-presence` |
| Wallet/ledger | `pix-wallet-ledger` |
| Transaction | `pix-transaction` |
| Realtime events | `pix-realtime-events` |
| Bot worker | `pix-bot` |
| GHCR owner | Your lowercase GitHub user or organization |
| Image tag | `poc-1` |

Keep all Azure resources in the same region for this proof of concept.

## 4. Prerequisites

### 4.1 Accounts and permissions

You need:

- An active Azure subscription.
- Permission to create resources and role assignments in the selected
  subscription or resource group.
- A GitHub account.
- A GitHub repository containing this monorepo.

### 4.2 Local tools

Install and verify:

```powershell
dotnet --version
docker version
git --version
```

The .NET command should report a .NET 10 SDK. Docker Desktop must be running.

Azure CLI is optional for the Portal deployment, but useful for verification:

```powershell
az version
```

### 4.3 Validate the repository

From the repository root:

```powershell
dotnet build RealtimePixPlatform.slnx --configuration Release
dotnet test RealtimePixPlatform.slnx --configuration Release
```

Do not publish images from a failing build.

## 5. Create Production Dockerfiles

Every image must be built with the monorepo root as its Docker build context
because services reference projects under `contracts` and `building-blocks`.

The following is the pattern for
`services/api-gateway/Dockerfile`:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY building-blocks/dotnet/RealtimePix.Eventing/RealtimePix.Eventing.csproj building-blocks/dotnet/RealtimePix.Eventing/
COPY services/api-gateway/ApiGateway.csproj services/api-gateway/

RUN dotnet restore services/api-gateway/ApiGateway.csproj

COPY . .

RUN dotnet publish services/api-gateway/ApiGateway.csproj \
    --configuration Release \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_ENVIRONMENT=Production

EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "ApiGateway.dll"]
```

For the other five services, include both shared project files before restore:

```dockerfile
COPY contracts/dotnet/RealtimePix.Contracts/RealtimePix.Contracts.csproj contracts/dotnet/RealtimePix.Contracts/
COPY building-blocks/dotnet/RealtimePix.Eventing/RealtimePix.Eventing.csproj building-blocks/dotnet/RealtimePix.Eventing/
```

Use this service mapping:

| Dockerfile | Project passed to restore/publish | Runtime DLL |
| --- | --- | --- |
| `services/api-gateway/Dockerfile` | `services/api-gateway/ApiGateway.csproj` | `ApiGateway.dll` |
| `services/identity-presence-service/Dockerfile` | `services/identity-presence-service/IdentityPresenceService.csproj` | `IdentityPresenceService.dll` |
| `services/wallet-ledger-service/Dockerfile` | `services/wallet-ledger-service/WalletLedgerService.csproj` | `WalletLedgerService.dll` |
| `services/transaction-service/Dockerfile` | `services/transaction-service/TransactionService.csproj` | `TransactionService.dll` |
| `services/realtime-events-service/Dockerfile` | `services/realtime-events-service/RealtimeEventsService.csproj` | `RealtimeEventsService.dll` |
| `services/bot-service/Dockerfile` | `services/bot-service/BotService.csproj` | `BotService.dll` |

Add a repository-root `.dockerignore`:

```text
**/bin
**/obj
**/node_modules
**/.next
.git
work
*.log
```

### 5.1 Build all images locally

Set your lowercase GHCR owner:

```powershell
$GhcrOwner = "<github-user-or-organization>"
$Tag = "poc-1"
```

Build each image from the repository root:

```powershell
docker build -f services/api-gateway/Dockerfile `
  -t "ghcr.io/$GhcrOwner/pix-api-gateway:$Tag" .

docker build -f services/identity-presence-service/Dockerfile `
  -t "ghcr.io/$GhcrOwner/pix-identity-presence:$Tag" .

docker build -f services/wallet-ledger-service/Dockerfile `
  -t "ghcr.io/$GhcrOwner/pix-wallet-ledger:$Tag" .

docker build -f services/transaction-service/Dockerfile `
  -t "ghcr.io/$GhcrOwner/pix-transaction:$Tag" .

docker build -f services/realtime-events-service/Dockerfile `
  -t "ghcr.io/$GhcrOwner/pix-realtime-events:$Tag" .

docker build -f services/bot-service/Dockerfile `
  -t "ghcr.io/$GhcrOwner/pix-bot:$Tag" .
```

### 5.2 Test images locally

Test at least one image before publishing:

```powershell
docker run --rm -p 8080:8080 `
  "ghcr.io/$GhcrOwner/pix-api-gateway:$Tag"
```

In another terminal:

```powershell
Invoke-RestMethod http://localhost:8080/health
```

Expected response:

```json
{
  "service": "api-gateway",
  "status": "ok"
}
```

Stop the container with `Ctrl+C`.

## 6. Publish Images to GitHub Container Registry

### 6.1 Create a GitHub token for the manual push

In GitHub:

1. Open your profile menu.
2. Select **Settings**.
3. Open **Developer settings**.
4. Open the personal access token section.
5. Create a token permitted to write packages.
6. Store the token in a password manager.

Do not commit the token or place it in a `.env` file.

### 6.2 Sign in to GHCR

```powershell
$GitHubToken = Read-Host "GitHub package token" -AsSecureString
$TokenPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($GitHubToken)
$PlainToken = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($TokenPointer)
$PlainToken | docker login ghcr.io -u $GhcrOwner --password-stdin
[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($TokenPointer)
Remove-Variable PlainToken
```

### 6.3 Push the images

```powershell
docker push "ghcr.io/$GhcrOwner/pix-api-gateway:$Tag"
docker push "ghcr.io/$GhcrOwner/pix-identity-presence:$Tag"
docker push "ghcr.io/$GhcrOwner/pix-wallet-ledger:$Tag"
docker push "ghcr.io/$GhcrOwner/pix-transaction:$Tag"
docker push "ghcr.io/$GhcrOwner/pix-realtime-events:$Tag"
docker push "ghcr.io/$GhcrOwner/pix-bot:$Tag"
```

### 6.4 Make the packages public

For each package in GitHub:

1. Open your GitHub profile or organization.
2. Open **Packages**.
3. Select the package.
4. Open **Package settings**.
5. Find **Danger Zone**.
6. Change package visibility to **Public**.
7. Confirm the package name.

Public packages avoid storing GHCR credentials in each Container App. This is
appropriate for an educational public repository. Keep packages private if the
source or image must not be public, and configure registry credentials as
Container App secrets instead.

## 7. Create a Cost Budget Before Resources

Azure budgets alert you about spending but do not stop resources.

1. Sign in to [Azure Portal](https://portal.azure.com/).
2. Search for **Subscriptions**.
3. Open the subscription you will use.
4. Select **Budgets** under Cost Management.
5. Select **Add**.
6. Name it `realtime-pix-poc-monthly`.
7. Choose a monthly reset period.
8. Enter a small amount you are comfortable spending, such as USD 5.
9. Add actual-cost alerts at 50%, 80%, and 100%.
10. Add your email address.
11. Add a forecasted alert if available.
12. Create the budget.

Cost data can be delayed. A budget is not a real-time circuit breaker.

## 8. Create the Resource Group

1. Search for **Resource groups**.
2. Select **Create**.
3. Select the correct subscription.
4. Enter `rg-realtime-pix-poc`.
5. Select the chosen region.
6. Open **Tags** and add:

| Name | Value |
| --- | --- |
| `application` | `realtime-pix` |
| `environment` | `poc` |
| `owner` | Your name or GitHub handle |
| `cost-purpose` | `learning` |

7. Select **Review + create**.
8. Select **Create**.

## 9. Create the Container Apps Environment

1. Search for **Container Apps Environments**.
2. Select **Create**.
3. Select `rg-realtime-pix-poc`.
4. Enter `cae-realtime-pix-poc`.
5. Select the same region as the resource group.
6. Choose the **Consumption** workload profile.
7. Do not add a dedicated workload profile.
8. Do not enable zone redundancy for this learning environment.
9. Do not add a virtual network for the first proof of concept.
10. Review the logging destination.

If you create a Log Analytics workspace:

- Give it a clear name such as `log-realtime-pix-poc`.
- Understand that Log Analytics has separate usage-based billing.
- Configure a short retention period if the Portal permits it.
- Delete the workspace with the resource group after the experiment.

11. Select **Review + create**.
12. Select **Create**.
13. Wait for deployment to finish.

## 10. Container App Configuration Matrix

Use this matrix while creating the six apps.

| App | Image | Ingress | External | Min | Max |
| --- | --- | --- | --- | ---: | ---: |
| `pix-wallet-ledger` | `ghcr.io/<owner>/pix-wallet-ledger:poc-1` | HTTP | No | 0 | 1 |
| `pix-transaction` | `ghcr.io/<owner>/pix-transaction:poc-1` | HTTP | No | 0 | 1 |
| `pix-identity-presence` | `ghcr.io/<owner>/pix-identity-presence:poc-1` | HTTP | Yes | 0 | 1 |
| `pix-realtime-events` | `ghcr.io/<owner>/pix-realtime-events:poc-1` | HTTP | Yes | 0 | 1 |
| `pix-bot` | `ghcr.io/<owner>/pix-bot:poc-1` | Disabled | N/A | 1 | 1 |
| `pix-api-gateway` | `ghcr.io/<owner>/pix-api-gateway:poc-1` | HTTP | Yes | 0 | 1 |

All HTTP apps use target port `8080`.

The bot is kept at one replica because it is a continuous `BackgroundService`.
Delete or stop it after learning if you want to minimize compute usage.

## 11. Create the Wallet Ledger App

1. Search for **Container Apps**.
2. Select **Create** and then **Container App**.
3. Select the correct subscription.
4. Select `rg-realtime-pix-poc`.
5. Enter `pix-wallet-ledger`.
6. Select `cae-realtime-pix-poc`.
7. Ensure the workload profile is Consumption.
8. For deployment source, select **Container image**.
9. Open the container configuration.
10. Select another/public registry if GHCR is not listed directly.
11. Enter:

```text
ghcr.io/<owner>/pix-wallet-ledger:poc-1
```

12. Set CPU to `0.25`.
13. Set memory to `0.5 Gi`.
14. Add environment variables:

| Name | Value |
| --- | --- |
| `ASPNETCORE_URLS` | `http://+:8080` |
| `DOTNET_ENVIRONMENT` | `Production` |
| `EventBus__Directory` | `/tmp/realtime-pix-bus` |

The event bus directory is container-local and does not make the distributed
flow work. It only prevents the service from trying to use a Windows-style
development location.

15. Enable ingress.
16. Select **Internal** ingress, not external.
17. Select HTTP/auto transport.
18. Set target port to `8080`.
19. Complete **Review + create**.
20. Select **Create**.

After creation:

1. Open the app.
2. Open **Scale**.
3. Select **Edit and deploy**.
4. Set minimum replicas to `0`.
5. Set maximum replicas to `1`.
6. Save and create the new revision.

Then configure `/health` probes as described in
[Configure health probes](#17-configure-health-probes).

## 12. Create the Transaction App

Repeat the wallet steps with:

```text
Name: pix-transaction
Image: ghcr.io/<owner>/pix-transaction:poc-1
Ingress: Internal
Target port: 8080
Minimum replicas: 0
Maximum replicas: 1
```

Use the same three base environment variables.

## 13. Create the Identity Presence App

Repeat the creation process with:

```text
Name: pix-identity-presence
Image: ghcr.io/<owner>/pix-identity-presence:poc-1
Ingress: External
Target port: 8080
Minimum replicas: 0
Maximum replicas: 1
```

Enable insecure HTTP only if you have a specific local test requirement.
Normal browser traffic must use the generated HTTPS URL.

Record the application URL:

```text
https://pix-identity-presence.<environment-domain>
```

The SignalR URL will be:

```text
https://pix-identity-presence.<environment-domain>/presence/hub
```

## 14. Create the Realtime Events App

Use:

```text
Name: pix-realtime-events
Image: ghcr.io/<owner>/pix-realtime-events:poc-1
Ingress: External
Target port: 8080
Minimum replicas: 0
Maximum replicas: 1
```

Record:

```text
https://pix-realtime-events.<environment-domain>
```

The SignalR URL will be:

```text
https://pix-realtime-events.<environment-domain>/events/hub
```

## 15. Create the Bot Worker

Use:

```text
Name: pix-bot
Image: ghcr.io/<owner>/pix-bot:poc-1
Ingress: Disabled
Minimum replicas: 1
Maximum replicas: 1
```

Add:

| Name | Value |
| --- | --- |
| `ASPNETCORE_URLS` | `http://+:8080` |
| `DOTNET_ENVIRONMENT` | `Production` |
| `EventBus__Directory` | `/tmp/realtime-pix-bus` |
| `WalletServiceUrl` | `http://pix-wallet-ledger` |

`http://pix-wallet-ledger` uses Container Apps service discovery inside the
same environment.

The bot app has a `/health` endpoint, but ingress is disabled. Validate it from
logs or temporarily enable internal ingress during troubleshooting.

## 16. Create the API Gateway

Use:

```text
Name: pix-api-gateway
Image: ghcr.io/<owner>/pix-api-gateway:poc-1
Ingress: External
Target port: 8080
Minimum replicas: 0
Maximum replicas: 1
```

Add:

| Name | Value |
| --- | --- |
| `ASPNETCORE_URLS` | `http://+:8080` |
| `DOTNET_ENVIRONMENT` | `Production` |
| `Services__IdentityPresence` | `http://pix-identity-presence` |
| `Services__WalletLedger` | `http://pix-wallet-ledger` |
| `Services__Transaction` | `http://pix-transaction` |
| `Services__RealtimeEvents` | `http://pix-realtime-events` |

Record the generated gateway HTTPS URL:

```text
https://pix-api-gateway.<environment-domain>
```

## 17. Configure Health Probes

Every service already exposes `GET /health`.

For each HTTP Container App:

1. Open the Container App.
2. Open **Containers** or **Revisions and replicas**, depending on the current
   Portal layout.
3. Select **Edit and deploy**.
4. Open the health probes section.
5. Add a startup probe:

| Setting | Value |
| --- | --- |
| Type | Startup |
| Transport | HTTP |
| Path | `/health` |
| Port | `8080` |
| Initial delay | `3` seconds |
| Period | `5` seconds |
| Timeout | `3` seconds |
| Failure threshold | `30` |

6. Add a liveness probe:

| Setting | Value |
| --- | --- |
| Type | Liveness |
| Transport | HTTP |
| Path | `/health` |
| Port | `8080` |
| Initial delay | `5` seconds |
| Period | `10` seconds |
| Timeout | `3` seconds |
| Failure threshold | `3` |

7. Add a readiness probe:

| Setting | Value |
| --- | --- |
| Type | Readiness |
| Transport | HTTP |
| Path | `/health` |
| Port | `8080` |
| Initial delay | `3` seconds |
| Period | `5` seconds |
| Timeout | `3` seconds |
| Failure threshold | `6` |

8. Save and deploy the new revision.
9. Confirm the revision becomes healthy before continuing.

The current `/health` endpoint only verifies that the process is responsive. It
does not verify database or event backbone connectivity.

## 18. Verify the Hosting Proof of Concept

Set the three external URLs:

```powershell
$GatewayUrl = "https://pix-api-gateway.<environment-domain>"
$PresenceUrl = "https://pix-identity-presence.<environment-domain>"
$RealtimeUrl = "https://pix-realtime-events.<environment-domain>"
```

### 18.1 Health checks

```powershell
Invoke-RestMethod "$GatewayUrl/health"
Invoke-RestMethod "$PresenceUrl/health"
Invoke-RestMethod "$RealtimeUrl/health"
```

Each request should return `status = ok`.

### 18.2 Verify gateway-to-presence routing

```powershell
$ClientId = [Guid]::NewGuid().ToString("N")
$Session = Invoke-RestMethod `
  -Method Post `
  -Uri "$GatewayUrl/sessions/anonymous" `
  -ContentType "application/json" `
  -Body (@{ clientId = $ClientId } | ConvertTo-Json)

$Session
```

This confirms:

- External gateway ingress works.
- Internal service discovery works.
- Gateway proxy configuration works.
- Identity service starts from zero if necessary.

### 18.3 Verify gateway-to-wallet routing

```powershell
$Accounts = Invoke-RestMethod `
  -Uri "$GatewayUrl/wallet/accounts?userId=$($Session.userId)"

$Accounts
```

The response should contain Bank A and Bank B accounts. They are only in memory.

### 18.4 Verify expected transfer limitation

Bootstrap the wallet:

```powershell
$Bootstrap = Invoke-RestMethod `
  -Method Post `
  -Uri "$GatewayUrl/wallet/users/$($Session.userId)/bootstrap" `
  -ContentType "application/json" `
  -Body "{}"
```

Create another session and retrieve its account:

```powershell
$Recipient = Invoke-RestMethod `
  -Method Post `
  -Uri "$GatewayUrl/sessions/anonymous" `
  -ContentType "application/json" `
  -Body (@{ clientId = [Guid]::NewGuid().ToString("N") } | ConvertTo-Json)

$RecipientAccounts = Invoke-RestMethod `
  -Uri "$GatewayUrl/wallet/accounts?userId=$($Recipient.userId)"
```

Submit a transfer:

```powershell
$TransferBody = @{
  idempotencyKey = [Guid]::NewGuid().ToString("N")
  senderUserId = $Session.userId
  senderAccountId = $Bootstrap.primaryAccount.accountId
  recipientUserId = $Recipient.userId
  recipientAccountId = $RecipientAccounts[0].accountId
  amount = 25
} | ConvertTo-Json

$Transfer = Invoke-RestMethod `
  -Method Post `
  -Uri "$GatewayUrl/pix/transfers" `
  -ContentType "application/json" `
  -Body $TransferBody
```

Query it:

```powershell
Invoke-RestMethod "$GatewayUrl/pix/transfers/$($Transfer.transferId)"
```

With the current repository, the status is expected to remain `requested`.
That is evidence of the known local event bus limitation, not an Azure routing
failure.

## 19. Connect the Frontend

For a local frontend pointing to Azure, create
`apps/web/.env.local`:

```dotenv
NEXT_PUBLIC_API_BASE_URL=https://pix-api-gateway.<environment-domain>
NEXT_PUBLIC_PRESENCE_HUB_URL=https://pix-identity-presence.<environment-domain>/presence/hub
NEXT_PUBLIC_EVENTS_HUB_URL=https://pix-realtime-events.<environment-domain>/events/hub
```

Restart Next.js after changing these variables:

```powershell
cd apps/web
npm.cmd run dev
```

Open two browser sessions and verify:

1. Both SignalR connections report connected.
2. Joining in one browser changes the presence list in the other.
3. Closing the last tab for a user removes that user.
4. The browser console has no CORS or WebSocket errors.

Because identity state is in memory and `max replicas = 1`, this can work as a
single-replica demonstration. A restart clears all users.

## 20. Inspect Logs and Revisions

For each Container App:

1. Open **Log stream**.
2. Select the current revision and replica.
3. Review console and system logs.
4. Open **Revisions and replicas**.
5. Confirm only the latest revision is active in single revision mode.
6. Open the active revision.
7. Confirm replica count, health, and image tag.

Typical failures:

| Symptom | Likely cause |
| --- | --- |
| Revision activation failed | Image is private, tag is wrong, or process did not bind to 8080 |
| HTTP 502/503 | Container is unhealthy or still starting |
| Gateway returns service URL not configured | Missing `Services__...` environment variable |
| Gateway connection refused | Wrong internal app name or target app failed to start |
| SignalR negotiate fails | Identity/realtime ingress is internal or CORS/frontend URL is wrong |
| Transfer remains requested | Expected until cloud event bus adapter exists |
| Data disappears | Expected while state is in memory |

## 21. Upgrade to a Functional Event-Driven Proof of Concept

Complete these code changes before calling the deployment functionally complete:

1. Add an event bus abstraction selected by configuration.
2. Implement Azure Event Grid publishing.
3. Add authenticated Event Grid webhook endpoints to each consumer.
4. Handle Event Grid subscription validation.
5. Preserve CloudEvents metadata and application event IDs.
6. Persist inbox deduplication records.
7. Persist outbox records transactionally with service state.
8. Replace all in-memory service stores with their owned PostgreSQL databases.
9. Add EF Core migrations or another repeatable migration mechanism.
10. Replace direct multi-replica SignalR state with Azure SignalR Service.
11. Change readiness endpoints to check required dependencies.
12. Run duplicate delivery, replay, and restart tests in Azure.

After these changes, configure Container App secrets:

```text
ConnectionStrings__IdentityPresence
ConnectionStrings__WalletLedger
ConnectionStrings__Transaction
ConnectionStrings__RealtimeProjection
EventGrid__TopicEndpoint
EventGrid__AccessKey
Azure__SignalR__ConnectionString
```

Use secret references rather than literal environment variable values.

## 22. Cleanup

When the proof of concept is finished:

1. Export or record anything you need.
2. Open `rg-realtime-pix-poc`.
3. Review all contained resources.
4. Select **Delete resource group**.
5. Enter the resource group name.
6. Confirm deletion.
7. Verify the resource group disappears.
8. Delete unneeded GHCR `poc-1` packages or versions.
9. Revoke the manual GitHub package token if it is no longer required.

Deleting only the Container Apps does not necessarily delete Log Analytics or
other resources. Deleting the dedicated proof-of-concept resource group is the
cleanest teardown.

## 23. Acceptance Checklist

### Hosting proof of concept

- [ ] Cost budget and alerts exist.
- [ ] Six immutable GHCR images are public and pullable.
- [ ] One Consumption Container Apps environment exists.
- [ ] Six independently deployed Container Apps exist.
- [ ] All HTTP services bind to port 8080.
- [ ] Gateway, identity, and realtime services have external HTTPS ingress.
- [ ] Wallet and transaction services use internal ingress.
- [ ] Bot ingress is disabled.
- [ ] Maximum replicas are set to one.
- [ ] Health probes pass.
- [ ] Gateway can create an anonymous session through internal routing.
- [ ] Gateway can retrieve wallet accounts through internal routing.
- [ ] Two browsers connect to the SignalR hubs.
- [ ] Logs and revisions are visible.

### Functional proof of concept

- [ ] Azure event backbone replaces the JSONL file.
- [ ] Service-owned PostgreSQL databases replace in-memory state.
- [ ] One transfer produces one debit, one credit, and one completion.
- [ ] Duplicate event delivery does not duplicate ledger entries.
- [ ] Restarting every service preserves state.
- [ ] Realtime flow remains correct after reconnect.

## 24. Official References

- [Deploy a Container App with the Azure Portal](https://learn.microsoft.com/en-us/azure/container-apps/quickstart-portal)
- [Container Apps ingress and service discovery](https://learn.microsoft.com/en-us/azure/container-apps/ingress-overview)
- [Communication between microservices](https://learn.microsoft.com/en-us/azure/container-apps/communication-between-microservices)
- [Health probes](https://learn.microsoft.com/en-us/azure/container-apps/health-probes)
- [Scaling](https://learn.microsoft.com/en-us/azure/container-apps/scale-app)
- [Secrets](https://learn.microsoft.com/en-us/azure/container-apps/manage-secrets)
- [Log streaming](https://learn.microsoft.com/en-us/azure/container-apps/log-streaming)
- [Revisions](https://learn.microsoft.com/en-us/azure/container-apps/revisions)
- [Azure budgets](https://learn.microsoft.com/en-us/azure/cost-management-billing/costs/tutorial-acm-create-budgets)
- [Publish Docker images to GHCR](https://docs.github.com/en/actions/tutorials/publish-packages/publish-docker-images)
- [Azure Event Grid custom topic quickstart](https://learn.microsoft.com/en-us/azure/event-grid/custom-event-quickstart-portal)
