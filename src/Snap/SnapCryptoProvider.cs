using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Snap
{
    public interface ISnapCryptoProvider
    {
        string Sha512(byte[] content);
        string Sha512(Stream content);
    }

    public sealed class SnapCryptoProvider : ISnapCryptoProvider
    {
        public string Sha512(byte[] content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));

            var sha512 = SHA512.Create();
            var hash = sha512.ComputeHash(content);

            return HashToString(hash);
        }

        public string Sha512(Stream content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));

            var sha512 = SHA512.Create();
            var hash = sha512.ComputeHash(content);

            return HashToString(hash);
        }
        
        static string HashToString(IEnumerable<byte> hash)
        {
            var result = new StringBuilder();
            foreach (var h in hash)
            {
                result.Append(h.ToString("X2"));
            }

            return result.ToString();
        }
    }
}
