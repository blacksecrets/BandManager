using BandManager.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Band> Bands => Set<Band>();
    public DbSet<BandMembership> BandMemberships => Set<BandMembership>();

    public DbSet<PlatformSetting> PlatformSettings => Set<PlatformSetting>();
    public DbSet<Platform> Platforms => Set<Platform>();
    public DbSet<ContentType> ContentTypes => Set<ContentType>();
    public DbSet<PlatformContentType> PlatformContentTypes => Set<PlatformContentType>();

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<BandPlatformSetting> BandPlatformSettings => Set<BandPlatformSetting>();
    public DbSet<BandSetting> BandSettings => Set<BandSetting>();

    public DbSet<CadenceRule> CadenceRules => Set<CadenceRule>();
    public DbSet<CatalogItem> CatalogItems => Set<CatalogItem>();
    public DbSet<ScheduleItem> ScheduleItems => Set<ScheduleItem>();
    public DbSet<Artifact> Artifacts => Set<Artifact>();

    public DbSet<Song> Songs => Set<Song>();
    public DbSet<BandInstrument> BandInstruments => Set<BandInstrument>();
    public DbSet<RepertoireEntry> RepertoireEntries => Set<RepertoireEntry>();
    public DbSet<GigSet> GigSets => Set<GigSet>();
    public DbSet<GigSetSong> GigSetSongs => Set<GigSetSong>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Band>(b =>
        {
            b.HasIndex(x => x.Slug).IsUnique();
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

        // --- Global reference data (identical for every Band) ---

        builder.Entity<PlatformSetting>(b => { b.HasKey(x => x.Key); });

        builder.Entity<Platform>(b =>
        {
            b.Property(x => x.CredentialFields)
                .HasConversion(JsonValueConverter.For<List<string>>(), JsonValueConverter.Comparer<List<string>>());
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
            b.HasOne(x => x.Song).WithMany().HasForeignKey(x => x.SongId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
