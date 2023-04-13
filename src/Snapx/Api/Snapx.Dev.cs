using System;
using System.Text.Json.Serialization;

namespace snapx.Api;

public sealed record Lock
{
    [JsonInclude]
    public string Name { get; set; }
    [JsonInclude]
    public TimeSpan Duration { get; set; }
}

public sealed record RenewLock 
{
    [JsonInclude]
    public string Name { get; set; }
    [JsonInclude]
    public string Challenge { get; set; }
}

public sealed record Unlock 
{
    [JsonInclude]
    public string Name { get; set; }
    [JsonInclude]
    public string Challenge { get; set; }
    [JsonInclude]
    public TimeSpan? BreakPeriod { get; set; }
}
