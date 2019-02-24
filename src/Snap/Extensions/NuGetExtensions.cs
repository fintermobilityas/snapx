using System;
using System.Collections.Generic;
using System.IO;
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

        internal static PackageIdentity BuildPackageIdentity([NotNull] this SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));            
            return new PackageIdentity(snapApp.BuildNugetUpstreamPackageId(), snapApp.Version.ToNuGetVersion());
        }

        internal static NuGetPackageSearchMedatadata BuildPackageSearchMedatadata([NotNull] this SnapApp snapApp,
            [NotNull] INuGetPackageSources nugetSources)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (nugetSources == null) throw new ArgumentNullException(nameof(nugetSources));

            var channel = snapApp.Channels.Single(x => x.Current);
            var updateFeed = (SnapNugetFeed) channel.UpdateFeed;
            var packageSource = nugetSources.Items.Single(x => x.Name == updateFeed.Name && x.SourceUri == updateFeed.Source);
            
            return new NuGetPackageSearchMedatadata(snapApp.BuildPackageIdentity(), packageSource, DateTimeOffset.Now, new List<PackageDependency>());
        }
               
        internal static DownloadResourceResult BuildDownloadResourceResult([NotNull] this SnapApp snapApp, 
            [NotNull] MemoryStream packageStream, [NotNull] INuGetPackageSources nugetSources)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (packageStream == null) throw new ArgumentNullException(nameof(packageStream));
            if (nugetSources == null) throw new ArgumentNullException(nameof(nugetSources));

            var channel = snapApp.Channels.Single(x => x.Current);
            var updateFeed = (SnapNugetFeed) channel.UpdateFeed;
            var packageSource = nugetSources.Items.Single(x => x.Name == updateFeed.Name && x.SourceUri == updateFeed.Source);

            return new DownloadResourceResult(new MemoryStream(packageStream.ToArray()), new PackageArchiveReader(packageStream), packageSource.Name);
        }
        
        internal static bool SuccessSafe(this DownloadResourceResult downloadResourceResult)
        {
            return downloadResourceResult != null && downloadResourceResult.Status == DownloadResourceResultStatus.Available;
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
