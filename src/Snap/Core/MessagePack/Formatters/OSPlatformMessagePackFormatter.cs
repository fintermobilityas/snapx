using System.Runtime.InteropServices;
using MessagePack;
using MessagePack.Formatters;

namespace Snap.Core.MessagePack.Formatters;

public sealed class OsPlatformMessagePackFormatter : IMessagePackFormatter<OSPlatform>
{
    public void Serialize(ref MessagePackWriter writer, OSPlatform value, MessagePackSerializerOptions options)
    {
        options.Resolver.GetFormatterWithVerify<string>().Serialize(ref writer, value.ToString(), options);
    }

    public OSPlatform Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        var osPlatform = options.Resolver.GetFormatterWithVerify<string>().Deserialize(ref reader, options);
        return OSPlatform.Create(osPlatform);
    }
}