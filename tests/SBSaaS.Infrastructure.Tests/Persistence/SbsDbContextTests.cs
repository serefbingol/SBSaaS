using Microsoft.EntityFrameworkCore;
using Moq;
using SBSaaS.Application.Interfaces;
using SBSaaS.Domain.Entities;
using SBSaaS.Domain.Entities.Projects;
using SBSaaS.Infrastructure.Persistence;
using System;
using System.Threading.Tasks;
using Xunit;

namespace SBSaaS.Infrastructure.Tests.Persistence;

public class SbsDbContextTests
{
    private readonly Guid _tenantA_Id = Guid.NewGuid();
    private readonly Guid _tenantB_Id = Guid.NewGuid();
    private readonly Mock<ITenantContext> _mockTenantContext;

    public SbsDbContextTests()
    {
        _mockTenantContext = new Mock<ITenantContext>();
    }

    private SbsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SbsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()) // Her test için temiz DB
            .Options;

        return new SbsDbContext(options, _mockTenantContext.Object);
    }

    [Fact]
    public async Task SaveChangesAsync_Should_SetTenantId_OnNewTenantScopedEntity()
    {
        // Arrange
        _mockTenantContext.Setup(t => t.TenantId).Returns(_tenantA_Id);
        var dbContext = CreateDbContext();
        var project = new Project { Id = Guid.NewGuid(), Name = "Test Project" };

        // Act
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync();

        // Assert
        Assert.Equal(_tenantA_Id, project.TenantId);
        Assert.NotEqual(default, project.CreatedUtc);
    }

    [Fact]
    public async Task SaveChangesAsync_Should_ThrowException_WhenUpdatingCrossTenantEntity()
    {
        // Arrange
        // 1. Tenant B olarak veri ekle
        _mockTenantContext.Setup(t => t.TenantId).Returns(_tenantB_Id);
        var dbContext = CreateDbContext();
        var projectForTenantB = new Project { Id = Guid.NewGuid(), Name = "Project B" };
        await dbContext.Projects.AddAsync(projectForTenantB);
        await dbContext.SaveChangesAsync();

        // 2. Şimdi Tenant A olarak davran
        _mockTenantContext.Setup(t => t.TenantId).Returns(_tenantA_Id);
        var detachedProject = await dbContext.Projects.AsNoTracking().FirstAsync();
        detachedProject.Name = "Updated by Tenant A";

        // Act & Assert - Tenant A, Tenant B'nin verisini güncellemeye çalışıyor
        dbContext.Projects.Update(detachedProject);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());
        Assert.Contains("Cross-tenant update attempt detected", exception.Message);
    }

    [Fact]
    public async Task SaveChangesAsync_Should_ThrowException_WhenModifyingTenantId()
    {
        // Arrange
        _mockTenantContext.Setup(t => t.TenantId).Returns(_tenantA_Id);
        var dbContext = CreateDbContext();
        var project = new Project { Id = Guid.NewGuid(), Name = "Initial Project" };
        await dbContext.Projects.AddAsync(project);
        await dbContext.SaveChangesAsync();

        // Act
        var savedProject = await dbContext.Projects.FirstAsync();
        savedProject.TenantId = _tenantB_Id; // TenantId'yi değiştirmeye çalış

        // Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());
        Assert.Contains("Changing the TenantId of an existing entity is not allowed", exception.Message);
    }

    [Fact]
    public async Task SaveChangesAsync_Should_ThrowException_WhenDeletingCrossTenantEntity()
    {
        // Arrange
        // 1. Tenant B olarak veri ekle
        _mockTenantContext.Setup(t => t.TenantId).Returns(_tenantB_Id);
        var dbContext = CreateDbContext();
        var projectForTenantB = new Project { Id = Guid.NewGuid(), Name = "Project B" };
        await dbContext.Projects.AddAsync(projectForTenantB);
        await dbContext.SaveChangesAsync();

        // 2. Şimdi Tenant A olarak davran
        _mockTenantContext.Setup(t => t.TenantId).Returns(_tenantA_Id);
        var projectToDelete = await dbContext.Projects.FirstAsync(); // Bu proje Tenant B'ye ait

        // Act & Assert
        dbContext.Projects.Remove(projectToDelete);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());
        Assert.Contains("Cross-tenant delete attempt detected", exception.Message);
    }

    [Fact]
    public async Task SaveChangesAsync_Should_ThrowException_WhenContextHasNoTenantId()
    {
        // Arrange
        _mockTenantContext.Setup(t => t.TenantId).Returns(Guid.Empty); // Geçerli tenant yok
        var dbContext = CreateDbContext();
        var project = new Project { Id = Guid.NewGuid(), Name = "Orphan Project" };

        // Act
        dbContext.Projects.Add(project);

        // Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());
        Assert.Contains("A valid tenant context is required", exception.Message);
    }
}
