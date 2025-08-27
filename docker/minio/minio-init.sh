#!/bin/sh
set -e

echo ">> MinIO init başlıyor..."

# MinIO hazır olana kadar bekle (node1'e health probe)
until curl -s "http://${MINIO1_IP}:9000/minio/health/ready" > /dev/null; do
  echo "MinIO hazır değil, bekleniyor..."
  sleep 5
done

# mc alias
mc alias set local "http://${MINIO1_IP}:9000" "${MINIO_ROOT_USER}" "${MINIO_ROOT_PASSWORD}"

# Bucket oluştur (varsa sorun yok)
mc mb --ignore-existing "local/${MINIO_BUCKET}"

# Opsiyonel: public download (gerekliyse)
# mc anonymous set download "local/${MINIO_BUCKET}"

# RabbitMQ notification config
mc admin config set local notify_amqp:1 \
  url="amqp://${RABBITMQ_USER}:${RABBITMQ_PASSWORD}@${RABBITMQ_IP}:${RABBITMQ_PORT}" \
  exchange="minio-exchange" \
  exchange_type="fanout" \
  routing_key="file.uploaded" \
  durable="true"

# Config reload
mc admin service restart local

sleep 3

# Event'i bucket'a bağla (yalnızca put olayı)
mc event remove "local/${MINIO_BUCKET}" --force || true
mc event add --ignore-existing "local/${MINIO_BUCKET}" arn:minio:sqs::1:amqp --event put

echo ">> MinIO init tamamlandı."
