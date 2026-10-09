#!/usr/bin/env bash
# TechStrap backup: the Postgres database (pg_dump -Fc) and the three data volumes, encrypted.
#
# Usage:
#   deploy/backup.sh --project <name> --out <dir> (--db-url <url> | --db-container <name> | --db-host <host>)
#                    (--passphrase-file <file> | --no-encrypt) [--keep-days N] [--dry-run]
#
#   --project <name>          Compose project that owns the volumes (default: techstrap)
#   --out <dir>               Backup root; a UTC timestamp directory is created under <dir>/<project>/ (default: ./backups)
#   --keep-days N             After a successful backup remove timestamp directories older than N days
#   --dry-run                 Print what would run; touch no docker, create no directory, write no file
#   --passphrase-file <file>  File holding the encryption passphrase (at least 16 bytes); never a flag value
#   --no-encrypt              Store plain files (dev only); the .enc suffix is dropped
#   --db-url <url>            Database URL (or env TECHSTRAP_DB_URL); the dump container joins --db-network
#   --db-container <name>     Dev: join that running Postgres container and read its POSTGRES_* variables
#   --db-host <host>          Host name; uses --db-network, --db-user, --db-name and env PGPASSWORD
#   --db-network <name>       Docker network for --db-url / --db-host (default: techstrap-db)
#   --db-user <user>          User for --db-host (default: techstrap)
#   --db-name <name>          Database for --db-host (default: techstrap)
#   --help                    Show this text
#
# The database is dumped first, then the volumes (see docs/runbooks/backup-restore.md, consistency caveat).
# The database URL and password are passed to containers by name (-e NAME), never on a command line.
set -euo pipefail
export MSYS_NO_PATHCONV=1

POSTGRES_IMAGE=postgres:17
ALPINE_IMAGE=alpine:3.23
VOLUMES=(techstrap-storage admin-keys portal-keys)
TICKETS_TABLE=tickets
ATTACHMENTS_TABLE=attachments

PROJECT=techstrap
OUT_DIR=./backups
KEEP_DAYS=
DB_NETWORK=techstrap-db
DB_URL=
DB_CONTAINER=
DB_HOST=
DB_USER=techstrap
DB_NAME=techstrap
PASSPHRASE_FILE=
NO_ENCRYPT=0
DRY_RUN=0
DEST=
DONE=0

usage() { sed -n '2,/^set -euo/p' "$0" | sed '$d' | sed 's/^# \{0,1\}//'; }
die() { echo "error: $*" >&2; exit 1; }
need() { [[ $# -ge 2 && -n "$2" ]] || die "$1 needs a value"; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    --project) need "$@"; PROJECT="$2"; shift 2 ;;
    --out) need "$@"; OUT_DIR="$2"; shift 2 ;;
    --keep-days) need "$@"; KEEP_DAYS="$2"; shift 2 ;;
    --dry-run) DRY_RUN=1; shift ;;
    --passphrase-file) need "$@"; PASSPHRASE_FILE="$2"; shift 2 ;;
    --no-encrypt) NO_ENCRYPT=1; shift ;;
    --db-url) need "$@"; DB_URL="$2"; shift 2 ;;
    --db-container) need "$@"; DB_CONTAINER="$2"; shift 2 ;;
    --db-host) need "$@"; DB_HOST="$2"; shift 2 ;;
    --db-network) need "$@"; DB_NETWORK="$2"; shift 2 ;;
    --db-user) need "$@"; DB_USER="$2"; shift 2 ;;
    --db-name) need "$@"; DB_NAME="$2"; shift 2 ;;
    --help|-h) usage; exit 0 ;;
    *) usage >&2; die "unknown flag: $1" ;;
  esac
done

