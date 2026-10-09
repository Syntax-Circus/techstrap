#!/usr/bin/env bash
# TechStrap restore: rebuilds a backup made by deploy/backup.sh, by default into a SCRATCH stack.
#
# Usage:
#   deploy/restore.sh --from <backup dir> --target-project <name> (--passphrase-file <file> | --no-encrypt)
#                     [--skip-volume <name>]... [--overwrite] [--dry-run]
#   deploy/restore.sh --from <backup dir> --target-project <project> --db-url <url> --db-network <net>
#                     --yes --confirm-project <project> --overwrite (--passphrase-file <file> | --no-encrypt)
#   deploy/restore.sh --teardown --target-project <name>
#
#   --from <dir>              Timestamp directory written by backup.sh (holds manifest.txt)
#   --target-project <name>   Project that receives the restore; its volumes are <name>_<volume>
#   --passphrase-file <file>  File holding the encryption passphrase; never a flag value
#   --no-encrypt              The backup was taken with --no-encrypt
#   --db-url <url>            Restore into this database (promotion) instead of a scratch Postgres container; needs --yes.
#                             No password in the URL (for example postgresql://techstrap@host:5432/techstrap): the password
#                             goes in env PGPASSWORD (required). Env TECHSTRAP_DB_URL is used when --confirm-project is given.
#   --db-network <net>        Docker network for --db-url (default: techstrap-db)
#   --confirm-project <name>  Promotion only: must equal --target-project (and the manifest project when they are the same)
#   --yes                     Confirm a restore into an external database
#   --overwrite               Replace volumes the target already owns; in scratch mode only objects labelled
#                             techstrap.restore-scratch=1, never a protected or live project
#   --skip-volume <name>      Do not restore techstrap-storage, admin-keys or portal-keys (repeatable, loudly warned)
#   --teardown                Remove only the labelled scratch container, networks and the four named volumes of the target
#   --dry-run                 Check the backup and print the plan; touch no docker
#   --help                    Show this text
#
# A restore never skips the keys volumes silently: a manifest that lacks one is refused.
# The database URL and password are passed to containers by name (-e NAME), never on a command line.
# The URL carries no password: it would be expanded into the pg_restore argument list and show in ps on the host.
set -euo pipefail
umask 077
export MSYS_NO_PATHCONV=1

POSTGRES_IMAGE=postgres:17
ALPINE_IMAGE=alpine:3.23
VOLUMES=(techstrap-storage admin-keys portal-keys)
PROTECTED_PROJECTS=(techstrap techstrap-uat techstrap-prod)

FROM=
TARGET=
PASSPHRASE_FILE=
NO_ENCRYPT=0
DB_URL=
DB_NETWORK=
CONFIRM_PROJECT=
YES=0
OVERWRITE=0
TEARDOWN=0
DRY_RUN=0
SKIP=()

usage() { sed -n '2,/^set -euo/p' "$0" | sed '$d' | sed 's/^# \{0,1\}//'; }
die() { echo "error: $*" >&2; exit 1; }
refuse() { echo "refusing: $*" >&2; exit 1; }
need() { [[ $# -ge 2 && -n "$2" ]] || die "$1 needs a value"; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    --from) need "$@"; FROM="$2"; shift 2 ;;
    --target-project) need "$@"; TARGET="$2"; shift 2 ;;
    --passphrase-file) need "$@"; PASSPHRASE_FILE="$2"; shift 2 ;;
    --no-encrypt) NO_ENCRYPT=1; shift ;;
    --db-url) need "$@"; DB_URL="$2"; shift 2 ;;
    --db-network) need "$@"; DB_NETWORK="$2"; shift 2 ;;
    --confirm-project) need "$@"; CONFIRM_PROJECT="$2"; shift 2 ;;
    --yes) YES=1; shift ;;
    --overwrite) OVERWRITE=1; shift ;;
    --skip-volume) need "$@"; SKIP+=("$2"); shift 2 ;;
    --teardown) TEARDOWN=1; shift ;;
    --dry-run) DRY_RUN=1; shift ;;
    --help|-h) usage; exit 0 ;;
    *) usage >&2; die "unknown flag: $1" ;;
  esac
done

[[ -n "$TARGET" ]] || die "--target-project is required"
[[ "$TARGET" =~ ^[a-z0-9][a-z0-9_-]*$ ]] || die "--target-project must match ^[a-z0-9][a-z0-9_-]*\$"

