# Azure Container Apps With Terraform and CI/CD

This guide replaces the manual Portal deployment with repeatable
infrastructure and automated application delivery.

It covers:

- Terraform for the Azure Container Apps environment and six Container Apps.
- Azure Blob Storage for remote Terraform state and locking.
- GitHub Actions with GitHub Container Registry and Azure OIDC.
- Azure Pipelines as an alternative, using workload identity federation.
- Testing, immutable image tags, deployment verification, rollback, and
  teardown.

Last verified against Microsoft, GitHub, and HashiCorp documentation:
June 18, 2026.

## 1. Recommended Delivery Model

Use the following separation:

| Concern | Tool |
| --- | --- |
| Compile and test .NET | GitHub Actions or Azure Pipelines |
| Build container images | Docker Buildx or Docker task |
| Store learning images | Public GitHub Container Registry |
| Provision Azure resources | Terraform |
| Authenticate CI to Azure | OIDC/workload identity federation |
| Deploy app revisions | Terraform image variables or `az containerapp update` |
| Store Terraform state | Azure Blob Storage with Microsoft Entra authentication |

The recommended first automated implementation lets Terraform control both
infrastructure and image tags. Every backend image uses the Git commit SHA.

Later, when builds should deploy only changed services, keep Terraform focused
on infrastructure and use `az containerapp update --image` for individual app
revisions.

## 2. Preconditions

Complete these items first:

- The repository is hosted on GitHub or Azure Repos.
- Every service has a production Dockerfile.
- Images build from the monorepo root.
- `dotnet test` passes.
- The Portal proof of concept has established the correct ports, environment
  variables, ingress, and health probes.
- You understand that the current JSONL event bus and in-memory stores prevent
  a functionally complete cloud deployment.

