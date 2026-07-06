#!/usr/bin/env bash
set -euo pipefail

OWNER="${OWNER:-germanao}"
REPO="${REPO:-realtime-pix-platform}"
BRANCH="${BRANCH:-main}"

gh repo edit "${OWNER}/${REPO}" \
  --delete-branch-on-merge \
  --enable-auto-merge=false \
  --enable-discussions=false \
  --enable-issues=true \
  --enable-projects=false \
  --enable-wiki=false

body="$(mktemp)"
cat > "${body}" <<JSON
{
  "required_status_checks": {
    "strict": true,
    "contexts": [
      "Backend build and tests",
      "Frontend build and tests",
      "Terraform format and validate",
      "Dockerfile checks"
    ]
  },
  "enforce_admins": false,
  "required_pull_request_reviews": {
    "required_approving_review_count": 1
  },
  "restrictions": null,
  "required_linear_history": true,
  "allow_force_pushes": false,
  "allow_deletions": false,
  "block_creations": false,
  "required_conversation_resolution": false,
  "lock_branch": false,
  "allow_fork_syncing": true
}
JSON

gh api \
  --method PUT \
  -H "Accept: application/vnd.github+json" \
  "/repos/${OWNER}/${REPO}/branches/${BRANCH}/protection" \
  --input "${body}"

rm -f "${body}"

echo "Repository and branch protection configured for ${OWNER}/${REPO}:${BRANCH}."
