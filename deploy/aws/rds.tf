resource "aws_db_subnet_group" "main" {
  name       = "${var.name}-db-subnets"
  subnet_ids = aws_subnet.private[*].id
  tags       = { Name = "${var.name}-db-subnets" }
}

resource "aws_db_instance" "main" {
  identifier     = "${var.name}-db"
  engine         = "postgres"
  engine_version = "16"

  instance_class        = var.db_instance_class
  allocated_storage     = var.db_allocated_storage
  max_allocated_storage = 100
  storage_type          = "gp3"
  storage_encrypted     = true

  db_name  = var.db_name
  username = var.db_username
  password = var.db_password
  port     = 5432

  db_subnet_group_name   = aws_db_subnet_group.main.name
  vpc_security_group_ids = [aws_security_group.db.id]
  publicly_accessible    = false
  multi_az               = false

  backup_retention_period = 7
  deletion_protection     = false
  skip_final_snapshot     = true
  apply_immediately       = true

  tags = { Name = "${var.name}-db" }
}

# Npgsql-format connection string the API reads from NEON_CONNECTION_STRING.
# SSL is required by RDS; Trust Server Certificate avoids bundling the RDS CA.
locals {
  db_connection_string = join(";", [
    "Host=${aws_db_instance.main.address}",
    "Port=5432",
    "Username=${var.db_username}",
    "Password=${var.db_password}",
    "Database=${var.db_name}",
    "SSL Mode=Require",
    "Trust Server Certificate=true",
  ])
}
