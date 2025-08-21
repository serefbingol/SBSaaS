#!/bin/bash
set -e

echo "PostgreSQL is ready. Creating pgagent extension..."
# Connect to the default database (or your specified POSTGRES_DB) and create the extension
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-EOSQL
    CREATE EXTENSION IF NOT EXISTS pgagent;
EOSQL
echo "pgagent extension created."
