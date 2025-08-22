#!/bin/bash
set -e

if [ "$1" = 'postgres' ]; then
    # Orijinal postgres entrypoint'ini gelen TÜM argümanlarla ("$@") arka planda başlat.
    /usr/local/bin/docker-entrypoint.sh "$@" &
    POSTGRES_PID=$!

    # PostgreSQL'in bağlantı kabul etmeye hazır olmasını bekle.
    until pg_isready -h localhost -p 5432 -U "$POSTGRES_USER"; do
        echo "PostgreSQL başlatılıyor... bekleniyor..."
        sleep 2
    done

    # PostgreSQL hazır olduğuna göre, pg_agent servisini başlat.
    echo "PostgreSQL hazır. pg_agent başlatılıyor..."
    pg_agent host=localhost port=5432 dbname="$POSTGRES_DB" user="$POSTGRES_USER" &

    # Ana PostgreSQL sürecinin bitmesini bekle (konteynerin kapanmaması için).
    wait $POSTGRES_PID
else
    # Başka bir komut ise (örn: bash), doğrudan onu çalıştır.
    exec "$@"
fi
