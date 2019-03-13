using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using JetBrains.Annotations;
using Mono.Cecil;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using Snap.Core.Models;

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
        string Sha512(SnapRelease snapRelease, IPackageCoreReader packageCoreReader, ISnapPack snapPack);
        string Sha512(SnapRelease snapRelease, PackageBuilder packageBuilder);
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
            
            content.Seek(0, SeekOrigin.Begin);

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

        public string Sha512(SnapRelease snapRelease, [NotNull] IPackageCoreReader packageCoreReader, [NotNull] ISnapPack snapPack)
        {
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
            if (packageCoreReader == null) throw new ArgumentNullException(nameof(packageCoreReader));
            if (snapPack == null) throw new ArgumentNullException(nameof(snapPack));
            return Sha512(packageCoreReader.GetFiles().Where(targetPath => targetPath.StartsWith(SnapConstants.NuspecRootTargetPath)).Select(packageCoreReader.GetStream));
        }
       
        public string Sha512([NotNull] SnapRelease snapRelease, [NotNull] PackageBuilder packageBuilder)
        {
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
            if (packageBuilder == null) throw new ArgumentNullException(nameof(packageBuilder));
            return Sha512(packageBuilder.Files.Where(x => x.EffectivePath.StartsWith(SnapConstants.NuspecRootTargetPath)).Select(x => x.GetStream()));
        }

        string Sha512(IEnumerable<Stream> inputStreams)
        {
            var sb = new StringBuilder();
            foreach (var srcStream in inputStreams)
            {
                using (var intermediateStream = new MemoryStream())
                {
                    srcStream.CopyTo(intermediateStream);
                    if (srcStream.CanSeek)
                    {
                        srcStream.Seek(0, SeekOrigin.Begin);                        
                    }
                    sb.Append(Sha512(intermediateStream));
                }
            }
            return Sha512(sb, Encoding.UTF8);
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
