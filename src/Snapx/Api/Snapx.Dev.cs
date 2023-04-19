using System;
using System.Text.Json.Serialization;

namespace snapx.Api;

[JsonSerializable(typeof(Lock))]
public partial class LockContext : JsonSerializerContext
{
    
}

public sealed record Lock
{
    [JsonInclude]
    public string Name { get; set; }
    [JsonInclude]
    public TimeSpan Duration { get; set; }
}

[JsonSerializable(typeof(Unlock))]
public partial class UnlockContext : JsonSerializerContext
{
    
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
