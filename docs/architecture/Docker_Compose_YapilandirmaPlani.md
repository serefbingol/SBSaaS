### **Doküman: Proje Docker Compose Entegrasyon ve Yapılandırma Planı**

**Amaç:**  Projenin tüm backend servislerini (API, Veritabanı, Dosya Depolama, Antivirüs) Docker Compose ile yönetilebilir, standartlaştırılmış ve güvenli bir geliştirme ortamında bir araya getirmek.

**Genel Yaklaşım:**  Servisler,  `internal`  ve  `public`  olarak iki ayrı sanal ağ üzerinde çalışacaktır. Kritik veritabanı ve depolama servisleri dış dünyaya tamamen kapalı  `internal`  ağda yer alırken, internet erişimi veya dış dünyaya açılması gereken servisler  `public`  ağda da bulunacaktır. Tüm yapılandırma, geliştirici tarafından kolayca düzenlenebilir bir  `.env`  dosyası üzerinden yönetilecektir.
**ÖNEMLİ:** 
Postgresql olarak oluşturulacak konteynır için, 17 (veya 17.5) versiyonu (Resmi sürüm) tercih edilecektir. Bu konteynıra Timescale, PostGIS, pg_agent eklentileri de kurulacaktır. PostgreSQL Konteynırında create extensions komutu ile bu eklentiler aktif edilecektir. 
docker klasörü içinde yer alan db klasörü altında, PostgreSQL servisini izole bir şekilde test etmek veya sadece PostgreSQL'i ayağa kaldırmak için kullanılabilir dosyalar mevcut olacaktır. 
----------

#### **1. İsimlendirme Standardı**

Tüm Docker konteynırları, projeye özgü olduklarını belirtmek ve diğer konteynırlarla karışmasını önlemek amacıyla  `sbsaas_`  öneki ile isimlendirilecektir.

-   **Örnekler:**  `sbsaas_postgre`,  `sbsaas_api`,  `sbsaas_minio1`

#### **2. Ağ Yapısı ve Statik IP Atamaları**

Oluşturulacak iki sanal ağ ve bu ağlardaki servislerin statik IP dağılımı aşağıdaki gibidir:

-   **`internal-network`  (Subnet: 172.28.0.0/24):**  Sadece iç servis haberleşmesi için.
-   **`public-network`  (Subnet: 172.29.0.0/24):**  İnternet erişimi gereken servisler için.


| Servis Adı       | Konteyner Adı    | Ağlar                                | IP Adresleri                 | Gerekçe                                                                                   |
| ---------------- | ---------------- | ------------------------------------ | ---------------------------- | ----------------------------------------------------------------------------------------- |
| **PostgreSQL**   | `sbsaas_postgre` | `internal-network`                   | `172.28.0.10`                | Veritabanı, sadece API tarafından erişilmesi gereken kritik bir servistir.                |
| **MinIO Node 1** | `sbsaas_minio1`  | `internal-network`                   | `172.28.0.21`                | Dağıtık modda çalışarak veri yedekliliği sağlar. Sadece API tarafından erişilir.          |
| **MinIO Node 2** | `sbsaas_minio2`  | `internal-network`                   | `172.28.0.22`                | Dağıtık modda çalışarak veri yedekliliği sağlar. Sadece API tarafından erişilir.          |
| **MinIO Node 3** | `sbsaas_minio3`  | `internal-network`                   | `172.28.0.23`                | Dağıtık modda çalışarak veri yedekliliği sağlar. Sadece API tarafından erişilir.          |
| **MinIO Node 4** | `sbsaas_minio4`  | `internal-network`                   | `172.28.0.24`                | Dağıtık modda çalışarak veri yedekliliği sağlar. Sadece API tarafından erişilir.          |
| **ClamAV**       | `sbsaas_clamav`  | `internal-network`, `public-network` | `172.28.0.30`, `172.29.0.30` | API ile iç ağdan haberleşirken, virüs tanımlarını güncellemek için internete erişmelidir. |
| **API**          | `sbsaas_api`     | `internal-network`, `public-network` | `172.28.0.40`, `172.29.0.40` | Diğer iç servislere erişmeli ve dışarıdan gelen isteklere cevap verebilmelidir.           |

#### **3. Port Yapılandırması**

