variable "region" {
  type    = string
  default = "us-east-1"
}

variable "aws_profile" {
  type    = string
  default = "lumencue-prod"
}

variable "name" {
  description = "Base name for all resources."
  type        = string
  default     = "lumencue-api"
}

variable "github_repo" {
  description = "owner/repo allowed to assume the CI deploy role."
  type        = string
  default     = "williammgyasii/lumencue-app"
}

variable "image_tag" {
  description = "ECR image tag the task definition runs. CI passes the git SHA."
  type        = string
  default     = "latest"
}

variable "db_public_access" {
  description = "TEMP: expose RDS publicly, restricted to admin_cidr only. Set false before onboarding real users."
  type        = bool
  default     = false
}

variable "admin_cidr" {
  description = "Public IP in CIDR form (e.g. 1.2.3.4/32) allowed to reach RDS while db_public_access is true."
  type        = string
  default     = ""
}

variable "domain_name" {
  description = "Root domain managed in Route 53 (registered in lumencue-prod)."
  type        = string
  default     = "lumencueapp.com"
}

variable "api_subdomain" {
  description = "Public hostname for the API (must be under domain_name)."
  type        = string
  default     = "api.lumencueapp.com"
}

# ---- Container ----

variable "container_port" {
  type    = number
  default = 8080
}

# Fargate task size. 256 CPU / 512 MB is the smallest and cheapest combination.
variable "task_cpu" {
  type    = number
  default = 256
}

variable "task_memory" {
  type    = number
  default = 512
}

variable "desired_count" {
  type    = number
  default = 1
}

# ---- Database ----

variable "db_name" {
  type    = string
  default = "lumencue"
}

variable "db_username" {
  type    = string
  default = "lumencue"
}

variable "db_password" {
  description = "Master password for the RDS PostgreSQL instance."
  type        = string
  sensitive   = true
}

variable "db_instance_class" {
  # db.t4g.micro is Free Tier eligible for 12 months on new accounts.
  type    = string
  default = "db.t4g.micro"
}

variable "db_allocated_storage" {
  type    = number
  default = 20
}

# ---- App secrets ----

variable "deepgram_api_key" {
  type      = string
  sensitive = true
}

variable "apibible_api_key" {
  type      = string
  sensitive = true
}
