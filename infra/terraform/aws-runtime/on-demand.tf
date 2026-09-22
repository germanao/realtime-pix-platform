data "archive_file" "controller" {
  type        = "zip"
  source_file = "${path.module}/controller.py"
  output_path = "${path.module}/.terraform/controller.zip"
}

resource "aws_dynamodb_table" "runtime_state" {
  name         = "${local.name}-runtime-state"
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "id"
  attribute {
    name = "id"
    type = "S"
  }
}

resource "aws_iam_role" "controller" {
  name = "${local.name}-wake-controller"
  assume_role_policy = jsonencode({
    Version   = "2012-10-17"
    Statement = [{ Effect = "Allow", Principal = { Service = "lambda.amazonaws.com" }, Action = "sts:AssumeRole" }]
  })
}

resource "aws_cloudwatch_log_group" "controller" {
  name              = "/aws/lambda/${local.name}-wake"
  retention_in_days = 7
}

resource "aws_iam_role_policy" "controller" {
  name = "${local.name}-wake-controller"
  role = aws_iam_role.controller.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      { Effect = "Allow", Action = ["ec2:StartInstances", "ec2:StopInstances"], Resource = aws_instance.runtime.arn },
      { Effect = "Allow", Action = ["ec2:DescribeInstances"], Resource = "*" },
      { Effect = "Allow", Action = ["dynamodb:UpdateItem"], Resource = aws_dynamodb_table.runtime_state.arn },
      { Effect = "Allow", Action = ["logs:CreateLogStream", "logs:PutLogEvents"], Resource = "${aws_cloudwatch_log_group.controller.arn}:*" }
    ]
  })
}

resource "aws_lambda_function" "controller" {
  function_name    = "${local.name}-wake"
  role             = aws_iam_role.controller.arn
  runtime          = "python3.13"
  handler          = "controller.handler"
  filename         = data.archive_file.controller.output_path
  source_code_hash = data.archive_file.controller.output_base64sha256
  timeout          = 20
  memory_size      = 128
  environment {
    variables = {
      INSTANCE_ID   = aws_instance.runtime.id
      STATE_TABLE   = aws_dynamodb_table.runtime_state.name
      ORIGIN_SECRET = random_password.origin_header.result
      DAILY_SECONDS = "86400"
      IDLE_SECONDS  = "1200"
    }
  }
  depends_on = [aws_iam_role_policy.controller]
}

resource "aws_lambda_function_url" "controller" {
  function_name      = aws_lambda_function.controller.function_name
  authorization_type = "NONE"
  cors {
    allow_origins = [var.allowed_cors_origin]
    allow_methods = ["POST"]
    allow_headers = ["content-type"]
    max_age       = 300
  }
}

resource "aws_lambda_permission" "controller_url" {
  statement_id           = "FunctionUrlInvoke"
  action                 = "lambda:InvokeFunctionUrl"
  function_name          = aws_lambda_function.controller.function_name
  principal              = "*"
  function_url_auth_type = "NONE"
}

resource "aws_lambda_permission" "controller_invoke" {
  statement_id             = "FunctionUrlOnly"
  action                   = "lambda:InvokeFunction"
  function_name            = aws_lambda_function.controller.function_name
  principal                = "*"
  invoked_via_function_url = true
}

resource "aws_cloudwatch_event_rule" "idle_sweep" {
  name                = "${local.name}-idle-sweep"
  schedule_expression = "rate(5 minutes)"
}

resource "aws_cloudwatch_event_target" "idle_sweep" {
  rule  = aws_cloudwatch_event_rule.idle_sweep.name
  arn   = aws_lambda_function.controller.arn
  input = jsonencode({ action = "sweep" })
}

resource "aws_lambda_permission" "idle_sweep" {
  statement_id  = "ScheduledIdleSweep"
  action        = "lambda:InvokeFunction"
  function_name = aws_lambda_function.controller.function_name
  principal     = "events.amazonaws.com"
  source_arn    = aws_cloudwatch_event_rule.idle_sweep.arn
}

data "aws_cloudfront_origin_request_policy" "except_host" {
  name = "Managed-AllViewerExceptHostHeader"
}
