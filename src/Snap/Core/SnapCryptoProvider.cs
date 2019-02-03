using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using JetBrains.Annotations;
using Mono.Cecil;

namespace Snap.Core
{
    internal interface ISnapCryptoProvider
    {
        string Sha512(byte[] content);
        string Sha512(Stream content);
        string Sha1(Stream content);
        string Sha1(AssemblyDefinition assemblyDefinition);
    }

    internal sealed class SnapCryptoProvider : ISnapCryptoProvider
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

        public string Sha1(Stream content)
        {
            var sha512 = SHA1.Create();
            var hash = sha512.ComputeHash(content);

            return HashToString(hash);
        }

        public string Sha1(AssemblyDefinition assemblyDefinition)
        {
            using (var outputStream = new MemoryStream())
            {
                assemblyDefinition.Write(outputStream);
                outputStream.Seek(0, SeekOrigin.Begin);
                return Sha1(outputStream);
            }
        }

        static string HashToString([NotNull] byte[] hash)
        {
            if (hash == null) throw new ArgumentNullException(nameof(hash));
            var builder = new StringBuilder();  
            foreach (var t in hash)
            {
                builder.Append(t.ToString("x2"));
            }  
            return builder.ToString();  
        }
    }
}
