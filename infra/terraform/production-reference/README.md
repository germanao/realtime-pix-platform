# Production reference profile

This root is deliberately not connected to a GitHub apply workflow. It validates the intended complete production topology in a separate subscription: private workload-profile networking, one private PostgreSQL server per state owner, seven dedicated workload identities and Container Apps, Premium messaging and registry tiers, Standard SignalR request-type restrictions, purge-protected Key Vault, and APIM Standard v2 outbound integration.

It is a reviewed starting point, not a universal production template. Before any real apply, add organization-specific egress/firewall, disaster recovery, regional capacity, data residency, and SLO decisions. Use a separate state backend and supply only non-secret identifiers through a reviewed environment file. Password authentication is disabled.

The profile intentionally has no apply workflow. A real rollout requires an immutable image tag already pushed to the private ACR, a migration runner with private VNet reachability, and a reviewed data-plane step that creates each managed identity as an Entra PostgreSQL principal in only its owned database before migrations and workloads are enabled. APIM exposes the internal Gateway plus only the two SignalR negotiate endpoints; browser clients connect to Azure SignalR after negotiation.

AzureRM 4.80 does not expose SignalR request-type ACLs. After a reviewed apply creates and approves the private endpoint, run the command emitted by `signalr_network_acl_command`. The script discovers the Azure-generated private-endpoint connection name, permits public `ClientConnection` only, permits private `ServerConnection`, `RESTAPI`, and `Trace`, and verifies the final rule set.