for s in "${SKIP[@]+"${SKIP[@]}"}"; do
  ok=0
  for v in "${VOLUMES[@]}"; do [[ "$s" == "$v" ]] && ok=1; done
  (( ok )) || die "--skip-volume accepts only: ${VOLUMES[*]}"
done

is_skipped() {
  local v
  for v in "${SKIP[@]+"${SKIP[@]}"}"; do [[ "$v" == "$1" ]] && return 0; done
  return 1
}

mget() { # $1 = key; prints the value from manifest.txt
  tr -d '\r' < "$FROM/manifest.txt" | awk -F= -v k="$1" '$1 == k { print substr($0, length(k) + 2); exit }'
}

# $1 = docker object kind (volume|network|container), $2 = name. Prints "missing", "scratch" or "unlabelled".
scratch_state() {
  local out
  if [[ "$1" == "container" ]]; then  # a container keeps its labels under .Config
    out=$(docker container inspect -f '{{ index .Config.Labels "techstrap.restore-scratch" }}' "$2" 2>/dev/null) || { echo missing; return 0; }
  else
    out=$(docker "$1" inspect -f '{{ index .Labels "techstrap.restore-scratch" }}' "$2" 2>/dev/null) || { echo missing; return 0; }
  fi
  if [[ "$out" == "1" ]]; then echo scratch; else echo unlabelled; fi
}

# Teardown removes only explicitly named objects and never a real project.
if (( TEARDOWN )); then
  MANIFEST_PROJECT=
  if [[ -n "$FROM" && -f "$FROM/manifest.txt" ]]; then MANIFEST_PROJECT=$(mget project); fi
  for p in "${PROTECTED_PROJECTS[@]}" $MANIFEST_PROJECT; do
    [[ "$TARGET" != "$p" ]] || refuse "teardown of the real project $p"
  done
  NAMES=("${TARGET}_pgdata" "${TARGET}_techstrap-storage" "${TARGET}_admin-keys" "${TARGET}_portal-keys")
  if (( DRY_RUN )); then
    echo "DRY-RUN: docker rm -f ${TARGET}-postgres (only if labelled techstrap.restore-scratch=1)"
    echo "DRY-RUN: docker network rm ${TARGET}-db (only if labelled techstrap.restore-scratch=1)"
    for n in "${NAMES[@]}"; do echo "DRY-RUN: docker volume rm $n (only if labelled techstrap.restore-scratch=1)"; done
    exit 0
  fi
  case "$(scratch_state container "${TARGET}-postgres")" in
    scratch) docker rm -f "${TARGET}-postgres" >/dev/null 2>&1 && echo "removed container ${TARGET}-postgres" || echo "container ${TARGET}-postgres not removed (missing or in use)" ;;
    unlabelled) echo "container ${TARGET}-postgres is not a restore-scratch object (no label); left alone" ;;
    *) echo "container ${TARGET}-postgres not found" ;;
  esac
  case "$(scratch_state network "${TARGET}-db")" in
    scratch) docker network rm "${TARGET}-db" >/dev/null 2>&1 && echo "removed network ${TARGET}-db" || echo "network ${TARGET}-db not removed (missing or in use)" ;;
    unlabelled) echo "network ${TARGET}-db is not a restore-scratch object (no label); left alone" ;;
    *) echo "network ${TARGET}-db not found" ;;
  esac
  for n in "${NAMES[@]}"; do
    case "$(scratch_state volume "$n")" in
      scratch) docker volume rm "$n" >/dev/null 2>&1 && echo "removed volume $n" || echo "volume $n not removed (missing or in use)" ;;
      unlabelled) echo "volume $n is not a restore-scratch object (no label); left alone" ;;
      *) echo "volume $n not found" ;;
    esac
  done
  echo "teardown complete: $TARGET"
  exit 0
fi

[[ -n "$FROM" ]] || die "--from is required"
[[ -f "$FROM/manifest.txt" ]] || die "no manifest.txt in $FROM"

# Manifest load and integrity check, before anything is restored.
[[ "$(mget format)" == "1" ]] || die "unsupported manifest format: $(mget format)"
MANIFEST_PROJECT=$(mget project)
ENCRYPTED=$(mget encrypted)
EXT=""
if [[ "$ENCRYPTED" == "true" ]]; then
  EXT=".enc"
  (( NO_ENCRYPT == 0 )) || die "the manifest says the backup is encrypted; drop --no-encrypt"
  [[ -n "$PASSPHRASE_FILE" ]] || die "--passphrase-file is required for an encrypted backup"
  [[ -f "$PASSPHRASE_FILE" && -r "$PASSPHRASE_FILE" ]] || die "passphrase file is not a readable file: $PASSPHRASE_FILE"
  PASS_PATH="$PASSPHRASE_FILE"
  if command -v cygpath >/dev/null 2>&1; then PASS_PATH=$(cygpath -m "$PASSPHRASE_FILE"); fi  # native openssl in Git Bash
