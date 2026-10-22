using Microsoft.EntityFrameworkCore;

namespace Sumire.Api.Data;

public class SumireDbContext(DbContextOptions<SumireDbContext> options) : DbContext(options)
{
    public const string Schema = "sumire";

    public DbSet<Instrument> Instruments => Set<Instrument>();
    public DbSet<DailyBar> DailyBars => Set<DailyBar>();
    public DbSet<Fundamental> Fundamentals => Set<Fundamental>();
    public DbSet<Forecast> Forecasts => Set<Forecast>();
    public DbSet<ForecastMetric> ForecastMetrics => Set<ForecastMetric>();
    public DbSet<SignalScore> SignalScores => Set<SignalScore>();
    public DbSet<ResearchJob> ResearchJobs => Set<ResearchJob>();
    public DbSet<Insight> Insights => Set<Insight>();
    public DbSet<Watchlist> Watchlists => Set<Watchlist>();
    public DbSet<WatchlistItem> WatchlistItems => Set<WatchlistItem>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<Setting> Settings => Set<Setting>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);

        b.Entity<Instrument>(e =>
        {
            e.HasKey(x => x.Code);
            e.Property(x => x.IndexMemberships).HasColumnType("text[]");
            e.HasIndex(x => x.Core);
        });

        b.Entity<DailyBar>(e =>
        {
            e.HasKey(x => new { x.Code, x.Date });
            e.HasIndex(x => x.Date);
        });

        b.Entity<Fundamental>().HasKey(x => x.Code);

        b.Entity<Forecast>(e =>
        {
            e.HasKey(x => new { x.Code, x.AsOf, x.Horizon, x.Model });
            e.HasIndex(x => x.AsOf);
        });

        b.Entity<ForecastMetric>().HasKey(x => new { x.Model, x.Horizon, x.Scope });

        b.Entity<SignalScore>(e =>
        {
            e.HasKey(x => x.Code);
            e.Property(x => x.Reasons).HasColumnType("jsonb");
        });

        b.Entity<ResearchJob>(e =>
        {
            e.Property(x => x.Packet).HasColumnType("jsonb");
            e.HasIndex(x => new { x.Status, x.Priority });
            e.HasIndex(x => x.CreatedAt);
        });

        b.Entity<Insight>(e =>
        {
            e.Property(x => x.Body).HasColumnType("jsonb");
            e.Property(x => x.Sources).HasColumnType("jsonb");
            e.Property(x => x.Warnings).HasColumnType("jsonb");
            e.HasIndex(x => new { x.Kind, x.Subject, x.CreatedAt });
        });

        b.Entity<Watchlist>(e =>
        {
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.WatchlistId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<WatchlistItem>().HasKey(x => new { x.WatchlistId, x.Code });

        b.Entity<Alert>(e =>
        {
            e.Property(x => x.Params).HasColumnType("jsonb");
            e.HasIndex(x => x.Active);
        });

        b.Entity<Setting>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Value).HasColumnType("jsonb");
        });
    }
}
