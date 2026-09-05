using BandManager.Data.Entities;

namespace BandManager.Data.Services;

/// <summary>Two independent signals, not one: Color is how urgent the due
/// date is (fill color), Ready is whether it could be posted right now
/// (border) - a not-yet-due item can already be ready, and a due/overdue
/// item can still be missing artifacts.</summary>
public record StatusResult(string Color, string Label, bool Ready);

/// <summary>Ported 1:1 from the old app's src/statusEngine.js - same
/// branch order, same day-math, same color/label vocabulary (the frontend
/// dashboard.css/dashboard.js this app reuses already expects these exact
/// strings).</summary>
public static class StatusEngine
{
    private const int DaysAheadVisible = 7; // items further out than this stay in "Future work"

    public static StatusResult ComputeStatus(ScheduleItem item, ISet<string> presentArtifactTypes, IReadOnlyList<string> requiredArtifacts)
    {
        if (item.Status == ScheduleItemStatus.Posted)
        {
            return new StatusResult("done", item.PostedVia == "manual-outside-system" ? "Posted (outside system)" : "Posted", true);
        }

        if (item.Status == ScheduleItemStatus.Cancelled)
        {
            return new StatusResult("cancelled", "Cancelled", false);
        }

        // A successful "Fart it out" records PostedAt/PostedVia
        // immediately but deliberately leaves Status as Open - the tile
        // keeps showing, with this distinct state, until explicitly
        // archived. Checked before due-date logic so it fully overrides it.
        if (item.Status == ScheduleItemStatus.Open && item.PostedAt is not null)
        {
            return new StatusResult("done-pending", $"Posted: {item.PostedAt:yyyy-MM-ddTHH:mm}", true);
        }

        if (item.AutoHandled)
        {
            return new StatusResult("auto", "Handled automatically by the platform", true);
        }

        var ready = requiredArtifacts.All(presentArtifactTypes.Contains);

        if (item.DueDate is null)
        {
            // No fixed due date (event-driven, "as soon as booked") - it's
            // been due since it first appeared, so say that concretely.
            // A gig-tied item can still flip Ready after this based on the
            // site's own completeness - that finalization happens in the
            // controller's serialization, not here (same split as the old app).
            if (ready)
            {
                return new StatusResult("complete", "Ready to post", true);
            }
            var since = item.CreatedAt.ToString("yyyy-MM-dd");
            return new StatusResult("yellow", $"Due: {since}", ready);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var daysOut = item.DueDate.Value.DayNumber - today.DayNumber;

        if (daysOut > DaysAheadVisible) return new StatusResult("grey", $"Due {item.DueDate:yyyy-MM-dd}", ready);
        if (daysOut > 0) return new StatusResult("neutral", $"Due {item.DueDate:yyyy-MM-dd}", ready);
        if (daysOut == 0) return new StatusResult("yellow", "Due today", ready);
        return new StatusResult("red", $"Past due ({item.DueDate:yyyy-MM-dd})", ready);
    }
}
