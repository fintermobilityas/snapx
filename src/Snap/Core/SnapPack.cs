using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using DotNet.Globbing;
using JetBrains.Annotations;
using Mono.Cecil;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using Snap.Core.Models;
using Snap.Core.Resources;
using Snap.Extensions;
using Snap.Logging;
using Snap.NuGet;

namespace Snap.Core
{
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
    public sealed class SnapReleaseFileChecksumMismatchException : Exception
    {
        public SnapReleaseChecksum Checksum { get; }
        public SnapRelease Release { get; }

        public SnapReleaseFileChecksumMismatchException([NotNull] SnapReleaseChecksum checksum, [NotNull] SnapRelease release) :
            base($"Checksum mismatch for filename: {checksum.Filename}. Nupkg: {release.Filename}. Nupkg file size: {release.FullFilesize}.")
        {
            Checksum = checksum ?? throw new ArgumentNullException(nameof(checksum));
            Release = release ?? throw new ArgumentNullException(nameof(release));
        }
    }

    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
    public sealed class SnapReleaseFileChecksumDeltaMismatchException : Exception
    {
        public SnapReleaseChecksum Checksum { get; }
        public SnapRelease Release { get; }

        public SnapReleaseFileChecksumDeltaMismatchException([NotNull] SnapReleaseChecksum checksum, [NotNull] SnapRelease release, long patchStreamFilesize) :
            base($"Checksum mismatch for filename: {checksum.Filename}. Original file size: {checksum.FullFilesize}. " +
                 $"Delta file size: {checksum.DeltaFilesize}. Patch file size: {patchStreamFilesize}. Nupkg: {release.Filename}. Nupkg file size: {release.FullFilesize}.")
        {
            Checksum = checksum ?? throw new ArgumentNullException(nameof(checksum));
            Release = release ?? throw new ArgumentNullException(nameof(release));
        }
    }

    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
    public sealed class SnapReleaseChecksumMismatchException : Exception
    {
        public SnapRelease Release { get; }

        public SnapReleaseChecksumMismatchException([NotNull] SnapRelease release) :
            base($"Checksum mismatch for nupkg: {release.Filename}. File size: {release.FullFilesize}.")
        {
            Release = release ?? throw new ArgumentNullException(nameof(release));
        }
    }

    internal interface ISnapPackageDetails
    {
        SnapAppsReleases SnapAppsReleases { get; }
        SnapApp SnapApp { get; }
        string NuspecFilename { get; }
        string NuspecBaseDirectory { get; }
        ISnapProgressSource SnapProgressSource { get; }
        IReadOnlyDictionary<string, string> NuspecProperties { get; }
        string PackagesDirectory { get; }
    }

    internal sealed class SnapPackageDetails : ISnapPackageDetails
    {
        public SnapAppsReleases SnapAppsReleases { get; set; }
        public SnapApp SnapApp { get; set; }
        public string NuspecFilename { get; set; }
        public string NuspecBaseDirectory { get; set; }
        public ISnapProgressSource SnapProgressSource { get; set; }
        public IReadOnlyDictionary<string, string> NuspecProperties { get; [UsedImplicitly] set; }
        public string PackagesDirectory { get; set; }
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal interface ISnapPack
    {
        IReadOnlyCollection<string> AlwaysRemoveTheseAssemblies { get; }
        IReadOnlyCollection<string> NeverGenerateBsDiffsTheseAssemblies { get; }

        Task<(MemoryStream fullNupkgMemoryStream, SnapRelease fullSnapRelease, MemoryStream deltaNupkgMemoryStream, SnapRelease deltaSnapRelease)> BuildPackageAsync([NotNull] ISnapPackageDetails packageDetails,
            [NotNull] ICoreRunLib coreRunLib, ILog logger = null, CancellationToken cancellationToken = default);

        Task<(MemoryStream outputStream, SnapApp fullSnapApp, SnapRelease fullSnapRelease)> RebuildPackageAsync([NotNull] string packagesDirectory,
            [NotNull] ISnapAppReleases existingAppReleases,
            [NotNull] SnapRelease snapRelease, SnapChannel snapChannel,
            ILog logger = null, CancellationToken cancellationToken = default);

        MemoryStream BuildReleasesPackage([NotNull] SnapApp snapApp, [NotNull] SnapAppsReleases snapAppsReleases);
        Task<SnapApp> GetSnapAppAsync([NotNull] IAsyncPackageCoreReader asyncPackageCoreReader, CancellationToken cancellationToken = default);

        Task<MemoryStream> GetSnapAssetAsync([NotNull] IAsyncPackageCoreReader asyncPackageCoreReader, [NotNull] string filename,
            CancellationToken cancellationToken = default);
    }

    [SuppressMessage("ReSharper", "UnusedTupleComponentInReturnValue")]
    internal sealed class SnapPack : ISnapPack
    {
        readonly ISnapFilesystem _snapFilesystem;
        readonly ISnapAppReader _snapAppReader;
        readonly ISnapAppWriter _snapAppWriter;
        readonly ISnapCryptoProvider _snapCryptoProvider;
        readonly ISnapEmbeddedResources _snapEmbeddedResources;

        public IReadOnlyCollection<string> AlwaysRemoveTheseAssemblies => new List<string>
        {
            _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, SnapConstants.SnapDllFilename).ForwardSlashesSafe(),
            _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, SnapConstants.SnapAppDllFilename).ForwardSlashesSafe()
        };

