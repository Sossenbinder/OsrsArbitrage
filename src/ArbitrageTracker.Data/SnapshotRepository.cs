using ArbitrageTracker.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ArbitrageTracker.Data;

public sealed class SnapshotRepository(ArbitrageDbContext db)
{
    public async Task SaveLatestAsync(IEnumerable<PriceSnapshot> snapshots, CancellationToken ct)
    {
        db.PriceSnapshots.AddRange(snapshots);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpsertBucketsAsync(IEnumerable<BucketSnapshot> buckets, CancellationToken ct)
    {
        foreach (var bucket in buckets)
        {
            bool exists = await db.BucketSnapshots.AnyAsync(
                x => x.ItemId == bucket.ItemId && x.Interval == bucket.Interval && x.Timestamp == bucket.Timestamp, ct);
            if (!exists) db.BucketSnapshots.Add(bucket);
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task ReplaceMappingsAsync(IEnumerable<ItemMappingEntity> mappings, CancellationToken ct)
    {
        await db.ItemMappings.ExecuteDeleteAsync(ct);
        db.ItemMappings.AddRange(mappings);
        await db.SaveChangesAsync(ct);
    }

    public async Task<long> SaveOpportunityAsync(OpportunitySnapshot opp, CancellationToken ct)
    {
        db.OpportunitySnapshots.Add(opp);
        await db.SaveChangesAsync(ct);
        return opp.Id;
    }

    public async Task PruneAsync(long olderThanUnixSeconds, CancellationToken ct)
    {
        await db.PriceSnapshots.Where(p => p.CapturedAt < olderThanUnixSeconds).ExecuteDeleteAsync(ct);
    }
}
