using System.Text.Json;
using BandManager.Data;
using BandManager.Data.Entities;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Web.Controllers;

public record SaveActRequest(
    string Name, string? IntroText, string? VideoNotes, string? GeneralNotes,
    string? TechContactName, string? TechContactPhone, string? TechContactEmail);

public record SetActGearRequest(List<Guid> BandGearItemIds);

public record SaveInputChannelRequest(int ChannelNumber, string Source, string? MicRecommendation, string? ProvidedBy, string? PositioningNotes);

public record SaveMonitorMixRequest(string Position, string MixDescription);

public record FrequencyRowRequest(string Frequency, string? EqMove, string? Reason);
public record SaveMicEqNoteRequest(string MicModel, string? Context, List<FrequencyRowRequest>? FrequencyRows, string? GeneralNotes);

/// <summary>
/// A band's performance configurations - see Act.cs's doc comment. Read
/// access is broad (BandMember - the Gig create/edit form's Act picker
/// needs the list) but every write is BandAdmin-only, matching how the
/// rest of a band's core configuration (Setup, Cadence, Band Roles) is
/// gated.
/// </summary>
[ApiController]
[Route("/api/acts")]
[Authorize(Policy = "BandAdmin")]
public class ActController(ApplicationDbContext db, IActiveBandAccessor activeBand) : ControllerBase
{
    private IActionResult? RequireActiveBand(out Guid bandId)
    {
        var id = activeBand.GetActiveBandId();
        if (id is null) { bandId = default; return BadRequest(new { error = "No active band selected." }); }
        bandId = id.Value;
        return null;
    }

    private static object Serialize(Act a) => new
    {
        id = a.Id,
        name = a.Name,
        isDefault = a.IsDefault,
        introText = a.IntroText,
        videoNotes = a.VideoNotes,
        generalNotes = a.GeneralNotes,
        techContactName = a.TechContactName,
        techContactPhone = a.TechContactPhone,
        techContactEmail = a.TechContactEmail,
        createdAt = a.CreatedAt
    };

