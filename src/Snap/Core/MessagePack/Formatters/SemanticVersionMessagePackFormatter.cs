using System;
using System.Text;
using MessagePack;
using MessagePack.Formatters;
using NuGet.Versioning;

namespace Snap.Core.MessagePack.Formatters
{
    public sealed class SemanticVersionMessagePackFormatter : IMessagePackFormatter<SemanticVersion>
    {
        public void Serialize(ref MessagePackWriter writer, SemanticVersion value, MessagePackSerializerOptions options)
        {
#if NETFULLFRAMEWORK
            var utf8StringBytes = Encoding.UTF8.GetBytes(value.ToString());
            writer.WriteString(utf8StringBytes);
#else
            var utf8StringBytes = Encoding.UTF8.GetBytes(value.ToString()).AsSpan();
            writer.WriteString(utf8StringBytes);
#endif
        }

        public SemanticVersion Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            var value = reader.ReadString();
            return SemanticVersion.Parse(value);
        }
    }
}
