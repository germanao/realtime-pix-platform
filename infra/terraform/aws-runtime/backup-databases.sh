#!/bin/bash
set -euo pipefail
umask 077
cd /opt/realtime-pix
backup_dir=$(mktemp -d /opt/realtime-pix/backup.XXXXXX)
stamp=$(date -u +%Y%m%dT%H%M%SZ)
account_id=$(aws sts get-caller-identity --query Account --output text)
backup_bucket="realtime-pix-tfstate-$account_id"
for database in identity_presence_db bank_a_ledger_db bank_b_ledger_db transaction_db realtime_projection_db event_bus_db; do
  docker compose exec -T postgres pg_dump -U postgres -d "$database" -Fc > "$backup_dir/$database.dump"
  aws s3 cp "$backup_dir/$database.dump" "s3://$backup_bucket/backups/aws-only/periodic/$stamp/$database.dump" --sse AES256 --region us-east-2 --only-show-errors
  # Remove only the exact temporary file after successful off-host upload.
  rm -- "$backup_dir/$database.dump"
done
rmdir -- "$backup_dir"