[[ "$PROJECT" =~ ^[a-z0-9][a-z0-9_-]*$ ]] || die "--project must match ^[a-z0-9][a-z0-9_-]*\$"
if [[ -n "$KEEP_DAYS" ]]; then
  if [[ ! "$KEEP_DAYS" =~ ^[0-9]+$ ]] || (( 10#$KEEP_DAYS < 1 )); then die "--keep-days must be a whole number of at least 1"; fi
fi

# The URL may come from the environment so it stays out of ps; it only counts when no other mode was chosen.
if [[ -z "$DB_URL" && -z "$DB_CONTAINER" && -z "$DB_HOST" && -n "${TECHSTRAP_DB_URL:-}" ]]; then
  DB_URL="$TECHSTRAP_DB_URL"
fi
MODES=0
[[ -n "$DB_URL" ]] && MODES=$((MODES + 1))
[[ -n "$DB_CONTAINER" ]] && MODES=$((MODES + 1))
[[ -n "$DB_HOST" ]] && MODES=$((MODES + 1))
(( MODES == 1 )) || die "choose one database mode (--db-url or env TECHSTRAP_DB_URL, --db-container, or --db-host)"

if (( NO_ENCRYPT == 0 )); then
  [[ -n "$PASSPHRASE_FILE" ]] || die "--passphrase-file is required unless --no-encrypt is given"
  [[ -f "$PASSPHRASE_FILE" && -r "$PASSPHRASE_FILE" ]] || die "passphrase file is not a readable file: $PASSPHRASE_FILE"
  PASS_BYTES=$(wc -c < "$PASSPHRASE_FILE" | tr -d ' ')
  (( PASS_BYTES >= 16 )) || die "passphrase file must hold at least 16 bytes"
fi
# Git Bash on Windows runs a native openssl that needs a Windows path; elsewhere the path is used as given.
PASS_PATH="$PASSPHRASE_FILE"
if [[ -n "$PASSPHRASE_FILE" ]] && command -v cygpath >/dev/null 2>&1; then PASS_PATH=$(cygpath -m "$PASSPHRASE_FILE"); fi

# Reads stdin, writes stdout. The passphrase is read from the file, never from the command line.
encrypt() {
  if (( NO_ENCRYPT )); then cat; else openssl enc -aes-256-cbc -pbkdf2 -salt -pass "file:${PASS_PATH}"; fi
}

sha() {
  if command -v sha256sum >/dev/null 2>&1; then sha256sum "$1" | cut -d' ' -f1; else shasum -a 256 "$1" | cut -d' ' -f1; fi
}

# A failed run never leaves a directory that retention or a restore could mistake for a good backup.
cleanup() {
  local rc=$?
  if (( rc != 0 && DONE == 0 )) && [[ -n "${DEST:-}" && -d "${DEST:-}" ]]; then
    echo "backup failed; removing the incomplete $DEST" >&2
    rm -rf -- "$DEST"
  fi
}
trap cleanup EXIT

# Database mode setup.
if [[ -n "$DB_URL" ]]; then
  DUMP_NETWORK="$DB_NETWORK"
  export TS_DB_URL="$DB_URL"
elif [[ -n "$DB_CONTAINER" ]]; then
  DUMP_NETWORK="container:${DB_CONTAINER}"
  if (( DRY_RUN )); then
    export TS_DB_URL=
  else
    PG_USER=$(docker exec "$DB_CONTAINER" printenv POSTGRES_USER)
    PG_PASS=$(docker exec "$DB_CONTAINER" printenv POSTGRES_PASSWORD)
    PG_DB=$(docker exec "$DB_CONTAINER" printenv POSTGRES_DB)
    export TS_DB_URL="postgresql://${PG_USER}:${PG_PASS}@localhost:5432/${PG_DB}"
    unset PG_USER PG_PASS PG_DB
  fi
else
  DUMP_NETWORK="$DB_NETWORK"
  export TS_DB_URL="postgresql://${DB_USER}@${DB_HOST}:5432/${DB_NAME}"
  export PGPASSWORD="${PGPASSWORD:-}"
fi

db_count() { # $1 = SQL identifier, already quoted where needed
  local out
  export TS_SQL="select count(*) from $1"
  out=$(docker run --rm --network "$DUMP_NETWORK" -e TS_DB_URL -e PGPASSWORD -e TS_SQL "$POSTGRES_IMAGE" sh -c 'psql -At "$TS_DB_URL" -c "$TS_SQL"')
  [[ "$out" =~ ^[0-9]+$ ]] || die "could not count rows in $1"
  echo "$out"
}

STAMP=$(date -u +%Y%m%dT%H%M%SZ)
TARGET_DIR="${OUT_DIR}/${PROJECT}"
if (( DRY_RUN )); then
  DEST="${TARGET_DIR}/${STAMP}"
  echo "DRY-RUN: mkdir -p $DEST"
else
  mkdir -p "$TARGET_DIR"
  DEST="${TARGET_DIR}/${STAMP}"
  mkdir "$DEST"
fi

EXT=".enc"
ENCRYPTED=true
if (( NO_ENCRYPT )); then EXT=""; ENCRYPTED=false; fi

# 1. The database first.
DB_FILE="db.dump${EXT}"
if (( DRY_RUN )); then
  echo "DRY-RUN: docker run --rm --network $DUMP_NETWORK -e TS_DB_URL -e PGPASSWORD $POSTGRES_IMAGE sh -c 'pg_dump -Fc --no-owner --dbname \"\$TS_DB_URL\"' | encrypt (passphrase file: ${PASSPHRASE_FILE:-none}) > $DEST/$DB_FILE"
  MIGRATIONS_COUNT=dry-run
  TICKET_COUNT=dry-run
  ATTACHMENT_COUNT=dry-run
  echo "DRY-RUN: row counts for \"__EFMigrationsHistory\", $TICKETS_TABLE, $ATTACHMENTS_TABLE"
else
  echo "+ pg_dump -Fc (in $POSTGRES_IMAGE) > $DEST/$DB_FILE"
  docker run --rm --network "$DUMP_NETWORK" -e TS_DB_URL -e PGPASSWORD "$POSTGRES_IMAGE" sh -c 'pg_dump -Fc --no-owner --dbname "$TS_DB_URL"' | encrypt > "$DEST/$DB_FILE"
  MIGRATIONS_COUNT=$(db_count '"__EFMigrationsHistory"')
  TICKET_COUNT=$(db_count "$TICKETS_TABLE")
  ATTACHMENT_COUNT=$(db_count "$ATTACHMENTS_TABLE")
fi

# 2. Then the volumes, read-only, in VOLUMES order.
for v in "${VOLUMES[@]}"; do
  FILE="${v}.tar.gz${EXT}"
  if (( DRY_RUN )); then
    echo "DRY-RUN: docker run --rm -v ${PROJECT}_${v}:/v:ro $ALPINE_IMAGE tar czf - -C /v . | encrypt > $DEST/$FILE"
  else
    docker volume inspect "${PROJECT}_${v}" >/dev/null 2>&1 || die "volume ${PROJECT}_${v} not found"
    echo "+ tar czf - ${PROJECT}_${v} > $DEST/$FILE"
    docker run --rm -v "${PROJECT}_${v}:/v:ro" "$ALPINE_IMAGE" tar czf - -C /v . | encrypt > "$DEST/$FILE"
  fi
done

# 3. The manifest: no key, passphrase or URL.
if (( DRY_RUN )); then
  echo "DRY-RUN: write $DEST/manifest.txt"
else
  {
    echo "created_utc=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    echo "project=${PROJECT}"
    echo "format=1"
    echo "encrypted=${ENCRYPTED}"
    echo "migrations_count=${MIGRATIONS_COUNT}"
    echo "ticket_count=${TICKET_COUNT}"
    echo "attachment_count=${ATTACHMENT_COUNT}"
    echo "file.db.bytes=$(wc -c < "$DEST/$DB_FILE" | tr -d ' ')"
    echo "file.db.sha256=$(sha "$DEST/$DB_FILE")"
    for v in "${VOLUMES[@]}"; do
      echo "file.${v}.bytes=$(wc -c < "$DEST/${v}.tar.gz${EXT}" | tr -d ' ')"
      echo "file.${v}.sha256=$(sha "$DEST/${v}.tar.gz${EXT}")"
    done
  } > "$DEST/manifest.txt"
fi

# 4. Retention, after a successful backup only.
if [[ -n "$KEEP_DAYS" ]]; then
  if CUTOFF=$(date -u -d "-${KEEP_DAYS} days" +%Y%m%dT%H%M%SZ 2>/dev/null); then
    :
  else
    CUTOFF=$(date -u -v-"${KEEP_DAYS}"d +%Y%m%dT%H%M%SZ)
  fi
  if [[ -d "$TARGET_DIR" ]]; then
    for d in "$TARGET_DIR"/*/; do
      [[ -d "$d" ]] || continue
      d="${d%/}"
      base="${d##*/}"
      [[ "$base" =~ ^[0-9]{8}T[0-9]{6}Z$ ]] || continue
      [[ "$base" < "$CUTOFF" ]] || continue
      if (( DRY_RUN )); then
        echo "DRY-RUN: would prune $d"
      else
        echo "prune: $d"
        rm -rf -- "$d"
      fi
    done
  fi
fi

DONE=1
if (( DRY_RUN )); then echo "dry run complete: nothing was written"; else echo "backup complete: $DEST"; fi
echo "migrations_count=$MIGRATIONS_COUNT ticket_count=$TICKET_COUNT attachment_count=$ATTACHMENT_COUNT"
