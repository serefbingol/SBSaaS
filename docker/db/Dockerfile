FROM postgres:17

# Bağımlılıkları kurmak için root kullanıcısına geç
USER root

# Eklentiler için gerekli derleme araçlarını ve bağımlılıkları tek bir RUN komutu ile kur.
# Bu, Docker imaj katmanlarını azaltır ve derleme süresini optimize eder.
# - build-essential & cmake: TimescaleDB'yi kaynaktan derlemek için.
# - git: TimescaleDB deposunu klonlamak için.
# - postgresql-17-postgis-3 & scripts: PostGIS eklentisi.
# - postgresql-17-pgagent: pg_agent eklentisi.

RUN apt-get update && apt-get install -y \
    build-essential \
    cmake \
    git \
    postgresql-server-dev-17 \
    postgresql-17-postgis-3 \
    postgresql-17-postgis-3-scripts \
    postgresql-17-pgagent \
    && rm -rf /var/lib/apt/lists/*

# PostgreSQL 18 için resmi paketler henüz mevcut olmadığından TimescaleDB'yi kaynaktan derle.
# Bu, en son PostgreSQL sürümünü kullanma hedefinizi gerçekleştirmek için en güvenilir yöntemdir.
RUN git clone https://github.com/timescale/timescaledb.git /tmp/timescaledb && \
    cd /tmp/timescaledb && \
    git checkout 2.21.3 && \
    ./bootstrap -DREGRESS_CHECKS=OFF -DPROJECT_INSTALL_METHOD="source" && \
    cd build && \
    make && \
    make install && \
    # İmaj boyutunu küçük tutmak için derleme sonrası kaynak kodunu temizle
    rm -rf /tmp/timescaledb

# PostgreSQL'in başlangıçta yükleyeceği paylaşılan kütüphanelere TimescaleDB ve pg_agent'ı ekle.
# Bu, eklentilerin `CREATE EXTENSION` ile aktif edilebilmesi için zorunludur.
# RUN echo "shared_preload_libraries = 'timescaledb,pg_agent'" >> /usr/share/postgresql/postgresql.conf.sample

# PostgreSQL ve pg_agent'ı doğru sırada başlatacak olan özel script'i kopyala ve çalıştırılabilir yap.
# Başlangıçta çalışacak SQL script'lerini kopyala
COPY ./init/ /docker-entrypoint-initdb.d/
# Özel entrypoint script'ini kopyala
COPY entrypoint.sh /usr/local/bin/entrypoint.sh
RUN chmod +x /usr/local/bin/entrypoint.sh

# Varsayılan postgres kullanıcısına geri dön
USER postgres

ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
CMD ["postgres"]
