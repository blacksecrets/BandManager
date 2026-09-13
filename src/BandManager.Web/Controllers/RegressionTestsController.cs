using BandManager.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BandManager.Web.Controllers;

/// <summary>
/// SuperAdmin's "Run Regression Tests" button on the Test Suite page - see
/// RegressionTestRunner for what actually runs and why it's safe against
/// real bands.
/// </summary>
[ApiController]
[Route("/api/regression-tests")]
[Authorize(Policy = "SuperAdmin")]
public class RegressionTestsController(RegressionTestRunner runner) : ControllerBase
{
    [HttpPost("run")]
    public async Task<IActionResult> Run()
    {
        var results = await runner.RunAllAsync();
        return Ok(results.Select(r => new { r.Name, r.Passed, r.Message, r.DurationMs }));
    }
}
