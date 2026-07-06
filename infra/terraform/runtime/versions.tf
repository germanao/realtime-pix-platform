terraform {
  required_version = ">= 1.11.0"

  backend "azurerm" {}

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
    vercel = {
      source  = "vercel/vercel"
      version = "~> 5.0"
    }
  }
}

provider "azurerm" {
  features {}
}

provider "vercel" {
  api_token = var.vercel_api_token
  team      = var.vercel_team_id
}