elif [[ "$ENCRYPTED" == "false" ]]; then
  (( NO_ENCRYPT )) || die "the manifest says the backup is not encrypted; pass --no-encrypt"
else
  die "manifest has no encrypted=true|false"
fi

decrypt() {
  if (( NO_ENCRYPT )); then cat; else openssl enc -d -aes-256-cbc -pbkdf2 -pass "file:${PASS_PATH}"; fi
}

sha() {
  if command -v sha256sum >/dev/null 2>&1; then sha256sum "$1" | cut -d' ' -f1; else shasum -a 256 "$1" | cut -d' ' -f1; fi
}

DB_FILE="db.dump${EXT}"
verify_file() { # $1 = manifest name, $2 = file name; dies on a size or checksum mismatch
  local path="$FROM/$2"
  [[ "$(wc -c < "$path" | tr -d ' ')" == "$(mget "file.$1.bytes")" ]] || die "checksum mismatch for $2 (size)"
  [[ "$(sha "$path")" == "$(mget "file.$1.sha256")" ]] || die "checksum mismatch for $2"
}

[[ -n "$(mget file.db.bytes)" && -f "$FROM/$DB_FILE" ]] || die "manifest is missing the database dump"
verify_file db "$DB_FILE"
RESTORE_VOLUMES=()
for v in "${VOLUMES[@]}"; do
  if is_skipped "$v"; then
    echo "WARNING: skipping volume $v (a skipped keys volume signs every agent out)"
    continue
  fi
  if [[ -z "$(mget "file.$v.bytes")" || -z "$(mget "file.$v.sha256")" || ! -f "$FROM/${v}.tar.gz${EXT}" ]]; then
    die "manifest is missing volume $v (use --skip-volume $v only if you mean to leave it out)"
  fi
  verify_file "$v" "${v}.tar.gz${EXT}"
  RESTORE_VOLUMES+=("$v")
done
echo "manifest ok: project=$MANIFEST_PROJECT created_utc=$(mget created_utc) checksums verified"

# Safety.
# The URL may come from the environment so it stays out of ps; it only counts when promotion was asked for (--confirm-project),
# so a stray TECHSTRAP_DB_URL can never turn a scratch restore into a restore into a real database.
if [[ -z "$DB_URL" && -n "$CONFIRM_PROJECT" && -n "${TECHSTRAP_DB_URL:-}" ]]; then
  DB_URL="$TECHSTRAP_DB_URL"
