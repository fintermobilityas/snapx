using System.Runtime.InteropServices;
using MessagePack;
using MessagePack.Formatters;

namespace Snap.Core.MessagePack.Formatters
{
    public sealed class OsPlatformMessagePackFormatter : IMessagePackFormatter<OSPlatform>
    {
        public int Serialize(ref byte[] bytes, int offset, OSPlatform value, IFormatterResolver formatterResolver)
        {
            return formatterResolver.GetFormatterWithVerify<string>().Serialize(ref bytes, offset, value.ToString(), formatterResolver);
        }

        public OSPlatform Deserialize(byte[] bytes, int offset, IFormatterResolver formatterResolver, out int readSize)
        {
            var version = formatterResolver.GetFormatterWithVerify<string>().Deserialize(bytes, offset, formatterResolver, out readSize);
            return OSPlatform.Create(version);
        }
    }
}