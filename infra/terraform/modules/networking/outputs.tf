output "virtual_network_id" { value = azurerm_virtual_network.this.id }
output "subnet_ids" { value = { for name, subnet in azurerm_subnet.this : name => subnet.id } }
output "private_dns_zone_ids" { value = { for name, zone in azurerm_private_dns_zone.this : name => zone.id } }
