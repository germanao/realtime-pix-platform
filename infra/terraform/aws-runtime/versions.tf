terraform {
  required_version = ">= 1.11.0"

  backend "s3" {
    bucket       = "realtime-pix-tfstate-886781461608"
    key          = "poc/aws-runtime.tfstate"
    region       = "us-east-2"
    encrypt      = true
    use_lockfile = true
  }

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.7"
    }
  }
}

provider "aws" {
  region = var.aws_region

  default_tags {
    tags = {
      Project     = "realtime-pix-platform"
      Environment = var.environment_name
      ManagedBy   = "terraform"
    }
  }
}
