# Service Bus topology module

Creates a Standard or Premium namespace with local authentication disabled, one duplicate-detecting integration-event topic, command queues, and restrictive SQL-filtered subscriptions. Azure creates a default subscription rule; the deployment workflow removes those default rules immediately after apply and leaves only the Terraform-managed filters.
