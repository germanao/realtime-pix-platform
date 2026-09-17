"""Run on the existing host through SSM after publishing pinned AWS images.

Reads the old Azure credentials only on the host. Stops writers, exports all five
service databases, verifies restored table counts, archives dumps to private S3,
and only then replaces the application configuration. Never deletes Azure.
"""
import hashlib
import json
import os
from pathlib import Path
import secrets
import shutil
import subprocess
import sys
import time
import urllib.parse
import urllib.request

ROOT = Path('/opt/realtime-pix')
STAGE = ROOT / 'migration-aws-only'
BACKUPS = STAGE / 'backups'
BUCKET = 's3://realtime-pix-tfstate-886781461608/backups/aws-only'
REGION = 'us-east-2'
DATABASES = {
    'IDENTITY_DB_CONNECTION': ('identity_presence_db', 'identity_app'),
    'BANK_A_DB_CONNECTION': ('bank_a_ledger_db', 'bank_a_app'),
    'BANK_B_DB_CONNECTION': ('bank_b_ledger_db', 'bank_b_app'),
    'TRANSACTION_DB_CONNECTION': ('transaction_db', 'transaction_app'),
    'REALTIME_DB_CONNECTION': ('realtime_projection_db', 'realtime_app'),
    'LEGACY_DB_CONNECTION': ('wallet_ledger_db', 'legacy_wallet_app'),
}


def run(args, *, env=None, data=None):
    result = subprocess.run(args, env=env, input=data, capture_output=True, text=True)
    if result.returncode:
        # Never include argument lists: they can contain newly generated passwords.
        raise RuntimeError(f'{args[0]} failed: {result.stderr[-2500:]}')
    return result.stdout


def read_env(path):
    return dict(line.split('=', 1) for line in path.read_text().splitlines()
                if '=' in line and not line.startswith('#'))


def save_private(path, text):
    path.write_text(text)
    path.chmod(0o600)


def source_environment(old, key, token):
    settings = {k.lower(): v.strip('"') for k, v in
                (part.split('=', 1) for part in old[key].split(';') if '=' in part)}
    return dict(os.environ, PGHOST=settings['host'], PGDATABASE=settings['database'],
                PGUSER=settings.get('username', settings.get('user id')), PGPASSWORD=token,
                PGSSLMODE='require', PGCONNECT_TIMEOUT='15')


def client(env, program, *args, data=None):
    return run(['docker', 'run', '--rm', '-i', '--network', 'realtime-pix-aws_default',
                '-v', f'{BACKUPS}:/backups',
                *[a for key in ['PGHOST', 'PGDATABASE', 'PGUSER', 'PGPASSWORD', 'PGSSLMODE', 'PGCONNECT_TIMEOUT']
                  for a in ['-e', key]], 'postgres:16-alpine', program, *args], env=env, data=data)


COUNT_SQL = """SELECT format('SELECT %L || ''|'' || count(*) FROM %I.%I;',
    tablename, schemaname, tablename) FROM pg_tables WHERE schemaname='public' ORDER BY tablename
\gexec
"""


def counts(env):
    return client(env, 'psql', '-X', '-At', '-v', 'ON_ERROR_STOP=1', data=COUNT_SQL).strip()


