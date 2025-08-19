using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace SBSaaS.API.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class SampleController : ControllerBase
{
    private readonly IStringLocalizer _loc;
    public SampleController(IStringLocalizerFactory factory)
    {
        var asm = typeof(Program).Assembly.GetName().Name!;
        _loc = factory.Create("Shared", asm);
    }

    [HttpGet("hello")]
    public IActionResult Hello() => Ok(new { message = _loc["Hello"] });
}
