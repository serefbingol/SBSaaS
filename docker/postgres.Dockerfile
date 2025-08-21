# Use the official PostgreSQL 17 image as the base
FROM postgres:17

# Install necessary packages for adding repositories
USER root
RUN apt-get update && apt-get install -y \
    gnupg \
    postgresql-common \
    apt-transport-https \
    lsb-release \
    wget \
    && rm -rf /var/lib/apt/lists/*

# Add PostgreSQL APT repository (if not already present in base image)
RUN sh -c 'echo "deb http://apt.postgresql.org/pub/repos/apt/ $(lsb_release -cs)-pgdg main" > /etc/apt/sources.list.d/pgdg.list'
RUN wget --quiet -O - https://www.postgresql.org/media/keys/ACCC4CF8.asc | apt-key add -

# Add TimescaleDB APT repository
RUN sh -c "echo 'deb https://packagecloud.io/timescale/timescaledb/debian/ $(lsb_release -cs) main' > /etc/apt/sources.list.d/timescaledb.list"
RUN wget --quiet -O - https://packagecloud.io/timescale/timescaledb/gpgkey | apt-key add -

# Update package list after adding new repositories
RUN apt-get update

# Install TimescaleDB, PostGIS, and pgagent
# Note: Replace 'postgresql-17-postgis-3' with the correct package name if different
RUN apt-get install -y \
    timescaledb-2-postgresql-17 \
    postgresql-17-postgis-3 \
    pgagent \
    && rm -rf /var/lib/apt/lists/*

# Copy initialization scripts
COPY docker/init-pgagent.sh /docker-entrypoint-initdb.d/
COPY docker/postgres-init.sql /docker-entrypoint-initdb.d/

# Copy a script to start pgagent and make it executable
COPY docker/start-pgagent.sh /usr/local/bin/
RUN chmod +x /usr/local/bin/start-pgagent.sh

# Switch back to the 'postgres' user
USER postgres

# Modify the container's entrypoint to run our custom start script
ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh"]
CMD ["postgres"]
