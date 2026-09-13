using BandManager.Data;
using BandManager.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record SetTestCaseResultRequest(string Status, string? Notes);

/// <summary>
/// SuperAdmin's manual QA tracker - see TestCase.cs's doc comment for why
/// this is keyed by a stable string (content re-seeds on every startup,
/// Status/Notes/LastTestedAt never do). SuperAdmin-only throughout, same
/// single-policy shape as SuperAdminController itself - no BandMember
/// mixing here, so a class-level [Authorize] is safe (see GigsController's
/// doc comment for why that's NOT always true elsewhere in this app).
/// </summary>
[ApiController]
[Route("/api/test-suite")]
[Authorize(Policy = "SuperAdmin")]
public class TestSuiteController(ApplicationDbContext db) : ControllerBase
{
    private static object Serialize(TestCase t) => new
    {
        key = t.Key,
        area = t.Area,
        title = t.Title,
        steps = t.Steps,
        expectedResult = t.ExpectedResult,
        sortOrder = t.SortOrder,
        status = t.Status.ToString(),
        notes = t.Notes,
        lastTestedAt = t.LastTestedAt
    };

    [HttpGet("cases")]
    public async Task<IActionResult> ListCases()
    {
        var cases = await db.TestCases.AsNoTracking().OrderBy(t => t.Area).ThenBy(t => t.SortOrder).ToListAsync();
        return Ok(cases.Select(Serialize));
    }

    [HttpPut("cases/{key}/result")]
    public async Task<IActionResult> SetResult(string key, [FromBody] SetTestCaseResultRequest request)
    {
        if (!Enum.TryParse<TestCaseStatus>(request.Status, ignoreCase: true, out var status))
            return BadRequest(new { error = "Invalid status." });

        var testCase = await db.TestCases.FindAsync(key);
        if (testCase is null) return NotFound(new { error = "Test case not found." });

        testCase.Status = status;
        testCase.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        testCase.LastTestedAt = status == TestCaseStatus.NotTested ? null : DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(Serialize(testCase));
    }

    // Bulk reset for starting a fresh testing pass without losing the
    // content itself (which lives in TestCaseSeedData, not here).
    [HttpPost("cases/reset-all")]
    public async Task<IActionResult> ResetAll()
    {
        await db.TestCases.ExecuteUpdateAsync(s => s
            .SetProperty(t => t.Status, TestCaseStatus.NotTested)
            .SetProperty(t => t.Notes, (string?)null)
            .SetProperty(t => t.LastTestedAt, (DateTime?)null));
        return Ok(new { ok = true });
    }
}
