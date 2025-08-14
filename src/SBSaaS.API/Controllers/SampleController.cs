using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using SBSaaS.API.Resources;

namespace SBSaaS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SampleController : ControllerBase
{
    private readonly IStringLocalizer _loc;
    public SampleController(IStringLocalizerFactory factory)
    {
        var type = typeof(SampleController);
        _loc = factory.Create("Shared", System.Reflection.Assembly.GetExecutingAssembly().GetName().Name!);
    }

    [HttpGet("hello")]
    public IActionResult GetHelloMessage()
    {
        // "Hello" anahtarının o anki dile karşılık gelen değerini alma
        var message = _loc["Hello"];
        return Ok(message);
    }
    public IActionResult Hello() => Ok(new { message = _loc["Hello"] });
}