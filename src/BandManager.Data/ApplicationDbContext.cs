using BandManager.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Band> Bands => Set<Band>();
    public DbSet<Act> Acts => Set<Act>();
    public DbSet<BandMembership> BandMemberships => Set<BandMembership>();
    public DbSet<BandMemberRole> BandMemberRoles => Set<BandMemberRole>();
    public DbSet<Gear> Gear => Set<Gear>();
    public DbSet<GearSetting> GearSettings => Set<GearSetting>();
    public DbSet<BandGearItem> BandGearItems => Set<BandGearItem>();
    public DbSet<ActGearItem> ActGearItems => Set<ActGearItem>();
    public DbSet<StagePlot> StagePlots => Set<StagePlot>();
    public DbSet<StagePlotItem> StagePlotItems => Set<StagePlotItem>();

    public DbSet<PlatformSetting> PlatformSettings => Set<PlatformSetting>();
    public DbSet<Platform> Platforms => Set<Platform>();
    public DbSet<ContentType> ContentTypes => Set<ContentType>();
    public DbSet<PlatformContentType> PlatformContentTypes => Set<PlatformContentType>();

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<BandPlatformSetting> BandPlatformSettings => Set<BandPlatformSetting>();
    public DbSet<BandSetting> BandSettings => Set<BandSetting>();

    public DbSet<CadenceRule> CadenceRules => Set<CadenceRule>();
    public DbSet<DefaultCadenceRuleTemplate> DefaultCadenceRuleTemplates => Set<DefaultCadenceRuleTemplate>();
    public DbSet<CalendarFeedToken> CalendarFeedTokens => Set<CalendarFeedToken>();
    public DbSet<UserExternalCalendarConnection> UserExternalCalendarConnections => Set<UserExternalCalendarConnection>();
    public DbSet<CatalogItem> CatalogItems => Set<CatalogItem>();
    public DbSet<ScheduleItem> ScheduleItems => Set<ScheduleItem>();
    public DbSet<Artifact> Artifacts => Set<Artifact>();

    public DbSet<Song> Songs => Set<Song>();
    public DbSet<BandInstrument> BandInstruments => Set<BandInstrument>();
    public DbSet<RepertoireEntry> RepertoireEntries => Set<RepertoireEntry>();
    public DbSet<GigSet> GigSets => Set<GigSet>();
    public DbSet<GigSetSong> GigSetSongs => Set<GigSetSong>();
    public DbSet<SongNote> SongNotes => Set<SongNote>();
    public DbSet<PrintPreference> PrintPreferences => Set<PrintPreference>();
    public DbSet<GigPrepDefaultItem> GigPrepDefaultItems => Set<GigPrepDefaultItem>();
    public DbSet<GigPrepChecklistItem> GigPrepChecklistItems => Set<GigPrepChecklistItem>();

    // --- Site content, now DB-backed (source of truth), site is an
    // optional best-effort publish target - see Gig.cs's doc comment ---
    public DbSet<Gig> Gigs => Set<Gig>();
    public DbSet<GigWithBand> GigWithBands => Set<GigWithBand>();
    public DbSet<Venue> Venues => Set<Venue>();
    public DbSet<Promoter> Promoters => Set<Promoter>();
    public DbSet<VenueContact> VenueContacts => Set<VenueContact>();
    public DbSet<VenueCadenceStep> VenueCadenceSteps => Set<VenueCadenceStep>();
    public DbSet<VenueCampaign> VenueCampaigns => Set<VenueCampaign>();
    public DbSet<VenueCommunication> VenueCommunications => Set<VenueCommunication>();
    public DbSet<MediaItem> MediaItems => Set<MediaItem>();
    public DbSet<GalleryImage> GalleryImages => Set<GalleryImage>();

    public DbSet<SongEditRequest> SongEditRequests => Set<SongEditRequest>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();

    // --- Calendar: rehearsals + availability ---
    public DbSet<Rehearsal> Rehearsals => Set<Rehearsal>();
    public DbSet<RecurringRehearsalRule> RecurringRehearsalRules => Set<RecurringRehearsalRule>();
    public DbSet<Availability> Availabilities => Set<Availability>();

    public DbSet<Flyer> Flyers => Set<Flyer>();
    public DbSet<CustomFlyerFont> CustomFlyerFonts => Set<CustomFlyerFont>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Band>(b =>
        {
            b.HasIndex(x => x.Slug).IsUnique();
        });

        builder.Entity<Act>(b =>
        {
            b.HasOne(x => x.Band).WithMany().HasForeignKey(x => x.BandId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<BandMembership>(b =>
        {
            b.HasKey(x => new { x.UserId, x.BandId });

            b.HasOne(x => x.User)
                .WithMany(u => u.BandMemberships)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(x => x.Band)
                .WithMany(band => band.Memberships)
                .HasForeignKey(x => x.BandId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<BandMemberRole>(b =>
        {
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Band).WithMany().HasForeignKey(x => x.BandId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.UserId, x.BandId, x.Role }).IsUnique();
        });

        builder.Entity<Gear>(b =>
        {
            b.HasOne(x => x.User).WithMany(u => u.Gear).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<GearSetting>(b =>
        {
            b.HasOne(x => x.Gear).WithMany(g => g.Settings).HasForeignKey(x => x.GearId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<BandGearItem>(b =>
        {
            b.HasOne(x => x.Band).WithMany().HasForeignKey(x => x.BandId).OnDelete(DeleteBehavior.Cascade);
            // SetNull, not Restrict/Cascade - if the owning member is ever
            // removed, the item just becomes an ownerless "Band Asset"
            // rather than disappearing or blocking the user's deletion.
            b.HasOne(x => x.OwnerUser).WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<ActGearItem>(b =>
        {
            b.HasOne(x => x.Act).WithMany().HasForeignKey(x => x.ActId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.BandGearItem).WithMany().HasForeignKey(x => x.BandGearItemId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.ActId, x.BandGearItemId }).IsUnique();
        });

        builder.Entity<StagePlot>(b =>
        {
            b.HasOne(x => x.Act).WithMany().HasForeignKey(x => x.ActId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.ActId).IsUnique();
        });

        builder.Entity<StagePlotItem>(b =>
        {
            b.HasOne(x => x.StagePlot).WithMany(p => p.Items).HasForeignKey(x => x.StagePlotId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.BandGearItem).WithMany().HasForeignKey(x => x.BandGearItemId).OnDelete(DeleteBehavior.Cascade);
        });

        // --- Global reference data (identical for every Band) ---

        builder.Entity<PlatformSetting>(b => { b.HasKey(x => x.Key); });

        builder.Entity<Platform>(b =>
        {
            b.Property(x => x.CredentialFields)
                .HasConversion(JsonValueConverter.For<List<CredentialField>>(), JsonValueConverter.Comparer<List<CredentialField>>());
        });

        builder.Entity<ContentType>(b =>
        {
            b.Property(x => x.RequiredArtifacts)
                .HasConversion(JsonValueConverter.ForRequired<List<string>>(), JsonValueConverter.ComparerRequired<List<string>>())
                .IsRequired();
        });

        builder.Entity<PlatformContentType>(b =>
        {
            b.HasKey(x => new { x.PlatformId, x.ContentTypeId });
            b.HasOne(x => x.Platform).WithMany().HasForeignKey(x => x.PlatformId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.ContentType).WithMany().HasForeignKey(x => x.ContentTypeId).OnDelete(DeleteBehavior.Cascade);
        });

        // --- Per-Band setup/config ---

        builder.Entity<Account>(b =>
        {
            b.HasOne(x => x.Band).WithMany().HasForeignKey(x => x.BandId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Platform).WithMany().HasForeignKey(x => x.PlatformId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.BandId, x.PlatformId }); // not unique - multi-account per platform is a future possibility, same as the old app's own note
        });

        builder.Entity<BandPlatformSetting>(b =>
        {
            b.HasKey(x => new { x.BandId, x.PlatformId });
            b.HasOne(x => x.Band).WithMany().HasForeignKey(x => x.BandId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Platform).WithMany().HasForeignKey(x => x.PlatformId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<BandSetting>(b =>
        {
            b.HasKey(x => new { x.BandId, x.Key });
            b.HasOne(x => x.Band).WithMany().HasForeignKey(x => x.BandId).OnDelete(DeleteBehavior.Cascade);
        });

        // --- Per-Band domain data ---

        builder.Entity<CadenceRule>(b =>
        {
            b.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.ContentType).WithMany().HasForeignKey(x => x.ContentTypeId).OnDelete(DeleteBehavior.Restrict);
            // SetNull (not Cascade) - losing an account shouldn't be
            // possible for a user row, but keep the rule itself intact and
            // simply unassigned if it ever happens.
            b.HasOne(x => x.AssigneeUser1).WithMany().HasForeignKey(x => x.AssigneeUserId1).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.AssigneeUser2).WithMany().HasForeignKey(x => x.AssigneeUserId2).OnDelete(DeleteBehavior.SetNull);
            b.Property(x => x.ScheduleDays)
                .HasConversion(JsonValueConverter.For<List<string>>(), JsonValueConverter.Comparer<List<string>>());
            b.Property(x => x.MessageTemplates)
                .HasConversion(JsonValueConverter.For<Dictionary<string, string>>(), JsonValueConverter.Comparer<Dictionary<string, string>>());
        });

        builder.Entity<DefaultCadenceRuleTemplate>(b =>
        {
            b.HasKey(x => x.Key);
            b.Property(x => x.ScheduleDays)
                .HasConversion(JsonValueConverter.For<List<string>>(), JsonValueConverter.Comparer<List<string>>());
            b.Property(x => x.MessageTemplates)
                .HasConversion(JsonValueConverter.For<Dictionary<string, string>>(), JsonValueConverter.Comparer<Dictionary<string, string>>());
        });

        builder.Entity<CatalogItem>(b =>
        {
            b.HasOne(x => x.Band).WithMany().HasForeignKey(x => x.BandId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ScheduleItem>(b =>
        {
            b.HasOne(x => x.Band).WithMany().HasForeignKey(x => x.BandId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.AssigneeUser1).WithMany().HasForeignKey(x => x.AssigneeUserId1).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.AssigneeUser2).WithMany().HasForeignKey(x => x.AssigneeUserId2).OnDelete(DeleteBehavior.SetNull);
            // Idempotent cadence generation: INSERT-if-not-exists on this
            // key, mirrors the old app's UNIQUE(template_key, due_date, gig_ref)
            // - scoped per Band now, so two Bands' identically-named rules
            // can never collide.
            b.HasIndex(x => new { x.BandId, x.TemplateKey, x.DueDate, x.GigRef }).IsUnique();
        });

        builder.Entity<Artifact>(b =>
        {
            b.HasOne(x => x.ScheduleItem).WithMany(s => s.Artifacts).HasForeignKey(x => x.ScheduleItemId).OnDelete(DeleteBehavior.Cascade);
        });

        // --- Repertoire / gig sets ---

        builder.Entity<Song>(b =>
        {
            b.Property(x => x.Tunings)
                .HasConversion(JsonValueConverter.For<Dictionary<string, string>>(), JsonValueConverter.Comparer<Dictionary<string, string>>());
        });

        builder.Entity<BandInstrument>(b =>
        {
            b.HasOne(x => x.Band).WithMany().HasForeignKey(x => x.BandId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<RepertoireEntry>(b =>
        {
            b.HasOne(x => x.Band).WithMany().HasForeignKey(x => x.BandId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Song).WithMany().HasForeignKey(x => x.SongId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.BandId, x.SongId }).IsUnique();
        });

        builder.Entity<GigSet>(b =>
        {
            b.HasOne(x => x.Band).WithMany().HasForeignKey(x => x.BandId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.BandId, x.GigRef }).IsUnique();
        });

        builder.Entity<GigSetSong>(b =>
        {
            b.HasOne(x => x.GigSet).WithMany(s => s.Songs).HasForeignKey(x => x.GigSetId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Song).WithMany().HasForeignKey(x => x.SongId).OnDelete(DeleteBehavior.Restrict).IsRequired(false);
        });

        builder.Entity<SongNote>(b =>
        {
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.RepertoireEntry).WithMany().HasForeignKey(x => x.RepertoireEntryId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.UserId, x.RepertoireEntryId }).IsUnique();
        });

        builder.Entity<PrintPreference>(b =>
        {
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.UserId).IsUnique();
        });

        builder.Entity<GigPrepDefaultItem>(b =>
        {
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<GigPrepChecklistItem>(b =>
        {
            b.HasOne(x => x.Gig).WithMany().HasForeignKey(x => x.GigId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // --- Site content (Gigs/Media/Gallery), DB-backed ---

        builder.Entity<Gig>(b =>
        {
            b.HasOne(x => x.Band).WithMany().HasForeignKey(x => x.BandId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.BandId, x.Ref }).IsUnique();
            b.HasOne(x => x.VenueEntity).WithMany().HasForeignKey(x => x.VenueId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.SelectedFlyer).WithMany().HasForeignKey(x => x.SelectedFlyerId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.ActEntity).WithMany().HasForeignKey(x => x.ActId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Promoter).WithMany().HasForeignKey(x => x.PromoterId).OnDelete(DeleteBehavior.SetNull);
        });

        // --- Venue outreach ---

        builder.Entity<Venue>(b =>
        {
            b.HasOne(x => x.Band).WithMany().HasForeignKey(x => x.BandId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.DefaultPromoter).WithMany().HasForeignKey(x => x.DefaultPromoterId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Promoter>(b =>
        {
            b.HasOne(x => x.Band).WithMany().HasForeignKey(x => x.BandId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<VenueContact>(b =>
        {
            b.HasOne(x => x.Venue).WithMany(v => v.Contacts).HasForeignKey(x => x.VenueId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<VenueCadenceStep>(b =>
        {
            b.HasOne(x => x.Band).WithMany().HasForeignKey(x => x.BandId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.BandId, x.StepNumber }).IsUnique();
        });

        builder.Entity<VenueCampaign>(b =>
        {
            b.HasOne(x => x.Band).WithMany().HasForeignKey(x => x.BandId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Venue).WithOne(v => v.Campaign).HasForeignKey<VenueCampaign>(x => x.VenueId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.BookedGig).WithMany().HasForeignKey(x => x.BookedGigId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<VenueCommunication>(b =>
        {
            b.HasOne(x => x.VenueCampaign).WithMany(c => c.Communications).HasForeignKey(x => x.VenueCampaignId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.LoggedByUser).WithMany().HasForeignKey(x => x.LoggedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<GigWithBand>(b =>
        {
            b.HasOne(x => x.Gig).WithMany(g => g.WithBands).HasForeignKey(x => x.GigId).OnDelete(DeleteBehavior.Cascade);
            // Restrict, not Cascade: a with-band (possibly a stub row with
            // no BandMemberships) may be referenced by many gigs across
            // many real Bands - deleting it out from under those isn't
            // something any current feature does, but Restrict makes that
            // an explicit decision later rather than a silent cascade now.
            b.HasOne(x => x.WithBand).WithMany().HasForeignKey(x => x.WithBandId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<MediaItem>(b =>
        {
            b.HasOne(x => x.Band).WithMany().HasForeignKey(x => x.BandId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.BandId, x.Ref }).IsUnique();
        });

        builder.Entity<GalleryImage>(b =>
        {
            b.HasOne(x => x.Band).WithMany().HasForeignKey(x => x.BandId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.BandId, x.Ref }).IsUnique();
        });

        // --- Song review / notifications ---

        builder.Entity<SongEditRequest>(b =>
        {
            b.HasOne(x => x.Song).WithMany().HasForeignKey(x => x.SongId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Band).WithMany().HasForeignKey(x => x.BandId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.RequestedByUser).WithMany().HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.ResolvedByUser).WithMany().HasForeignKey(x => x.ResolvedByUserId).OnDelete(DeleteBehavior.Restrict);
            b.Property(x => x.Changes)
                .HasConversion(JsonValueConverter.ForRequired<Dictionary<string, SongFieldChange>>(), JsonValueConverter.ComparerRequired<Dictionary<string, SongFieldChange>>())
                .IsRequired();
            // App-level check (SongsController.ProposeEdit) is the primary
            // guard against a second concurrent proposal for the same Song;
            // this is the race-condition backstop.
            b.HasIndex(x => x.SongId).HasFilter("\"Status\" = 0").IsUnique();
        });

        builder.Entity<Notification>(b =>
        {
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.SongEditRequest).WithMany().HasForeignKey(x => x.SongEditRequestId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.Gig).WithMany().HasForeignKey(x => x.GigId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne(x => x.Rehearsal).WithMany().HasForeignKey(x => x.RehearsalId).OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(x => new { x.UserId, x.IsRead });
        });

        builder.Entity<NotificationPreference>(b =>
        {
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.UserId, x.Kind }).IsUnique();
        });

        // --- Calendar: rehearsals + availability ---

        builder.Entity<RecurringRehearsalRule>(b =>
        {
            b.HasOne(x => x.Band).WithMany().HasForeignKey(x => x.BandId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Rehearsal>(b =>
        {
            b.HasOne(x => x.Band).WithMany().HasForeignKey(x => x.BandId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.RecurringRehearsalRule).WithMany().HasForeignKey(x => x.RecurringRehearsalRuleId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Availability>(b =>
        {
            b.HasOne(x => x.Band).WithMany().HasForeignKey(x => x.BandId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.BandId, x.UserId, x.Date }).IsUnique();
        });

        builder.Entity<CalendarFeedToken>(b =>
        {
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.UserId).IsUnique();
            b.HasIndex(x => x.Token).IsUnique();
        });

        builder.Entity<UserExternalCalendarConnection>(b =>
        {
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.UserId, x.Provider }).IsUnique();
        });

        // --- Flyer management ---

        builder.Entity<Flyer>(b =>
        {
            b.HasOne(x => x.Band).WithMany().HasForeignKey(x => x.BandId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.GeneratedCatalogItem).WithMany().HasForeignKey(x => x.GeneratedCatalogItemId).OnDelete(DeleteBehavior.Cascade);
            // SetNull, not Restrict - deleting a source image the user was
            // warned about is allowed to proceed (see CatalogController);
            // the flyer just loses its editable background afterward.
            b.HasOne(x => x.SourceCatalogItem).WithMany().HasForeignKey(x => x.SourceCatalogItemId).OnDelete(DeleteBehavior.SetNull);
            b.Property(x => x.Fields)
                .HasConversion(JsonValueConverter.ForRequired<List<FlyerFieldDef>>(), JsonValueConverter.ComparerRequired<List<FlyerFieldDef>>())
                .IsRequired();
            b.HasIndex(x => new { x.BandId, x.GigRef });
        });
    }
}
