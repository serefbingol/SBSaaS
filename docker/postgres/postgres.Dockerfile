# ==========================
# Base Image: PostgreSQL 17
# ==========================
FROM postgres:17

# ==========================
# Gerekli paketler ve araçlar
# ==========================
RUN apt-get update && apt-get install -y --no-install-recommends \
    ca-certificates curl gnupg lsb-release wget build-essential \
    postgresql-server-dev-17 cmake git pkg-config libssl-dev libprotobuf-c-dev \
    protobuf-compiler libxml2-dev libgeos-dev libproj-dev libjson-c-dev \
    liblz4-dev libzstd-dev libpq-dev \
    && rm -rf /var/lib/apt/lists/*

# ==========================
# TimescaleDB 2.21.3 Kurulumu (GitHub)
# ==========================
RUN git clone --branch 2.21.3 https://github.com/timescale/timescaledb.git /tmp/timescaledb && \
    cd /tmp/timescaledb && \
    ./bootstrap -DREGRESS_CHECKS=OFF -DCMAKE_BUILD_TYPE=Release && \
    cd build && make && make install && \
    rm -rf /tmp/timescaledb

# ==========================
# PostGIS 3.5 Kurulumu
# ==========================
RUN apt-get update && apt-get install -y --no-install-recommends \
    postgis postgresql-17-postgis-3 \
    && rm -rf /var/lib/apt/lists/*

# ==========================
# pgAgent Kurulumu
# ==========================
RUN apt-get update && apt-get install -y --no-install-recommends pgagent \
    && rm -rf /var/lib/apt/lists/*

# ==========================
# PostgreSQL preload ayarları
# ==========================
RUN echo "shared_preload_libraries='timescaledb,pg_stat_statements'" >> /usr/share/postgresql/postgresql.conf.sample

# ==========================
# Init SQL ve entrypoint
# ==========================
COPY 01-extensions.sql /docker-entrypoint-initdb.d/01-extensions.sql
COPY 02-schemas.sql.template /docker-entrypoint-initdb.d/02-schemas.sql.template
COPY entrypoint.sh /docker-entrypoint-initdb.d/00-entrypoint.sh
RUN chmod +x /docker-entrypoint-initdb.d/00-entrypoint.sh
