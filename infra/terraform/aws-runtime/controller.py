"""Public wake + scheduled idle shutdown for exactly one demo EC2 instance.

No credentials reach the browser. CloudFront supplies the origin secret. A
DynamoDB conditional lease serializes wake/sweep decisions across invocations.
The daily runtime allowance is enforced even if an anonymous caller keeps waking.
"""
import hmac
import json
import os
import time
import uuid


def decide(state, now, instance_state, launched_at, wake, daily_seconds=86400, idle_seconds=1200):
    state = dict(state)
    day = now // 86400
    used = int(state.get("used", 0)) if state.get("day") == day else 0
    active = instance_state in ("pending", "running", "stopping")
    if active or state.get("active", False):
        since = int(state.get("metered", launched_at))
        used += max(0, now - max(since, day * 86400))
    state.update(day=day, used=used, metered=now, active=active)
    state.setdefault("activity", now)
    exhausted = used >= daily_seconds
    if wake and not exhausted:
        state["activity"] = now
    idle = now - int(state["activity"]) >= idle_seconds
    action = None
    status = instance_state
    if exhausted or (idle and not wake):
        if instance_state in ("running", "pending"):
            action = "stop"
        status = "daily-limit" if exhausted else "idle"
    elif wake and instance_state == "stopped":
        action = "start"
        state.update(active=True, metered=now)
        status = "starting"
    return state, action, status


def response(code, status):
    return {"statusCode": code, "headers": {
        "content-type": "application/json", "cache-control": "no-store", "retry-after": "5"
    }, "body": json.dumps({"status": status})}


def handler(event, context):
    import boto3
    from botocore.exceptions import ClientError

    http = event.get("requestContext", {}).get("http")
    wake = http is not None
    if wake:
        headers = {key.lower(): value for key, value in event.get("headers", {}).items()}
        if not hmac.compare_digest(headers.get("x-realtime-pix-origin", ""), os.environ["ORIGIN_SECRET"]):
            return response(403, "forbidden")
        if http.get("method") != "POST" or event.get("rawPath") != "/runtime/wake":
            return response(405, "method-not-allowed")
    elif event.get("action") != "sweep":
        return response(400, "invalid-event")

    now = int(time.time())
    token = str(uuid.uuid4())
    table = boto3.resource("dynamodb").Table(os.environ["STATE_TABLE"])
    key = {"id": "runtime"}
    try:
        locked = table.update_item(Key=key,
            UpdateExpression="SET lockUntil = :until, lockToken = :token",
            ConditionExpression="attribute_not_exists(lockUntil) OR lockUntil < :now",
            ExpressionAttributeValues={":until": now + 30, ":token": token, ":now": now},
            ReturnValues="ALL_NEW")["Attributes"]
    except ClientError as error:
        if error.response["Error"]["Code"] == "ConditionalCheckFailedException":
            return response(429, "busy")
        raise

    try:
        ec2 = boto3.client("ec2")
        instance_id = os.environ["INSTANCE_ID"]
        instance = ec2.describe_instances(InstanceIds=[instance_id])["Reservations"][0]["Instances"][0]
        state, action, status = decide(json.loads(locked.get("payload", "{}")), now,
            instance["State"]["Name"], int(instance["LaunchTime"].timestamp()), wake,
            int(os.environ.get("DAILY_SECONDS", "86400")), int(os.environ.get("IDLE_SECONDS", "1200")))
        # Persist accounting BEFORE EC2 actions; a crash cannot erase charged runtime.
        table.update_item(Key=key, UpdateExpression="SET payload = :payload",
            ConditionExpression="lockToken = :token",
            ExpressionAttributeValues={":payload": json.dumps(state), ":token": token})
        if action == "start":
            ec2.start_instances(InstanceIds=[instance_id])
        elif action == "stop":
            ec2.stop_instances(InstanceIds=[instance_id])
        # 409 makes the browser show the daily-limit notice without retrying wake.
        return response(409 if status == "daily-limit" else 202 if status != "running" else 200, status)
    finally:
        table.update_item(Key=key, UpdateExpression="REMOVE lockUntil, lockToken",
            ConditionExpression="lockToken = :token", ExpressionAttributeValues={":token": token})
