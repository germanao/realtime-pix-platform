output "instance_id" { value = aws_instance.runtime.id }
output "elastic_ip" { value = aws_eip.runtime.public_ip }
output "cloudfront_url" { value = "https://${aws_cloudfront_distribution.runtime.domain_name}" }
output "ecr_repositories" { value = { for key, repo in aws_ecr_repository.service : key => repo.repository_url } }
output "runtime_parameter_path" { value = local.runtime_param_path }
