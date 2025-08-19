using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SBSaaS.Infrastructure.Audit;
using SBSaaS.Infrastructure.Persistence;

namespace SBSaaS.API.Controllers;

[ApiController]
[Route("api/v1/audit")]
[Authorize]
public class AuditController : ControllerBase
{
    private readonly SbsDbContext _db;
    public AuditController(SbsDbContext db) => _db = db;

    [HttpGet("change-log")]
    public async Task<IActionResult> Query([FromQuery] DateTime? from, [FromQuery] DateTime? to,
                                           [FromQuery] string? table, [FromQuery] string? operation,
                                           [FromQuery] string? userId,
                                           [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var q = _db.Set<ChangeLog>().AsNoTracking();
        if (from is not null) q = q.Where(x => x.UtcDate >= from);
        if (to   is not null) q = q.Where(x => x.UtcDate <= to);
        if (!string.IsNullOrWhiteSpace(table)) q = q.Where(x => x.TableName == table);
        if (!string.IsNullOrWhiteSpace(operation)) q = q.Where(x => x.Operation == operation);
        if (!string.IsNullOrWhiteSpace(userId)) q = q.Where(x => x.UserId == userId);

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(x => x.UtcDate)
                           .Skip((page - 1) * pageSize)
                           .Take(pageSize)
                           .ToListAsync();
        return Ok(new { items, page, pageSize, total });
    }
}
