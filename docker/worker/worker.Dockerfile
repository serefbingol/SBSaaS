FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# .csproj dosyalarını kopyalayarak Docker katman önbelleğinden (layer cache) faydalanalım.
COPY src/SBSaaS.Worker/SBSaaS.Worker.csproj SBSaaS.Worker/
COPY src/SBSaaS.Application/SBSaaS.Application.csproj SBSaaS.Application/
COPY src/SBSaaS.Infrastructure/SBSaaS.Infrastructure.csproj SBSaaS.Infrastructure/
COPY src/SBSaaS.Domain/SBSaaS.Domain.csproj SBSaaS.Domain/
COPY src/SBSaaS.Common/SBSaaS.Common.csproj SBSaaS.Common/

# Bağımlılıkları geri yükle
RUN dotnet restore "SBSaaS.Worker/SBSaaS.Worker.csproj"

# Geri kalan tüm kaynak kodunu kopyala
COPY src/. .

# Uygulamayı yayınla (publish)
WORKDIR "/src/SBSaaS.Worker"
RUN dotnet publish "SBSaaS.Worker.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SBSaaS.Worker.dll"]
