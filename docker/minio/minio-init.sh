#!/bin/sh
set -e

echo ">> MinIO init başlıyor..."

# MinIO hazır olana kadar bekle (node1'e health probe)
echo "MinIO health check başlatılıyor..."
until curl -s "http://${MINIO1_IP}:9000/minio/health/ready" | grep -q "X-Minio-Server-Status: online"; do
  echo "MinIO hazır değil, bekleniyor... (X-Minio-Server-Status: offline)"
  sleep 5
done
echo "MinIO health check başarılı."

# mc alias'ı ayarla
echo "mc alias ayarlanıyor..."
mc alias set local "http://${MINIO1_IP}:9000" "${MINIO_ROOT_USER}" "${MINIO_ROOT_PASSWORD}"
echo "mc alias ayarlandı."

# mc alias'ın çalışır durumda olduğundan emin ol (örneğin, mc ls ile)
echo "mc alias kontrol ediliyor..."
until mc ls local > /dev/null; do
  echo "MinIO alias hazır değil, bekleniyor... (mc ls exit code: $?)"
  sleep 5
done
echo "mc alias hazır."

# Bucket oluştur (varsa sorun yok)
echo "Bucket oluşturuluyor..."
mc mb --ignore-existing "local/${MINIO_BUCKET}"
echo "Bucket oluşturuldu."

# Geliştirme ortamı için geniş CORS ayarları.
# Presigned URL ile client-side (tarayıcıdan) dosya yükleme (PUT) için bu gereklidir.
# Üretimde 'cors_allow_origin' değerini kendi alan adlarınızla kısıtlayın.
echo "CORS ayarları yapılıyor..."
mc admin config set local api cors_allow_origin="*"
echo "CORS ayarları yapıldı."

# RabbitMQ bildirim entegrasyonu (Worker servisini tetiklemek için)
echo "RabbitMQ bildirim hedefi ayarlanıyor..."
mc admin config set local notify_amqp:1 \
  endpoint="http://${RABBITMQ_USER}:${RABBITMQ_PASSWORD}@${RABBITMQ_IP}:5672" \
  exchange="${RABBITMQ_EXCHANGE}" \
  exchange_type="direct" \
  routing_key="file.uploaded" \
  mandatory="on" \
  durable="on"
echo "RabbitMQ bildirim hedefi ayarlandı."

# Bucket için olay bildirimini etkinleştir
echo "Bucket için olay bildirimi etkinleştiriliyor..."
mc event add local/${MINIO_BUCKET} arn:minio:sqs::1:amqp --event s3:ObjectCreated:*
echo "Bucket için olay bildirimi etkinleştirildi."

# Yapılandırmayı yeniden yükle
echo "MinIO servisi yeniden başlatılıyor..."
mc admin service restart local
echo "MinIO servisi yeniden başlatıldı."

echo ">> MinIO init tamamlandı."