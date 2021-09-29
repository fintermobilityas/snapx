using MessagePack;
using MessagePack.Formatters;
using NuGet.Versioning;

namespace Snap.Core.MessagePack.Formatters;

public sealed class SemanticVersionMessagePackFormatter : IMessagePackFormatter<SemanticVersion>
{
    public void Serialize(ref MessagePackWriter writer, SemanticVersion value, MessagePackSerializerOptions options)
    {
        options.Resolver.GetFormatterWithVerify<string>().Serialize(ref writer, value.ToString(), options);
    }

    public SemanticVersion Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        var version = options.Resolver.GetFormatterWithVerify<string>().Deserialize(ref reader, options);
        return SemanticVersion.Parse(version);
    }
}