Geliştirme ortamlarında sıkça yaşanan port çakışmalarını önlemek amacıyla, servisler host makinesinde standart olmayan portlar üzerinden yayınlanacaktır.
| Servis        | Host Portu (Yayın) | Konteyner Portu (İç) | Açıklama                                                     |
| ------------- | ------------------ | -------------------- | ------------------------------------------------------------ |
| PostgreSQL    | `5437`             | `5432`               | Standart PostgreSQL portu (5432) ile çakışmayı önler.        |
| MinIO API     | `9010`             | `9000`               | `sbsaas_minio1` üzerinden dışarıya açılan API portu.         |
| MinIO Console | `9011`             | `9001`               | `sbsaas_minio1` üzerinden dışarıya açılan web arayüzü portu. |
| SBSaaS API    | `8088`             | `8080`               | API'nin dış dünyaya açılacağı ana port.                      |
| ClamAV        | `3311`             | `3310`               | ClamAV daemon servisi için standart port.<br><br>            |

#### **4. Merkezi Yapılandırma (`.env`  Dosyası)**

`docker`  klasörü altında oluşturulacak olan  `.env`  dosyası, tüm ortam değişkenlerini ve hassas verileri (şifreler, anahtarlar) içerecektir. Bu dosya  `.gitignore`  içinde yer almalı ve her geliştirici kendi lokalinde bu dosyayı oluşturmalıdır.

**Örnek  `.env`  Dosya İçeriği:**

```env
# === GENEL AYARLAR ===
# Proje genelinde kullanılacak zaman dilimi
TZ=Europe/Istanbul

# === POSTGRESQL VERİTABANI AYARLARI ===
# Veritabanı adı
POSTGRES_DB=sbsaas_db
# Veritabanı yönetici kullanıcı adı
POSTGRES_USER=sbsaas_user
# Veritabanı yönetici şifresi (Lütfen geliştirme ortamı için bile olsa güçlü bir şifre kullanın)
POSTGRES_PASSWORD=GucluSifre2025!
# Host makinesinde PostgreSQL'in yayınlanacağı port
POSTGRES_PORT=5437

# === MINIO OBJECT STORAGE AYARLARI (DAĞITIK MOD) ===
# MinIO erişim anahtarı (Kullanıcı adı gibi)
MINIO_ROOT_USER=sbsaas_minio_admin
# MinIO gizli anahtarı (Şifre gibi)
MINIO_ROOT_PASSWORD=GucluMinioSifre2025!
# Host makinesinde MinIO API'sinin yayınlanacağı port
MINIO_API_PORT=9010
# Host makinesinde MinIO Web Arayüzü'nün yayınlanacağı port
MINIO_CONSOLE_PORT=9011

# === SBSAAS API AYARLARI ===
# API'nin host makinesinde yayınlanacağı port
API_PORT=8088
# JWT (JSON Web Token) için kullanılacak gizli anahtar. BU DEĞER MUTLAKA DEĞİŞTİRİLMELİDİR.
JWT_SECRET=BuCokGizliBirAnahtarDegistirilmeliVeKarmaşıkOlmalı!
# JWT Token'ı yayınlayan (issuer) taraf
JWT_ISSUER=sbsaas_api
# JWT Token'ını kullanacak hedef kitle (audience)
JWT_AUDIENCE=sbsaas_clients

# === CLAMAV ANTIVIRUS AYARLARI ===
# ClamAV servisinin host makinesinde yayınlanacağı port
CLAMAV_PORT=3310

```

#### **5. Veri Kalıcılığı (Volumes)**

Servislerin ürettiği ve sakladığı verilerin, konteynırlar yeniden başlatılsa bile kalıcı olmasını sağlamak için Docker'ın isimlendirilmiş  `volume`'leri kullanılacaktır.

-   `sbsaas_postgres_data`: PostgreSQL veritabanı dosyaları için.
-   `sbsaas_minio1_data`,  `sbsaas_minio2_data`,  `sbsaas_minio3_data`,  `sbsaas_minio4_data`: MinIO'nun 4 node'u için ayrı ayrı veri saklama alanları.
-   `sbsaas_clamav_db`: ClamAV tarafından indirilen ve güncellenen virüs tanım veritabanı için.

#### **6. API Servisi Build Yapılandırması**

`sbsaas_api`  servisinin imajı, projenin kök dizininden derlenecektir. Bu, Dockerfile içinde  `../src`  gibi göreceli path'lerin doğru çalışmasını sağlar.  `docker-compose.yml`  içindeki ilgili tanım:

-   `context: .`
-   `dockerfile: ./docker/api.Dockerfile`

#### **7. ClamAV Antivirüs Servisi**

Sisteme yüklenecek dosyaların güvenliğini sağlamak amacıyla,  `clamav/clamav`  imajı kullanılarak bir  `sbsaas_clamav`  servisi eklenecektir. Bu servis,  `freshclam`  isimli aracı sayesinde virüs veritabanını periyodik olarak otomatik güncelleyecektir. API, dosya yükleme işlemlerinde bu servisi kullanacaktır.