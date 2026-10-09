# Backup and restore runbook

How to back up and restore a TechStrap installation with `deploy/backup.sh` and `deploy/restore.sh`. Both scripts need only Docker and openssl; they run the PostgreSQL client tools and `tar` inside containers, so nothing else is installed on the host.

## Scope

Backed up:

- the TechStrap Postgres database (`pg_dump -Fc`);
- the `techstrap-storage` volume (`/app/storage`: `attachments/{ticketId:N}/{guid:N}` and `kb-images/{guid}.{ext}`);
- the `admin-keys` and `portal-keys` volumes (ASP.NET Data Protection keys, `/app/dataprotection-keys`);
- a plain-text `manifest.txt` with the creation time, the project, the row counts and the size and SHA-256 of every file. It holds no key, passphrase or URL.

Not backed up:

- observability data (logs, traces, Sentry);
- the `/etc/techstrap/<env>/` env files and their secrets: keep them in the operator's secret store;
- reverse-proxy configuration and TLS material;
- container images (re-pullable by tag);
- the external Postgres server configuration and roles.

The email outbox lives in the database (90-day retention, D-039; dead letters are kept), so it is restored with the dump.

Each backup is one directory, `<out>/<project>/<UTC yyyyMMddTHHmmssZ>/`, holding `db.dump.enc`, `techstrap-storage.tar.gz.enc`, `admin-keys.tar.gz.enc`, `portal-keys.tar.gz.enc` and `manifest.txt`. With `--no-encrypt` the `.enc` suffix is dropped. The `backups/` folder is git-ignored.

## RPO and RTO

- RPO (data you can lose): 24 h, with a daily backup.
- RTO (time to be back): 4 h, restore plus verification.

These are the accepted 12b targets (D-051). They are proven on UAT in 12c (T15); until then the rehearsal record below is the only measurement.

## Prerequisites and secrets

- Docker and openssl on the host that runs the scripts.
- The Docker engine can reach the external Postgres through the `techstrap-db` network (the same network the deploy compose uses).
- A passphrase file, readable only by the backup user:

  ```bash
  install -m 0600 /dev/null /etc/techstrap/backup.pass
  openssl rand -base64 36 > /etc/techstrap/backup.pass
  ```

  The passphrase is read from the file; it is never a flag value and never appears in `ps`.
- The database URL in the environment variable `TECHSTRAP_DB_URL`, without a password: `postgresql://techstrap@host:5432/techstrap`. The password goes in the environment variable `PGPASSWORD`, which both live in the same secret store as `.env.api`. The scripts hand both to the container by name (`-e TS_DB_URL`, `-e PGPASSWORD`), so neither appears on a command line or in `ps` on the host, and they refuse a URL that carries a password (`refusing: --db-url must not contain a password; put it in PGPASSWORD`, exit 2). The `Host=...;Database=...` string in `.env.api` (`ConnectionStrings__TechStrap`) is an Npgsql connection string, not a URL, and does not work here.
- Keep a copy of the passphrase off the box. A lost passphrase makes every backup unreadable.

## Taking a backup

Always rehearse with `--dry-run` first: it prints every step and touches no docker, directory or file.

Deployed stack (UAT or production, external Postgres):

```bash
# TECHSTRAP_DB_URL=postgresql://techstrap@host:5432/techstrap and PGPASSWORD are already in the environment (never on the command line)
deploy/backup.sh --project techstrap-uat --out /var/backups/techstrap --keep-days 14 \
  --passphrase-file /etc/techstrap/backup.pass
```

Development stack (the compose file at the repository root, project `techstrap`):

```bash
deploy/backup.sh --project techstrap --db-container <postgres container from docker ps> --no-encrypt --out ./backups
```

`--db-host <host>` is the third database mode (with `--db-network`, `--db-user`, `--db-name` and the password in `PGPASSWORD`). Exactly one mode must be given. In the `--db-container` mode the script reads the container's `POSTGRES_*` variables with `docker exec` and forwards the password as `PGPASSWORD` by name.

The script checks that all three volumes exist before it dumps anything, dumps the database first, then archives the three volumes read-only, then writes the manifest. Files are created with `umask 077`. A failed run removes its half-written directory and exits non-zero. A missing volume is fatal: a backup that quietly lacks the keys volumes is a failed backup.

Schedule it with a systemd timer. `/etc/systemd/system/techstrap-backup.service`:

```ini
[Unit]
Description=TechStrap backup

[Service]
Type=oneshot
EnvironmentFile=/etc/techstrap/backup.env
ExecStart=/opt/techstrap/deploy/backup.sh --project techstrap-uat --out /var/backups/techstrap --keep-days 14 --passphrase-file /etc/techstrap/backup.pass
```

