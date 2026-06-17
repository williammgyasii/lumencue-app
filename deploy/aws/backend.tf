terraform {
  backend "s3" {
    bucket = "lumencue-tfstate-618015514647"
    key    = "api/terraform.tfstate"
    region = "us-east-1"
    # Native S3 state locking (no DynamoDB needed). Requires Terraform >= 1.10.
    use_lockfile = true
    encrypt      = true
  }
}