fi
if [[ -n "$DB_URL" ]]; then
  if [[ "$DB_URL" =~ ://[^/@]*:[^/@]+@ ]]; then
    echo "refusing: --db-url must not contain a password; put it in PGPASSWORD" >&2
    exit 2
  fi
  (( YES )) || refuse "a restore into an external database needs --yes"
  [[ -n "${PGPASSWORD:-}" || $DRY_RUN -eq 1 ]] || die "PGPASSWORD must be set in the environment when a database URL is used"
  DB_NETWORK="${DB_NETWORK:-techstrap-db}"
  [[ "$CONFIRM_PROJECT" == "$TARGET" ]] || refuse "promotion needs --confirm-project $TARGET (it must equal --target-project)"
  if [[ "$TARGET" == "$MANIFEST_PROJECT" ]]; then
    [[ "$CONFIRM_PROJECT" == "$MANIFEST_PROJECT" ]] || refuse "target project $TARGET is the project the backup came from; promotion needs --confirm-project $MANIFEST_PROJECT"
  fi
else
  # Scratch mode never touches a protected or live project, with or without --overwrite.
  for p in "${PROTECTED_PROJECTS[@]}" "$MANIFEST_PROJECT"; do
    [[ "$TARGET" != "$p" ]] || refuse "$TARGET is a protected or live project; promotion uses --db-url --yes --confirm-project $TARGET"
  done
fi
if (( DRY_RUN == 0 )); then
  OWNED=()
  CHECK=(${RESTORE_VOLUMES[@]+"${RESTORE_VOLUMES[@]}"})
  [[ -n "$DB_URL" ]] || CHECK+=(pgdata)
  for v in "${CHECK[@]}"; do
    if docker volume inspect "${TARGET}_${v}" >/dev/null 2>&1; then
      OWNED+=("${TARGET}_${v}")
      if [[ -z "$DB_URL" && "$(scratch_state volume "${TARGET}_${v}")" != "scratch" ]]; then
        refuse "volume ${TARGET}_${v} exists but carries no techstrap.restore-scratch=1 label, so it is not a scratch object; --overwrite does not apply to it"
      fi
    fi
  done
  if (( ${#OWNED[@]} > 0 && OVERWRITE == 0 )); then
    refuse "project $TARGET already owns ${OWNED[*]}; pass --overwrite to replace them"
  fi
  if [[ -z "$DB_URL" ]]; then
    for pair in "container:${TARGET}-postgres" "network:${TARGET}-db"; do
      if [[ "$(scratch_state "${pair%%:*}" "${pair#*:}")" == "unlabelled" ]]; then
        refuse "${pair%%:*} ${pair#*:} exists but carries no techstrap.restore-scratch=1 label, so it is not a scratch object"
      fi
    done
  fi
fi

# Database target.
if [[ -n "$DB_URL" ]]; then
  RESTORE_NETWORK="$DB_NETWORK"
  export TS_DB_URL="$DB_URL"
else
  # The network is named like the deploy compose's external db network (TECHSTRAP_DB_NETWORK=<target>-db), so the apps can join it.
  RESTORE_NETWORK="${TARGET}-db"
  if (( DRY_RUN )); then
    echo "DRY-RUN: scratch $POSTGRES_IMAGE container ${TARGET}-postgres on network $RESTORE_NETWORK with volume ${TARGET}_pgdata (all labelled techstrap.restore-scratch=1)"
    export TS_DB_URL=
    export PGPASSWORD=
  else
    if (( OVERWRITE )); then
      # Only labelled objects reach this point (checked above), so these removals cannot hit a real stack.
      docker rm -f "${TARGET}-postgres" >/dev/null 2>&1 || true
      docker volume rm "${TARGET}_pgdata" >/dev/null 2>&1 || true
    fi
    docker network inspect "$RESTORE_NETWORK" >/dev/null 2>&1 || docker network create --label techstrap.restore-scratch=1 "$RESTORE_NETWORK" >/dev/null
    docker volume create --label techstrap.restore-scratch=1 "${TARGET}_pgdata" >/dev/null
    POSTGRES_PASSWORD=$(openssl rand -hex 16)
    export POSTGRES_PASSWORD
    echo "+ docker run -d --name ${TARGET}-postgres --network $RESTORE_NETWORK -v ${TARGET}_pgdata:/var/lib/postgresql/data $POSTGRES_IMAGE"
    docker run -d --label techstrap.restore-scratch=1 --name "${TARGET}-postgres" --network "$RESTORE_NETWORK" -e POSTGRES_USER=techstrap -e POSTGRES_DB=techstrap -e POSTGRES_PASSWORD -v "${TARGET}_pgdata:/var/lib/postgresql/data" "$POSTGRES_IMAGE" >/dev/null
    export PGPASSWORD="$POSTGRES_PASSWORD"
    export TS_DB_URL="postgresql://techstrap@${TARGET}-postgres:5432/techstrap"
    unset POSTGRES_PASSWORD
    ready=0
    for _ in $(seq 1 60); do
      if docker exec "${TARGET}-postgres" pg_isready -h 127.0.0.1 -U techstrap >/dev/null 2>&1; then ready=1; break; fi
      sleep 1
    done
    (( ready )) || die "scratch Postgres ${TARGET}-postgres did not become ready in 60 s"
  fi
fi

# Database restore.
RESTORE_FLAGS=
if [[ -n "$DB_URL" ]]; then RESTORE_FLAGS="--single-transaction --exit-on-error"; fi  # promotion: all or nothing
export TS_RESTORE_FLAGS="$RESTORE_FLAGS"
if (( DRY_RUN )); then
  echo "DRY-RUN: decrypt < $FROM/$DB_FILE | docker run --rm -i --network $RESTORE_NETWORK -e TS_DB_URL -e PGPASSWORD -e TS_RESTORE_FLAGS $POSTGRES_IMAGE sh -c 'pg_restore \$TS_RESTORE_FLAGS --clean --if-exists --no-owner --dbname \"\$TS_DB_URL\"' (TS_RESTORE_FLAGS='${RESTORE_FLAGS}')"
else
  echo "+ pg_restore ${RESTORE_FLAGS} (in $POSTGRES_IMAGE) < $FROM/$DB_FILE"
  decrypt < "$FROM/$DB_FILE" | docker run --rm -i --network "$RESTORE_NETWORK" -e TS_DB_URL -e PGPASSWORD -e TS_RESTORE_FLAGS "$POSTGRES_IMAGE" sh -c 'pg_restore $TS_RESTORE_FLAGS --clean --if-exists --no-owner --dbname "$TS_DB_URL"'
fi

# Volumes.
for v in ${RESTORE_VOLUMES[@]+"${RESTORE_VOLUMES[@]}"}; do
  if (( DRY_RUN )); then
    echo "DRY-RUN: docker volume create ${TARGET}_${v}; decrypt < $FROM/${v}.tar.gz${EXT} | docker run --rm -i -v ${TARGET}_${v}:/v $ALPINE_IMAGE tar xzf - -C /v"
    continue
  fi
  if (( OVERWRITE )) && docker volume inspect "${TARGET}_${v}" >/dev/null 2>&1; then
    # Scratch mode: only a volume labelled techstrap.restore-scratch=1 may be cleared. Promotion is already gated by --yes and --confirm-project.
    if [[ -z "$DB_URL" && "$(scratch_state volume "${TARGET}_${v}")" != "scratch" ]]; then
      refuse "volume ${TARGET}_${v} is not a labelled scratch volume; not clearing it"
    fi
    echo "+ clearing ${TARGET}_${v} before the restore (--overwrite)"
    docker run --rm -v "${TARGET}_${v}:/v" "$ALPINE_IMAGE" find /v -mindepth 1 -delete
  fi
  if [[ -n "$DB_URL" ]]; then
    docker volume create "${TARGET}_${v}" >/dev/null
  else
    docker volume create --label techstrap.restore-scratch=1 "${TARGET}_${v}" >/dev/null
  fi
  echo "+ tar xzf - > ${TARGET}_${v}"
  decrypt < "$FROM/${v}.tar.gz${EXT}" | docker run --rm -i -v "${TARGET}_${v}:/v" "$ALPINE_IMAGE" tar xzf - -C /v
done

if (( DRY_RUN )); then
  echo "DRY-RUN: would verify __EFMigrationsHistory, ticket and attachment counts against the manifest"
  exit 0
fi

# Verification.
db_count() { # $1 = SQL identifier, already quoted where needed
  local out
  export TS_SQL="select count(*) from $1"
  out=$(docker run --rm --network "$RESTORE_NETWORK" -e TS_DB_URL -e PGPASSWORD -e TS_SQL "$POSTGRES_IMAGE" sh -c 'psql -At "$TS_DB_URL" -c "$TS_SQL"')
  [[ "$out" =~ ^[0-9]+$ ]] || die "could not count rows in $1"
  echo "$out"
}

FAILED=0
GOT=$(db_count '"__EFMigrationsHistory"')
WANT=$(mget migrations_count)
[[ "$GOT" == "$WANT" ]] || die "__EFMigrationsHistory count $GOT does not match the manifest $WANT"
echo "migrations: $GOT (manifest $WANT) ok"

for pair in "tickets:tickets:ticket_count" "attachments:attachments:attachment_count"; do
  label="${pair%%:*}"
  rest="${pair#*:}"
  table="${rest%%:*}"
  key="${rest#*:}"
  GOT=$(db_count "$table")
  WANT=$(mget "$key")
  if [[ "$GOT" == "$WANT" ]]; then
    echo "$label: $GOT (manifest $WANT) ok"
  elif [[ "$WANT" =~ ^[0-9]+$ ]] && (( GOT < WANT )); then
    # The counts in the manifest are read after the dump, so a row deleted in that window is expected.
    echo "$label: $GOT (manifest $WANT) WARNING: fewer rows than the manifest; writes during the backup window are expected"
  else
    echo "$label: $GOT (manifest $WANT) MISMATCH"
    FAILED=1
  fi
done

if ! is_skipped techstrap-storage; then
  FILES=$(docker run --rm -v "${TARGET}_techstrap-storage:/v:ro" "$ALPINE_IMAGE" sh -c 'find /v/attachments -type f 2>/dev/null | wc -l' | tr -d ' ')
  echo "attachment files in ${TARGET}_techstrap-storage: $FILES (database rows: $GOT)"
fi

(( FAILED == 0 )) || die "row counts do not match the manifest"

echo "restore complete: $TARGET"
if [[ -z "$DB_URL" ]]; then
  echo "next: scratch database ${TARGET}-postgres on network ${RESTORE_NETWORK}; see docs/runbooks/backup-restore.md to start the apps against it"
  echo "next: when done, run deploy/restore.sh --teardown --target-project ${TARGET}"
fi
