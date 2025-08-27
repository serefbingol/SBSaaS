#!/usr/bin/env bash
set -euo pipefail

DB_NAME="${POSTGRES_DB}"
DB_USER="${POSTGRES_USER}"

echo ">>> [00-entrypoint] Target DB: ${DB_NAME} | DB User: ${DB_USER}"

# Şema şablonunu ENV ile doldur
sed "s/:DB_USER/${DB_USER}/g" /docker-entrypoint-initdb.d/02-schemas.sql.template > /tmp/02-schemas.sql

# Şemaları uygula
psql -v ON_ERROR_STOP=1 --username "${POSTGRES_USER}" --dbname "${POSTGRES_DB}" -f /tmp/02-schemas.sql

echo ">>> [00-entrypoint] Schemas applied on ${DB_NAME}"
