using System.Text.Json;
using System.Text.Json.Serialization;
using NuGet.Versioning;
using Snap.Core.Json;
using Xunit;

namespace Snap.Tests.Core.Json;

file sealed class SemanticVersionDto
{
    [JsonInclude, JsonConverter(typeof(SemanticVersionSystemTextJsonConverter))]
    public SemanticVersion Value { get; init; }
}

public sealed class SemanticVersionSystemTextJsonConverterTests
{
    [Fact]
    public void TestSerializeDeserialize()
    {
        var dto = new SemanticVersionDto
        {
            Value = new SemanticVersion(1, 0, 0, "abc", "metadata")
        };

        var json = JsonSerializer.Serialize(dto);   
        var deserializedDto = JsonSerializer.Deserialize<SemanticVersionDto>(json);
        Assert.NotNull(deserializedDto);
        Assert.Equal(dto.Value, deserializedDto.Value);
    }
}