def main():
    os.chdir(ROOT)
    os.umask(0o077)
    if (STAGE / 'complete.json').exists():
        raise RuntimeError('Migration already completed; refusing to recopy live databases.')
    if (ROOT / 'data/postgres/PG_VERSION').exists():
        raise RuntimeError('A destination database already exists. Inspect it before resuming; nothing was overwritten.')
    tag = sys.argv[1]
    if not tag.startswith('aws-') or len(tag) != 44:
        raise RuntimeError('A pinned aws-<40-character commit> image tag is required.')
    BACKUPS.mkdir(parents=True, exist_ok=True)
    old = read_env(ROOT / '.env')
    # Preserve the retired wallet's historical data as well as the active services.
    identity_parts = old['IDENTITY_DB_CONNECTION'].split(';')
    old['LEGACY_DB_CONNECTION'] = ';'.join('Database=wallet_ledger_db' if p.lower().startswith('database=') else p for p in identity_parts)
    body = urllib.parse.urlencode({'client_id': old['AZURE_CLIENT_ID'],
        'client_secret': old['AZURE_CLIENT_SECRET'], 'grant_type': 'client_credentials',
        'scope': 'https://ossrdbms-aad.database.windows.net/.default'}).encode()
    request = urllib.request.Request('https://login.microsoftonline.com/' + old['AZURE_TENANT_ID'] + '/oauth2/v2.0/token', data=body)
    token = json.load(urllib.request.urlopen(request, timeout=30))['access_token']
    new = {'POSTGRES_PASSWORD': secrets.token_hex(24), 'RUNTIME_IMAGE_TAG': tag}
    roles = {}
    for key, (database, role) in {**DATABASES, 'BUS_DB_CONNECTION': ('event_bus_db', 'event_bus_app')}.items():
        password = secrets.token_hex(24)
        roles[key] = (database, role, password)
        new[key] = f'Host=postgres;Database={database};Username={role};Password={password};SSL Mode=Disable;Maximum Pool Size=12;Timeout=10;Command Timeout=30'
    save_private(STAGE / 'aws.env', ''.join(f'{k}={v}\n' for k, v in new.items()))
    compose = ['docker', 'compose', '--env-file', str(STAGE / 'aws.env'), '-f', str(STAGE / 'compose.aws.yml'), '--project-directory', str(ROOT)]
    run([*compose, 'pull'])
    shutil.copy2(ROOT / 'docker-compose.yml', STAGE / 'azure-compose.yml')
    shutil.copy2(ROOT / '.env', STAGE / 'azure.env')
    (STAGE / 'azure.env').chmod(0o600)
    # Stop ingress first. Source Azure applications are already suspended.
    run(['docker', 'compose', 'stop', 'edge'])
    run(['docker', 'compose', 'stop', '-t', '30'])
    print('Application writers stopped. Copying source databases.', flush=True)
    manifest = {}
    for key, (database, role) in DATABASES.items():
        source = source_environment(old, key, token)
        before = counts(source)
        client(source, 'pg_dump', '--format=custom', '--no-owner', '--no-privileges', '--file', f'/backups/{database}.dump')
        after = counts(source)
        if before != after:
            raise RuntimeError('Source rows changed during backup; do not cut over or delete Azure.')
        manifest[database] = {'rows': after, 'sha256': hashlib.sha256((BACKUPS / f'{database}.dump').read_bytes()).hexdigest()}
        print('Exported ' + database, flush=True)
    save_private(BACKUPS / 'manifest.json', json.dumps(manifest, indent=2))
    # Off-host recovery copy is required before changing routing or deleting Azure.
    backup_uri = BUCKET + '/' + time.strftime('%Y%m%dT%H%M%SZ', time.gmtime())
    run(['aws', 's3', 'cp', str(BACKUPS), backup_uri, '--recursive', '--sse', 'AES256', '--region', REGION])
    run([*compose, 'up', '-d', '--wait', 'postgres'])
    admin = dict(os.environ, PGHOST='postgres', PGDATABASE='postgres', PGUSER='postgres',
                 PGPASSWORD=new['POSTGRES_PASSWORD'], PGSSLMODE='disable', PGCONNECT_TIMEOUT='10')
    for key, (database, role, password) in roles.items():
        client(admin, 'psql', '-X', '-v', 'ON_ERROR_STOP=1', data=f"CREATE ROLE {role} LOGIN PASSWORD '{password}';\nCREATE DATABASE {database} OWNER {role};\nREVOKE CONNECT ON DATABASE {database} FROM PUBLIC;\nGRANT CONNECT ON DATABASE {database} TO {role};\n")
        destination = dict(admin, PGDATABASE=database, PGUSER=role, PGPASSWORD=password)
        if key == 'BUS_DB_CONNECTION':
            client(destination, 'psql', '-X', '-v', 'ON_ERROR_STOP=1', data=(STAGE / 'bus-schema.sql').read_text())
        else:
            client(destination, 'pg_restore', '--exit-on-error', '--no-owner', '--no-privileges', '--dbname', database, f'/backups/{database}.dump')
            if counts(destination) != manifest[database]['rows']:
                raise RuntimeError('Restored row counts differ for ' + database)
            print('Verified restored table counts: ' + database, flush=True)
    # Replay the durable outbox into the new transport. Existing inbox keys and
    # idempotent command handlers suppress already completed work; unpublished or
    # stranded Service Bus messages are not discarded at the provider boundary.
    bus_database, bus_role, bus_password = roles['BUS_DB_CONNECTION']
    bus_env = dict(admin, PGDATABASE=bus_database, PGUSER=bus_role, PGPASSWORD=bus_password)
    for key, (database, role) in DATABASES.items():
        if key == 'LEGACY_DB_CONNECTION':
            continue
        destination = dict(admin, PGDATABASE=database, PGUSER=role, PGPASSWORD=roles[key][2])
        envelopes = client(destination, 'psql', '-X', '-At', '-v', 'ON_ERROR_STOP=1', '-c', 'SELECT "EnvelopeJson"::text FROM integration_outbox_messages ORDER BY "OccurredAt";')
        rows = '\n'.join(json.dumps(json.loads(raw), separators=(',', ':')) for raw in envelopes.splitlines())
        if rows:
            script = "CREATE TEMP TABLE migration_envelopes (j jsonb);\nCOPY migration_envelopes FROM STDIN WITH (FORMAT csv, DELIMITER E'\\x01', QUOTE E'\\x02');\n"
            script += rows + '\n\\.\n'
            script += "INSERT INTO bus_messages(id,kind,destination,envelope) SELECT (j->>'eventId')::uuid,coalesce(j->>'messageKind','Event'),coalesce(j->>'destination','platform-events'),j FROM migration_envelopes ON CONFLICT(id) DO NOTHING;\n"
            client(bus_env, 'psql', '-X', '-v', 'ON_ERROR_STOP=1', data=script)
    shutil.copy2(STAGE / 'aws.env', ROOT / '.env')
    shutil.copy2(STAGE / 'compose.aws.yml', ROOT / 'docker-compose.yml')
    run(['aws', 'ssm', 'put-parameter', '--name', '/realtime-pix/poc/aws-only-env', '--type', 'SecureString', '--overwrite', '--value', 'file://' + str(ROOT / '.env'), '--region', REGION])
    start = Path('/usr/local/sbin/realtime-pix-start')
    start.write_text(start.read_text().replace('/compose-env', '/aws-only-env'))
    run(['docker', 'compose', 'up', '-d', '--remove-orphans'])
    run(['docker', 'compose', 'restart', 'edge'])
    report = {'backup': backup_uri, 'databases': list(DATABASES.values()), 'imageTag': tag, 'rowCountsVerified': True}
    save_private(STAGE / 'complete.json', json.dumps(report, indent=2))
    print(json.dumps(report), flush=True)


if __name__ == '__main__':
    main()
