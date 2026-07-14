output "resource_group_name" { value = azurerm_resource_group.this.name }
output "private_postgresql_servers" { value = { for key, server in module.postgresql : key => server.fqdn } }
output "service_bus_namespace_id" { value = module.service_bus.namespace_id }
output "signalr_endpoint" { value = module.signalr.endpoint }
output "apim_gateway_url" { value = module.apim.gateway_url }
output "api_url" { value = "${module.apim.gateway_url}/api" }
output "presence_hub_url" { value = "${module.apim.gateway_url}/presence/hub" }
output "events_hub_url" { value = "${module.apim.gateway_url}/events/hub" }
output "workload_identities" {
  value = { for key, identity in module.workload_identity : key => {
    name         = identity.name
    principal_id = identity.principal_id
  } }
}
output "signalr_network_acl_command" {
  value = "RG=${azurerm_resource_group.this.name} SIGNALR_NAME=sig-${local.name} scripts/cloud/configure-production-signalr-acl.sh"
}
