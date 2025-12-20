#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

ENV_FILE=".env.local"

if [[ -f "$ENV_FILE" ]]; then
  echo "[setup-env] $ENV_FILE already exists."
  exit 0
fi

echo "[setup-env] Creating $ENV_FILE (git-ignored)."

read -r -p "AUTH_MASTER_CODE (required): " master
if [[ -z "${master// }" ]]; then
  echo "[setup-env] AUTH_MASTER_CODE is required."
  exit 1
fi

read -r -p "AUTH_TOKEN_TTL_MINUTES (default 10): " ttl
ttl="${ttl:-10}"

cat >"$ENV_FILE" <<EOF
# Local secrets (DO NOT COMMIT)
AUTH_MASTER_CODE=$master
AUTH_TOKEN_TTL_MINUTES=$ttl
EOF

echo "[setup-env] Wrote $ENV_FILE"

