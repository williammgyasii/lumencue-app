# Admin bastion for inspecting RDS from a local SQL client via SSM port-forwarding.
# - No public ports (security group has zero inbound rules).
# - Reached only through AWS SSM using your IAM credentials, so it is not
#   internet-attackable even though it sits in a public subnet for SSM egress.
# - t3.micro is Free Tier eligible (750 hrs/mo). Stop it when idle to use ~nothing.

data "aws_ssm_parameter" "al2023" {
  count = var.enable_bastion ? 1 : 0
  name  = "/aws/service/ami-amazon-linux-latest/al2023-ami-kernel-default-x86_64"
}

data "aws_iam_policy_document" "ec2_assume" {
  statement {
    actions = ["sts:AssumeRole"]
    principals {
      type        = "Service"
      identifiers = ["ec2.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "bastion" {
  count              = var.enable_bastion ? 1 : 0
  name               = "${var.name}-bastion-role"
  assume_role_policy = data.aws_iam_policy_document.ec2_assume.json
}

resource "aws_iam_role_policy_attachment" "bastion_ssm" {
  count      = var.enable_bastion ? 1 : 0
  role       = aws_iam_role.bastion[0].name
  policy_arn = "arn:aws:iam::aws:policy/AmazonSSMManagedInstanceCore"
}

resource "aws_iam_instance_profile" "bastion" {
  count = var.enable_bastion ? 1 : 0
  name  = "${var.name}-bastion-profile"
  role  = aws_iam_role.bastion[0].name
}

resource "aws_security_group" "bastion" {
  count       = var.enable_bastion ? 1 : 0
  name        = "${var.name}-bastion-sg"
  description = "Bastion: no inbound, outbound only (SSM + DB)"
  vpc_id      = aws_vpc.main.id

  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = { Name = "${var.name}-bastion-sg" }
}

resource "aws_instance" "bastion" {
  count                       = var.enable_bastion ? 1 : 0
  ami                         = data.aws_ssm_parameter.al2023[0].value
  instance_type               = "t3.micro"
  subnet_id                   = aws_subnet.public[0].id
  associate_public_ip_address = true
  vpc_security_group_ids      = [aws_security_group.bastion[0].id]
  iam_instance_profile        = aws_iam_instance_profile.bastion[0].name

  tags = { Name = "${var.name}-bastion" }
}

output "bastion_instance_id" {
  description = "Use with aws ssm start-session."
  value       = var.enable_bastion ? aws_instance.bastion[0].id : null
}

output "bastion_port_forward_command" {
  description = "Run locally, then connect DataGrip to localhost:5432."
  value = var.enable_bastion ? join(" ", [
    "aws ssm start-session --profile lumencue-prod --region ${var.region}",
    "--target ${aws_instance.bastion[0].id}",
    "--document-name AWS-StartPortForwardingSessionToRemoteHost",
    "--parameters host=${aws_db_instance.main.address},portNumber=5432,localPortNumber=5432",
  ]) : null
}
