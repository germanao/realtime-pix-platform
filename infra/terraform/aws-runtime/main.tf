data "aws_vpc" "default" {
  default = true
}

data "aws_subnets" "default" {
  filter {
    name   = "vpc-id"
    values = [data.aws_vpc.default.id]
  }
}

data "aws_ssm_parameter" "al2023_x86_64" {
  name = "/aws/service/ami-amazon-linux-latest/al2023-ami-kernel-default-x86_64"
}

data "aws_ec2_managed_prefix_list" "cloudfront" {
  name = "com.amazonaws.global.cloudfront.origin-facing"
}

locals {
  name               = "realtime-pix-${var.environment_name}"
  runtime_param_path = "/realtime-pix/${var.environment_name}"
  repositories = toset([
    "api-gateway",
    "identity-presence-service",
    "bank-ledger-service",
    "transaction-service",
    "realtime-events-service"
  ])
}

# Retained after the AWS Free plan denied GitHub OIDC. They are empty and
# harmless, and keeping them avoids destructive cleanup during recovery.
resource "aws_ecr_repository" "service" {
  for_each             = local.repositories
  name                 = "realtime-pix/${each.key}"
  image_tag_mutability = "IMMUTABLE"

  image_scanning_configuration {
    scan_on_push = true
  }
}

resource "aws_ecr_lifecycle_policy" "service" {
  for_each   = aws_ecr_repository.service
  repository = each.value.name
  policy = jsonencode({
    rules = [{
      rulePriority = 1
      description  = "Retain the ten newest demo images"
      selection = {
        tagStatus   = "any"
        countType   = "imageCountMoreThan"
        countNumber = 10
      }
      action = { type = "expire" }
    }]
  })
}

resource "aws_iam_role" "runtime" {
  name = "${local.name}-runtime"
  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Service = "ec2.amazonaws.com" }
      Action    = "sts:AssumeRole"
    }]
  })
}

resource "aws_iam_role_policy_attachment" "ssm_core" {
  role       = aws_iam_role.runtime.name
  policy_arn = "arn:aws:iam::aws:policy/AmazonSSMManagedInstanceCore"
}

resource "aws_iam_role_policy" "runtime" {
  name = "${local.name}-runtime"
  role = aws_iam_role.runtime.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect   = "Allow"
        Action   = ["ssm:GetParameter", "ssm:GetParameters", "ssm:GetParametersByPath"]
        Resource = "arn:aws:ssm:${var.aws_region}:*:parameter${local.runtime_param_path}/*"
      }
    ]
  })
}

resource "aws_iam_instance_profile" "runtime" {
  name = local.name
  role = aws_iam_role.runtime.name
}

resource "random_password" "origin_header" {
  length  = 32
  special = false
}

resource "aws_security_group" "runtime" {
  name        = local.name
  description = "CloudFront-only ingress for the realtime PIX demo"
  vpc_id      = data.aws_vpc.default.id

  ingress {
    description     = "HTTP from CloudFront origins"
    from_port       = 80
    to_port         = 80
    protocol        = "tcp"
    prefix_list_ids = [data.aws_ec2_managed_prefix_list.cloudfront.id]
  }

  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }
}

resource "aws_instance" "runtime" {
  ami                         = data.aws_ssm_parameter.al2023_x86_64.value
  instance_type               = var.instance_type
  subnet_id                   = sort(data.aws_subnets.default.ids)[0]
  vpc_security_group_ids      = [aws_security_group.runtime.id]
  iam_instance_profile        = aws_iam_instance_profile.runtime.name
  associate_public_ip_address = true
  # Subsequent bootstrap changes are rolled out with SSM to avoid replacing
  # the persistent demo host and its disk.
  user_data_replace_on_change = false
  user_data = templatefile("${path.module}/user-data.sh.tftpl", {
    aws_region          = var.aws_region
    parameter_path      = local.runtime_param_path
    origin_header       = random_password.origin_header.result
    allowed_cors_origin = var.allowed_cors_origin
  })

  root_block_device {
    volume_type           = "gp3"
    volume_size           = 20
    encrypted             = true
    delete_on_termination = true
  }

  metadata_options {
    http_endpoint = "enabled"
    http_tokens   = "required"
  }

  tags = { Name = local.name }
}

data "aws_caller_identity" "current" {}

resource "aws_eip" "runtime" {
  domain = "vpc"
  tags   = { Name = local.name }
}

resource "aws_eip_association" "runtime" {
  instance_id   = aws_instance.runtime.id
  allocation_id = aws_eip.runtime.id
}

