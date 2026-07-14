# Terraform state backend module

Creates a private, versioned Azure Blob state backend with change feed and soft-delete retention. Destruction is blocked at both storage-account and container levels. Shared-key support remains enabled only for documented break-glass state recovery; normal Terraform access uses Microsoft Entra data-plane RBAC.