Use the Dockerfile structure from the
[Portal guide](azure-container-apps-portal-poc.md#5-create-production-dockerfiles).

Use a clean target resource group for the Terraform exercise. This guide uses
`rg-realtime-pix-iac-poc`, which is deliberately different from the manual
Portal guide. Pointing Terraform at manually created Container Apps with the
same names will cause resource-already-exists errors unless those resources are
imported into Terraform state.

## 3. Proposed Repository Layout

Add the following structure:

```text
.github/
`-- workflows/
    `-- backend-azure.yml

infra/
`-- azure/
    `-- terraform/
        |-- versions.tf
        |-- providers.tf
        |-- backend.tf
        |-- variables.tf
        |-- main.tf
        |-- outputs.tf
        `-- environments/
            `-- poc.tfvars

azure-pipelines.yml
```

The GitHub workflow and Azure Pipeline are alternatives. Do not enable both to
deploy the same environment unless you intentionally coordinate them.

## 4. Decide the Bootstrap Boundary

Terraform cannot store its state in an Azure Storage account until that
account exists. The following small bootstrap layer is created manually:

- Resource group for Terraform state.
- Storage account.
- Private blob container.
- Target resource group for the Container Apps environment.
- Microsoft Entra application or Azure DevOps service connection.
- Role assignments for the CI identity.

Everything inside the target resource group after that point is managed by
Terraform.

This avoids assigning subscription-wide Contributor access merely so Terraform
can create its own resource group.

## 5. Create the Terraform State Backend

### 5.1 Sign in and select the subscription

```powershell
az login
az account list --output table
az account set --subscription "<subscription-id-or-name>"
az account show --output table
```

### 5.2 Choose globally unique names

Storage account names must be globally unique, lowercase, and contain only
letters and numbers.

```powershell
$Location = "brazilsouth"
$StateResourceGroup = "rg-realtime-pix-tfstate"
$AppResourceGroup = "rg-realtime-pix-iac-poc"
$StorageAccount = "strealtimepixtf<unique>"
$StateContainer = "tfstate"
```

If Container Apps or another required service is unavailable in the selected
region, use `eastus` consistently instead.

### 5.3 Create the bootstrap resources

```powershell
az group create `
  --name $StateResourceGroup `
  --location $Location `
  --tags application=realtime-pix environment=poc purpose=terraform-state

az group create `
  --name $AppResourceGroup `
  --location $Location `
  --tags application=realtime-pix environment=poc purpose=application

az storage account create `
  --name $StorageAccount `
  --resource-group $StateResourceGroup `
  --location $Location `
  --sku Standard_LRS `
  --kind StorageV2 `
  --min-tls-version TLS1_2 `
  --allow-blob-public-access false
```

Azure Storage is not part of the Container Apps free grant. A small state blob
normally costs very little, but it is still a billable resource.

### 5.4 Grant your local user data-plane access

Get the storage account resource ID:

```powershell
$StorageAccountId = az storage account show `
  --name $StorageAccount `
  --resource-group $StateResourceGroup `
  --query id `
  --output tsv
```

Get your signed-in identity object ID:

```powershell
$SignedInObjectId = az ad signed-in-user show --query id --output tsv
```

Assign blob access:

```powershell
az role assignment create `
  --assignee-object-id $SignedInObjectId `
  --assignee-principal-type User `
  --role "Storage Blob Data Contributor" `
  --scope $StorageAccountId
```

Role propagation can take several minutes.

After the role is effective, create the private state container:

```powershell
az storage container create `
  --name $StateContainer `
  --account-name $StorageAccount `
  --auth-mode login
```

## 6. Create the Terraform Configuration

The following configuration intentionally deploys public GHCR images. It does
not create Azure Container Registry.

### 6.1 `versions.tf`

```hcl
terraform {
  required_version = ">= 1.10.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
  }
}
```

Pinning the major provider version prevents an unreviewed major upgrade.
Commit the generated `.terraform.lock.hcl`.

### 6.2 `backend.tf`

Keep credentials and environment-specific backend names out of source:

```hcl
terraform {
  backend "azurerm" {}
}
```

Backend values will be supplied during `terraform init`.

### 6.3 `providers.tf`

```hcl
provider "azurerm" {
  features {}
}
```

The subscription ID is provided through `ARM_SUBSCRIPTION_ID`.

### 6.4 `variables.tf`

```hcl
variable "resource_group_name" {
  description = "Existing resource group that contains the application resources."
  type        = string
}

variable "location" {
  description = "Azure region for all application resources."
  type        = string
}

variable "environment_name" {
  description = "Azure Container Apps environment name."
  type        = string
}

variable "log_analytics_workspace_name" {
  description = "Log Analytics workspace name."
  type        = string
}

variable "ghcr_owner" {
  description = "Lowercase GitHub user or organization that owns the images."
  type        = string
}

variable "image_tag" {
  description = "Immutable image tag, normally a Git commit SHA."
  type        = string
}
```

### 6.5 `main.tf`

```hcl
data "azurerm_resource_group" "main" {
  name = var.resource_group_name
}

resource "azurerm_log_analytics_workspace" "main" {
  name                = var.log_analytics_workspace_name
  location            = var.location
  resource_group_name = data.azurerm_resource_group.main.name
  sku                 = "PerGB2018"
  retention_in_days   = 30

  tags = {
    application = "realtime-pix"
    environment = "poc"
    managed-by  = "terraform"
  }
}

resource "azurerm_container_app_environment" "main" {
  name                       = var.environment_name
  location                   = var.location
  resource_group_name        = data.azurerm_resource_group.main.name
  log_analytics_workspace_id = azurerm_log_analytics_workspace.main.id

  tags = {
    application = "realtime-pix"
    environment = "poc"
    managed-by  = "terraform"
  }
}

locals {
  base_environment = {
    ASPNETCORE_URLS  = "http://+:8080"
    DOTNET_ENVIRONMENT = "Production"
  }

  apps = {
    pix-wallet-ledger = {
      image             = "ghcr.io/${var.ghcr_owner}/pix-wallet-ledger:${var.image_tag}"
      ingress_enabled   = true
      external_enabled  = false
      minimum_replicas  = 0
      maximum_replicas  = 1
      environment = merge(local.base_environment, {
        EventBus__Directory = "/tmp/realtime-pix-bus"
      })
    }

    pix-transaction = {
      image             = "ghcr.io/${var.ghcr_owner}/pix-transaction:${var.image_tag}"
      ingress_enabled   = true
      external_enabled  = false
      minimum_replicas  = 0
      maximum_replicas  = 1
      environment = merge(local.base_environment, {
        EventBus__Directory = "/tmp/realtime-pix-bus"
      })
    }

    pix-identity-presence = {
      image             = "ghcr.io/${var.ghcr_owner}/pix-identity-presence:${var.image_tag}"
      ingress_enabled   = true
      external_enabled  = true
      minimum_replicas  = 0
      maximum_replicas  = 1
      environment = merge(local.base_environment, {
        EventBus__Directory = "/tmp/realtime-pix-bus"
      })
    }

    pix-realtime-events = {
      image             = "ghcr.io/${var.ghcr_owner}/pix-realtime-events:${var.image_tag}"
      ingress_enabled   = true
      external_enabled  = true
      minimum_replicas  = 0
      maximum_replicas  = 1
      environment = merge(local.base_environment, {
        EventBus__Directory = "/tmp/realtime-pix-bus"
      })
    }

    pix-bot = {
      image             = "ghcr.io/${var.ghcr_owner}/pix-bot:${var.image_tag}"
      ingress_enabled   = false
      external_enabled  = false
      minimum_replicas  = 1
      maximum_replicas  = 1
      environment = merge(local.base_environment, {
        EventBus__Directory = "/tmp/realtime-pix-bus"
        WalletServiceUrl    = "http://pix-wallet-ledger"
      })
    }

    pix-api-gateway = {
      image             = "ghcr.io/${var.ghcr_owner}/pix-api-gateway:${var.image_tag}"
      ingress_enabled   = true
      external_enabled  = true
      minimum_replicas  = 0
      maximum_replicas  = 1
      environment = merge(local.base_environment, {
        Services__IdentityPresence = "http://pix-identity-presence"
        Services__WalletLedger     = "http://pix-wallet-ledger"
        Services__Transaction      = "http://pix-transaction"
        Services__RealtimeEvents   = "http://pix-realtime-events"
      })
    }
  }
}

resource "azurerm_container_app" "apps" {
  for_each = local.apps

  name                         = each.key
  container_app_environment_id = azurerm_container_app_environment.main.id
  resource_group_name          = data.azurerm_resource_group.main.name
  revision_mode                = "Single"

  template {
    min_replicas = each.value.minimum_replicas
    max_replicas = each.value.maximum_replicas

    container {
      name   = each.key
      image  = each.value.image
      cpu    = 0.25
      memory = "0.5Gi"

      dynamic "env" {
        for_each = each.value.environment

        content {
          name  = env.key
          value = env.value
        }
      }

      startup_probe {
        transport               = "HTTP"
        port                    = 8080
        path                    = "/health"
        initial_delay           = 3
        interval_seconds        = 5
        timeout                 = 3
        failure_count_threshold = 30
      }

      liveness_probe {
        transport               = "HTTP"
        port                    = 8080
        path                    = "/health"
        initial_delay           = 5
        interval_seconds        = 10
        timeout                 = 3
        failure_count_threshold = 3
      }

      readiness_probe {
        transport               = "HTTP"
        port                    = 8080
        path                    = "/health"
        initial_delay           = 3
        interval_seconds        = 5
        timeout                 = 3
        failure_count_threshold = 6
        success_count_threshold = 1
      }
    }
  }

  dynamic "ingress" {
    for_each = each.value.ingress_enabled ? [1] : []

    content {
      external_enabled = each.value.external_enabled
      target_port      = 8080
      transport        = "auto"

      traffic_weight {
        latest_revision = true
        percentage      = 100
      }
    }
  }

  tags = {
    application = "realtime-pix"
    environment = "poc"
    service     = each.key
    managed-by  = "terraform"
  }
}
```

Provider schemas can evolve within a major version. Run `terraform validate`
after initialization. If a probe field changes in the provider version selected
by `.terraform.lock.hcl`, use the current
[`azurerm_container_app`](https://registry.terraform.io/providers/hashicorp/azurerm/latest/docs/resources/container_app)
schema as the authority.

### 6.6 `outputs.tf`

```hcl
output "api_gateway_url" {
  value = "https://${azurerm_container_app.apps["pix-api-gateway"].latest_revision_fqdn}"
}

output "presence_hub_url" {
  value = "https://${azurerm_container_app.apps["pix-identity-presence"].latest_revision_fqdn}/presence/hub"
}

output "events_hub_url" {
  value = "https://${azurerm_container_app.apps["pix-realtime-events"].latest_revision_fqdn}/events/hub"
}
```

### 6.7 `environments/poc.tfvars`

```hcl
resource_group_name         = "rg-realtime-pix-iac-poc"
location                    = "Brazil South"
environment_name            = "cae-realtime-pix-iac-poc"
log_analytics_workspace_name = "log-realtime-pix-iac-poc"
ghcr_owner                  = "<lowercase-github-owner>"
image_tag                   = "poc-1"
```

Do not store passwords or connection strings in `.tfvars` committed to Git.

## 7. Initialize and Validate Terraform Locally

Set authentication for local Azure CLI use:

```powershell
$env:ARM_USE_AZUREAD = "true"
$env:ARM_USE_CLI = "true"
```

Initialize:

```powershell
terraform -chdir=infra/azure/terraform init `
  -backend-config="storage_account_name=$StorageAccount" `
  -backend-config="container_name=$StateContainer" `
  -backend-config="key=realtime-pix-poc.tfstate" `
  -backend-config="use_azuread_auth=true" `
  -backend-config="use_cli=true"
```

Format and validate:

```powershell
terraform -chdir=infra/azure/terraform fmt -check -recursive
terraform -chdir=infra/azure/terraform validate
```

Create a plan:

```powershell
terraform -chdir=infra/azure/terraform plan `
  -var-file=environments/poc.tfvars `
  -out=poc.tfplan
```

Review the plan carefully. It should create:

- One Log Analytics workspace.
- One Container Apps environment.
- Six Container Apps.

Apply:

```powershell
terraform -chdir=infra/azure/terraform apply poc.tfplan
```

Read outputs:

```powershell
terraform -chdir=infra/azure/terraform output
```

## 8. Configure GitHub Actions OIDC

Use OIDC instead of storing an Azure client secret.

### 8.1 Create an app registration

In Azure Portal:

1. Search for **Microsoft Entra ID**.
2. Open **App registrations**.
3. Select **New registration**.
4. Name it `github-realtime-pix-poc`.
5. Select the single-tenant option unless you have a specific multi-tenant
   requirement.
6. Leave redirect URI empty.
7. Select **Register**.
8. Record:
   - Application/client ID.
   - Directory/tenant ID.
   - Azure subscription ID.

Do not create a client secret.

### 8.2 Create the GitHub environment

In GitHub:

1. Open the repository.
2. Open **Settings**.
3. Open **Environments**.
4. Select **New environment**.
5. Name it `poc`.
6. Optionally require a reviewer before deployment.
7. Restrict deployment branches to `main`.

Using a GitHub environment gives a stable OIDC subject and allows manual
approval before Terraform apply.

### 8.3 Add the federated credential

In the Azure app registration:

1. Open **Certificates & secrets**.
2. Open **Federated credentials**.
3. Select **Add credential**.
4. Choose **GitHub Actions deploying Azure resources**.
5. Enter the GitHub organization or username.
6. Enter the repository name.
7. Select entity type **Environment**.
8. Enter environment name `poc`.
9. Give the credential a name such as `github-poc`.
10. Select **Add**.

The environment name must exactly match the environment used by the workflow.

### 8.4 Assign Azure roles

Open `rg-realtime-pix-iac-poc`:

1. Open **Access control (IAM)**.
2. Select **Add role assignment**.
3. Select **Contributor**.
4. Select the `github-realtime-pix-poc` enterprise application.
5. Complete the assignment.

Open the Terraform state storage account:

1. Open **Access control (IAM)**.
2. Add **Storage Blob Data Contributor**.
3. Select the same application.
4. Complete the assignment.

The Contributor role does not grant blob data-plane access, which is why the
second role is required.

### 8.5 Configure GitHub secrets and variables

In the `poc` GitHub environment, add secrets:

| Name | Value |
| --- | --- |
| `AZURE_CLIENT_ID` | App registration client ID |
| `AZURE_TENANT_ID` | Directory/tenant ID |
| `AZURE_SUBSCRIPTION_ID` | Subscription ID |

Add the following repository variable, because the image build job does not
reference the protected deployment environment:

| Name | Value |
| --- | --- |
| `GHCR_OWNER` | Lowercase GitHub owner |

Add these variables to the `poc` GitHub environment:

| Name | Value |
| --- | --- |
| `TF_STATE_STORAGE_ACCOUNT` | Terraform state storage account |
| `TF_STATE_CONTAINER` | `tfstate` |
| `TF_STATE_KEY` | `realtime-pix-poc.tfstate` |

IDs are not passwords, but storing them consistently as environment secrets
keeps the workflow configuration simple.

## 9. GitHub Actions Workflow

Create `.github/workflows/backend-azure.yml`.

The first version builds all six images on every backend deployment. This is
slower but easier to understand and ensures all images share the same commit
tag.

```yaml
name: Backend CI and Azure deployment

on:
  workflow_dispatch:
  push:
    branches:
      - main
    paths:
      - "services/**"
      - "contracts/**"
      - "building-blocks/**"
      - "tests/**"
      - "Directory.Build.props"
      - "RealtimePixPlatform.slnx"
      - "infra/azure/terraform/**"
      - ".github/workflows/backend-azure.yml"

concurrency:
  group: realtime-pix-poc
  cancel-in-progress: false

env:
  REGISTRY: ghcr.io
  GHCR_OWNER: ${{ vars.GHCR_OWNER }}
  TF_WORKING_DIRECTORY: infra/azure/terraform

jobs:
  test:
    runs-on: ubuntu-latest

    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "10.0.x"

      - name: Restore
        run: dotnet restore RealtimePixPlatform.slnx

      - name: Build
        run: dotnet build RealtimePixPlatform.slnx --configuration Release --no-restore

      - name: Test
        run: dotnet test RealtimePixPlatform.slnx --configuration Release --no-build

  build-images:
    needs: test
    runs-on: ubuntu-latest

    permissions:
      contents: read
      packages: write

    strategy:
      fail-fast: false
      matrix:
        include:
          - image: pix-api-gateway
            dockerfile: services/api-gateway/Dockerfile
          - image: pix-identity-presence
            dockerfile: services/identity-presence-service/Dockerfile
          - image: pix-wallet-ledger
            dockerfile: services/wallet-ledger-service/Dockerfile
          - image: pix-transaction
            dockerfile: services/transaction-service/Dockerfile
          - image: pix-realtime-events
            dockerfile: services/realtime-events-service/Dockerfile
          - image: pix-bot
            dockerfile: services/bot-service/Dockerfile

    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup Docker Buildx
        uses: docker/setup-buildx-action@v3

      - name: Sign in to GHCR
        uses: docker/login-action@v3
        with:
          registry: ${{ env.REGISTRY }}
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      - name: Build and push
        uses: docker/build-push-action@v6
        with:
          context: .
          file: ${{ matrix.dockerfile }}
          push: true
          tags: |
            ${{ env.REGISTRY }}/${{ env.GHCR_OWNER }}/${{ matrix.image }}:${{ github.sha }}
          cache-from: type=gha,scope=${{ matrix.image }}
          cache-to: type=gha,mode=max,scope=${{ matrix.image }}

  terraform:
    needs: build-images
    runs-on: ubuntu-latest
    environment: poc

    permissions:
      contents: read
      id-token: write

    env:
      ARM_USE_OIDC: "true"
      ARM_USE_AZUREAD: "true"
      ARM_CLIENT_ID: ${{ secrets.AZURE_CLIENT_ID }}
      ARM_TENANT_ID: ${{ secrets.AZURE_TENANT_ID }}
      ARM_SUBSCRIPTION_ID: ${{ secrets.AZURE_SUBSCRIPTION_ID }}

    defaults:
      run:
        working-directory: ${{ env.TF_WORKING_DIRECTORY }}

    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup Terraform
        uses: hashicorp/setup-terraform@v3

      - name: Terraform init
        run: |
          terraform init \
            -backend-config="storage_account_name=${{ vars.TF_STATE_STORAGE_ACCOUNT }}" \
            -backend-config="container_name=${{ vars.TF_STATE_CONTAINER }}" \
            -backend-config="key=${{ vars.TF_STATE_KEY }}" \
            -backend-config="use_oidc=true" \
            -backend-config="use_azuread_auth=true"

      - name: Terraform format check
        run: terraform fmt -check -recursive

      - name: Terraform validate
        run: terraform validate

      - name: Terraform plan
        run: |
          terraform plan \
            -var-file=environments/poc.tfvars \
            -var="image_tag=${{ github.sha }}" \
            -out=poc.tfplan

      - name: Terraform apply
        run: terraform apply -auto-approve poc.tfplan

      - name: Verify gateway health
        shell: bash
        run: |
          gateway_url="$(terraform output -raw api_gateway_url)"

          for attempt in {1..20}; do
            if curl --fail --silent --show-error "$gateway_url/health"; then
              exit 0
            fi

            sleep 15
          done

          echo "Gateway health check failed after 20 attempts."
          exit 1
```

### 9.1 First workflow run and GHCR visibility

New GHCR packages may initially be private.

Before Terraform can pull them without credentials:

1. Run only the image build portion or publish the first image versions
   manually.
2. Open each GitHub package.
3. Change visibility to public.
4. Re-run the complete workflow.

For private images, add registry credentials or a supported identity-based
registry configuration to each Container App. Never place a personal package
token directly in Terraform source or workflow YAML.

### 9.2 Why commit SHA tags matter

Do not deploy only `latest`.

The SHA tag:

- Identifies exactly which source produced a revision.
- Forces Container Apps to create a predictable new revision.
- Supports rollback to a known image.
- Avoids stale registry and runtime caching ambiguity.

## 10. Pull Request Terraform Plans

Do not apply infrastructure from pull requests. Add a separate job that runs
only format, validate, and plan.

Recommended behavior:

| Event | Actions |
| --- | --- |
| Pull request | Build, test, Docker build without push, Terraform validate and plan |
| Push to `main` | Build, test, push immutable images, Terraform apply |
| Manual dispatch | Re-run a selected known commit |

Do not expose Azure deployment credentials to workflows triggered from
untrusted forks.

## 11. Improve to Per-Service Deployments

The all-images workflow is suitable for learning. Later:

1. Detect changed paths.
2. Build only affected service images.
3. Always rebuild services when `contracts` or `building-blocks` change.
4. Deploy each service independently with:

```bash
az containerapp update \
  --name pix-wallet-ledger \
  --resource-group rg-realtime-pix-iac-poc \
  --image ghcr.io/<owner>/pix-wallet-ledger:<commit-sha>
```

5. Keep Terraform image fields from reverting CI changes.

There are two valid ownership models:

- Terraform owns image tags. CI calls `terraform apply`.
- Terraform owns infrastructure while CI owns image revisions. Terraform uses
  lifecycle rules or separate modules to avoid reverting image tags.

Do not mix the models without an explicit drift strategy.

## 12. Azure Pipelines Alternative

Use this section instead of GitHub Actions if source and CI/CD are managed in
Azure DevOps.

### 12.1 Create the Azure Resource Manager service connection

In Azure DevOps:

1. Open the project.
2. Open **Project settings**.
3. Open **Service connections**.
4. Select **New service connection**.
5. Select **Azure Resource Manager**.
6. Select automatic app registration with **Workload identity federation**.
7. Scope it to the subscription and `rg-realtime-pix-iac-poc`.
8. Name it `sc-realtime-pix-poc`.
9. Do not grant it to every pipeline.
10. Save it and authorize only the deployment pipeline.

Workload identity federation avoids a stored client secret.

### 12.2 Create the GHCR registry service connection

If Azure Pipelines builds images for GHCR:

1. Create a GitHub token with package write permission.
2. In Azure DevOps, create a Docker Registry service connection.
3. Select another registry.
4. Enter `https://ghcr.io`.
5. Enter the GitHub username.
6. Enter the token.
7. Name it `sc-ghcr-realtime-pix`.
8. Restrict access to the required pipeline.

GitHub Actions is preferable for GHCR because it can use `GITHUB_TOKEN` instead
of a long-lived package token.

### 12.3 Create the pipeline

Create `azure-pipelines.yml`:

```yaml
trigger:
  branches:
    include:
      - main
  paths:
    include:
      - services/**
      - contracts/**
      - building-blocks/**
      - tests/**
      - infra/azure/terraform/**
      - Directory.Build.props
      - RealtimePixPlatform.slnx

pool:
  vmImage: ubuntu-latest

variables:
  buildConfiguration: Release
  ghcrOwner: "<lowercase-github-owner>"
  tag: "$(Build.SourceVersion)"
  azureServiceConnection: "sc-realtime-pix-poc"
  ghcrServiceConnection: "sc-ghcr-realtime-pix"
  terraformDirectory: "infra/azure/terraform"

stages:
  - stage: Test
    jobs:
      - job: BackendTests
        steps:
          - task: UseDotNet@2
            inputs:
              packageType: sdk
              version: "10.0.x"

          - script: dotnet restore RealtimePixPlatform.slnx
            displayName: Restore

          - script: dotnet build RealtimePixPlatform.slnx --configuration $(buildConfiguration) --no-restore
            displayName: Build

          - script: dotnet test RealtimePixPlatform.slnx --configuration $(buildConfiguration) --no-build
            displayName: Test

  - stage: BuildImages
    dependsOn: Test
    jobs:
      - job: Build
        strategy:
          matrix:
            ApiGateway:
              imageName: pix-api-gateway
              dockerfile: services/api-gateway/Dockerfile
            IdentityPresence:
              imageName: pix-identity-presence
              dockerfile: services/identity-presence-service/Dockerfile
            WalletLedger:
              imageName: pix-wallet-ledger
              dockerfile: services/wallet-ledger-service/Dockerfile
            Transaction:
              imageName: pix-transaction
              dockerfile: services/transaction-service/Dockerfile
            RealtimeEvents:
              imageName: pix-realtime-events
              dockerfile: services/realtime-events-service/Dockerfile
            Bot:
              imageName: pix-bot
              dockerfile: services/bot-service/Dockerfile

        steps:
          - task: Docker@2
            displayName: Build and push $(imageName)
            inputs:
              command: buildAndPush
              containerRegistry: $(ghcrServiceConnection)
              repository: $(ghcrOwner)/$(imageName)
              Dockerfile: $(dockerfile)
              buildContext: .
              tags: |
                $(tag)

  - stage: Terraform
    dependsOn: BuildImages
    jobs:
      - deployment: DeployInfrastructure
        environment: realtime-pix-poc
        strategy:
          runOnce:
            deploy:
              steps:
                - checkout: self

                - task: AzureCLI@2
                  displayName: Terraform plan and apply
                  inputs:
                    azureSubscription: $(azureServiceConnection)
                    scriptType: bash
                    scriptLocation: inlineScript
                    addSpnToEnvironment: true
                    workingDirectory: $(terraformDirectory)
                    inlineScript: |
                      set -euo pipefail

                      export ARM_USE_OIDC=true
                      export ARM_USE_AZUREAD=true
                      export ARM_CLIENT_ID="$servicePrincipalId"
                      export ARM_TENANT_ID="$tenantId"
                      export ARM_OIDC_TOKEN="$idToken"
                      export ARM_OIDC_AZURE_SERVICE_CONNECTION_ID="$AZURESUBSCRIPTION_SERVICE_CONNECTION_ID"
                      export ARM_SUBSCRIPTION_ID="$(az account show --query id --output tsv)"

                      terraform init \
                        -backend-config="storage_account_name=$(TF_STATE_STORAGE_ACCOUNT)" \
                        -backend-config="container_name=$(TF_STATE_CONTAINER)" \
                        -backend-config="key=$(TF_STATE_KEY)" \
                        -backend-config="use_oidc=true" \
                        -backend-config="use_azuread_auth=true"

                      terraform fmt -check -recursive
                      terraform validate

                      terraform plan \
                        -var-file=environments/poc.tfvars \
                        -var="image_tag=$(tag)" \
                        -out=poc.tfplan

                      terraform apply -auto-approve poc.tfplan
```

The Microsoft-hosted agent must have the chosen Terraform version. If it does
not, add an approved Terraform installer task or a checked-in installation
script before the `AzureCLI@2` task.

Create secret pipeline variables:

```text
TF_STATE_STORAGE_ACCOUNT
TF_STATE_CONTAINER
TF_STATE_KEY
```

These names are not sensitive, but marking environment-specific values secret
reduces accidental exposure and editing.

### 12.4 Alternative Azure Container Apps task

If Terraform has already created the apps, Azure Pipelines can deploy an
existing image with `AzureContainerApps@1`:

```yaml
- task: AzureContainerApps@1
  inputs:
    azureSubscription: sc-realtime-pix-poc
    imageToDeploy: ghcr.io/<owner>/pix-api-gateway:$(Build.SourceVersion)
    containerAppName: pix-api-gateway
    resourceGroup: rg-realtime-pix-iac-poc
```

For public GHCR images, no registry pull credentials are required. For private
images, configure registry authentication on the Container App.

## 13. Add Real Secrets After Cloud Adapters Exist

The current code does not consume the planned PostgreSQL or Event Grid settings.
After those adapters are implemented, do not put their values in ordinary
Terraform variables committed to Git.

Options:

1. Store Container App secrets using sensitive CI variables.
2. Store secrets in Azure Key Vault and reference them through managed identity.
3. Provision secret names through Terraform while injecting values from a
   protected secret store.

Planned settings:

```text
ConnectionStrings__IdentityPresence
ConnectionStrings__WalletLedger
ConnectionStrings__Transaction
ConnectionStrings__RealtimeProjection
EventGrid__TopicEndpoint
EventGrid__AccessKey
Azure__SignalR__ConnectionString
```

Mark Terraform variables as `sensitive = true`, but remember that sensitive
values can still exist in Terraform state. Protect the state container.

## 14. Deployment Verification

After every deployment:

1. Read Terraform outputs.
2. Call gateway `/health`.
3. Create an anonymous session through the gateway.
4. Retrieve wallet accounts through the gateway.
5. Inspect the active revisions.
6. Inspect system and console logs.
7. Confirm every active revision uses the expected SHA tag.
8. Confirm no app has scaled beyond one replica.
9. Connect two SignalR clients.
10. Record the expected transfer limitation until the cloud event adapter is
    implemented.

Example:

```powershell
$GatewayUrl = terraform -chdir=infra/azure/terraform output -raw api_gateway_url

Invoke-RestMethod "$GatewayUrl/health"
```

## 15. Rollback

### 15.1 Re-run a known commit

The simplest rollback is:

1. Find the last successful workflow commit SHA.
2. Run the workflow manually for that source revision, or set
   `image_tag` to that SHA.
3. Apply Terraform.
4. Verify health and behavior.

### 15.2 Activate an older Container Apps revision

In an incident:

1. Open the affected Container App.
2. Open **Revisions and replicas**.
3. Locate the last healthy revision.
4. Activate it or move traffic to it, depending on revision mode.

Single revision mode normally moves traffic only after the new revision passes
readiness checks. For controlled blue/green releases, use multiple revision
mode and explicit traffic weights.

## 16. Drift and Ownership Rules

After Terraform manages a resource:

- Do not change persistent settings manually in the Portal.
- Emergency Portal changes must be represented in Terraform afterward.
- Run `terraform plan` to detect drift.
- Do not allow Portal, Terraform, GitHub Actions, and Azure Pipelines to all
  independently own the same image field.
- Keep one authoritative deployment path per environment.

The Portal remains useful for logs, metrics, revisions, and diagnostics.

## 17. Destroy the Automated Environment

Terraform destruction removes resources it manages, not the manually
bootstrapped resource groups and state storage.

Create and review a destroy plan:

```powershell
terraform -chdir=infra/azure/terraform plan `
  -destroy `
  -var-file=environments/poc.tfvars `
  -out=destroy.tfplan
```

Apply it:

```powershell
terraform -chdir=infra/azure/terraform apply destroy.tfplan
```

Then:

1. Confirm the Container Apps and Log Analytics resources are gone.
2. Retain Terraform state if the environment will be recreated.
3. Delete the state storage resource group only when its states are no longer
   needed.
4. Delete the target resource group if it is empty.
5. Delete unused GHCR image versions.
6. Remove the Entra app registration and federated credentials if the
   deployment path is retired.

Never delete the state storage account while managed infrastructure still
exists.

## 18. Automation Acceptance Checklist

- [ ] Terraform state uses Azure Blob Storage.
- [ ] State access uses Microsoft Entra ID rather than an account key.
- [ ] The CI identity has Storage Blob Data Contributor on the state storage.
- [ ] The CI identity is scoped to the target resource group.
- [ ] GitHub Actions or Azure Pipelines uses workload identity federation.
- [ ] No Azure client secret is stored in CI.
- [ ] All backend tests run before image publishing.
- [ ] Six Docker images use immutable commit SHA tags.
- [ ] GHCR package visibility or authentication is configured.
- [ ] Terraform format and validation run in CI.
- [ ] Pull requests do not apply infrastructure.
- [ ] Production/environment deployment can require approval.
- [ ] Terraform creates one environment and six independent Container Apps.
- [ ] Maximum replicas remain one while state is in memory.
- [ ] The gateway health check runs after deployment.
- [ ] Rollback to a known SHA is documented and tested.
- [ ] Manual Portal changes are not used as normal configuration management.

## 19. Functional Platform Acceptance Checklist

These checks remain mandatory after infrastructure automation is complete:

- [ ] Event Grid or another real event backbone replaces the JSONL file.
- [ ] Each data-owning service uses its PostgreSQL database.
- [ ] Outbox publication survives a service crash.
- [ ] Inbox records deduplicate event replays.
- [ ] One click creates exactly one transfer.
- [ ] Duplicate delivery creates no duplicate debit or credit.
- [ ] User presence works after service restarts.
- [ ] Azure SignalR works with more than one replica.
- [ ] Correlation and causation IDs appear in centralized logs.
- [ ] A full transfer remains traceable through the architecture visualization.

## 20. Official References

- [GitHub Actions deployment to Container Apps](https://learn.microsoft.com/en-us/azure/container-apps/github-actions)
- [Azure Pipelines deployment to Container Apps](https://learn.microsoft.com/en-us/azure/container-apps/azure-pipelines)
- [AzureContainerApps@1 task](https://learn.microsoft.com/en-us/azure/devops/pipelines/tasks/reference/azure-container-apps-v1)
- [Azure CLI pipeline task](https://learn.microsoft.com/en-us/azure/devops/pipelines/tasks/reference/azure-cli-v2)
- [GitHub OIDC authentication to Azure](https://learn.microsoft.com/en-us/azure/developer/github/connect-from-azure-openid-connect)
- [Azure DevOps workload identity service connection](https://learn.microsoft.com/en-us/azure/devops/pipelines/library/connect-to-azure)
- [Terraform AzureRM backend and OIDC](https://developer.hashicorp.com/terraform/language/backend/azurerm)
- [Store Terraform state in Azure Storage](https://learn.microsoft.com/en-us/azure/developer/terraform/store-state-in-azure-storage)
- [Terraform plan](https://developer.hashicorp.com/terraform/cli/commands/plan)
- [Terraform apply](https://developer.hashicorp.com/terraform/cli/commands/apply)
- [Publish Docker images to GHCR](https://docs.github.com/en/actions/tutorials/publish-packages/publish-docker-images)
- [Container Apps ARM and YAML specification](https://learn.microsoft.com/en-us/azure/container-apps/azure-resource-manager-api-spec)
- [Container Apps revisions](https://learn.microsoft.com/en-us/azure/container-apps/revisions)
