using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Snap.Core
{
    public interface ISnapCryptoProvider
    {
        string Sha512(byte[] content);
    }

    public sealed class SnapCryptoProvider : ISnapCryptoProvider
    {
        public string Sha512(byte[] content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));

            var sha512 = SHA512.Create();
            var hash = sha512.ComputeHash(content);

            var result = new StringBuilder();
            foreach (var h in hash)
            {
                result.Append(h.ToString("X2"));
            }
            return result.ToString();
        }
    }
}
