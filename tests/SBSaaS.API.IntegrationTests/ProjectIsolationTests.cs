using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SBSaaS.Domain.Entities;
using SBSaaS.Domain.Entities.Projects;
using SBSaaS.Infrastructure.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;

namespace SBSaaS.API.IntegrationTests;

public class ProjectIsolationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly Guid _tenantA_Id = Guid.NewGuid();
    private readonly Guid _tenantB_Id = Guid.NewGuid();

    public ProjectIsolationTests(WebApplicationFactory<Program> factory)
    {
        // Not: Üretimde, burası Testcontainers ile geçici bir veritabanı oluşturacak
        // veya her testten önce veritabanını temizleyecek şekilde yapılandırılmalıdır.
        _factory = factory;
    }

    private async Task SeedDataForTenantsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SbsDbContext>();

        // Önceki testlerden kalan verileri temizle
        await dbContext.Projects.ExecuteDeleteAsync();

        dbContext.Projects.AddRange(
            new Project { Id = Guid.NewGuid(), Name = "Project A1", TenantId = _tenantA_Id, Code = "A1" },
            new Project { Id = Guid.NewGuid(), Name = "Project A2", TenantId = _tenantA_Id, Code = "A2" },
            new Project { Id = Guid.NewGuid(), Name = "Project B1", TenantId = _tenantB_Id, Code = "B1" }
        );
        await dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task GetProjects_WhenCalledWithTenantHeader_ShouldOnlyReturnOwnProjects()
    {
        // Arrange
        await SeedDataForTenantsAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", _tenantA_Id.ToString());

        // Act
        var response = await client.GetFromJsonAsync<PagedResult<Project>>("/api/v1/projects");

        // Assert
        Assert.NotNull(response);
        Assert.Equal(2, response.Total);
        Assert.All(response.Items, item => Assert.Equal(_tenantA_Id, item.TenantId));
        Assert.DoesNotContain(response.Items, item => item.Name == "Project B1");
    }

    [Fact]
    public async Task GetProjectById_WhenCalledForAnotherTenant_ShouldReturnNotFound()
    {
        // Arrange
        await SeedDataForTenantsAsync();
        var client = _factory.CreateClient();

        // Tenant B'ye ait projenin ID'sini al
        Guid projectB_Id;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SbsDbContext>();
            projectB_Id = (await db.Projects.FirstAsync(p => p.TenantId == _tenantB_Id)).Id;
        }

        // Tenant A olarak istek yap
        client.DefaultRequestHeaders.Add("X-Tenant-Id", _tenantA_Id.ToString());

        // Act
        var response = await client.GetAsync($"/api/v1/projects/{projectB_Id}");

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    // API yanıtı için yardımcı DTO
    private record PagedResult<T>(List<T> Items, int Page, int PageSize, int Total);
}
