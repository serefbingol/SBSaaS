FROM alpine/git:latest
RUN apk add --no-cache curl && \
    wget https://dl.minio.io/client/mc/release/linux-amd64/mc && \
    chmod +x mc && \
    mv mc /usr/local/bin/