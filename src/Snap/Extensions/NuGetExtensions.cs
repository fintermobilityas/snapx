using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using NuGet.Configuration;
using NuGet.Packaging;
using Snap.Core.Models;
using Snap.NuGet;

namespace Snap.Extensions
{
    internal static class NuGetExtensions
    {        
        internal static async Task<ManifestMetadata> GetManifestMetadataAsync([NotNull] this PackageArchiveReader packageArchiveReader, CancellationToken cancellationToken)
        {
            if (packageArchiveReader == null) throw new ArgumentNullException(nameof(packageArchiveReader));
            using (var nuspecStream = await packageArchiveReader.GetNuspec().ReadToEndAsync(cancellationToken, true))
            {
                return Manifest.ReadFrom(nuspecStream, false)?.Metadata;
            }
        }
        
        static bool IsPasswordEncryptionSupportedImpl()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        }

        internal static bool IsPasswordEncryptionSupported([NotNull] this SnapNugetFeed nugetFeed)
        {
            if (nugetFeed == null) throw new ArgumentNullException(nameof(nugetFeed));
            return IsPasswordEncryptionSupportedImpl();
        }
        
        internal static bool IsPasswordEncryptionSupported([NotNull] this ISettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return IsPasswordEncryptionSupportedImpl();
        }
        
        internal static bool IsPasswordEncryptionSupported([NotNull] this INuGetPackageSources packageSources)
        {
            if (packageSources == null) throw new ArgumentNullException(nameof(packageSources));
            return IsPasswordEncryptionSupportedImpl();
        }
    }
}
