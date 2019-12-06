using System;
using System.Runtime.InteropServices;
using System.Text;
using MessagePack;
using MessagePack.Formatters;

namespace Snap.Core.MessagePack.Formatters
{
    public sealed class OsPlatformMessagePackFormatter : IMessagePackFormatter<OSPlatform>
    {
        public void Serialize(ref MessagePackWriter writer, OSPlatform value, MessagePackSerializerOptions options)
        {
#if NETFULLFRAMEWORK
            var utf8StringBytes = Encoding.UTF8.GetBytes(value.ToString());
            writer.WriteString(utf8StringBytes);
#else
            var utf8StringBytes = Encoding.UTF8.GetBytes(value.ToString()).AsSpan();
            writer.WriteString(utf8StringBytes);
#endif
        }

        public OSPlatform Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            var value = reader.ReadString();
            return OSPlatform.Create(value);
        }
    }
}
