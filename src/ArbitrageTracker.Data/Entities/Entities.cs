namespace ArbitrageTracker.Data.Entities;

public class PriceSnapshot
{
    public long Id { get; set; }
    public int ItemId { get; set; }
    public long CapturedAt { get; set; } // unix seconds
    public long? High { get; set; }
    public long HighTime { get; set; }
    public long? Low { get; set; }
    public long LowTime { get; set; }
}

public class BucketSnapshot
{
    public long Id { get; set; }
    public int ItemId { get; set; }
    public string Interval { get; set; } = "5m"; // "5m" | "1h"
    public long Timestamp { get; set; }
    public long? AvgHighPrice { get; set; }
    public long? AvgLowPrice { get; set; }
    public long HighPriceVolume { get; set; }
    public long LowPriceVolume { get; set; }
}

public class OpportunitySnapshot
{
    public long Id { get; set; }
    public int ItemId { get; set; }
    public long DetectedAt { get; set; }
    public long BuyPrice { get; set; }
    public long SellPrice { get; set; }
    public long NetMargin { get; set; }
    public long ExpectedCycleProfit { get; set; }
    public double SafetyScore { get; set; }
    // Forward proxy validation (filled in later by ProxyOutcomeJob).
    public bool? ProxyValidated { get; set; }
    public long? ProxyEvaluatedAt { get; set; }
}

public class ItemMappingEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Examine { get; set; } = "";
    public bool Members { get; set; }
    public int LowAlch { get; set; }
    public int HighAlch { get; set; }
    public int BuyLimit { get; set; }
    public long Value { get; set; }
    public string Icon { get; set; } = "";
}

public class AppSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
