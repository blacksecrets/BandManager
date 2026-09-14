using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record SaveExpenseRequest(string Purpose, string? Vendor, string? VendorUrl, string PurchaseDate, decimal Amount);

/// <summary>
/// A member's reimbursable-expense log - GET without "/all" is always the
/// caller's own rows (a personal log, same framing as
/// AccountingController.GetMyPayouts); "/all" is the BandAdmin oversight
/// view across every member, for marking things reimbursed. A BandAdmin
/// never edits someone else's expense entry directly, only its
/// IsReimbursed flag - same split GigPayoutRecipient.IsPaid already uses.
/// </summary>
[ApiController]
[Route("/api/expenses")]
[Authorize(Policy = "BandMember")]
public class ExpensesController(ApplicationDbContext db, IActiveBandAccessor activeBand, IWebHostEnvironment env) : ControllerBase
{
    private const long MaxReceiptBytes = 20 * 1024 * 1024;

    private IActionResult? RequireActiveBand(out Guid bandId)
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) { bandId = default; return BadRequest(new { error = "No active band selected." }); }
        bandId = id.Value;
        return null;
    }

    private static object Serialize(Expense e) => new
    {
        id = e.Id,
        userId = e.UserId,
        memberName = e.User?.DisplayName,
        purpose = e.Purpose,
        vendor = e.Vendor,
        vendorUrl = e.VendorUrl,
        receiptUrl = e.ReceiptFileName is { } fn ? $"/receipts/{fn}" : null,
        purchaseDate = e.PurchaseDate.ToString("yyyy-MM-dd"),
        amount = e.Amount,
        isReimbursed = e.IsReimbursed
    };

    [HttpGet]
    public async Task<IActionResult> ListMine()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var expenses = await db.Expenses.AsNoTracking()
            .Where(e => e.BandId == bandId && e.UserId == userId)
            .OrderByDescending(e => e.PurchaseDate).ToListAsync();
        return Ok(expenses.Select(Serialize));
    }

    [HttpGet("all")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> ListAll()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var expenses = await db.Expenses.AsNoTracking().Include(e => e.User)
            .Where(e => e.BandId == bandId)
            .OrderByDescending(e => e.PurchaseDate).ToListAsync();
        return Ok(expenses.Select(Serialize));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveExpenseRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        var (expense, validationErr) = Build(bandId, userId.Value, request, null);
        if (validationErr is not null) return validationErr;

        db.Expenses.Add(expense!);
        await db.SaveChangesAsync();
        return Ok(Serialize(expense!));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveExpenseRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var existing = await db.Expenses.FirstOrDefaultAsync(e => e.Id == id && e.BandId == bandId && e.UserId == userId);
        if (existing is null) return NotFound(new { error = "Not found" });
        var (_, validationErr) = Build(bandId, userId.Value, request, existing);
        if (validationErr is not null) return validationErr;

        await db.SaveChangesAsync();
        return Ok(Serialize(existing));
    }

    private static (Expense? Expense, IActionResult? Error) Build(Guid bandId, Guid userId, SaveExpenseRequest request, Expense? existing)
    {
        var purpose = request.Purpose?.Trim();
        if (string.IsNullOrEmpty(purpose)) return (null, new BadRequestObjectResult(new { error = "Purpose is required." }));
        if (!DateOnly.TryParse(request.PurchaseDate, out var date))
            return (null, new BadRequestObjectResult(new { error = "A valid purchase date is required." }));
        if (request.Amount <= 0) return (null, new BadRequestObjectResult(new { error = "Amount must be greater than zero." }));

        var expense = existing ?? new Expense { BandId = bandId, UserId = userId, Purpose = purpose };
        expense.Purpose = purpose;
        expense.Vendor = string.IsNullOrWhiteSpace(request.Vendor) ? null : request.Vendor.Trim();
        expense.VendorUrl = string.IsNullOrWhiteSpace(request.VendorUrl) ? null : request.VendorUrl.Trim();
        expense.PurchaseDate = date;
        expense.Amount = request.Amount;
        return (expense, null);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();

        var expense = await db.Expenses.FirstOrDefaultAsync(e => e.Id == id && e.BandId == bandId && e.UserId == userId);
        if (expense is null) return NotFound(new { error = "Not found" });

        if (expense.ReceiptFileName is { } fn)
        {
            try { System.IO.File.Delete(Path.Combine(env.ContentRootPath, "data", "receipts", fn)); } catch { /* best-effort */ }
        }
        db.Expenses.Remove(expense);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpPost("{id:guid}/receipt")]
    [RequestSizeLimit(MaxReceiptBytes)]
    public async Task<IActionResult> UploadReceipt(Guid id)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var userId = User.GetUserId();
        if (userId is null) return Unauthorized();
        var expense = await db.Expenses.FirstOrDefaultAsync(e => e.Id == id && e.BandId == bandId && e.UserId == userId);
        if (expense is null) return NotFound(new { error = "Not found" });

        if (!Request.HasFormContentType) return BadRequest(new { error = "No file provided" });
        var form = await Request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0) return BadRequest(new { error = "No file provided" });

        var receiptsDir = Path.Combine(env.ContentRootPath, "data", "receipts");
        Directory.CreateDirectory(receiptsDir);

        if (expense.ReceiptFileName is { } oldFileName)
        {
            try { System.IO.File.Delete(Path.Combine(receiptsDir, oldFileName)); } catch { /* best-effort */ }
        }

        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(ext) || ext.Length > 10) ext = ".bin";
        var fileName = $"{expense.Id}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}{ext}";
        await using (var stream = System.IO.File.Create(Path.Combine(receiptsDir, fileName)))
        {
            await file.CopyToAsync(stream);
        }

        expense.ReceiptFileName = fileName;
        await db.SaveChangesAsync();
        return Ok(Serialize(expense));
    }

    [HttpPut("{id:guid}/reimbursed")]
    [Authorize(Policy = "BandAdmin")]
    public async Task<IActionResult> SetReimbursed(Guid id, [FromBody] bool isReimbursed)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var expense = await db.Expenses.FirstOrDefaultAsync(e => e.Id == id && e.BandId == bandId);
        if (expense is null) return NotFound(new { error = "Not found" });

        expense.IsReimbursed = isReimbursed;
        await db.SaveChangesAsync();
        return Ok(new { ok = true, isReimbursed = expense.IsReimbursed });
    }
}
