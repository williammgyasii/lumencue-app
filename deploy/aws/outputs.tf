output "ecr_repository_url" {
  description = "Push the API image here (tag :latest)."
  value       = aws_ecr_repository.api.repository_url
}

output "api_url" {
  description = "Public API endpoint (after DNS + cert validation)."
  value       = "https://${var.api_subdomain}"
}

output "alb_dns_name" {
  description = "Load balancer hostname (works before DNS is repointed)."
  value       = aws_lb.api.dns_name
}

output "rds_endpoint" {
  value = aws_db_instance.main.address
}

output "hosted_zone_nameservers" {
  description = "Zone nameservers (domain registered in-account already delegates here)."
  value       = data.aws_route53_zone.main.name_servers
}
