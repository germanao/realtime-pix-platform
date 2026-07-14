# Observability module

Creates a workspace-based Application Insights resource, an operational workbook, an action group, and baseline request/outbox alerts. Platform metric alerts remain at the environment root because their scopes are outputs of PostgreSQL, Service Bus, and Container Apps modules.
