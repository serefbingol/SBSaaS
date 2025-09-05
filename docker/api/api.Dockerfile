FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# .csproj dosyalarını kopyalayarak Docker katman önbelleğinden (layer cache) faydalanalım.
# Bu sayede, sadece proje bağımlılıkları değiştiğinde 'dotnet restore' adımı tekrar çalışır.
COPY src/SBSaaS.API/SBSaaS.API.csproj SBSaaS.API/
COPY src/SBSaaS.Application/SBSaaS.Application.csproj SBSaaS.Application/
COPY src/SBSaaS.Infrastructure/SBSaaS.Infrastructure.csproj SBSaaS.Infrastructure/
COPY src/SBSaaS.Domain/SBSaaS.Domain.csproj SBSaaS.Domain/
COPY src/SBSaaS.Common/SBSaaS.Common.csproj SBSaaS.Common/

# Bağımlılıkları geri yükle
RUN dotnet restore "SBSaaS.API/SBSaaS.API.csproj"

# Geri kalan tüm kaynak kodunu kopyala
COPY src/. .

# Uygulamayı yayınla (publish)
WORKDIR "/src/SBSaaS.API"
RUN dotnet publish "SBSaaS.API.csproj" -c Release -o /app/publish

# Final stage: Sadece runtime için gerekli dosyaları içeren küçük imajı oluştur
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SBSaaS.API.dll"]
