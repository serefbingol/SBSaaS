using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SBSaaS.Application.Interfaces;

namespace SBSaaS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FilesController : ControllerBase
{
    private readonly IFileStorage _files;
    public FilesController(IFileStorage files) => _files = files;

    [HttpPost("upload")]
    [Authorize]
    public async Task<IActionResult> Upload([FromForm] IFormFile file, [FromQuery] string bucket="sbs-files")
    {
        if (file == null || file.Length == 0) return BadRequest("file required");
        await using var stream = file.OpenReadStream();
        var name = Guid.NewGuid() + Path.GetExtension(file.FileName);
        await _files.UploadAsync(bucket, name, stream, file.ContentType, HttpContext.RequestAborted);
        return Ok(new { objectName = name, bucket });
    }
}