terraform {
  required_version = ">= 1.5"
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
  }
}

provider "aws" {
  region = var.region
  # Local runs use the named SSO profile; CI sets aws_profile="" and authenticates via OIDC.
  profile = var.aws_profile == "" ? null : var.aws_profile

  default_tags {
    tags = {
      Project   = "LumenCue"
      ManagedBy = "Terraform"
      Stack     = "api"
    }
  }
}