`/etc/techstrap/backup.env` holds `TECHSTRAP_DB_URL=postgresql://techstrap@host:5432/techstrap` and `PGPASSWORD=...` (mode 0600). `/etc/systemd/system/techstrap-backup.timer`:

```ini
[Unit]
Description=Daily TechStrap backup

[Timer]
OnCalendar=*-*-* 02:30:00
Persistent=true
RandomizedDelaySec=10m

[Install]
WantedBy=timers.target
```

Enable it with `systemctl enable --now techstrap-backup.timer`. The cron alternative is a `crontab -e` line (export `TECHSTRAP_DB_URL` and `PGPASSWORD` in the crontab or a wrapper first):

```cron
30 2 * * * /opt/techstrap/deploy/backup.sh --project techstrap-uat --out /var/backups/techstrap --keep-days 14 --passphrase-file /etc/techstrap/backup.pass
```

## Encryption and off-box copy

Files are encrypted with AES-256-CBC and PBKDF2 through `openssl enc`, which needs no extra install. To check or open one file by hand:

```bash
openssl enc -d -aes-256-cbc -pbkdf2 -pass file:/etc/techstrap/backup.pass -in db.dump.enc | pg_restore -l
```

`age` is a fine alternative if you prefer public-key encryption, but the scripts do not use it.

A backup on the same box does not survive the box. After the script succeeds, copy the whole timestamp directory off the box (`rsync` or `rclone`), for example from the same timer unit with `ExecStartPost`.

## Retention

`--keep-days N` removes timestamp directories older than N days under `<out>/<project>/`, after a successful backup only. Only directories whose names look like a backup timestamp are considered. A reasonable policy is 14 days local plus whatever the operator keeps off the box.

## Restoring

`deploy/restore.sh` verifies the size and SHA-256 of every file against the manifest before it restores anything, and refuses a manifest that lacks any of the three volume archives. It restores into a scratch stack by default.

1. Pick the backup directory (`<out>/<project>/<timestamp>`).
2. Restore into a scratch project:

   ```bash
   deploy/restore.sh --from /var/backups/techstrap/techstrap-uat/20261009T023000Z \
     --target-project techstrap-restore --passphrase-file /etc/techstrap/backup.pass
   ```

   This starts a throwaway `postgres:17` container `techstrap-restore-postgres` on the network `techstrap-restore-db` and creates the volumes `techstrap-restore_pgdata`, `techstrap-restore_techstrap-storage`, `techstrap-restore_admin-keys` and `techstrap-restore_portal-keys`. Every scratch object is labelled `techstrap.restore-scratch=1`. The restore refuses a target that is `techstrap`, `techstrap-uat`, `techstrap-prod` or the manifest's project, with or without `--overwrite` (`refusing: <target> is a protected or live project; promotion uses --db-url --yes --confirm-project <target>`); it refuses a target that already owns any of the volumes unless you pass `--overwrite`, and `--overwrite` only replaces objects that carry the label (an unlabelled volume of that name is refused outright).
