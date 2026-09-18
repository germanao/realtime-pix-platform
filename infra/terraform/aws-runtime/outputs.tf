output "instance_id" { value = aws_instance.runtime.id }
output "elastic_ip" { value = aws_eip.runtime.public_ip }
output "cloudfront_url" { value = "https://${aws_cloudfront_distribution.runtime.domain_name}" }
output "runtime_parameter_path" { value = local.runtime_param_path }
