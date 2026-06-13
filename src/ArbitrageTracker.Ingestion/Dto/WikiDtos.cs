using System.Text.Json.Serialization;

namespace ArbitrageTracker.Ingestion.Dto;

public sealed class WikiLatestResponse
{
    [JsonPropertyName("data")]
    public Dictionary<string, WikiLatestEntry> Data { get; set; } = new();
}

public sealed class WikiLatestEntry
{
    [JsonPropertyName("high")] public long? High { get; set; }
    [JsonPropertyName("highTime")] public long? HighTime { get; set; }
    [JsonPropertyName("low")] public long? Low { get; set; }
    [JsonPropertyName("lowTime")] public long? LowTime { get; set; }
}

public sealed class WikiMappingItem
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("examine")] public string Examine { get; set; } = "";
    [JsonPropertyName("members")] public bool Members { get; set; }
    [JsonPropertyName("lowalch")] public int LowAlch { get; set; }
    [JsonPropertyName("highalch")] public int HighAlch { get; set; }
    [JsonPropertyName("limit")] public int Limit { get; set; }
    [JsonPropertyName("value")] public long Value { get; set; }
    [JsonPropertyName("icon")] public string Icon { get; set; } = "";
}

public sealed class WikiBucketResponse
{
    [JsonPropertyName("data")]
    public Dictionary<string, WikiBucketEntry> Data { get; set; } = new();
}

public sealed class WikiBucketEntry
{
    [JsonPropertyName("avgHighPrice")] public long? AvgHighPrice { get; set; }
    [JsonPropertyName("avgLowPrice")] public long? AvgLowPrice { get; set; }
    [JsonPropertyName("highPriceVolume")] public long HighPriceVolume { get; set; }
    [JsonPropertyName("lowPriceVolume")] public long LowPriceVolume { get; set; }
}