3. Read the verification output (see the checklist below).
4. Optionally start the apps against the scratch stack. The deploy compose takes the project from `TECHSTRAP_PROJECT`, so the volumes are the `techstrap-restore_*` ones, and the external database network from `TECHSTRAP_DB_NETWORK`, which is the scratch network `techstrap-restore-db`. Use a subnet, reverse-proxy CIDR and host ports that no other stack on the host uses, and an env directory whose `.env.api` and `.env.worker` set `ConnectionStrings__TechStrap` with `Host=techstrap-restore-postgres` (the scratch password is in the scratch container's `POSTGRES_PASSWORD`: `docker exec techstrap-restore-postgres printenv POSTGRES_PASSWORD`):

   ```bash
   export TECHSTRAP_PROJECT=techstrap-restore
   export TECHSTRAP_DB_NETWORK=techstrap-restore-db
   export TECHSTRAP_SUBNET=172.16.99.0/24
   export REVERSE_PROXY_CIDR=172.16.99.1/32
   export TECHSTRAP_API_PORT=18080 TECHSTRAP_ADMIN_PORT=18081 TECHSTRAP_PORTAL_PORT=18082
   export TECHSTRAP_ENV_DIR=/etc/techstrap/restore     # copy of the env files, Host=techstrap-restore-postgres
   # plus the four TECHSTRAP_*_IMAGE variables, as in the deploy env file
   docker compose -f deploy/docker-compose.yml up -d --wait
   ```

   Take it down with `docker compose -f deploy/docker-compose.yml down` (never with `-v`) before the teardown, with `TECHSTRAP_PROJECT=techstrap-restore` still exported so compose cannot default to the live project. Merely stopped containers would keep the scratch volumes and network "in use", and compose's own `techstrap-restore_default` network must go too; `down` without `-v` removes the containers and that network and leaves every volume for the teardown.
5. Remove the scratch stack:

   ```bash
   deploy/restore.sh --teardown --target-project techstrap-restore
   ```

   Teardown removes only the labelled container, the network `techstrap-restore-db` and the four named volumes of that project, and says `not removed (missing or in use)` for one it could not remove. It refuses `techstrap`, `techstrap-uat`, `techstrap-prod` and the project named in the manifest; when working near a real project, also pass `--from <backup dir>` with `--teardown` so the manifest's project is protected too. The scripts never remove volumes wholesale.

Promotion into the real stack is a deliberate second step. It needs `--db-url` (or `TECHSTRAP_DB_URL` together with `--confirm-project`), `PGPASSWORD`, `--yes` and `--confirm-project` equal to `--target-project` (and to the manifest project when they are the same). The database is restored with `pg_restore --single-transaction --exit-on-error`, so a failure leaves the database as it was.

0. Take a fresh backup of the current state first (the restored `admin_events` stops at the backup time, and the erasure list below needs the live one).
1. Stop the apps: `docker compose ... stop api worker admin portal`. Never use a command that removes the volumes.
2. Prefer restoring into a freshly created, empty database over `--clean` on the live one: `CREATE DATABASE techstrap_restored OWNER techstrap;`, put it in `TECHSTRAP_DB_URL`, restore, then repoint `ConnectionStrings__TechStrap` at it. `--clean` cannot remove objects that are absent from the dump, so a live database restored with `--clean` can keep objects the backup never had. Then restore:

   ```bash
   # TECHSTRAP_DB_URL=postgresql://techstrap@host:5432/techstrap_restored and PGPASSWORD are in the environment
   deploy/restore.sh --from <dir> --target-project techstrap-uat --passphrase-file /etc/techstrap/backup.pass \
     --db-network techstrap-db --yes --confirm-project techstrap-uat --overwrite
   ```

   `--overwrite` clears each target volume before extracting, so files that are not in the backup do not linger.
3. Start the apps and run the checklist.

## Verification checklist

- [ ] The `__EFMigrationsHistory` count equals the manifest (the script stops with an error if not).
- [ ] The ticket and attachment counts match the manifest (`ok`, not `MISMATCH`); the attachment file count printed next to the database count is plausible. The manifest counts are read after the dump, so fewer restored rows than the manifest prints `WARNING: fewer rows than the manifest; writes during the backup window are expected` and the restore continues; more rows than the manifest, or a different migrations count, fails.
- [ ] After a restore run (in Git Bash prefix `MSYS_NO_PATHCONV=1`) `docker run --rm -v <project>_techstrap-storage:/v:ro alpine:3.23 stat -c %u /v /v/attachments`: `attachments/` must show `10001` (the Api runs as uid 10001; tar runs as root and restores the archived ownership). The volume root itself (`/v`) is an open check for the 12c drill.
- [ ] One attachment downloads in the Admin.
- [ ] An admin signs in.
- [ ] `/health/ready` is 200 on api and worker; `/health/live` is 200 on admin and portal.
- [ ] The outbox drains (no growing backlog in the Admin dead-letter view).
- [ ] Erasures made after the backup are re-run (see the consistency caveat).
- [ ] The keys volumes are present, so agents stay signed in.

## Consistency caveat

The dump is taken first, then the volumes. A file written between the two is in the volume but not in the dump: an orphan file, harmless. The reverse cannot happen.

Ticket hard delete and requester erase delete their files after the database commit, so a restore can resurrect rows and files that were erased after the backup time. "Attachments are append-only" is false. After a restore, list the erasures made since the backup and run them again:

```sql
select occurred_at, type, subject_type, subject_id
from admin_events
where occurred_at > '<created_utc from manifest.txt>'
  and type in ('TicketDeleted', 'RequesterErased')
order by occurred_at;
```

Run this against the live database before you overwrite it (or against a copy taken just before the restore), because the restored `admin_events` stops at the backup time.

## Disaster scenarios

- Database corruption: restore the dump into a new database (scratch restore, or `--db-url` to a fresh empty database), then repoint the apps. Leave the volumes alone with `--skip-volume` for each of the three names and read the warnings.
- Volume loss: restore into a scratch project, passing `--skip-volume` for each volume you do not need (the script prints a `WARNING: skipping volume` line for every one), then copy the lost volume's content from `<scratch>_<volume>` into the real volume with a throwaway container while the apps are stopped. The scratch restore also loads the database; ignore it.
- Host loss: on a new host install Docker, recreate the env files from the secret store, restore everything (database and all three volumes), and redeploy the images by tag.

## Volume ownership

Never write into the storage or keys volumes as root. The Api, Admin and Portal containers run as uid 10001, so a directory created by a root `docker run` (for example `attachments/`) makes uploads answer 403 until it is chowned. Use `--user 10001:10001` on any `docker run` that writes into those volumes, or go through the app. A restore extracts with `tar` as root and restores the ownership recorded in the archive, so verify it afterwards (see the checklist).

## Open checks for 12c (T15)

- `pg_restore --clean` against the real role on UAT: the `citext` extension and object ownership.
- Ownership of the volume root (`/v`) after a restore.
- Admin access-token claims after a restore are T14, not part of this drill.

## Keys volumes

Losing `admin-keys` signs every agent out, because the sign-in cookies cannot be decrypted. Losing `portal-keys` invalidates only in-flight portal antiforgery tokens and form posts. Both are restored by default, and a restore never skips them silently: a manifest without one is refused, and the only way to leave one out is the explicit `--skip-volume`, which prints a warning.

## Client and server version

`pg_dump` and `pg_restore` run in a `postgres:17` container so the client matches the PG17 server. A newer client on the host, such as 18, writes a dump format that an older server's tools may refuse. When the server is upgraded, bump `POSTGRES_IMAGE` in both scripts. On Windows with Git Bash the scripts set `MSYS_NO_PATHCONV=1` and convert the passphrase path for the native openssl.

## 12b rehearsal record

Run on 2026-10-09 against the development stack (`docker compose up -d --wait`, project `techstrap`, Postgres 17, Docker 29.8.2, Git Bash on Windows 11; `TECHSTRAP_MAILPIT_PORT=18025` because port 8025 was taken by another project). Seed data plus two tickets from `scripts/Send-TestTicket.ps1`, and one file placed by hand under `attachments/` in the storage volume so the volume had content to compare. The file was planted with a `docker run` as root, which left `attachments/` root-owned; the Api runs as uid 10001, so uploads answered 403 until a `chown` inside the container (see "Volume ownership"). The passphrase file was a throwaway 0600 file outside the repository.

```bash
deploy/backup.sh --project techstrap --db-container techstrap-postgres-1   --passphrase-file <throwaway 0600 file> --out ./backups
deploy/restore.sh --from ./backups/techstrap/20261009T143202Z --target-project techstrap-restore   --passphrase-file <same file>
docker run --rm -v techstrap-restore_techstrap-storage:/v:ro alpine:3.23 find /v -type f
deploy/restore.sh --teardown --target-project techstrap-restore
docker volume ls --filter name=techstrap
```

Backup (7.3 s): `manifest.txt` read `format=1`, `encrypted=true`, `migrations_count=11`, `ticket_count=13`, `attachment_count=0`, with bytes and SHA-256 for `db` (61232 bytes), `techstrap-storage` (224), `admin-keys` (720) and `portal-keys` (720).

Restore into `techstrap-restore` (13.2 s, including the scratch Postgres start):

```text
manifest ok: project=techstrap created_utc=2026-10-09T14:32:08Z checksums verified
migrations: 11 (manifest 11) ok
tickets: 13 (manifest 13) ok
attachments: 0 (manifest 0) ok
attachment files in techstrap-restore_techstrap-storage: 1 (database rows: 0)
restore complete: techstrap-restore
```

The file `/v/attachments/rehearsal/file1` was present in `techstrap-restore_techstrap-storage`. A second run without `--overwrite` printed `refusing: project techstrap-restore already owns ...` and exited 1; with `--overwrite` it succeeded again. A restore with the target `techstrap` printed `refusing: target project techstrap is the project the backup came from ...`, and `--teardown --target-project techstrap` printed `refusing: teardown of the real project techstrap`.

Teardown removed `techstrap-restore_pgdata`, `techstrap-restore_techstrap-storage`, `techstrap-restore_admin-keys` and `techstrap-restore_portal-keys`; `docker volume ls --filter name=techstrap` then showed the real dev volumes (`techstrap_pgdata`, `techstrap_techstrap-storage`, `techstrap_admin-keys`, `techstrap_portal-keys`) untouched. A copy with one byte appended to `admin-keys.tar.gz.enc` was refused with `checksum mismatch`, and a copy with the `portal-keys` entry removed was refused with `manifest is missing volume portal-keys` (with `--skip-volume admin-keys` the admin warning printed first).

Deviations: the first backup attempt failed because Git Bash's native openssl could not open a `/c/...` passphrase path; the scripts now convert it with `cygpath -m`, and the incomplete backup directory was removed by the failure trap. Not exercised: the `--db-url` promotion path and the systemd timer (UAT, 12c T15). Stack start-up to a restore was not timed as a whole: the 4 h RTO is a target, not a measurement. The dev database holds 13 tickets and no attachment rows, so the attachment count comparison was 0 against 0.