    [HttpGet]
    [Authorize(Policy = "BandMember")]
    public async Task<IActionResult> List()
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var acts = await db.Acts.AsNoTracking().Where(a => a.BandId == bandId)
            .OrderByDescending(a => a.IsDefault).ThenBy(a => a.Name).ToListAsync();
        return Ok(acts.Select(Serialize));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "BandMember")]
    public async Task<IActionResult> Get(Guid id)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var act = await db.Acts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id && a.BandId == bandId);
        if (act is null) return NotFound(new { error = "Act not found" });
        return Ok(Serialize(act));
    }

    private static void ApplyFields(Act act, SaveActRequest request)
    {
        act.Name = request.Name.Trim()[..Math.Min(request.Name.Trim().Length, 200)];
        act.IntroText = string.IsNullOrWhiteSpace(request.IntroText) ? null : request.IntroText.Trim();
        act.VideoNotes = string.IsNullOrWhiteSpace(request.VideoNotes) ? null : request.VideoNotes.Trim();
        act.GeneralNotes = string.IsNullOrWhiteSpace(request.GeneralNotes) ? null : request.GeneralNotes.Trim();
        act.TechContactName = string.IsNullOrWhiteSpace(request.TechContactName) ? null : request.TechContactName.Trim();
        act.TechContactPhone = string.IsNullOrWhiteSpace(request.TechContactPhone) ? null : request.TechContactPhone.Trim();
        act.TechContactEmail = string.IsNullOrWhiteSpace(request.TechContactEmail) ? null : request.TechContactEmail.Trim();
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveActRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(new { error = "Name is required." });

        var act = new Act { BandId = bandId, Name = request.Name };
        ApplyFields(act, request);
        db.Acts.Add(act);
        await db.SaveChangesAsync();
        return Ok(Serialize(act));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveActRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(new { error = "Name is required." });

        var act = await db.Acts.FirstOrDefaultAsync(a => a.Id == id && a.BandId == bandId);
        if (act is null) return NotFound(new { error = "Act not found" });

        ApplyFields(act, request);
        await db.SaveChangesAsync();
        return Ok(Serialize(act));
    }

    // The one Act every Band is created with (see SuperAdminController.CreateBand)
    // is undeletable, guaranteeing a Band is never left with zero Acts.
    // Every other Act is deletable, unless a Gig still points at it -
    // reassign those first rather than silently orphaning them (the DB's
    // own Restrict FK on Gig.ActId backs this up either way).
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var act = await db.Acts.FirstOrDefaultAsync(a => a.Id == id && a.BandId == bandId);
        if (act is null) return Ok(new { ok = true });
        if (act.IsDefault) return BadRequest(new { error = "This band's default Act can't be deleted." });

        var gigCount = await db.Gigs.CountAsync(g => g.BandId == bandId && g.ActId == id);
        if (gigCount > 0)
            return BadRequest(new { error = $"This Act is still assigned to {gigCount} gig{(gigCount == 1 ? "" : "s")} - reassign those first." });

        db.Acts.Remove(act);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    private static object SerializeGearItem(BandGearItem g) => new
    {
        id = g.Id,
        type = g.Type,
        make = g.Make,
        model = g.Model,
        lengthInches = g.LengthInches,
        widthInches = g.WidthInches,
        depthInches = g.DepthInches,
        weightPounds = g.WeightPounds,
        ownerUserId = g.OwnerUserId,
        ownerName = g.OwnerUser == null ? null : (g.OwnerUser.FirstName ?? g.OwnerUser.UserName!.Split('@')[0])
    };

    // This Act's Gear List - a selection from the band's full Gear
    // Catalog (BandGearController), in the order they were assigned.
    [HttpGet("{actId:guid}/gear")]
    [Authorize(Policy = "BandMember")]
    public async Task<IActionResult> GetGear(Guid actId)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (!await db.Acts.AnyAsync(a => a.Id == actId && a.BandId == bandId)) return NotFound(new { error = "Act not found" });

        var items = await db.ActGearItems.AsNoTracking().Include(x => x.BandGearItem).ThenInclude(g => g.OwnerUser)
            .Where(x => x.ActId == actId)
            .OrderBy(x => x.SortOrder)
            .Select(x => x.BandGearItem)
            .ToListAsync();
        return Ok(items.Select(SerializeGearItem));
    }

    // Replace-the-set save, like a checklist - Band Admin picks from the
    // full Gear Catalog and this becomes the Act's Gear List exactly as
    // submitted (order preserved), same "just rewrite it" approach as
    // this session's other checklist-shaped saves.
    [HttpPut("{actId:guid}/gear")]
    public async Task<IActionResult> SetGear(Guid actId, [FromBody] SetActGearRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        var act = await db.Acts.FirstOrDefaultAsync(a => a.Id == actId && a.BandId == bandId);
        if (act is null) return NotFound(new { error = "Act not found" });

        var validIds = await db.BandGearItems.Where(g => g.BandId == bandId && request.BandGearItemIds.Contains(g.Id))
            .Select(g => g.Id).ToListAsync();

        db.ActGearItems.RemoveRange(db.ActGearItems.Where(x => x.ActId == actId));
        var sort = 0;
        foreach (var gearId in request.BandGearItemIds.Where(validIds.Contains))
            db.ActGearItems.Add(new ActGearItem { ActId = actId, BandGearItemId = gearId, SortOrder = sort++ });

        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    private static object SerializeInputChannel(TechRiderInputChannel c) => new
    {
        id = c.Id,
        channelNumber = c.ChannelNumber,
        source = c.Source,
        micRecommendation = c.MicRecommendation,
        providedBy = c.ProvidedBy,
        positioningNotes = c.PositioningNotes
    };

    // This Act's Tech Rider Input/Mic Splitter Channel List - an ordered
    // table, not a checklist, so it gets normal per-row CRUD rather than
    // Gear List's replace-the-set save.
    [HttpGet("{actId:guid}/input-channels")]
    [Authorize(Policy = "BandMember")]
    public async Task<IActionResult> GetInputChannels(Guid actId)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (!await db.Acts.AnyAsync(a => a.Id == actId && a.BandId == bandId)) return NotFound(new { error = "Act not found" });

        var channels = await db.TechRiderInputChannels.AsNoTracking()
            .Where(c => c.ActId == actId).OrderBy(c => c.SortOrder).ToListAsync();
        return Ok(channels.Select(SerializeInputChannel));
    }

    [HttpPost("{actId:guid}/input-channels")]
    public async Task<IActionResult> AddInputChannel(Guid actId, [FromBody] SaveInputChannelRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (!await db.Acts.AnyAsync(a => a.Id == actId && a.BandId == bandId)) return NotFound(new { error = "Act not found" });
        if (string.IsNullOrWhiteSpace(request.Source)) return BadRequest(new { error = "Source is required." });

        var maxSort = await db.TechRiderInputChannels.Where(c => c.ActId == actId).Select(c => (int?)c.SortOrder).MaxAsync() ?? -1;
        var channel = new TechRiderInputChannel
        {
            ActId = actId,
            ChannelNumber = request.ChannelNumber,
            Source = request.Source.Trim(),
            MicRecommendation = string.IsNullOrWhiteSpace(request.MicRecommendation) ? null : request.MicRecommendation.Trim(),
            ProvidedBy = request.ProvidedBy is "Venue" or "Band" or "Either" ? request.ProvidedBy : null,
            PositioningNotes = string.IsNullOrWhiteSpace(request.PositioningNotes) ? null : request.PositioningNotes.Trim(),
            SortOrder = maxSort + 1
        };
        db.TechRiderInputChannels.Add(channel);
        await db.SaveChangesAsync();
        return Ok(SerializeInputChannel(channel));
    }

    [HttpPut("{actId:guid}/input-channels/{id:guid}")]
    public async Task<IActionResult> UpdateInputChannel(Guid actId, Guid id, [FromBody] SaveInputChannelRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (!await db.Acts.AnyAsync(a => a.Id == actId && a.BandId == bandId)) return NotFound(new { error = "Act not found" });
        if (string.IsNullOrWhiteSpace(request.Source)) return BadRequest(new { error = "Source is required." });

        var channel = await db.TechRiderInputChannels.FirstOrDefaultAsync(c => c.Id == id && c.ActId == actId);
        if (channel is null) return NotFound(new { error = "Not found" });

        channel.ChannelNumber = request.ChannelNumber;
        channel.Source = request.Source.Trim();
        channel.MicRecommendation = string.IsNullOrWhiteSpace(request.MicRecommendation) ? null : request.MicRecommendation.Trim();
        channel.ProvidedBy = request.ProvidedBy is "Venue" or "Band" or "Either" ? request.ProvidedBy : null;
        channel.PositioningNotes = string.IsNullOrWhiteSpace(request.PositioningNotes) ? null : request.PositioningNotes.Trim();
        await db.SaveChangesAsync();
        return Ok(SerializeInputChannel(channel));
    }

    [HttpDelete("{actId:guid}/input-channels/{id:guid}")]
    public async Task<IActionResult> DeleteInputChannel(Guid actId, Guid id)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (!await db.Acts.AnyAsync(a => a.Id == actId && a.BandId == bandId)) return NotFound(new { error = "Act not found" });

        var channel = await db.TechRiderInputChannels.FirstOrDefaultAsync(c => c.Id == id && c.ActId == actId);
        if (channel is null) return Ok(new { ok = true });

        db.TechRiderInputChannels.Remove(channel);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    private static object SerializeMonitorMix(TechRiderMonitorMix m) => new
    {
        id = m.Id,
        position = m.Position,
        mixDescription = m.MixDescription
    };

    [HttpGet("{actId:guid}/monitor-mixes")]
    [Authorize(Policy = "BandMember")]
    public async Task<IActionResult> GetMonitorMixes(Guid actId)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (!await db.Acts.AnyAsync(a => a.Id == actId && a.BandId == bandId)) return NotFound(new { error = "Act not found" });

        var mixes = await db.TechRiderMonitorMixes.AsNoTracking()
            .Where(m => m.ActId == actId).OrderBy(m => m.SortOrder).ToListAsync();
        return Ok(mixes.Select(SerializeMonitorMix));
    }

    [HttpPost("{actId:guid}/monitor-mixes")]
    public async Task<IActionResult> AddMonitorMix(Guid actId, [FromBody] SaveMonitorMixRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (!await db.Acts.AnyAsync(a => a.Id == actId && a.BandId == bandId)) return NotFound(new { error = "Act not found" });
        if (string.IsNullOrWhiteSpace(request.Position)) return BadRequest(new { error = "Position is required." });
        if (string.IsNullOrWhiteSpace(request.MixDescription)) return BadRequest(new { error = "Mix description is required." });

        var maxSort = await db.TechRiderMonitorMixes.Where(m => m.ActId == actId).Select(m => (int?)m.SortOrder).MaxAsync() ?? -1;
        var mix = new TechRiderMonitorMix
        {
            ActId = actId,
            Position = request.Position.Trim(),
            MixDescription = request.MixDescription.Trim(),
            SortOrder = maxSort + 1
        };
        db.TechRiderMonitorMixes.Add(mix);
        await db.SaveChangesAsync();
        return Ok(SerializeMonitorMix(mix));
    }

    [HttpPut("{actId:guid}/monitor-mixes/{id:guid}")]
    public async Task<IActionResult> UpdateMonitorMix(Guid actId, Guid id, [FromBody] SaveMonitorMixRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (!await db.Acts.AnyAsync(a => a.Id == actId && a.BandId == bandId)) return NotFound(new { error = "Act not found" });
        if (string.IsNullOrWhiteSpace(request.Position)) return BadRequest(new { error = "Position is required." });
        if (string.IsNullOrWhiteSpace(request.MixDescription)) return BadRequest(new { error = "Mix description is required." });

        var mix = await db.TechRiderMonitorMixes.FirstOrDefaultAsync(m => m.Id == id && m.ActId == actId);
        if (mix is null) return NotFound(new { error = "Not found" });

        mix.Position = request.Position.Trim();
        mix.MixDescription = request.MixDescription.Trim();
        await db.SaveChangesAsync();
        return Ok(SerializeMonitorMix(mix));
    }

    [HttpDelete("{actId:guid}/monitor-mixes/{id:guid}")]
    public async Task<IActionResult> DeleteMonitorMix(Guid actId, Guid id)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (!await db.Acts.AnyAsync(a => a.Id == actId && a.BandId == bandId)) return NotFound(new { error = "Act not found" });

        var mix = await db.TechRiderMonitorMixes.FirstOrDefaultAsync(m => m.Id == id && m.ActId == actId);
        if (mix is null) return Ok(new { ok = true });

        db.TechRiderMonitorMixes.Remove(mix);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    private static object SerializeMicEqNote(TechRiderMicEqNote n) => new
    {
        id = n.Id,
        micModel = n.MicModel,
        context = n.Context,
        frequencyRows = JsonSerializer.Deserialize<List<FrequencyRowRequest>>(n.FrequencyRowsJson) ?? new List<FrequencyRowRequest>(),
        generalNotes = n.GeneralNotes
    };

    [HttpGet("{actId:guid}/mic-eq-notes")]
    [Authorize(Policy = "BandMember")]
    public async Task<IActionResult> GetMicEqNotes(Guid actId)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (!await db.Acts.AnyAsync(a => a.Id == actId && a.BandId == bandId)) return NotFound(new { error = "Act not found" });

        var notes = await db.TechRiderMicEqNotes.AsNoTracking()
            .Where(n => n.ActId == actId).OrderBy(n => n.SortOrder).ToListAsync();
        return Ok(notes.Select(SerializeMicEqNote));
    }

    [HttpPost("{actId:guid}/mic-eq-notes")]
    public async Task<IActionResult> AddMicEqNote(Guid actId, [FromBody] SaveMicEqNoteRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (!await db.Acts.AnyAsync(a => a.Id == actId && a.BandId == bandId)) return NotFound(new { error = "Act not found" });
        if (string.IsNullOrWhiteSpace(request.MicModel)) return BadRequest(new { error = "Mic model is required." });

        var maxSort = await db.TechRiderMicEqNotes.Where(n => n.ActId == actId).Select(n => (int?)n.SortOrder).MaxAsync() ?? -1;
        var note = new TechRiderMicEqNote
        {
            ActId = actId,
            MicModel = request.MicModel.Trim(),
            Context = string.IsNullOrWhiteSpace(request.Context) ? null : request.Context.Trim(),
            FrequencyRowsJson = JsonSerializer.Serialize(request.FrequencyRows ?? new List<FrequencyRowRequest>()),
            GeneralNotes = string.IsNullOrWhiteSpace(request.GeneralNotes) ? null : request.GeneralNotes.Trim(),
            SortOrder = maxSort + 1
        };
        db.TechRiderMicEqNotes.Add(note);
        await db.SaveChangesAsync();
        return Ok(SerializeMicEqNote(note));
    }

    [HttpPut("{actId:guid}/mic-eq-notes/{id:guid}")]
    public async Task<IActionResult> UpdateMicEqNote(Guid actId, Guid id, [FromBody] SaveMicEqNoteRequest request)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (!await db.Acts.AnyAsync(a => a.Id == actId && a.BandId == bandId)) return NotFound(new { error = "Act not found" });
        if (string.IsNullOrWhiteSpace(request.MicModel)) return BadRequest(new { error = "Mic model is required." });

        var note = await db.TechRiderMicEqNotes.FirstOrDefaultAsync(n => n.Id == id && n.ActId == actId);
        if (note is null) return NotFound(new { error = "Not found" });

        note.MicModel = request.MicModel.Trim();
        note.Context = string.IsNullOrWhiteSpace(request.Context) ? null : request.Context.Trim();
        note.FrequencyRowsJson = JsonSerializer.Serialize(request.FrequencyRows ?? new List<FrequencyRowRequest>());
        note.GeneralNotes = string.IsNullOrWhiteSpace(request.GeneralNotes) ? null : request.GeneralNotes.Trim();
        await db.SaveChangesAsync();
        return Ok(SerializeMicEqNote(note));
    }

    [HttpDelete("{actId:guid}/mic-eq-notes/{id:guid}")]
    public async Task<IActionResult> DeleteMicEqNote(Guid actId, Guid id)
    {
        if (RequireActiveBand(out var bandId) is { } err) return err;
        if (!await db.Acts.AnyAsync(a => a.Id == actId && a.BandId == bandId)) return NotFound(new { error = "Act not found" });

        var note = await db.TechRiderMicEqNotes.FirstOrDefaultAsync(n => n.Id == id && n.ActId == actId);
        if (note is null) return Ok(new { ok = true });

        db.TechRiderMicEqNotes.Remove(note);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}
