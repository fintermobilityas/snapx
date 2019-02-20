using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using JetBrains.Annotations;
using Mono.Cecil;

namespace Snap.Core
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [SuppressMessage("ReSharper", "UnusedMemberInSuper.Global")]
    internal interface ISnapCryptoProvider
    {
        string Sha512(byte[] content);
        string Sha512(Stream content);
        string Sha512(StringBuilder content, Encoding encoding);
        string Sha512(AssemblyDefinition assemblyDefinition);
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

        public string Sha512([NotNull] StringBuilder content, [NotNull] Encoding encoding)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (encoding == null) throw new ArgumentNullException(nameof(encoding));
            return Sha512(encoding.GetBytes(content.ToString()));
        }

        public string Sha512([NotNull] AssemblyDefinition assemblyDefinition)
        {
            if (assemblyDefinition == null) throw new ArgumentNullException(nameof(assemblyDefinition));
            using (var outputStream = new MemoryStream())
            {
                assemblyDefinition.Write(outputStream);
                outputStream.Seek(0, SeekOrigin.Begin);
                return Sha512(outputStream);
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
