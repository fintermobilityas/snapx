using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using JetBrains.Annotations;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using Snap.Core.Models;
using Snap.Extensions;

namespace Snap.Core;

internal interface ISnapCryptoProvider
{
    string Sha256(byte[] content);
    string Sha256(Stream content);
    string Sha256(StringBuilder content, Encoding encoding);
    string Sha256(SnapRelease snapRelease, IPackageCoreReader packageCoreReader, ISnapPack snapPack);
    string Sha256(SnapRelease snapRelease, PackageBuilder packageBuilder);
}

internal sealed class SnapCryptoProvider : ISnapCryptoProvider
{
    public string Sha256(byte[] content)
    {
        if (content == null) throw new ArgumentNullException(nameof(content));

        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(content);

        return HashToString(hash);
    }

    public string Sha256(Stream content)
    {
        if (content == null) throw new ArgumentNullException(nameof(content));

        if (!content.CanSeek)
        {
            throw new Exception("Stream must be seekable");
        }

        content.Seek(0, SeekOrigin.Begin);

        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(content);

        content.Seek(0, SeekOrigin.Begin);

        return HashToString(hash);
    }

    public string Sha256([NotNull] StringBuilder content, [NotNull] Encoding encoding)
    {
        if (content == null) throw new ArgumentNullException(nameof(content));
        if (encoding == null) throw new ArgumentNullException(nameof(encoding));
        return Sha256(encoding.GetBytes(content.ToString()));
    }

    public string Sha256(SnapRelease snapRelease, [NotNull] IPackageCoreReader packageCoreReader, [NotNull] ISnapPack snapPack)
    {
        if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
        if (packageCoreReader == null) throw new ArgumentNullException(nameof(packageCoreReader));
        if (snapPack == null) throw new ArgumentNullException(nameof(snapPack));

        var packageArchiveFiles = packageCoreReader.GetFiles();
            
        var checksumFiles = GetChecksumFilesForSnapRelease(snapRelease);

        var inputStreams = checksumFiles
            .Select(checksum => (checksum, targetPath: packageArchiveFiles.SingleOrDefault(targetPath => checksum.NuspecTargetPath == targetPath)))
            .Select(x =>
            {
                var (checksum, packageArchiveReaderTargetPath) = x;
                if (packageArchiveReaderTargetPath == null)
                {
                    throw new FileNotFoundException($"Unable to find file in nupkg: {snapRelease.Filename}.", checksum.NuspecTargetPath);
                }
                return (checksum, packageCoreReader.GetStream(packageArchiveReaderTargetPath));
            });

        return Sha256(inputStreams);
    }

    public string Sha256([NotNull] SnapRelease snapRelease, [NotNull] PackageBuilder packageBuilder)
    {
        if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
        if (packageBuilder == null) throw new ArgumentNullException(nameof(packageBuilder));

        var checksumFiles = GetChecksumFilesForSnapRelease(snapRelease);

        var enumerable = checksumFiles
            .Select(checksum => (checksum, packageFile: packageBuilder.GetPackageFile(checksum.NuspecTargetPath, StringComparison.OrdinalIgnoreCase)));

        return Sha256(enumerable.Select(x =>
        {
            var (checksum, packageFile) = x;
            if (packageFile == null)
            {
                throw new FileNotFoundException($"Unable to find file in nupkg: {snapRelease.Filename}.", checksum.NuspecTargetPath);
            }

            return (checksum, packageFile.GetStream());
        }));
    }

    string Sha256(IEnumerable<(SnapReleaseChecksum targetPath, Stream srcStream)> inputStreams)
    {
        var sb = new StringBuilder();
        foreach (var (_, srcStream) in inputStreams)
        {
            if (srcStream.CanSeek)
            {
                srcStream.Seek(0, SeekOrigin.Begin);
                var sha256 = Sha256(srcStream);
                sb.Append(sha256);
                srcStream.Seek(0, SeekOrigin.Begin);
                continue;
            }

            using var intermediateStream = new MemoryStream();
            {
                srcStream.CopyTo(intermediateStream);
                var sha256 = Sha256(intermediateStream);
                sb.Append(sha256);
            }
        }

        return Sha256(sb, Encoding.UTF8);
    }

    static string HashToString([NotNull] byte[] hash)
    {
        if (hash == null) throw new ArgumentNullException(nameof(hash));
        var builder = new StringBuilder();
        foreach (var t in hash)
        {
            builder.Append(t.ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    static IEnumerable<SnapReleaseChecksum> GetChecksumFilesForSnapRelease(SnapRelease snapRelease)
    {
        var files = snapRelease.IsDelta ? snapRelease.New.Concat(snapRelease.Modified).ToList() : snapRelease.Files;
        return files.OrderBy(x => x.NuspecTargetPath, new OrdinalIgnoreCaseComparer());
    }
}
