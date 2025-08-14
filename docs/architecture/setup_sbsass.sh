#!/bin/bash

# Çözüm adı
SOLUTION_NAME="SBSaaS"

# 1. Ana klasörü oluştur
mkdir $SOLUTION_NAME
cd $SOLUTION_NAME

# 2. Solution oluştur
dotnet new sln -n $SOLUTION_NAME

# 3. Klasör yapısı
mkdir -p src tests build/ci build/scripts docs/{architecture,api,requirements}

# 4. Projeler
cd src
dotnet new classlib -n $SOLUTION_NAME.Domain
dotnet new classlib -n $SOLUTION_NAME.Application
dotnet new classlib -n $SOLUTION_NAME.Infrastructure
dotnet new classlib -n $SOLUTION_NAME.Common
dotnet new webapi -n $SOLUTION_NAME.API
dotnet new blazorwasm -n $SOLUTION_NAME.WebApp --no-https

cd ../tests
dotnet new xunit -n $SOLUTION_NAME.UnitTests
dotnet new xunit -n $SOLUTION_NAME.IntegrationTests

cd ..

# 5. Projeleri solution’a ekle
dotnet sln add src/$SOLUTION_NAME.Domain/$SOLUTION_NAME.Domain.csproj
dotnet sln add src/$SOLUTION_NAME.Application/$SOLUTION_NAME.Application.csproj
dotnet sln add src/$SOLUTION_NAME.Infrastructure/$SOLUTION_NAME.Infrastructure.csproj
dotnet sln add src/$SOLUTION_NAME.Common/$SOLUTION_NAME.Common.csproj
dotnet sln add src/$SOLUTION_NAME.API/$SOLUTION_NAME.API.csproj
dotnet sln add src/$SOLUTION_NAME.WebApp/$SOLUTION_NAME.WebApp.csproj
dotnet sln add tests/$SOLUTION_NAME.UnitTests/$SOLUTION_NAME.UnitTests.csproj
dotnet sln add tests/$SOLUTION_NAME.IntegrationTests/$SOLUTION_NAME.IntegrationTests.csproj

# 6. Proje referansları
dotnet add src/$SOLUTION_NAME.Application/$SOLUTION_NAME.Application.csproj reference src/$SOLUTION_NAME.Domain/$SOLUTION_NAME.Domain.csproj
dotnet add src/$SOLUTION_NAME.Infrastructure/$SOLUTION_NAME.Infrastructure.csproj reference src/$SOLUTION_NAME.Application/$SOLUTION_NAME.Application.csproj src/$SOLUTION_NAME.Domain/$SOLUTION_NAME.Domain.csproj
dotnet add src/$SOLUTION_NAME.Common/$SOLUTION_NAME.Common.csproj reference src/$SOLUTION_NAME.Domain/$SOLUTION_NAME.Domain.csproj
dotnet add src/$SOLUTION_NAME.API/$SOLUTION_NAME.API.csproj reference src/$SOLUTION_NAME.Application/$SOLUTION_NAME.Application.csproj src/$SOLUTION_NAME.Infrastructure/$SOLUTION_NAME.Infrastructure.csproj src/$SOLUTION_NAME.Common/$SOLUTION_NAME.Common.csproj
dotnet add src/$SOLUTION_NAME.WebApp/$SOLUTION_NAME.WebApp.csproj reference src/$SOLUTION_NAME.Common/$SOLUTION_NAME.Common.csproj

# 7. Test projeleri referansları
dotnet add tests/$SOLUTION_NAME.UnitTests/$SOLUTION_NAME.UnitTests.csproj reference src/$SOLUTION_NAME.Application/$SOLUTION_NAME.Application.csproj src/$SOLUTION_NAME.Domain/$SOLUTION_NAME.Domain.csproj
dotnet add tests/$SOLUTION_NAME.IntegrationTests/$SOLUTION_NAME.IntegrationTests.csproj reference src/$SOLUTION_NAME.API/$SOLUTION_NAME.API.csproj src/$SOLUTION_NAME.Infrastructure/$SOLUTION_NAME.Infrastructure.csproj

echo "✅ SBSaaS proje yapısı başarıyla oluşturuldu."