resource "aws_cloudfront_origin_request_policy" "realtime" {
  name = "${local.name}-all-viewer"
  cookies_config { cookie_behavior = "all" }
  query_strings_config { query_string_behavior = "all" }
  headers_config { header_behavior = "allViewer" }
}

resource "aws_cloudfront_cache_policy" "disabled" {
  name        = "${local.name}-no-cache"
  default_ttl = 0
  max_ttl     = 0
  min_ttl     = 0
  parameters_in_cache_key_and_forwarded_to_origin {
    enable_accept_encoding_brotli = false
    enable_accept_encoding_gzip   = false
    cookies_config { cookie_behavior = "none" }
    headers_config { header_behavior = "none" }
    query_strings_config { query_string_behavior = "none" }
  }
}

resource "aws_cloudfront_distribution" "runtime" {
  enabled         = true
  is_ipv6_enabled = true
  comment         = local.name
  price_class     = "PriceClass_100"

  origin {
    # Bind CloudFront to the Elastic IP. The instance's initial public DNS can
    # retain the launch-time ephemeral address after the EIP is associated.
    domain_name = aws_eip.runtime.public_dns
    origin_id   = "ec2-runtime"
    custom_header {
      name  = "X-Realtime-Pix-Origin"
      value = random_password.origin_header.result
    }
    custom_origin_config {
      http_port              = 80
      https_port             = 443
      origin_protocol_policy = "http-only"
      origin_ssl_protocols   = ["TLSv1.2"]
    }
  }

  default_cache_behavior {
    target_origin_id         = "ec2-runtime"
    viewer_protocol_policy   = "redirect-to-https"
    allowed_methods          = ["DELETE", "GET", "HEAD", "OPTIONS", "PATCH", "POST", "PUT"]
    cached_methods           = ["GET", "HEAD", "OPTIONS"]
    compress                 = false
    cache_policy_id          = aws_cloudfront_cache_policy.disabled.id
    origin_request_policy_id = aws_cloudfront_origin_request_policy.realtime.id
  }

  restrictions {
    geo_restriction {
      restriction_type = "none"
    }
  }
  viewer_certificate {
    cloudfront_default_certificate = true
  }
}

resource "aws_iam_role" "scheduler" {
  name = "${local.name}-scheduler"
  assume_role_policy = jsonencode({
    Version   = "2012-10-17"
    Statement = [{ Effect = "Allow", Principal = { Service = "scheduler.amazonaws.com" }, Action = "sts:AssumeRole" }]
  })
}

resource "aws_iam_role_policy" "scheduler" {
  name = "${local.name}-scheduler"
  role = aws_iam_role.scheduler.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = ["ec2:StartInstances", "ec2:StopInstances"]
      Resource = aws_instance.runtime.arn
    }]
  })
}

resource "aws_scheduler_schedule" "start" {
  name                         = "${local.name}-weekday-start"
  schedule_expression          = "cron(40 8 ? * MON-FRI *)"
  schedule_expression_timezone = "America/Sao_Paulo"
  flexible_time_window { mode = "OFF" }
  target {
    arn      = "arn:aws:scheduler:::aws-sdk:ec2:startInstances"
    role_arn = aws_iam_role.scheduler.arn
    input    = jsonencode({ InstanceIds = [aws_instance.runtime.id] })
    retry_policy {
      maximum_event_age_in_seconds = 3600
      maximum_retry_attempts       = 2
    }
  }
}

resource "aws_scheduler_schedule" "stop" {
  name                         = "${local.name}-weekday-stop"
  schedule_expression          = "cron(10 15 ? * MON-FRI *)"
  schedule_expression_timezone = "America/Sao_Paulo"
  flexible_time_window { mode = "OFF" }
  target {
    arn      = "arn:aws:scheduler:::aws-sdk:ec2:stopInstances"
    role_arn = aws_iam_role.scheduler.arn
    input    = jsonencode({ InstanceIds = [aws_instance.runtime.id] })
    retry_policy {
      maximum_event_age_in_seconds = 3600
      maximum_retry_attempts       = 2
    }
  }
}

resource "aws_budgets_budget" "monthly" {
  name         = "${local.name}-monthly"
  budget_type  = "COST"
  limit_amount = tostring(var.monthly_budget_usd)
  limit_unit   = "USD"
  time_unit    = "MONTHLY"

  dynamic "notification" {
    for_each = toset([50, 80, 100])
    content {
      comparison_operator        = "GREATER_THAN"
      threshold                  = notification.value
      threshold_type             = "PERCENTAGE"
      notification_type          = "FORECASTED"
      subscriber_email_addresses = [var.budget_email]
    }
  }
}
