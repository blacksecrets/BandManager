namespace BandManager.Data.Entities;

public enum TestCaseStatus { NotTested, Pass, Fail, Blocked }

/// <summary>
/// One manual QA scenario for SuperAdmin's Test Suite page - Richard's
/// "beat the shit out of it" checklist. Keyed by a stable string (not a
/// Guid) so the content (Area/Title/Steps/ExpectedResult/SortOrder) can be
/// upserted from TestCaseSeedData on every startup the same way
/// DbSeeder's other reference data already works - editing a test case's
/// wording later takes effect on the next restart. Status/Notes/
/// LastTestedAt are the one part of this row a human actually owns; the
/// seed upsert never touches them, so a completed pass/fail survives a
/// content edit to the same test case.
/// </summary>
public class TestCase
{
    public required string Key { get; set; }
    public required string Area { get; set; }
    public required string Title { get; set; }
    public required string Steps { get; set; }
    public required string ExpectedResult { get; set; }
    public int SortOrder { get; set; }

    public TestCaseStatus Status { get; set; } = TestCaseStatus.NotTested;
    public string? Notes { get; set; }
    public DateTime? LastTestedAt { get; set; }
}
