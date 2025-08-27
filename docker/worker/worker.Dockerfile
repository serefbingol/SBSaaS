FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY src/SBSaaS.Worker/SBSaaS.Worker.csproj src/SBSaaS.Worker/
COPY src/SBSaaS.Application/SBSaaS.Application.csproj src/SBSaaS.Application/
COPY src/SBSaaS.Infrastructure/SBSaaS.Infrastructure.csproj src/SBSaaS.Infrastructure/
COPY src/SBSaaS.Domain/SBSaaS.Domain.csproj src/SBSaaS.Domain/
COPY src/SBSaaS.Common/SBSaaS.Common.csproj src/SBSaaS.Common/
RUN dotnet restore "src/SBSaaS.Worker/SBSaaS.Worker.csproj"

COPY . .
RUN dotnet publish "src/SBSaaS.Worker/SBSaaS.Worker.csproj" -c Release -o /app/publish --self-contained false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SBSaaS.Worker.dll"]
