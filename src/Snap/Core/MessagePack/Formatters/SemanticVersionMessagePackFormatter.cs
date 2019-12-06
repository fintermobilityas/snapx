using System;
using System.Text;
using MessagePack;
using MessagePack.Formatters;
using NuGet.Versioning;

namespace Snap.Core.MessagePack.Formatters
{
    public sealed class SemanticVersionMessagePackFormatter : IMessagePackFormatter<SemanticVersion>
    {
        public int Serialize(ref byte[] bytes, int offset, SemanticVersion value, IFormatterResolver formatterResolver)
        {
            return formatterResolver.GetFormatterWithVerify<string>()
                .Serialize(ref bytes, offset, value.ToString(), formatterResolver);
        }

        public SemanticVersion Deserialize(byte[] bytes, int offset, IFormatterResolver formatterResolver,
            out int readSize)
        {
            var version = formatterResolver.GetFormatterWithVerify<string>()
                .Deserialize(bytes, offset, formatterResolver, out readSize);
            return SemanticVersion.Parse(version);
        }
    }
}
