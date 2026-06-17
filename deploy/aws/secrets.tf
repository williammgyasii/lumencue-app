# Secrets live in SSM Parameter Store (SecureString) and are injected into the task
# at launch via the ECS "secrets" block, so they never appear in the task definition.

resource "aws_ssm_parameter" "db_connection" {
  name  = "/${var.name}/NEON_CONNECTION_STRING"
  type  = "SecureString"
  value = local.db_connection_string
}

resource "aws_ssm_parameter" "deepgram" {
  name  = "/${var.name}/DEEPGRAM_API_KEY"
  type  = "SecureString"
  value = var.deepgram_api_key
}

resource "aws_ssm_parameter" "apibible" {
  name  = "/${var.name}/APIBIBLE_API_KEY"
  type  = "SecureString"
  value = var.apibible_api_key
}
