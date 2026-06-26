# Public-read bucket that hosts custom Bible translation files (e.g. the Passion Translation).
# The desktop app downloads each file once and caches it locally, so this only serves a handful of
# static JSON files. Objects live under translations/<code>.json; only that prefix is public.

data "aws_caller_identity" "current" {}

resource "aws_s3_bucket" "bibles" {
  bucket = "${var.name}-bibles-${data.aws_caller_identity.current.account_id}"
}

# We grant public read through a bucket policy (not ACLs), so leave ACLs blocked but allow the
# policy to take effect.
resource "aws_s3_bucket_public_access_block" "bibles" {
  bucket                  = aws_s3_bucket.bibles.id
  block_public_acls       = true
  ignore_public_acls      = true
  block_public_policy     = false
  restrict_public_buckets = false
}

resource "aws_s3_bucket_policy" "bibles_public_read" {
  bucket = aws_s3_bucket.bibles.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Sid       = "PublicReadTranslations"
      Effect    = "Allow"
      Principal = "*"
      Action    = "s3:GetObject"
      Resource  = "${aws_s3_bucket.bibles.arn}/translations/*"
    }]
  })

  depends_on = [aws_s3_bucket_public_access_block.bibles]
}

output "bibles_bucket" {
  description = "Name of the public Bible-hosting bucket."
  value       = aws_s3_bucket.bibles.bucket
}

output "bibles_base_url" {
  description = "Base URL the app uses to download translation files."
  value       = "https://${aws_s3_bucket.bibles.bucket}.s3.${var.region}.amazonaws.com/translations"
}
