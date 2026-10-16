#!/usr/bin/env bash
# Prepare once before starting test assemblies, so registry failures do not fan out
# into thousands of fixture failures. No credentials are required for fallback.
set -euo pipefail

target="${TECHSTRAP_TEST_POSTGRES_IMAGE:-techstrap-test-postgres:17}"
if docker image inspect "$target" >/dev/null 2>&1; then
  echo "Test Postgres image already available locally: $target"
  exit 0
fi

for source in \
  docker.io/library/postgres:17 \
  mirror.gcr.io/library/postgres:17 \
  public.ecr.aws/docker/library/postgres:17; do
  echo "Preparing test Postgres from $source"
  if timeout --kill-after=5s 60s docker pull "$source"; then
    docker tag "$source" "$target"
    docker image inspect "$target" --format '{{.Id}}'
    echo "Test Postgres prepared locally: $target (source: $source)"
    exit 0
  fi
  echo "::warning::Pull from $source failed or timed out; trying the next registry."
done

echo "::error::Unable to prepare test Postgres image: all registries failed or timed out. Tests have not started." >&2
exit 1
