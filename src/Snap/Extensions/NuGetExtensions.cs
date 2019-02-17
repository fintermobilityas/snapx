using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using Snap.Core.Models;
using Snap.NuGet;

namespace Snap.Extensions
{
    internal static class NuGetExtensions
    {
        internal static PackageIdentity BuildPackageIdentity([NotNull] this SnapRelease snapRelease)
        {
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
            return new PackageIdentity(snapRelease.UpstreamId, snapRelease.Version.ToNuGetVersion());
        }
        
        internal static bool IsMaybeASuccessfullDownloadSafe(this DownloadResourceResult downloadResourceResult)
        {
            return downloadResourceResult != null && (downloadResourceResult.Status == DownloadResourceResultStatus.Available ||
                                                      downloadResourceResult.Status == DownloadResourceResultStatus.AvailableWithoutStream);
        }
        
        public static IPackageFile GetPackageFile([NotNull] this PackageBuilder packageBuilder, [NotNull] string filename)
        {
            if (packageBuilder == null) throw new ArgumentNullException(nameof(packageBuilder));
            if (filename == null) throw new ArgumentNullException(nameof(filename));

            return packageBuilder.Files.SingleOrDefault(x => string.Equals(
                filename.ForwardSlashesSafe(), 
                x.Path.ForwardSlashesSafe(), 
                StringComparison.InvariantCultureIgnoreCase));
        }

        internal static async Task<NuspecReader> GetNuspecReaderAsync([NotNull] this IAsyncPackageCoreReader asyncPackageCoreReader, CancellationToken cancellationToken)
        {
            if (asyncPackageCoreReader == null) throw new ArgumentNullException(nameof(asyncPackageCoreReader));
            using (var nuspecStream = await asyncPackageCoreReader.GetNuspecAsync(cancellationToken).ReadToEndAsync(cancellationToken, true))
            {
                return new NuspecReader(nuspecStream);
            }
        }

        internal static async Task<ManifestMetadata> GetManifestMetadataAsync([NotNull] this IAsyncPackageCoreReader asyncPackageCoreReader, CancellationToken cancellationToken)
        {
            if (asyncPackageCoreReader == null) throw new ArgumentNullException(nameof(asyncPackageCoreReader));
            using (var nuspecStream = await asyncPackageCoreReader.GetNuspecAsync(cancellationToken).ReadToEndAsync(cancellationToken, true))
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