        public IReadOnlyCollection<string> NeverGenerateBsDiffsTheseAssemblies => new List<string>
        {
            _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapAppDllFilename).ForwardSlashesSafe()
        };

        public SnapPack(ISnapFilesystem snapFilesystem, [NotNull] ISnapAppReader snapAppReader, [NotNull] ISnapAppWriter snapAppWriter,
            [NotNull] ISnapCryptoProvider snapCryptoProvider, [NotNull] ISnapEmbeddedResources snapEmbeddedResources)
        {
            _snapFilesystem = snapFilesystem ?? throw new ArgumentNullException(nameof(snapFilesystem));
            _snapAppReader = snapAppReader ?? throw new ArgumentNullException(nameof(snapAppReader));
            _snapAppWriter = snapAppWriter ?? throw new ArgumentNullException(nameof(snapAppWriter));
            _snapCryptoProvider = snapCryptoProvider ?? throw new ArgumentNullException(nameof(snapCryptoProvider));
            _snapEmbeddedResources = snapEmbeddedResources ?? throw new ArgumentNullException(nameof(snapEmbeddedResources));
        }

        public async Task<(MemoryStream fullNupkgMemoryStream, SnapRelease fullSnapRelease, MemoryStream deltaNupkgMemoryStream, SnapRelease deltaSnapRelease)> BuildPackageAsync(ISnapPackageDetails packageDetails, ICoreRunLib coreRunLib,
            ILog logger = null, CancellationToken cancellationToken = default)
        {
            if (packageDetails == null) throw new ArgumentNullException(nameof(packageDetails));
            if (coreRunLib == null) throw new ArgumentNullException(nameof(coreRunLib));

            var (fullNupkgPackageBuilder, _, fullSnapRelease, deltaNupkgPackageBuilder, _, deltaSnapRelease) =
                await BuildPackageAsyncInternal(packageDetails, coreRunLib, logger, cancellationToken);

            fullSnapRelease.Sort();            
            
            var fullNupkgMemoryStream = new MemoryStream();
            
            fullSnapRelease.FullSha512Checksum = _snapCryptoProvider.Sha512(fullSnapRelease, fullNupkgPackageBuilder);

            fullNupkgPackageBuilder.Save(fullNupkgMemoryStream);
            fullNupkgMemoryStream.Seek(0, SeekOrigin.Begin);
            
            fullSnapRelease.FullFilesize = fullNupkgMemoryStream.Length;

            if (fullSnapRelease.IsGenisis)
            {
                packageDetails.SnapAppsReleases.Add(fullSnapRelease);
                return (fullNupkgMemoryStream, fullSnapRelease, null, null);
            }

            deltaSnapRelease.Sort();

            var deltaNupkgMemoryStream = new MemoryStream();

            deltaSnapRelease.DeltaSha512Checksum =
                fullSnapRelease.DeltaSha512Checksum = _snapCryptoProvider.Sha512(deltaSnapRelease, deltaNupkgPackageBuilder);
                
            deltaNupkgPackageBuilder.Save(deltaNupkgMemoryStream);

            deltaSnapRelease.DeltaFilesize = 
                fullSnapRelease.DeltaFilesize = deltaNupkgMemoryStream.Length;
            deltaSnapRelease.FullSha512Checksum = fullSnapRelease.FullSha512Checksum;
            deltaSnapRelease.FullFilesize = fullSnapRelease.FullFilesize;

            packageDetails.SnapAppsReleases.Add(deltaSnapRelease);

            return (fullNupkgMemoryStream, fullSnapRelease, deltaNupkgMemoryStream, deltaSnapRelease);
        }

        async Task<(PackageBuilder fullNupkgPackageBuilder, SnapApp fullSnapApp, SnapRelease fullSnapRelease, PackageBuilder deltaNupkgBuilder, SnapApp deltaSnapApp, SnapRelease deltaSnapRelease)>
            BuildPackageAsyncInternal(
                [NotNull] ISnapPackageDetails packageDetails, 
                [NotNull] ICoreRunLib coreRunLib, ILog logger = null,
                CancellationToken cancellationToken = default)
        {
            if (packageDetails == null) throw new ArgumentNullException(nameof(packageDetails));
            if (coreRunLib == null) throw new ArgumentNullException(nameof(coreRunLib));

            Validate(packageDetails);

            var snapApp = packageDetails.SnapApp;
            var snapChannel = snapApp.GetDefaultChannelOrThrow();
            var snapReleases = packageDetails.SnapAppsReleases;
            var packagesDirectory = packageDetails.PackagesDirectory;
            var snapAppReleases = snapReleases.GetReleases(snapApp);

            var snapRelease = new SnapRelease
            {
                Id = snapApp.Id,
                Version = snapApp.Version,
                UpstreamId = snapApp.BuildNugetUpstreamPackageId(),
                Channels = new List<string> { snapChannel.Name },
                Target = snapApp.Target,
                IsGenisis = false,
                IsFull = false
            };            
    
            if (!snapAppReleases.Any())
            {
                var genisisSnapRelease = new SnapRelease(snapRelease)
                {
                    IsGenisis = true,
                    IsFull = true
                };

                genisisSnapRelease.Filename = genisisSnapRelease.BuildNugetLocalFilename();
            
                var (genisisPackageBuilder, nuspecMemoryStream, _, genisisSnapApp, _) =
                    await BuildFullPackageAsyncInternal(packageDetails, genisisSnapRelease, coreRunLib, logger, cancellationToken);
                using (nuspecMemoryStream)
                {
                    return (genisisPackageBuilder, genisisSnapApp, fullSnapRelease: genisisSnapRelease, null, null, null);                    
                }
            }
            
            var deltaSnapRelease = new SnapRelease(snapRelease)
            {
                IsGenisis = false,
                IsFull = false
            };
            
            deltaSnapRelease.Filename = deltaSnapRelease.BuildNugetLocalFilename(); 

            var previousSnapRelease = snapAppReleases.GetMostRecentRelease(snapChannel);

            var (previousNupkgPackageBuilder, _, _) = await RebuildFullPackageAsyncInternal(
                packagesDirectory, snapAppReleases, previousSnapRelease, snapChannel, logger, cancellationToken);

            var (currentFullNupkgPackageBuilder, currentNuspecMemoryStream, currentNuspecPropertiesResolver, 
                currentFullSnapApp, currentFullSnapRelease) = await BuildFullPackageAsyncInternal(
                    packageDetails, deltaSnapRelease, coreRunLib, logger, cancellationToken);

            using (currentNuspecMemoryStream)
            {
                var deltaNupkgBuilder = await BuildDeltaPackageAsyncInternal(
                    packagesDirectory,
                    packageDetails,
                    previousSnapRelease,
                    currentFullSnapRelease,
                    deltaSnapRelease,
                    previousNupkgPackageBuilder,
                    currentFullNupkgPackageBuilder, 
                    currentNuspecMemoryStream,
                    currentNuspecPropertiesResolver,
                    logger, 
                    cancellationToken
                );
                
                var deltaSnapApp = new SnapApp(snapApp)
                {
                    IsFull = false
                }; 
                
                return (currentFullNupkgPackageBuilder, currentFullSnapApp, currentFullSnapRelease, deltaNupkgBuilder, deltaSnapApp, deltaSnapRelease);                
            }
        }

        async Task<(PackageBuilder packageBuilder, MemoryStream nuspecStream, Func<string, string> nuspecPropertiesResolver, SnapApp fullSnapApp, SnapRelease fullSnapRelease)> 
            BuildFullPackageAsyncInternal([NotNull] ISnapPackageDetails packageDetails, [NotNull] SnapRelease snapRelease, ICoreRunLib coreRunLib, 
                ILog logger = null, CancellationToken cancellationToken = default)
        {
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
           
            var pathComparisonType = GetPathStringComparisonType(snapRelease);
            var alwaysRemoveTheseAssemblies = AlwaysRemoveTheseAssemblies.ToList();
            alwaysRemoveTheseAssemblies.Add(_snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(packageDetails.SnapApp));

            var (_, nuspecPropertiesResolver) = BuildNuspecProperties(packageDetails);

            var nuspecIntermediateStream = await _snapFilesystem
                .FileReadAsync(packageDetails.NuspecFilename, cancellationToken);

            var (nuspecStream, packageFiles) =
                GetNuspecStream(packageDetails, nuspecIntermediateStream,
                    nuspecPropertiesResolver, packageDetails.NuspecBaseDirectory, snapRelease, packageDetails.SnapApp);

            var nuspecStreamCpy = new MemoryStream(nuspecStream.ToArray());

            using (nuspecStream)
            using (nuspecIntermediateStream)
            {
                var packageBuilder = new PackageBuilder(nuspecStream, packageDetails.NuspecBaseDirectory, nuspecPropertiesResolver);
                packageBuilder.Files.Clear(); // NB! We are _NOT_ loading files twice into memory.

                foreach (var (filename, targetPath) in packageFiles)
                {
                    var srcStream = await _snapFilesystem.FileRead(filename).ReadToEndAsync(cancellationToken);
                    AddPackageFile(packageBuilder, srcStream, targetPath, string.Empty, snapRelease);
                }

                var mainExecutableFileName = _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(packageDetails.SnapApp);
                var mainExecutableTargetPath = _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, mainExecutableFileName).ForwardSlashesSafe();
                var mainExecutablePackageFile = packageBuilder.GetPackageFile(mainExecutableTargetPath, pathComparisonType);
                if (mainExecutablePackageFile == null)
                {
                    throw new FileNotFoundException("Main executable is missing in nuspec", mainExecutableTargetPath);
                }

                AlwaysRemoveTheseAssemblies.ForEach(targetPath => packageBuilder.RemovePackageFile(targetPath, pathComparisonType));

                await AddPackageAssetsAsync(coreRunLib, packageBuilder, packageDetails.SnapApp, snapRelease, logger, cancellationToken);

                var fullSnapApp = new SnapApp(packageDetails.SnapApp)
                {
                    IsFull = true,
                    IsGenisis = packageDetails.SnapApp.IsGenisis
                };

                var fullSnapRelease = new SnapRelease(snapRelease)
                {
                    IsFull = fullSnapApp.IsFull, 
                    IsGenisis = fullSnapApp.IsGenisis
                };

                fullSnapRelease.Filename = fullSnapRelease.BuildNugetLocalFilename();

                return (packageBuilder, nuspecStreamCpy, nuspecPropertiesResolver, fullSnapApp, fullSnapRelease);
            }
        }

        async Task<PackageBuilder> BuildDeltaPackageAsyncInternal([NotNull] string packagesDirectory,
            [NotNull] ISnapPackageDetails packageDetails,
            [NotNull] SnapRelease previousFullSnapRelease,
            [NotNull] SnapRelease currentFullSnapRelease,
            [NotNull] SnapRelease currentDeltaSnapRelease,
            [NotNull] PackageBuilder previousFullNupkgPackageBuilder,
            [NotNull] PackageBuilder currentFullNupkgPackageBuilder,
            [NotNull] MemoryStream currentFullNupkgNuspecMemoryStream,
            [NotNull] Func<string, string> currentFullNupkgNuspecPropertiesResolverFn,
            ILog logger = null, CancellationToken cancellationToken = default)
        {
            if (packagesDirectory == null) throw new ArgumentNullException(nameof(packagesDirectory));
            if (packageDetails == null) throw new ArgumentNullException(nameof(packageDetails));
            if (previousFullSnapRelease == null) throw new ArgumentNullException(nameof(previousFullSnapRelease));
            if (previousFullNupkgPackageBuilder == null) throw new ArgumentNullException(nameof(previousFullNupkgPackageBuilder));
            if (currentFullNupkgPackageBuilder == null) throw new ArgumentNullException(nameof(currentFullNupkgPackageBuilder));
            if (currentFullNupkgNuspecMemoryStream == null) throw new ArgumentNullException(nameof(currentFullNupkgNuspecMemoryStream));
            if (currentFullNupkgNuspecPropertiesResolverFn == null) throw new ArgumentNullException(nameof(currentFullNupkgNuspecPropertiesResolverFn));

            var pathComparisonType = GetPathStringComparisonType(currentFullSnapRelease);

            async Task AddNoBsDiffPackageFileAsync(PackageBuilder packageBuilder, SnapReleaseChecksum checksum, IPackageFile packageFile)
            {
                if (packageBuilder == null) throw new ArgumentNullException(nameof(packageBuilder));
                if (checksum == null) throw new ArgumentNullException(nameof(checksum));
                if (packageFile == null) throw new ArgumentNullException(nameof(packageFile));
                
                if (checksum.DeltaSha512Checksum != null)
                {
                    throw new Exception(
                        $"Expected {nameof(checksum.DeltaSha512Checksum)} to be null. " +
                        $"Filename: {checksum.Filename}. " +
                        $"Nupkg: {currentFullSnapRelease.Filename}. ");
                }

                if (checksum.DeltaFilesize != 0)
                {
                    throw new Exception($"Expected {nameof(checksum.DeltaFilesize)} to be 0 (zero). " +
                                        $"Filename: {checksum.Filename}. " +
                                        $"Nupkg: {currentFullSnapRelease.Filename}. ");
                }

                var srcStream = await packageFile.GetStream().ReadToEndAsync(cancellationToken);
                AddPackageFile(packageBuilder, srcStream, packageFile.EffectivePath, string.Empty);
            }

            bool ShouldGenerateBsDiff(IPackageFile packageFile)
            {
                if (packageFile == null) throw new ArgumentNullException(nameof(packageFile));
                var targetPath = NeverGenerateBsDiffsTheseAssemblies
                    .SingleOrDefault(x =>
                        string.Equals(x, packageFile.EffectivePath, pathComparisonType));
                return targetPath == null;
            }
            
            currentFullNupkgNuspecMemoryStream.Seek(0, SeekOrigin.Begin);
            
            var deltaNupkgPackageBuilder = new PackageBuilder(currentFullNupkgNuspecMemoryStream, packageDetails.NuspecBaseDirectory, currentFullNupkgNuspecPropertiesResolverFn);
            deltaNupkgPackageBuilder.Files.Clear(); // NB! We are _NOT_ loading files twice into memory.

            var deletedChecksums = previousFullSnapRelease.Files.ToList();
            
            foreach (var currentChecksum in currentFullSnapRelease.Files)
            {
                var currentPackageFile = currentFullNupkgPackageBuilder.GetPackageFile(currentChecksum.NuspecTargetPath, pathComparisonType);    
                var previousChecksum =
                    previousFullSnapRelease.Files.SingleOrDefault(x => string.Equals(x.NuspecTargetPath, currentChecksum.NuspecTargetPath, pathComparisonType));
                    
                if (previousChecksum == null)
                {
                    currentDeltaSnapRelease.New.Add(currentChecksum);
                    var srcStream = await currentPackageFile.GetStream().ReadToEndAsync(cancellationToken);
                    AddPackageFile(deltaNupkgPackageBuilder, srcStream, currentChecksum.NuspecTargetPath, string.Empty);
                    continue;
                }
                
                if (string.Equals(previousChecksum.FullSha512Checksum, currentChecksum.FullSha512Checksum, pathComparisonType)
                    && string.Equals(previousChecksum.DeltaSha512Checksum, currentChecksum.DeltaSha512Checksum, pathComparisonType))
                {
                    currentDeltaSnapRelease.Unmodified.Add(currentChecksum);
                    deletedChecksums.Remove(previousChecksum);
                    continue;
                }

                currentDeltaSnapRelease.Modified.Add(currentChecksum);
                deletedChecksums.Remove(previousChecksum);

                if (!ShouldGenerateBsDiff(currentPackageFile))
                {
                    await AddNoBsDiffPackageFileAsync(deltaNupkgPackageBuilder, currentChecksum, currentPackageFile);
                    continue;
                }

                var previousPackageFile = previousFullNupkgPackageBuilder.GetPackageFile(currentChecksum.NuspecTargetPath, pathComparisonType);
                
                using (var oldDataStream = await previousPackageFile.GetStream().ReadToEndAsync(cancellationToken))
                using (var newDataStream = await currentPackageFile.GetStream().ReadToEndAsync(cancellationToken))
                {
                    var patchStream = new MemoryStream();
                    
                    var deltaChecksum = new SnapReleaseChecksum(currentChecksum);
                    
                    SnapBinaryPatcher.Create(oldDataStream.ToArray(), newDataStream.ToArray(), patchStream);
            
                    currentChecksum.DeltaSha512Checksum = deltaChecksum.DeltaSha512Checksum = _snapCryptoProvider.Sha512(patchStream);
                    currentChecksum.DeltaFilesize = deltaChecksum.DeltaFilesize = patchStream.Length;

                    AddPackageFile(deltaNupkgPackageBuilder, patchStream, currentChecksum.NuspecTargetPath, string.Empty);
                }
            }

            foreach (var deletedChecksum in deletedChecksums)
            {
                currentDeltaSnapRelease.Deleted.Add(deletedChecksum);
            }
   
            if (currentDeltaSnapRelease.Files.Count != currentFullSnapRelease.Files.Count)
            {
                throw new Exception("Expected delta files count to equal full files count. " +
                                    $"Delta count: {currentDeltaSnapRelease.Files.Count}. " +
                                    $"Full count: {currentFullSnapRelease.Files.Count}.");
            }

            var totalDeltaFiles = currentDeltaSnapRelease.New.Count +
                                  currentDeltaSnapRelease.Modified.Count;

            if (totalDeltaFiles != deltaNupkgPackageBuilder.Files.Count)
            {
                throw new Exception("Expected total delta files count to equal delta nupkg files count. " +
                                    $"Total delta files count: {totalDeltaFiles}. " +
                                    $"Total delta nupkg files count: {deltaNupkgPackageBuilder.Files.Count}.");
            }

            return deltaNupkgPackageBuilder;
        }

        public async Task<(MemoryStream outputStream, SnapApp fullSnapApp, SnapRelease fullSnapRelease)> RebuildPackageAsync(string packagesDirectory,
            ISnapAppReleases existingAppReleases,
            SnapRelease snapRelease, [NotNull] SnapChannel snapChannel, ILog logger = null, CancellationToken cancellationToken = default)
        {
            if (packagesDirectory == null) throw new ArgumentNullException(nameof(packagesDirectory));
            if (existingAppReleases == null) throw new ArgumentNullException(nameof(existingAppReleases));
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));

            var outputStream = new MemoryStream();

            var (packageBuilder, fullSnapApp, fullSnapRelease) =
                await RebuildFullPackageAsyncInternal(packagesDirectory, existingAppReleases, snapRelease, snapChannel, logger, cancellationToken);
            packageBuilder.Save(outputStream);
            outputStream.Seek(0, SeekOrigin.Begin);

            snapRelease.Sort();

            return (outputStream, fullSnapApp, fullSnapRelease);
        }

        async Task<(PackageBuilder packageBuilder, SnapApp fullSnapApp, SnapRelease fullSnapRelease)> RebuildFullPackageAsyncInternal(string packagesDirectory,
            ISnapAppReleases snapAppReleases, SnapRelease snapRelease, [NotNull] SnapChannel snapChannel, ILog logger = null,
            CancellationToken cancellationToken = default)
        {
            if (packagesDirectory == null) throw new ArgumentNullException(nameof(packagesDirectory));
            if (snapAppReleases == null) throw new ArgumentNullException(nameof(snapAppReleases));
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
            if (snapChannel == null) throw new ArgumentNullException(nameof(snapChannel));

            _snapFilesystem.DirectoryExistsThrowIfNotExists(packagesDirectory);
    
            if (!snapAppReleases.Any())
            {
                throw new ArgumentException("Cannot be empty", nameof(snapAppReleases));
            }

            var genisisSnapRelease = snapAppReleases.GetGenisisRelease(snapChannel);
            if (!genisisSnapRelease.IsGenisis)
            {
                throw new FileNotFoundException("First release must be genisis release.", snapRelease.Filename);
            }

            if (!genisisSnapRelease.IsFull || genisisSnapRelease.IsDelta)
            {
                throw new FileNotFoundException("Expected genisis to be a full release.", snapRelease.Filename);
            }

            var (packageBuilder, genisisSnapApp) =
                await BuildPackageFromReleaseAsync(packagesDirectory, snapAppReleases, genisisSnapRelease, logger, cancellationToken);

            var pathComparisonType = GetPathStringComparisonType(genisisSnapRelease);

            var deltasToApply = snapAppReleases.GetDeltaReleasesOlderThanOrEqualTo(snapChannel, snapRelease.Version).ToList();
            if (!deltasToApply.Any())
            {
                var genisisReleaseChecksum = _snapCryptoProvider.Sha512(snapRelease, packageBuilder);
                if (genisisReleaseChecksum != snapRelease.FullSha512Checksum)
                {
                    throw new SnapReleaseChecksumMismatchException(snapRelease);
                }

                return (packageBuilder, genisisSnapApp, snapRelease);
            }
            
            if (!snapRelease.IsDelta)
            {
                throw new Exception($"Unable to rebuild full nupkg because release is not of type delta: {snapRelease.Filename}");
            }

            var reassembledFullSnapRelease = SnapRelease.AsFull(genisisSnapRelease);                        
            var reassembledSnapApp = new SnapApp(genisisSnapApp)
            {
                IsGenisis = false
            };

            foreach (var deltaSnapRelease in deltasToApply)
            {
                await ApplyDeltaPackageAsync(deltaSnapRelease);                
            }
            
            reassembledFullSnapRelease.Sort();

            var reassembledNupkgSha512Checksum = _snapCryptoProvider.Sha512(reassembledFullSnapRelease, packageBuilder);
            if (reassembledNupkgSha512Checksum != snapRelease.FullSha512Checksum)
            {
                throw new SnapReleaseChecksumMismatchException(snapRelease);
            }
            
            return (packageBuilder, reassembledSnapApp, reassembledFullSnapRelease);

            async Task<SnapApp> ApplyDeltaPackageAsync(SnapRelease deltaRelease)
            {
                if (deltaRelease == null) throw new ArgumentNullException(nameof(deltaRelease));

                if (deltaRelease.IsGenisis)
                {
                    throw new Exception($"A delta cannot be the full release. Nupkg: {deltaRelease.Filename}");
                }

                if (deltaRelease.IsFull)
                {
                    throw new Exception($"A delta cannot be a full release. Nupkg: {deltaRelease.Filename}");
                }

                if (!deltaRelease.IsDelta)
                {
                    throw new Exception($"Expected to apply a delta release. Nupkg: {deltaRelease.Filename}");
                }

                var deltaAbsolutePath = _snapFilesystem.PathCombine(packagesDirectory, deltaRelease.Filename);
                _snapFilesystem.FileExistsThrowIfNotExists(deltaAbsolutePath);

                var deltaNupkgMemoryStream = _snapFilesystem.FileRead(deltaAbsolutePath);
                using (var packageArchiveReader = new PackageArchiveReader(deltaNupkgMemoryStream))
                {
                    var deltaSnapApp = await GetSnapAppAsync(packageArchiveReader, cancellationToken);
                    if (deltaSnapApp == null)
                    {
                        throw new FileNotFoundException($"Unable to extract snap app. Nupkg: {deltaRelease.Filename}", SnapConstants.SnapAppDllFilename);
                    }

                    if (!deltaSnapApp.IsDelta)
                    {
                        throw new Exception($"Expected to read a delta snap app. Nupkg: {deltaRelease.Filename}");
                    }

                    foreach (var checksum in deltaRelease.Deleted)
                    {
                        var existingFullChecksum = reassembledFullSnapRelease.Files.SingleOrDefault(x => x.NuspecTargetPath == checksum.NuspecTargetPath);
                        if (existingFullChecksum == null)
                        {
                            throw new FileNotFoundException(
                                $"Unable to remove 'Deleted' file from full release: {reassembledFullSnapRelease.Filename}. " +
                                $"Filename: {checksum.NuspecTargetPath}. " +
                                $"Nupkg: {checksum.Filename}.");
                        }

                        reassembledFullSnapRelease.Files.Remove(existingFullChecksum);
                    }

                    foreach (var deltaChecksum in deltaRelease.New)
                    {
                        var srcStream = await packageArchiveReader.GetStream(deltaChecksum.NuspecTargetPath).ReadToEndAsync(cancellationToken);
                        var sha512Checksum = _snapCryptoProvider.Sha512(srcStream);
                        if (deltaChecksum.FullSha512Checksum != sha512Checksum)
                        {
                            throw new SnapReleaseFileChecksumMismatchException(deltaChecksum, snapRelease);
                        }

                        var existingFullChecksum = reassembledFullSnapRelease.Files.SingleOrDefault(x => x.NuspecTargetPath == deltaChecksum.NuspecTargetPath);
                        if (existingFullChecksum != null)
                        {
                            throw new Exception(
                                $"Unable to add file to full release: {reassembledFullSnapRelease.Filename} because it already exists. " +
                                $"Filename: {deltaChecksum.NuspecTargetPath}. " +
                                $"Nupkg: {deltaChecksum.Filename}.");
                        }
                        
                        AddPackageFile(packageBuilder, srcStream, deltaChecksum.NuspecTargetPath, string.Empty, reassembledFullSnapRelease);
                    }

                    foreach (var deltaChecksum in deltaRelease.Modified)
                    {                       
                        var existingChecksum = reassembledFullSnapRelease.Files.SingleOrDefault(x => x.NuspecTargetPath == deltaChecksum.NuspecTargetPath);
                        if (existingChecksum == null)
                        {
                            throw new Exception(
                                $"Unable to modify file in full release: {reassembledFullSnapRelease.Filename} because it does not exists. " +
                                $"Filename: {deltaChecksum.NuspecTargetPath}. " +
                                $"Nupkg: {deltaChecksum.Filename}.");
                        }

                        reassembledFullSnapRelease.Files.Remove(existingChecksum);

                        var packageFile = packageBuilder.GetPackageFile(deltaChecksum.NuspecTargetPath, pathComparisonType);
                        var packageFileStream = packageFile.GetStream();
                        packageFileStream.Seek(0, SeekOrigin.Begin);

                        var neverGenerateBsDiffThisAssembly =
                            NeverGenerateBsDiffsTheseAssemblies.SingleOrDefault(x =>
                                string.Equals(x, deltaChecksum.NuspecTargetPath, pathComparisonType));

                        var outputStream = new MemoryStream((int) deltaChecksum.FullFilesize);
                        using (var patchStream = await packageArchiveReader.GetStream(deltaChecksum.NuspecTargetPath).ReadToEndAsync(cancellationToken))
                        {
                            string sha512Checksum;
                            if (neverGenerateBsDiffThisAssembly != null)
                            {
                                await patchStream.CopyToAsync(outputStream, cancellationToken);

                                sha512Checksum = _snapCryptoProvider.Sha512(outputStream);
                                if (deltaChecksum.FullSha512Checksum != sha512Checksum)
                                {
                                    throw new SnapReleaseFileChecksumDeltaMismatchException(deltaChecksum, snapRelease, patchStream.Length);
                                }

                                goto done;
                            }

                            sha512Checksum = _snapCryptoProvider.Sha512(patchStream);
                            if (deltaChecksum.DeltaSha512Checksum != sha512Checksum)
                            {
                                throw new SnapReleaseFileChecksumDeltaMismatchException(deltaChecksum, snapRelease, patchStream.Length);
                            }

                            MemoryStream OpenPatchStream()
                            {
                                var intermediatePatchStream = new MemoryStream();
                                // ReSharper disable once AccessToDisposedClosure
                                patchStream.Seek(0, SeekOrigin.Begin);
                                // ReSharper disable once AccessToDisposedClosure
                                patchStream.CopyTo(intermediatePatchStream);
                                intermediatePatchStream.Seek(0, SeekOrigin.Begin);
                                return intermediatePatchStream;
                            }

                            SnapBinaryPatcher.Apply(packageFileStream, OpenPatchStream, outputStream);

                            sha512Checksum = _snapCryptoProvider.Sha512(outputStream);
                            if (deltaChecksum.FullSha512Checksum != sha512Checksum)
                            {
                                throw new SnapReleaseFileChecksumMismatchException(deltaChecksum, snapRelease);
                            }

                            done:
                            if (!packageBuilder.Files.Remove(packageFile))
                            {
                                throw new FileNotFoundException($"Unable to replace file. Nupkg: {deltaRelease.Filename}", deltaChecksum.Filename);
                            }

                            AddPackageFile(packageBuilder, outputStream, deltaChecksum.NuspecTargetPath, string.Empty, reassembledFullSnapRelease);
                        }

                        packageBuilder.Populate(await packageArchiveReader.GetManifestMetadataAsync(cancellationToken));
                    }
                    
                    reassembledFullSnapRelease.Version = reassembledSnapApp.Version = packageBuilder.Version = deltaRelease.Version.ToNuGetVersion();
                    reassembledFullSnapRelease.Filename = deltaRelease.BuildNugetFullLocalFilename();
                    reassembledFullSnapRelease.FullSha512Checksum = deltaRelease.FullSha512Checksum;
                    reassembledFullSnapRelease.FullFilesize = deltaRelease.FullFilesize;
                    reassembledFullSnapRelease.DeltaSha512Checksum = deltaRelease.DeltaSha512Checksum;
                    reassembledFullSnapRelease.DeltaFilesize = deltaRelease.DeltaFilesize;

                    // Nuspec properties                    
                    reassembledFullSnapRelease.ReleaseNotes = deltaRelease.ReleaseNotes;
                    reassembledFullSnapRelease.CreatedDateUtc = deltaRelease.CreatedDateUtc;

                    return deltaSnapApp;
                }
            }
        }

        async Task<(PackageBuilder packageBuilder, SnapApp snapApp)> BuildPackageFromReleaseAsync([NotNull] string packagesDirectory,
            [NotNull] ISnapAppReleases appReleases, [NotNull] SnapRelease snapRelease, ILog logger = null, CancellationToken cancellationToken = default)
        {
            if (packagesDirectory == null) throw new ArgumentNullException(nameof(packagesDirectory));
            if (appReleases == null) throw new ArgumentNullException(nameof(appReleases));
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));

            var nupkgAbsolutePath = _snapFilesystem.PathCombine(packagesDirectory, snapRelease.Filename);
            _snapFilesystem.FileExistsThrowIfNotExists(nupkgAbsolutePath);

            using (var inputStream = _snapFilesystem.FileRead(nupkgAbsolutePath))
            using (var packageArchiveReader = new PackageArchiveReader(inputStream))
            {
                var snapApp = await GetSnapAppAsync(packageArchiveReader, cancellationToken);
                if (snapApp == null)
                {
                    throw new FileNotFoundException(SnapConstants.SnapAppDllFilename);
                }

                var packageBuilder = new PackageBuilder();
                packageBuilder.Populate(await packageArchiveReader.GetManifestMetadataAsync(cancellationToken));

                foreach (var checksum in snapRelease.Files)
                {
                    var srcStream = await packageArchiveReader.GetStreamAsync(checksum.NuspecTargetPath, cancellationToken).ReadToEndAsync(cancellationToken);

                    AddPackageFile(packageBuilder, srcStream, checksum.NuspecTargetPath, string.Empty);
                }

                var releaseChecksum = _snapCryptoProvider.Sha512(snapRelease, packageBuilder);
                if (releaseChecksum != snapRelease.FullSha512Checksum)
                {
                    throw new SnapReleaseChecksumMismatchException(snapRelease);
                }
                
                return (packageBuilder, snapApp);
            }
        }

        async Task AddPackageAssetsAsync([NotNull] ICoreRunLib coreRunLib, [NotNull] PackageBuilder packageBuilder,
            [NotNull] SnapApp snapApp, [NotNull] SnapRelease snapRelease, ILog logger = null, CancellationToken cancellationToken = default)
        {
            if (coreRunLib == null) throw new ArgumentNullException(nameof(coreRunLib));
            if (packageBuilder == null) throw new ArgumentNullException(nameof(packageBuilder));
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));

            // Icon (Windows has native platform support for icons)
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                && snapApp.Target.Icon != null)
            {
                var iconExt = _snapFilesystem.PathGetExtension(snapApp.Target.Icon);
                if (iconExt == null)
                {
                    throw new Exception($"Icon must have a valid extension: {snapApp.Target.Icon}.");
                }

                var iconMemoryStream = await _snapFilesystem.FileReadAsync(snapApp.Target.Icon, cancellationToken);

                snapApp.Target.Icon = $"{snapApp.Id}{iconExt}";

                AddPackageFile(packageBuilder, iconMemoryStream, SnapConstants.NuspecAssetsTargetPath, snapApp.Target.Icon, snapRelease);
            }

            // Snap.dll
            using (var snapDllAssemblyDefinition = await _snapFilesystem.FileReadAssemblyDefinitionAsync(typeof(SnapPack).Assembly.Location, cancellationToken))
            using (var snapDllAssemblyDefinitionOptimized =
                _snapAppWriter.OptimizeSnapDllForPackageArchive(snapDllAssemblyDefinition, snapApp.Target.Os))
            {
                var snapDllOptimizedMemoryStream = new MemoryStream();
                snapDllAssemblyDefinitionOptimized.Write(snapDllOptimizedMemoryStream);

                AddPackageFile(packageBuilder, snapDllOptimizedMemoryStream,
                    SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapDllFilename, snapRelease);
            }

            // Corerun
            var (coreRunStream, coreRunFilename, _) = _snapEmbeddedResources.GetCoreRunForSnapApp(snapApp, _snapFilesystem, coreRunLib);
            AddPackageFile(packageBuilder, coreRunStream, SnapConstants.NuspecAssetsTargetPath, coreRunFilename, snapRelease);

            // Snap.App.dll
            using (var snapAppDllAssembly = _snapAppWriter.BuildSnapAppAssembly(snapApp))
            {
                var snapAppMemoryStream = new MemoryStream();
                snapAppDllAssembly.Write(snapAppMemoryStream);

                AddPackageFile(packageBuilder, snapAppMemoryStream, SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapAppDllFilename, snapRelease);
            }
        }

        (MemoryStream nuspecStream, List<(string filename, string targetPath)> packgeFiles) GetNuspecStream([NotNull] ISnapPackageDetails packageDetails,
            MemoryStream nuspecStream, [NotNull] Func<string, string> propertyProvider, [NotNull] string baseDirectory, [NotNull] SnapRelease snapRelease,
            [NotNull] SnapApp snapApp)
        {
            if (packageDetails == null) throw new ArgumentNullException(nameof(packageDetails));
            if (nuspecStream == null) throw new ArgumentNullException(nameof(nuspecStream));
            if (propertyProvider == null) throw new ArgumentNullException(nameof(propertyProvider));
            if (baseDirectory == null) throw new ArgumentNullException(nameof(baseDirectory));
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));

            const string nuspecXmlNs = "http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd";

            var nugetVersion = new NuGetVersion(packageDetails.SnapApp.Version.ToFullString());
            var upstreamPackageId = packageDetails.SnapApp.BuildNugetUpstreamPackageId();
            var packageFiles = new List<(string filename, string targetPath)>();

            MemoryStream RewriteNuspecStreamWithEssentials()
            {
                var nuspecDocument = XmlUtility.LoadSafe(nuspecStream);
                if (nuspecDocument == null)
                {
                    throw new Exception("Failed to parse nuspec");
                }

                var metadata = nuspecDocument.SingleOrDefault(XName.Get("metadata", nuspecXmlNs));
                if (metadata == null)
                {
                    throw new Exception("The required element 'metadata' is missing from the nuspec");
                }

                var id = metadata.SingleOrDefault(XName.Get("id", nuspecXmlNs));
                if (id != null)
                {
                    id.Value = upstreamPackageId;
                }
                else
                {
                    metadata.Add(new XElement("id", upstreamPackageId));
                }

                var title = metadata.SingleOrDefault(XName.Get("title", nuspecXmlNs));
                if (title == null)
                {
                    throw new Exception("The required element 'description' is missing from the nuspec");
                }

                var version = metadata.SingleOrDefault(XName.Get("version", nuspecXmlNs));
                if (version == null)
                {
                    metadata.Add(new XElement("version", nugetVersion.ToFullString()));
                }
                else
                {
                    version.Value = nugetVersion.ToFullString();
                }

                var description = metadata.SingleOrDefault(XName.Get("description", nuspecXmlNs));
                if (description == null)
                {
                    metadata.Add(new XElement("description", title.Value));
                }

                var releaseNotes = metadata.SingleOrDefault(XName.Get("releasenotes", nuspecXmlNs));
                snapApp.ReleaseNotes = snapRelease.ReleaseNotes = releaseNotes?.Value;

                nuspecDocument.SingleOrDefault(XName.Get("files", nuspecXmlNs))?.Remove();

                var files = nuspecDocument.SingleOrDefault(XName.Get("files", nuspecXmlNs));
                var excludeAttribute = files?.Attribute("exclude");

                var defaultExcludePattern = new List<Glob>
                {
                    Glob.Parse("**/*.nuspec"),
                    Glob.Parse("**/*.pdb"),
                    Glob.Parse("**/*.dll.xml")
                };

                const char excludePatternDelimeter = ';';

                var excludePatterns = string.IsNullOrWhiteSpace(excludeAttribute?.Value) ? defaultExcludePattern :
                    excludeAttribute.Value.Contains(excludePatternDelimeter) ? excludeAttribute.Value.Split(excludePatternDelimeter)
                        .Where(x => !string.IsNullOrWhiteSpace(x)).Select(Glob.Parse).ToList() :
                    new List<Glob> {Glob.Parse(excludeAttribute.Value)};

                var allFiles = _snapFilesystem.DirectoryGetAllFilesRecursively(packageDetails.NuspecBaseDirectory).ToList();
                foreach (var fileAbsolutePath in allFiles)
                {
                    var relativePath = fileAbsolutePath.Replace(packageDetails.NuspecBaseDirectory, string.Empty).Substring(1);
                    var excludeFile = excludePatterns.Any(x => x.IsMatch(relativePath));
                    if (excludeFile)
                    {
                        continue;
                    }

                    packageFiles.Add((fileAbsolutePath, targetPath: _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, relativePath)));
                }

                var rewrittenNuspecStream = new MemoryStream();
                nuspecDocument.Save(rewrittenNuspecStream);
                rewrittenNuspecStream.Seek(0, SeekOrigin.Begin);

                return rewrittenNuspecStream;
            }

            using (var nuspecStreamRewritten = RewriteNuspecStreamWithEssentials())
            {
                var manifestMetadata = Manifest.ReadFrom(nuspecStreamRewritten, propertyProvider, true);
                
                var outputStream = new MemoryStream();
                manifestMetadata.Save(outputStream);
                outputStream.Seek(0, SeekOrigin.Begin);

                return (outputStream, packageFiles);
            }
        }

        public MemoryStream BuildReleasesPackage(SnapApp snapApp, SnapAppsReleases snapAppsReleases)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (snapAppsReleases == null) throw new ArgumentNullException(nameof(snapAppsReleases));

            var snapAppReleases = snapAppsReleases.GetReleases(snapApp);
            if (!snapAppReleases.Any())
            {
                throw new Exception("Cannot build an empty release package");
            }

            var genisisRelease = snapAppReleases.GetGenisisRelease(snapApp.GetDefaultChannelOrThrow());
            if (genisisRelease == null)
            {
                throw new Exception("Missing genisis release");
            }

            if (!genisisRelease.IsGenisis)
            {
                throw new Exception($"Genisis release is not a genisis release: {genisisRelease.Filename}");
            }

            if (!genisisRelease.IsFull || genisisRelease.IsDelta)
            {
                throw new Exception($"Genisis release must be full release: {genisisRelease.Filename}");
            }

            var packageBuilder = new PackageBuilder
            {
                Id = snapApp.BuildNugetReleasesUpstreamPackageId(),
                Version = snapAppsReleases.Version.ToNuGetVersion(),
                Description =
                    $"Snapx application database. This file contains release details for application: {snapApp.Id}. " +
                    $"Channels: {string.Join(", ", snapApp.Channels.Select(x => x.Name))}.",
                Authors = {"Snapx"}
            };

            foreach (var checksum in snapAppReleases)
            {
                if (checksum.IsGenisis)
                {
                    var expectedGenisisFilename = new SnapApp
                    {
                        Id = checksum.Id,
                        Version = checksum.Version,
                        Target = checksum.Target,
                        IsGenisis = true
                    }.BuildNugetFullLocalFilename();

                    if (checksum.Filename != expectedGenisisFilename)
                    {
                        throw new Exception($"Invalid genisis filename: {checksum.Filename}. Expected: {expectedGenisisFilename}");
                    }
                }
                else if (checksum.IsFull)
                {
                    var expectedFullFilename = new SnapApp
                    {
                        Id = checksum.Id,
                        Version = checksum.Version,
                        Target = checksum.Target,
                        IsFull = true
                    }.BuildNugetFullLocalFilename();

                    if (checksum.Filename != expectedFullFilename)
                    {
                        throw new Exception($"Invalid full filename: {checksum.Filename}. Expected: {expectedFullFilename}");
                    }
                }
                else if (checksum.IsDelta)
                {
                    var expectedDeltaFilename = new SnapApp
                    {
                        Id = checksum.Id,
                        Version = checksum.Version,
                        Target = checksum.Target
                    }.BuildNugetDeltaLocalFilename();

                    if (checksum.Filename != expectedDeltaFilename)
                    {
                        throw new Exception($"Invalid delta filename: {checksum.Filename}. Expected: {expectedDeltaFilename}");
                    }
                }
                else
                {
                    throw new NotSupportedException($"Expected either delta or genisis release. Filename: {checksum.Filename}");
                }

                if (checksum.FullFilesize <= 0)
                {
                    throw new Exception($"Invalid file size: {checksum.FullSha512Checksum}. Must be greater than zero! Filename: {checksum.Filename}");
                }

                if (checksum.FullSha512Checksum == null || checksum.FullSha512Checksum.Length != 128)
                {
                    throw new Exception($"Invalid checksum: {checksum.FullSha512Checksum}. Filename: {checksum.Filename}");
                }

                var expectedUpstreamId = new SnapApp
                {
                    Id = checksum.Id,
                    Version = checksum.Version,
                    Target = checksum.Target
                }.BuildNugetUpstreamPackageId();

                if (checksum.UpstreamId != expectedUpstreamId)
                {
                    throw new Exception($"Invalid upstream id: {checksum.UpstreamId}. Expected: {expectedUpstreamId}");
                }
            }

            var yamlString = _snapAppWriter.ToSnapReleasesYamlString(snapAppsReleases);

            using (var snapsReleasesStream = new MemoryStream(Encoding.UTF8.GetBytes(yamlString)))
            {
                AddPackageFile(packageBuilder, snapsReleasesStream, SnapConstants.NuspecRootTargetPath, SnapConstants.ReleasesFilename);

                var outputStream = new MemoryStream();
                packageBuilder.Save(outputStream);

                outputStream.Seek(0, SeekOrigin.Begin);
                return outputStream;
            }
        }

        public async Task<SnapApp> GetSnapAppAsync(IAsyncPackageCoreReader asyncPackageCoreReader, CancellationToken cancellationToken = default)
        {
            if (asyncPackageCoreReader == null) throw new ArgumentNullException(nameof(asyncPackageCoreReader));
            using (var assemblyStream = await GetSnapAssetAsync(asyncPackageCoreReader, SnapConstants.SnapAppDllFilename, cancellationToken))
            using (var assemblyDefinition = AssemblyDefinition.ReadAssembly(assemblyStream, new ReaderParameters(ReadingMode.Immediate)))
            {
                var snapApp = assemblyDefinition.GetSnapApp(_snapAppReader);
                return snapApp;
            }
        }

        public async Task<MemoryStream> GetSnapAssetAsync(IAsyncPackageCoreReader asyncPackageCoreReader, string filename,
            CancellationToken cancellationToken = default)
        {
            if (asyncPackageCoreReader == null) throw new ArgumentNullException(nameof(asyncPackageCoreReader));
            if (filename == null) throw new ArgumentNullException(nameof(filename));

            var targetPath = _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, filename);

            return await asyncPackageCoreReader.GetStreamAsync(targetPath, cancellationToken).ReadToEndAsync(cancellationToken);
        }

        void ThrowIfUnspportedTargetOsPlatform(OSPlatform targetOs)
        {
            if (targetOs == OSPlatform.Windows)
            {
                using (var coreRun = _snapEmbeddedResources.CoreRunWindows)
                {
                    if (coreRun.Length <= 0)
                    {
                        throw new FileNotFoundException($"corerun.exe is missing in Snap assembly. Target os: {OSPlatform.Windows}");
                    }

                    return;
                }
            }

            if (targetOs == OSPlatform.Linux)
            {
                using (var coreRun = _snapEmbeddedResources.CoreRunLinux)
                {
                    if (coreRun.Length <= 0)
                    {
                        throw new FileNotFoundException($"corerun is missing in Snap assembly. Target os: {OSPlatform.Linux}");
                    }
                }

                return;
            }

            throw new PlatformNotSupportedException();
        }

        void AddPackageFile([NotNull] PackageBuilder packageBuilder, [NotNull] Stream srcStream,
            [NotNull] string targetPath, [NotNull] string filename, SnapRelease snapRelease = null)
        {
            if (packageBuilder == null) throw new ArgumentNullException(nameof(packageBuilder));
            if (srcStream == null) throw new ArgumentNullException(nameof(srcStream));
            if (targetPath == null) throw new ArgumentNullException(nameof(targetPath));
            if (filename == null) throw new ArgumentNullException(nameof(filename));

            if (!targetPath.StartsWith(SnapConstants.NuspecRootTargetPath))
            {
                throw new Exception($"Invalid {nameof(targetPath)}: {targetPath}. Must begin with: {SnapConstants.NuspecRootTargetPath}");
            }

            var nuGetFramework = NuGetFramework.Parse(SnapConstants.NuspecTargetFrameworkMoniker);

            targetPath = targetPath.ForwardSlashesSafe();

            if (filename == string.Empty)
            {
                var lastSlashIndex = targetPath.LastIndexOf("/", StringComparison.InvariantCulture);
                if (lastSlashIndex == -1)
                {
                    throw new Exception($"Expected target path to contain filename: {targetPath}");
                }

                filename = targetPath.Substring(lastSlashIndex + 1);
            }
            else
            {
                targetPath = _snapFilesystem.PathCombine(targetPath, filename).ForwardSlashesSafe();
            }

            var checksum = new SnapReleaseChecksum
            {
                NuspecTargetPath = targetPath,
                Filename = filename,
                FullFilesize = srcStream.Length,
                FullSha512Checksum = _snapCryptoProvider.Sha512(srcStream)
            };

            snapRelease?.Files.Add(checksum);
            packageBuilder.Files.Add(new InMemoryPackageFile(srcStream, nuGetFramework, checksum.NuspecTargetPath, checksum.Filename));
        }

        void Validate([NotNull] ISnapPackageDetails packageDetails)
        {
            if (packageDetails == null) throw new ArgumentNullException(nameof(packageDetails));

            if (packageDetails.SnapApp == null)
            {
                throw new Exception("Snap app cannot be null");
            }

            if (packageDetails.SnapApp.Version == null)
            {
                throw new Exception("Snap app version cannot be null");
            }

            if (!packageDetails.SnapApp.IsValidAppId())
            {
                throw new Exception($"Snap id is invalid: {packageDetails.SnapApp.Id}");
            }

            if (!packageDetails.SnapApp.IsValidChannelName())
            {
                throw new Exception($"Invalid channel name: {packageDetails.SnapApp.GetCurrentChannelOrThrow().Name}. Snap id: {packageDetails.SnapApp.Id}");
            }

            if (packageDetails.SnapAppsReleases == null)
            {
                throw new ArgumentNullException(nameof(packageDetails.SnapAppsReleases));
            }

            if (packageDetails.NuspecFilename == null)
            {
                throw new ArgumentNullException(nameof(packageDetails.NuspecFilename));
            }

            if (packageDetails.NuspecBaseDirectory == null)
            {
                throw new ArgumentNullException(nameof(packageDetails.NuspecBaseDirectory));
            }

            if (packageDetails.NuspecProperties == null)
            {
                throw new ArgumentNullException(nameof(packageDetails.NuspecProperties));
            }

            if (packageDetails.PackagesDirectory == null)
            {
                throw new ArgumentNullException(nameof(packageDetails.PackagesDirectory));
            }

            _snapFilesystem.FileExistsThrowIfNotExists(packageDetails.NuspecFilename);
            _snapFilesystem.DirectoryExistsThrowIfNotExists(packageDetails.NuspecBaseDirectory);
            _snapFilesystem.DirectoryExistsThrowIfNotExists(packageDetails.PackagesDirectory);
            
            ThrowIfUnspportedTargetOsPlatform(packageDetails.SnapApp.Target.Os);
        }

        (Dictionary<string, string> properties, Func<string, string> propertiesResolverFunc) BuildNuspecProperties([NotNull] ISnapPackageDetails packageDetails)
        {
            if (packageDetails == null) throw new ArgumentNullException(nameof(packageDetails));

            var nuspecProperties = new Dictionary<string, string>();

            if (packageDetails.NuspecProperties != null)
            {
                foreach (var (key, value) in packageDetails.NuspecProperties)
                {
                    if (!nuspecProperties.ContainsKey(key.ToLowerInvariant()))
                    {
                        nuspecProperties.Add(key, value);
                    }
                }
            }

            string NuspecPropertyProvider(string propertyName)
            {
                return nuspecProperties.TryGetValue(propertyName, out var value) ? value : null;
            }

            return (nuspecProperties, NuspecPropertyProvider);
        }
        
        static StringComparison GetPathStringComparisonType([NotNull] SnapRelease snapRelease)
        {
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
            return snapRelease.Target.Os == OSPlatform.Windows ? 
                StringComparison.InvariantCultureIgnoreCase : StringComparison.InvariantCulture;
        }
    }
}
