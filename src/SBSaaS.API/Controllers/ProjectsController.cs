using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBSaaS.Application.Interfaces;
using SBSaaS.Domain.Entities.Projects;
using SBSaaS.Infrastructure.Persistence;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SBSaaS.API.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class ProjectsController : ControllerBase
{
    private readonly SbsDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public ProjectsController(SbsDbContext dbContext, ITenantContext tenantContext)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Project>>> GetProjects()
    {
        var projects = await _dbContext.Projects
            .Where(p => p.TenantId == _tenantContext.TenantId)
            .ToListAsync();
        return Ok(new PagedResult<Project>(projects, 1, projects.Count, projects.Count));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Project>> GetProjectById(Guid id)
    {
        var project = await _dbContext.Projects
            .Where(p => p.Id == id && p.TenantId == _tenantContext.TenantId)
            .FirstOrDefaultAsync();

        if (project == null)
        {
            return NotFound();
        }

        return Ok(project);
    }

    // Helper DTO for PagedResult, as used in integration tests
    public record PagedResult<T>(List<T> Items, int Page, int PageSize, int Total);
}