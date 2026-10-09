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
- The database URL in the environment variable `TECHSTRAP_DB_URL`, kept in the same secret store as `.env.api`. The scripts hand it to the container by name (`-e TS_DB_URL`), so it never appears on a command line.
- Keep a copy of the passphrase off the box. A lost passphrase makes every backup unreadable.

## Taking a backup

Always rehearse with `--dry-run` first: it prints every step and touches no docker, directory or file.

Deployed stack (UAT or production, external Postgres):

```bash
deploy/backup.sh --project techstrap-uat --out /var/backups/techstrap --keep-days 14 \
  --passphrase-file /etc/techstrap/backup.pass --db-url "$TECHSTRAP_DB_URL"
```

Development stack (the compose file at the repository root, project `techstrap`):

```bash
deploy/backup.sh --project techstrap --db-container <postgres container from docker ps> --no-encrypt --out ./backups
```

`--db-host <host>` is the third database mode (with `--db-network`, `--db-user`, `--db-name` and the password in `PGPASSWORD`). Exactly one mode must be given.

The script dumps the database first, then archives the three volumes read-only, then writes the manifest. A failed run removes its half-written directory and exits non-zero. A missing volume is fatal: a backup that quietly lacks the keys volumes is a failed backup.

Schedule it with a systemd timer. `/etc/systemd/system/techstrap-backup.service`:

```ini
[Unit]
Description=TechStrap backup

[Service]
Type=oneshot
EnvironmentFile=/etc/techstrap/backup.env
ExecStart=/opt/techstrap/deploy/backup.sh --project techstrap-uat --out /var/backups/techstrap --keep-days 14 --passphrase-file /etc/techstrap/backup.pass
```

`/etc/techstrap/backup.env` holds `TECHSTRAP_DB_URL=...` (mode 0600). `/etc/systemd/system/techstrap-backup.timer`:

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

Enable it with `systemctl enable --now techstrap-backup.timer`. The cron alternative is a `crontab -e` line (export `TECHSTRAP_DB_URL` in the crontab or a wrapper first):

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

   This starts a throwaway `postgres:17` container `techstrap-restore-postgres` on the network `techstrap-restore_default` and creates the volumes `techstrap-restore_pgdata`, `techstrap-restore_techstrap-storage`, `techstrap-restore_admin-keys` and `techstrap-restore_portal-keys`. It refuses a target project that already owns any of them unless you pass `--overwrite`.
3. Read the verification output (see the checklist below).
4. Optionally start the apps against the scratch stack: use a separate `TECHSTRAP_PROJECT`, mount the `techstrap-restore_*` volumes, and point `ConnectionStrings__TechStrap` at `Host=techstrap-restore-postgres` on the network `techstrap-restore_default`.
5. Remove the scratch stack:

   ```bash
   deploy/restore.sh --teardown --target-project techstrap-restore
   ```

   Teardown removes only that project's container, network and the four named volumes. It refuses `techstrap`, `techstrap-uat`, `techstrap-prod` and the project named in the manifest; when working near a real project, also pass `--from <backup dir>` with `--teardown` so the manifest's project is protected too. The scripts never remove volumes wholesale.

Promotion into the real stack is a deliberate second step:

1. Stop the apps: `docker compose ... stop api worker admin portal`. Never use a command that removes the volumes.
2. Restore into the real project with the external database:

   ```bash
   deploy/restore.sh --from <dir> --target-project techstrap-uat --passphrase-file /etc/techstrap/backup.pass \
     --db-url "$TECHSTRAP_DB_URL" --db-network techstrap-db --yes --confirm-project techstrap-uat --overwrite
   ```

   `--overwrite` clears each target volume before extracting, so files that are not in the backup do not linger.
3. Start the apps and run the checklist.

## Verification checklist

- [ ] The `__EFMigrationsHistory` count equals the manifest (the script stops with an error if not).
- [ ] The ticket and attachment counts match the manifest (`ok`, not `MISMATCH`); the attachment file count printed next to the database count is plausible.
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

## Keys volumes

Losing `admin-keys` signs every agent out, because the sign-in cookies cannot be decrypted. Losing `portal-keys` invalidates only in-flight portal antiforgery tokens and form posts. Both are restored by default, and a restore never skips them silently: a manifest without one is refused, and the only way to leave one out is the explicit `--skip-volume`, which prints a warning.

## Client and server version

`pg_dump` and `pg_restore` run in a `postgres:17` container so the client matches the PG17 server. A newer client on the host, such as 18, writes a dump format that an older server's tools may refuse. When the server is upgraded, bump `POSTGRES_IMAGE` in both scripts. On Windows with Git Bash the scripts set `MSYS_NO_PATHCONV=1` and convert the passphrase path for the native openssl.

## 12b rehearsal record

Run on 2026-10-09 against the development stack (`docker compose up -d --wait`, project `techstrap`, Postgres 17, Docker 29.8.2, Git Bash on Windows 11; `TECHSTRAP_MAILPIT_PORT=18025` because port 8025 was taken by another project). Seed data plus two tickets from `scripts/Send-TestTicket.ps1`, and one file placed by hand under `attachments/` in the storage volume so the volume had content to compare. The passphrase file was a throwaway 0600 file outside the repository.

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
