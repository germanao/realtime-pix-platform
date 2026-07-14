# PostgreSQL Flexible Server module

Creates a Microsoft Entra-only PostgreSQL Flexible Server and an explicit set of service-owned databases. Public access is supported for the POC profile; private access requires both a delegated subnet and private DNS zone. Runtime identities and database roles are intentionally provisioned outside Terraform because the PostgreSQL data plane requires a token-authenticated migration step.
