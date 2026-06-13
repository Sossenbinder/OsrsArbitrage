using ArbitrageTracker.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ArbitrageTracker.Data;

public class ArbitrageDbContext(DbContextOptions<ArbitrageDbContext> options) : DbContext(options)
{
    public DbSet<PriceSnapshot> PriceSnapshots => Set<PriceSnapshot>();
    public DbSet<BucketSnapshot> BucketSnapshots => Set<BucketSnapshot>();
    public DbSet<OpportunitySnapshot> OpportunitySnapshots => Set<OpportunitySnapshot>();
    public DbSet<ItemMappingEntity> ItemMappings => Set<ItemMappingEntity>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<PriceSnapshot>().HasIndex(p => new { p.ItemId, p.CapturedAt });
        b.Entity<BucketSnapshot>().HasIndex(x => new { x.ItemId, x.Interval, x.Timestamp }).IsUnique();
        b.Entity<OpportunitySnapshot>().HasIndex(o => new { o.ItemId, o.DetectedAt });
        b.Entity<AppSetting>().HasKey(s => s.Key);
    }
}
