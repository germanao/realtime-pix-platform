variable "aws_region" {
  description = "AWS project region. The free-plan project SCP currently permits us-east-2."
  type        = string
  default     = "us-east-2"
}

variable "environment_name" {
  type    = string
  default = "poc"
}

variable "instance_type" {
  type    = string
  default = "c7i-flex.large"
}

variable "monthly_budget_usd" {
  type    = number
  default = 15
}

variable "budget_email" {
  description = "Email that receives AWS budget notifications."
  type        = string
}

variable "allowed_cors_origin" {
  type    = string
  default = "https://realtime-pix-web.vercel.app"
}
