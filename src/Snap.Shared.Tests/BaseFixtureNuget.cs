using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using JetBrains.Annotations;
using Moq;
using NuGet.Configuration;
using Snap.Core.Models;
using Snap.NuGet;
using Snap.Extensions;

namespace Snap.Shared.Tests
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedMember.Global")]
    public class BaseFixtureNuget
    {
        internal void SetupReleases([NotNull] Mock<INugetService> nugetServiceMock, [NotNull] MemoryStream releasesMemoryStream,
            [NotNull] INuGetPackageSources nuGetPackageSources,
            [NotNull] params SnapApp[] snapAppses)
        {
            if (nugetServiceMock == null) throw new ArgumentNullException(nameof(nugetServiceMock));
            if (releasesMemoryStream == null) throw new ArgumentNullException(nameof(releasesMemoryStream));
            if (nuGetPackageSources == null) throw new ArgumentNullException(nameof(nuGetPackageSources));
            if (snapAppses == null) throw new ArgumentNullException(nameof(snapAppses));
            if (snapAppses.Length == 0) throw new ArgumentException("Value cannot be an empty collection.", nameof(snapAppses));
            
            foreach (var snapApp in snapAppses)
            {
                SetupGetMetadatasAsync(nugetServiceMock, nuGetPackageSources, snapApp);
            }
            
            var genesisSnapApp = snapAppses.First();
            var releasesUpstreamPackageId = genesisSnapApp.BuildNugetReleasesUpstreamId();

            SetupDownloadLatestAsync(nugetServiceMock,
                genesisSnapApp, releasesUpstreamPackageId, releasesMemoryStream, nuGetPackageSources);
        }
        
        internal void SetupGetMetadatasAsync([NotNull] Mock<INugetService> nugetServiceMock, [NotNull] INuGetPackageSources nuGetPackageSources, [NotNull] SnapApp snapApp)
        {
            if (nugetServiceMock == null) throw new ArgumentNullException(nameof(nugetServiceMock));
            if (nuGetPackageSources == null) throw new ArgumentNullException(nameof(nuGetPackageSources));
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));

            var upstreamPackageId = snapApp.BuildNugetUpstreamId();

            nugetServiceMock.Setup(x =>
                    x.GetMetadatasAsync(
                        It.Is<string>(v => string.Equals(v, upstreamPackageId)),
                        It.IsAny<NuGetPackageSources>(),
                        It.IsAny<CancellationToken>(),
                        It.IsAny<bool>(),
                        It.IsAny<bool>()))
                .ReturnsAsync(() =>
                {
                    var packageSearchMedatadata = snapApp.BuildPackageSearchMetadata(nuGetPackageSources);
                    return new List<NuGetPackageSearchMedatadata> { packageSearchMedatadata };
                });

            nugetServiceMock.Setup(x =>
                    x.GetLatestMetadataAsync(
                        It.Is<string>(v => string.Equals(v, upstreamPackageId)),
                        It.IsAny<PackageSource>(),
                        It.IsAny<CancellationToken>(),
                        It.IsAny<bool>(),
                                It.IsAny<bool>()))
                .ReturnsAsync(() =>
                {
                    return snapApp.BuildPackageSearchMetadata(nuGetPackageSources);
                });
        }
        
        internal void SetupDownloadAsyncWithProgressAsync([NotNull] Mock<INugetService> nugetServiceMock, [NotNull] SnapApp snapApp, 
            [NotNull] MemoryStream packageStream, [NotNull] INuGetPackageSources nuGetPackageSources)
        {
            if (nugetServiceMock == null) throw new ArgumentNullException(nameof(nugetServiceMock));
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (packageStream == null) throw new ArgumentNullException(nameof(packageStream));
            if (nuGetPackageSources == null) throw new ArgumentNullException(nameof(nuGetPackageSources));
            
            var packageIdentity = snapApp.BuildPackageIdentity();
            var downloadResourceResult = snapApp.BuildDownloadResourceResult(packageStream, nuGetPackageSources);
            
            nugetServiceMock
                .Setup(x => x.DownloadAsyncWithProgressAsync(
                        It.IsAny<PackageSource>(),
                        It.Is<DownloadContext>(v => v.PackageIdentity.Equals(packageIdentity)),
                        It.IsAny<INugetServiceProgressSource>(),
                        It.IsAny<CancellationToken>()
                ))
                .ReturnsAsync( () => downloadResourceResult);
        }
        
        internal void SetupDownloadLatestAsync([NotNull] Mock<INugetService> nugetServiceMock, [NotNull] SnapApp snapApp, string upstreamPackageId,
            [NotNull] MemoryStream packageStream, [NotNull] INuGetPackageSources nuGetPackageSources)
        {
            if (nugetServiceMock == null) throw new ArgumentNullException(nameof(nugetServiceMock));
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (packageStream == null) throw new ArgumentNullException(nameof(packageStream));
            if (nuGetPackageSources == null) throw new ArgumentNullException(nameof(nuGetPackageSources));

            var downloadResourceResult = snapApp.BuildDownloadResourceResult(packageStream, nuGetPackageSources);

            nugetServiceMock
                .Setup(x => x
                    .DownloadLatestAsync(
                        It.Is<string>(v => v.Equals(upstreamPackageId)),
                        It.IsAny<PackageSource>(), 
                        It.IsAny<CancellationToken>(),
                        It.IsAny<bool>(),
                        It.IsAny<bool>())
                )
                .ReturnsAsync( () => downloadResourceResult);
        }
    }
}
