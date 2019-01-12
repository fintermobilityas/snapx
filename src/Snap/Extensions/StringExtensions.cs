using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

namespace Snap.Core.Extensions
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal static class StringExtensions
    {
        [SuppressMessage("ReSharper", "UnusedMember.Global")]
        public static string RemoveByteOrderMarkerIfPresent(this string content)
        {
            return string.IsNullOrEmpty(content) ?
                string.Empty : RemoveByteOrderMarkerIfPresent(Encoding.UTF8.GetBytes(content));
        }

        [SuppressMessage("ReSharper", "UnusedMember.Global")]
        public static string RemoveByteOrderMarkerIfPresent(this byte[] content)
        {
            byte[] output = { };

            if (content == null)
            {
                goto done;
            }

            bool Matches(IReadOnlyCollection<byte> bom, IReadOnlyList<byte> src)
            {
                if (src.Count < bom.Count) return false;

                return !bom.Where((chr, index) => src[index] != chr).Any();
            }

            var utf32Be = new byte[] { 0x00, 0x00, 0xFE, 0xFF };
            var utf32Le = new byte[] { 0xFF, 0xFE, 0x00, 0x00 };
            var utf16Be = new byte[] { 0xFE, 0xFF };
            var utf16Le = new byte[] { 0xFF, 0xFE };
            var utf8 = new byte[] { 0xEF, 0xBB, 0xBF };

            if (Matches(utf32Be, content))
            {
                output = new byte[content.Length - utf32Be.Length];
            }
            else if (Matches(utf32Le, content))
            {
                output = new byte[content.Length - utf32Le.Length];
            }
            else if (Matches(utf16Be, content))
            {
                output = new byte[content.Length - utf16Be.Length];
            }
            else if (Matches(utf16Le, content))
            {
                output = new byte[content.Length - utf16Le.Length];
            }
            else if (Matches(utf8, content))
            {
                output = new byte[content.Length - utf8.Length];
            }
            else
            {
                output = content;
            }

            done:
            if (output.Length > 0)
            {
                Buffer.BlockCopy(content, content.Length - output.Length, output, 0, output.Length);
            }

            return Encoding.UTF8.GetString(output);
        }
    }
}
