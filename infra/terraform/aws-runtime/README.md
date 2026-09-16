# AWS on-demand runtime

This stack runs the request-serving tier on the existing `c7i-flex.large` EC2 host. A visitor POSTs `/runtime/wake` through CloudFront; a small Lambda starts that one instance if stopped. The browser polls deep readiness before joining. There is no office-hours restriction. Azure Container Apps remain at scale-to-zero as the fallback.

The controller renews activity on each browser heartbeat, stops the host after 20 minutes without activity, and allows six hours of aggregate EC2 runtime per UTC day. EventBridge enforces shutdown every five minutes (so timing has up to five minutes of tolerance). The old weekday start/stop schedules are disabled. A DynamoDB conditional lease serializes decisions; the daily accounting survives Lambda restarts. HTTP 409 signals the daily limit and makes the browser rebind to Azure. All API requests and the events hub stay on one runtime until that explicit rebind or a page reload.

This is credit-conscious, not a guarantee of zero AWS charges: EC2, the retained EIP/disk, Lambda, logs and DynamoDB can consume credits or incur charges under the account's terms. The existing $15 monthly budget is an alert, not a spending cutoff. The runtime allowance caps this host's operating time, not the whole AWS bill. The anonymous wake URL has an origin secret (only CloudFront supplies it), but public callers can consume the shared daily allowance. No browser receives AWS credentials.

The AWS free-plan project currently enforces `us-east-2` with an organization service-control policy. Changing the AWS CLI default region does not change that policy.

Before applying, create the encrypted `/realtime-pix/poc/compose-env` SSM parameter. Never commit its value. The deployment workflow publishes x86-64 images to the existing public GHCR packages under the `aws-latest` tag.

Boot uses cached images and pulls only missing images. Deploy new images explicitly with SSM (`docker compose pull`, then `docker compose up -d`). Bootstrap changes are also rolled out through SSM: cloud-init does not rerun user data on a normal start. The AMI alias and user data are ignored for the existing Terraform instance to avoid unexpected host replacement.

## Verification

Run `python -m unittest -v test_controller.py`, Terraform validation/plan, web unit tests/build and .NET tests before release. Test a stopped instance via the public wake endpoint, readiness, then an actual browser visit. Also verify Azure fallback, one events connection per tab, heartbeat/leave isolation, and expiry after two minutes without heartbeats. Existing `ConnectedAt` stores the lease renewal timestamp; no database schema migration is required. Stale connection rows are excluded from online counts without deleting users or balances.

The free SignalR configuration uses one server connection per hub per deployment and one client events connection per browser. With Azure and AWS both active, two hubs each require four server connections total; ten browser tabs bring the total to fourteen. Presence uses HTTP polling. The service's daily message allowance still applies, so the UI retains HTTP polling when live updates fail.

Rollback: disable the idle-sweep rule and re-enable the two weekday schedules together, then restore the prior frontend routing. Do not disable the shutdown guard while leaving the public wake endpoint active indefinitely. Reverting application images alone does not revert cloud schedules.
