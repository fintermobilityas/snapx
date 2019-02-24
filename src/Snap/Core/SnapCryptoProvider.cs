using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using JetBrains.Annotations;
using Mono.Cecil;
using NuGet.Packaging;
using NuGet.Packaging.Core;

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
        string Sha512(IPackageCoreReader packageCoreReader, Encoding encoding);
        string Sha512(PackageBuilder packageBuilder, Encoding encoding);
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

            if (!content.CanSeek)
            {
                throw new Exception("Stream must be seekable");
            }

            var sha512 = SHA512.Create();
            var hash = sha512.ComputeHash(content);

            content.Seek(0, SeekOrigin.Begin);

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

        public string Sha512([NotNull] IPackageCoreReader packageCoreReader, [NotNull] Encoding encoding)
        {
            if (packageCoreReader == null) throw new ArgumentNullException(nameof(packageCoreReader));
            if (encoding == null) throw new ArgumentNullException(nameof(encoding));
            
            var checksums = new StringBuilder();
            foreach (var inMemoryFile in packageCoreReader.GetFiles().Where(x => x.StartsWith(SnapConstants.SnapNuspecTargetPath)))
            {
                var srcStream = packageCoreReader.GetStream(inMemoryFile);
                using (var dstStream = new MemoryStream())
                {
                    srcStream.CopyTo(dstStream);
                    checksums.Append(Sha512(dstStream));
                }
            }

            return Sha512(checksums, encoding);
        }

        public string Sha512([NotNull] PackageBuilder packageBuilder, [NotNull] Encoding encoding)
        {
            if (packageBuilder == null) throw new ArgumentNullException(nameof(packageBuilder));
            if (encoding == null) throw new ArgumentNullException(nameof(encoding));
            
            var checksums = new StringBuilder();
            foreach (var inMemoryFile in packageBuilder.Files.Where(x => x.Path.StartsWith(SnapConstants.SnapNuspecTargetPath)))
            {
                var srcStream = inMemoryFile.GetStream();
                using (var dstStream = new MemoryStream())
                {
                    srcStream.CopyTo(dstStream);
                    checksums.Append(Sha512(dstStream));
                }
                srcStream.Seek(0, SeekOrigin.Begin);
            }

            return Sha512(checksums, encoding);
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
