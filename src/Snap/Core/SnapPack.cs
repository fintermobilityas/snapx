using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
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
using SharpCompress.Common;
using SharpCompress.Writers;
using Snap.Core.Models;
using Snap.Core.Resources;
using Snap.Extensions;
using Snap.NuGet;
using Snap.Reflection;

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

    internal interface ISnapNuspecDetails
    {
        string NuspecFilename { get; }
        string NuspecBaseDirectory { get; }
        IReadOnlyDictionary<string, string> NuspecProperties { get; }
    }

    internal interface ISnapPackageDetails : ISnapNuspecDetails
    {
        SnapAppsReleases SnapAppsReleases { get; }
        SnapApp SnapApp { get; }
        string PackagesDirectory { get; }
    }

    internal sealed class SnapPackageDetails : ISnapPackageDetails
    {
        public SnapAppsReleases SnapAppsReleases { get; set; }
        public SnapApp SnapApp { get; set; }
        public string NuspecFilename { get; set; }
        public string NuspecBaseDirectory { get; set; }
        public IReadOnlyDictionary<string, string> NuspecProperties { get; [UsedImplicitly] set; }
        public string PackagesDirectory { get; set; }

        public SnapPackageDetails()
        {
            NuspecProperties = new Dictionary<string, string>();
        }
    }

    internal interface IRebuildPackageProgressSource
    {
        Action<(int progressPercentage, long filesRestored, long filesToRestore)> Progress { get; set; }
        void Raise(int progressPercentage, long filesRestored, long filesToRestore);
    }

    internal sealed class RebuildPackageProgressSource : IRebuildPackageProgressSource
    {
        public Action<(int progressPercentage, long filesRestored, long filesToRestore)> Progress { get; set; }
        public void Raise(int progressPercentage, long filesRestored, long filesToRestore)
        {
            Progress?.Invoke((progressPercentage, filesRestored, filesToRestore));
        }
    } 

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal interface ISnapPack
    {
        IReadOnlyCollection<string> AlwaysRemoveTheseAssemblies { get; }
        IReadOnlyCollection<string> NeverGenerateBsDiffsTheseAssemblies { get; }

        Task<(MemoryStream fullNupkgMemoryStream, SnapApp fullSnapApp, SnapRelease fullSnapRelease, MemoryStream deltaNupkgMemoryStream, SnapApp deltaSnapApp, SnapRelease deltaSnapRelease)> 
            BuildPackageAsync([NotNull] ISnapPackageDetails packageDetails, [NotNull] ICoreRunLib coreRunLib, CancellationToken cancellationToken = default);
        Task<(MemoryStream outputStream, SnapApp fullSnapApp, SnapRelease fullSnapRelease)> RebuildPackageAsync([NotNull] string packagesDirectory,
            [NotNull] ISnapAppChannelReleases snapAppChannelReleases, [NotNull] SnapRelease snapRelease, 
            IRebuildPackageProgressSource rebuildPackageProgressSource = null, CancellationToken cancellationToken = default);
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
        readonly SemanticVersion _snapDllVersion;

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
            
            var informationalVersion = typeof(Snapx).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion;
            _snapDllVersion = !NuGetVersion.TryParse(informationalVersion, out var currentVersion) ? null : currentVersion;
        }

        public async Task<(MemoryStream fullNupkgMemoryStream, SnapApp fullSnapApp, SnapRelease fullSnapRelease, MemoryStream deltaNupkgMemoryStream, SnapApp deltaSnapApp, SnapRelease deltaSnapRelease)> 
            BuildPackageAsync(ISnapPackageDetails packageDetails, ICoreRunLib coreRunLib, CancellationToken cancellationToken = default)
        {
            if (packageDetails == null) throw new ArgumentNullException(nameof(packageDetails));
            if (coreRunLib == null) throw new ArgumentNullException(nameof(coreRunLib));

            var (fullNupkgPackageBuilder, fullSnapApp, fullSnapRelease, deltaNupkgPackageBuilder, deltaSnapApp, deltaSnapRelease) =
                await BuildPackageAsyncInternal(packageDetails, coreRunLib, cancellationToken);

            fullSnapRelease.Sort();            
            
            var fullNupkgMemoryStream = new MemoryStream();
            
            fullSnapRelease.FullSha512Checksum = _snapCryptoProvider.Sha512(fullSnapRelease, fullNupkgPackageBuilder);

            fullNupkgPackageBuilder.Save(fullNupkgMemoryStream);
            fullNupkgMemoryStream.Seek(0, SeekOrigin.Begin);
            
            fullSnapRelease.FullFilesize = fullNupkgMemoryStream.Length;

            if (fullSnapRelease.IsGenesis)
            {
                packageDetails.SnapAppsReleases.Add(fullSnapRelease);
                return (fullNupkgMemoryStream, fullSnapApp, fullSnapRelease, null, null, null);
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

            deltaNupkgMemoryStream.Seek(0, SeekOrigin.Begin);

            return (fullNupkgMemoryStream, fullSnapApp, fullSnapRelease, deltaNupkgMemoryStream, deltaSnapApp, deltaSnapRelease);
        }

        async Task<(PackageBuilder fullNupkgPackageBuilder, SnapApp fullSnapApp, SnapRelease fullSnapRelease, PackageBuilder deltaNupkgBuilder, SnapApp deltaSnapApp, SnapRelease deltaSnapRelease)>
            BuildPackageAsyncInternal(
                [NotNull] ISnapPackageDetails packageDetails, 
                [NotNull] ICoreRunLib coreRunLib,
                CancellationToken cancellationToken = default)
        {
            if (packageDetails == null) throw new ArgumentNullException(nameof(packageDetails));
            if (coreRunLib == null) throw new ArgumentNullException(nameof(coreRunLib));

            Validate(packageDetails);

            var packagesDirectory = packageDetails.PackagesDirectory;
            var snapAppsReleases = packageDetails.SnapAppsReleases;
            var snapAppChannelReleases = snapAppsReleases.GetReleases(packageDetails.SnapApp, 
                packageDetails.SnapApp.GetDefaultChannelOrThrow());

            var snapAppMetadataOnly = new SnapApp(packageDetails.SnapApp)
            {
                IsFull = false,
                IsGenesis = false,
                ReleaseNotes = null
            };

            var snapReleaseMetadataOnly = new SnapRelease
            {
                Id = snapAppMetadataOnly.Id,
                Version = snapAppMetadataOnly.Version,
                Channels = new List<string> { snapAppChannelReleases.Channel.Name },
                Target = new SnapTarget(snapAppMetadataOnly.Target)
            };            

            if (!snapAppChannelReleases.Any())
            {           
                var (genesisPackageBuilder, nuspecMemoryStream, _, genesisSnapApp, genesisSnapRelease) = 
                    await BuildFullPackageAsyncInternal(packageDetails, snapAppChannelReleases, snapAppMetadataOnly, 
                        snapReleaseMetadataOnly, coreRunLib, cancellationToken);

                genesisSnapRelease.Channels = genesisSnapApp.Channels.Select(x => x.Name).ToList();

                using (nuspecMemoryStream)
                {
                    return (genesisPackageBuilder, genesisSnapApp, genesisSnapRelease, null, null, null);                    
                }
            }
            
            var previousSnapRelease = snapAppChannelReleases.GetMostRecentRelease();

            var (previousNupkgPackageBuilder, _, _) = await RebuildFullPackageAsyncInternal(
                packagesDirectory, snapAppChannelReleases, previousSnapRelease, cancellationToken: cancellationToken);

            var (currentFullNupkgPackageBuilder, currentNuspecMemoryStream, currentNuspecPropertiesResolver, 
                currentFullSnapApp, currentFullSnapRelease) = await BuildFullPackageAsyncInternal(
                    packageDetails, snapAppChannelReleases, snapAppMetadataOnly, snapReleaseMetadataOnly, coreRunLib, cancellationToken);

            var deltaSnapApp = currentFullSnapApp.AsDeltaSnapApp();
            var deltaSnapRelease = currentFullSnapRelease.AsDeltaRelease();

            using (currentNuspecMemoryStream)
            {
                var deltaNupkgBuilder = await BuildDeltaPackageAsyncInternal(
                    packagesDirectory,
                    packageDetails,
                    previousSnapRelease,
                    currentFullSnapRelease,
                    deltaSnapApp,
                    deltaSnapRelease,
                    previousNupkgPackageBuilder,
                    currentFullNupkgPackageBuilder, 
                    currentNuspecMemoryStream,
                    currentNuspecPropertiesResolver, 
                    cancellationToken
                );
 
                return (currentFullNupkgPackageBuilder, currentFullSnapApp, currentFullSnapRelease, deltaNupkgBuilder, deltaSnapApp, deltaSnapRelease);                
            }
        }

        async Task<(PackageBuilder packageBuilder, MemoryStream nuspecStream, Func<string, string> nuspecPropertiesResolver, SnapApp fullSnapApp, SnapRelease fullSnapRelease)> 
            BuildFullPackageAsyncInternal([NotNull] ISnapNuspecDetails snapNuspecDetails, [NotNull] ISnapAppChannelReleases snapAppChannelReleases, SnapApp snapAppMetadataOnly,
                [NotNull] SnapRelease snapReleaseMetadataOnly, [NotNull] ICoreRunLib coreRunLib, CancellationToken cancellationToken = default)
        {
            if (snapNuspecDetails == null) throw new ArgumentNullException(nameof(snapNuspecDetails));
            if (snapAppChannelReleases == null) throw new ArgumentNullException(nameof(snapAppChannelReleases));
            if (snapReleaseMetadataOnly == null) throw new ArgumentNullException(nameof(snapReleaseMetadataOnly));
            if (coreRunLib == null) throw new ArgumentNullException(nameof(coreRunLib));

            var isGenesis = !snapAppChannelReleases.Any();

            var fullSnapApp = snapAppMetadataOnly.AsFullSnapApp(isGenesis);
            var fullSnapRelease = snapReleaseMetadataOnly.AsFullRelease(isGenesis);

            var alwaysRemoveTheseAssemblies = AlwaysRemoveTheseAssemblies.ToList();
            alwaysRemoveTheseAssemblies.Add(_snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(fullSnapApp));

            var (_, nuspecPropertiesResolver) = BuildNuspecProperties(snapNuspecDetails.NuspecProperties);

            var nuspecIntermediateStream = await _snapFilesystem
                .FileReadAsync(snapNuspecDetails.NuspecFilename, cancellationToken);

            var (nuspecStream, packageFiles) = BuildNuspec(nuspecIntermediateStream, nuspecPropertiesResolver,
                snapNuspecDetails.NuspecBaseDirectory, fullSnapApp, fullSnapRelease);

            var nuspecStreamCpy = new MemoryStream(nuspecStream.ToArray()); // PackageBuilder closes stream

            using (nuspecStream)
            using (nuspecIntermediateStream)
            {
                var packageBuilder = new PackageBuilder(nuspecStream, snapNuspecDetails.NuspecBaseDirectory, nuspecPropertiesResolver);
                packageBuilder.Files.Clear(); // NB! We are _NOT_ loading files twice into memory.    
                
                foreach (var (filename, targetPath) in packageFiles)
                {
                    var srcStream = await _snapFilesystem.FileRead(filename).ReadToEndAsync(cancellationToken);
                    AddPackageFile(packageBuilder, srcStream, targetPath, string.Empty, fullSnapRelease);
                }

                var mainExecutableFileName = _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(fullSnapApp);
                var mainExecutableTargetPath = _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, mainExecutableFileName).ForwardSlashesSafe();
                var mainExecutablePackageFile = packageBuilder.GetPackageFile(mainExecutableTargetPath, StringComparison.OrdinalIgnoreCase);
                if (mainExecutablePackageFile == null)
                {
                    throw new FileNotFoundException("Main executable is missing in nuspec", mainExecutableTargetPath);
                }

                if (fullSnapApp.Target.Icon != null
                    && fullSnapApp.Target.Os == OSPlatform.Windows
                    && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    using (var tmpDir = _snapFilesystem.WithDisposableTempDirectory())
                    using (var mainExecutableStream = await mainExecutablePackageFile.GetStream().ReadToEndAsync(cancellationToken))
                    {
                        var mainExecutableTempFilename = _snapFilesystem.PathCombine(tmpDir.WorkingDirectory, mainExecutableFileName);
                        using (var mainExecutableTmpStream = _snapFilesystem.FileWrite(mainExecutableTempFilename))
                        {
                            await mainExecutableStream.CopyToAsync(mainExecutableTmpStream, cancellationToken);                                        
                        }

                        if (!coreRunLib.SetIcon(mainExecutableTempFilename, fullSnapApp.Target.Icon))
                        {
                            throw new Exception($"Failed to update icon for executable {mainExecutableTempFilename}. Icon: {fullSnapApp.Target.Icon}.");
                        }

                        var mainExecutableByteArray = await _snapFilesystem.FileReadAllBytesAsync(mainExecutableTempFilename, cancellationToken);
                        var mainExecutableStreamWithIcon = new MemoryStream(mainExecutableByteArray);
                       
                        AddPackageFile(packageBuilder, mainExecutableStreamWithIcon, 
                            mainExecutablePackageFile.EffectivePath, string.Empty, fullSnapRelease, true);
                    }
                }

                AlwaysRemoveTheseAssemblies.ForEach(targetPath =>
                {
                    var packageFile = packageBuilder.GetPackageFile(targetPath, StringComparison.OrdinalIgnoreCase);
                    if (packageFile == null)
                    {
                        return;
                    }

                    if (!packageBuilder.Files.Remove(packageFile))
                    {
                        throw new Exception($"Failed to remove {targetPath} from {nameof(packageBuilder)}.");      
                    }

                    var removeThisAssembly = fullSnapRelease.Files.SingleOrDefault(x => x.NuspecTargetPath == targetPath);
                    if (removeThisAssembly == null)
                    {
                        throw new Exception($"Failed to remove {targetPath} from {nameof(fullSnapRelease)}.");                        
                    }

                    fullSnapRelease.Files.Remove(removeThisAssembly);

                    if (!targetPath.EndsWith(SnapConstants.SnapDllFilename)) return;
                    
                    using (var snapAssemblyDefinition = AssemblyDefinition.ReadAssembly(packageFile.GetStream()))
                    {
                        var cecil = new CecilAssemblyReflector(snapAssemblyDefinition);
                        var snapAssemblyInformationalVersionAttribute = cecil
                            .GetAttribute<AssemblyInformationalVersionAttribute>();

                        if (snapAssemblyInformationalVersionAttribute == null)
                        {
                            throw new Exception($"Failed to get assembly version from {targetPath}.");
                        }

                        var snapAssemblyInformationVersionValue = snapAssemblyInformationalVersionAttribute.Values.First().Value;
                        
                        if (!NuGetVersion.TryParse(snapAssemblyInformationVersionValue, out var snapAssemblyVersion))
                        {
                            throw new Exception($"Failed to parse assembly version: {snapAssemblyInformationVersionValue}. Target path: {targetPath}");
                        }

                        if (snapAssemblyVersion != _snapDllVersion)
                        {
                            throw new Exception(
                                $"Invalid {SnapConstants.SnapDllFilename} version. " +
                                $"Expected: {Snapx.Version} but was {snapAssemblyInformationVersionValue}. " +
                                "You must either upgrade snapx dotnet cli tool or the Snapx.Core nuget package in your csproj.");
                        }
                    }
                });

                await AddSnapAssetsAsync(snapNuspecDetails, coreRunLib, packageBuilder, fullSnapApp, fullSnapRelease, cancellationToken);

                return (packageBuilder, nuspecStreamCpy, nuspecPropertiesResolver, fullSnapApp, fullSnapRelease);
            }
        }

        async Task<PackageBuilder> BuildDeltaPackageAsyncInternal([NotNull] string packagesDirectory,
            [NotNull] ISnapNuspecDetails snapNuspecDetails,
            [NotNull] SnapRelease previousFullSnapRelease,
            [NotNull] SnapRelease currentFullSnapRelease,
            [NotNull] SnapApp currentDeltaSnapApp,
            [NotNull] SnapRelease currentDeltaSnapRelease,
            [NotNull] PackageBuilder previousFullNupkgPackageBuilder,
            [NotNull] PackageBuilder currentFullNupkgPackageBuilder,
            [NotNull] MemoryStream currentFullNupkgNuspecMemoryStream,
            [NotNull] Func<string, string> currentFullNupkgNuspecPropertiesResolverFn, CancellationToken cancellationToken = default)
        {
            if (packagesDirectory == null) throw new ArgumentNullException(nameof(packagesDirectory));
            if (snapNuspecDetails == null) throw new ArgumentNullException(nameof(snapNuspecDetails));
            if (previousFullSnapRelease == null) throw new ArgumentNullException(nameof(previousFullSnapRelease));
            if (currentDeltaSnapApp == null) throw new ArgumentNullException(nameof(currentDeltaSnapApp));
            if (previousFullNupkgPackageBuilder == null) throw new ArgumentNullException(nameof(previousFullNupkgPackageBuilder));
            if (currentFullNupkgPackageBuilder == null) throw new ArgumentNullException(nameof(currentFullNupkgPackageBuilder));
            if (currentFullNupkgNuspecMemoryStream == null) throw new ArgumentNullException(nameof(currentFullNupkgNuspecMemoryStream));
            if (currentFullNupkgNuspecPropertiesResolverFn == null) throw new ArgumentNullException(nameof(currentFullNupkgNuspecPropertiesResolverFn));

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
                        string.Equals(x, packageFile.EffectivePath, StringComparison.OrdinalIgnoreCase));
                return targetPath == null;
            }
            
            currentFullNupkgNuspecMemoryStream.Seek(0, SeekOrigin.Begin);

            var deltaNupkgPackageBuilder = new PackageBuilder(
                currentFullNupkgNuspecMemoryStream, 
                snapNuspecDetails.NuspecBaseDirectory,
                currentFullNupkgNuspecPropertiesResolverFn)
            {
                Id = currentDeltaSnapRelease.UpstreamId,
                Version = currentDeltaSnapRelease.Version.ToNuGetVersion()
            };
            
            deltaNupkgPackageBuilder.Files.Clear(); // NB! We are _NOT_ loading files twice into memory.

            var deletedChecksums = previousFullSnapRelease.Files.ToList();
            
            foreach (var currentChecksum in currentFullSnapRelease.Files)
            {
                var currentPackageFile = currentFullNupkgPackageBuilder.GetPackageFile(currentChecksum.NuspecTargetPath, StringComparison.OrdinalIgnoreCase);    
                var previousChecksum =
                    previousFullSnapRelease.Files.SingleOrDefault(x => string.Equals(x.NuspecTargetPath, currentChecksum.NuspecTargetPath, StringComparison.OrdinalIgnoreCase));
                    
                if (previousChecksum == null)
                {
                    currentDeltaSnapRelease.New.Add(currentChecksum);
                    var srcStream = await currentPackageFile.GetStream().ReadToEndAsync(cancellationToken);
                    AddPackageFile(deltaNupkgPackageBuilder, srcStream, currentChecksum.NuspecTargetPath, string.Empty);
                    continue;
                }
                
                if (string.Equals(previousChecksum.FullSha512Checksum, currentChecksum.FullSha512Checksum, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(previousChecksum.DeltaSha512Checksum, currentChecksum.DeltaSha512Checksum, StringComparison.OrdinalIgnoreCase))
                {
                    currentDeltaSnapRelease.Unmodified.Add(currentChecksum.NuspecTargetPath);
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

                var previousPackageFile = previousFullNupkgPackageBuilder.GetPackageFile(currentChecksum.NuspecTargetPath, StringComparison.OrdinalIgnoreCase);
                
                using (var oldDataStream = await previousPackageFile.GetStream().ReadToEndAsync(cancellationToken))
                using (var newDataStream = await currentPackageFile.GetStream().ReadToEndAsync(cancellationToken))
                {
                    var patchStream = new MemoryStream();

                    if (newDataStream.Length > 0
                        && oldDataStream.Length > 0)
                    {
                        SnapBinaryPatcher.Create(oldDataStream.ToArray(), newDataStream.ToArray(), patchStream);
                    } else if (newDataStream.Length > 0)
                    {
                        await newDataStream.CopyToAsync(patchStream, cancellationToken);
                    }
            
                    currentChecksum.DeltaSha512Checksum = _snapCryptoProvider.Sha512(patchStream);
                    currentChecksum.DeltaFilesize = patchStream.Length;

                    if (currentChecksum.DeltaFilesize == 0 
                        && currentChecksum.DeltaSha512Checksum != SnapConstants.Sha512EmptyFileChecksum)
                    {
                        throw new Exception($"Expected empty file checksum to equal {SnapConstants.Sha512EmptyFileChecksum}. Target path: {currentChecksum.NuspecTargetPath}.");
                    }

                    AddPackageFile(deltaNupkgPackageBuilder, patchStream, currentChecksum.NuspecTargetPath, string.Empty);
                }
            }

            foreach (var deletedChecksum in deletedChecksums)
            {
                currentDeltaSnapRelease.Deleted.Add(deletedChecksum.NuspecTargetPath);
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
            ISnapAppChannelReleases snapAppChannelReleases, SnapRelease snapRelease, IRebuildPackageProgressSource rebuildPackageProgressSource = null, CancellationToken cancellationToken = default)
        {
            if (packagesDirectory == null) throw new ArgumentNullException(nameof(packagesDirectory));
            if (snapAppChannelReleases == null) throw new ArgumentNullException(nameof(snapAppChannelReleases));
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));

            var outputStream = new MemoryStream();

            var (packageBuilder, fullSnapApp, fullSnapRelease) =
                await RebuildFullPackageAsyncInternal(packagesDirectory, snapAppChannelReleases, snapRelease, rebuildPackageProgressSource, cancellationToken);
            packageBuilder.Save(outputStream);
            outputStream.Seek(0, SeekOrigin.Begin);

            snapRelease.Sort();

            return (outputStream, fullSnapApp, fullSnapRelease);
        }

        async Task<(PackageBuilder packageBuilder, SnapApp fullSnapApp, SnapRelease fullSnapRelease)> RebuildFullPackageAsyncInternal(string packagesDirectory,
            ISnapAppChannelReleases snapAppChannelReleases, SnapRelease snapRelease,
            IRebuildPackageProgressSource rebuildPackageProgressSource = null,
            CancellationToken cancellationToken = default)
        {
            if (packagesDirectory == null) throw new ArgumentNullException(nameof(packagesDirectory));
            if (snapAppChannelReleases == null) throw new ArgumentNullException(nameof(snapAppChannelReleases));
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));

            _snapFilesystem.DirectoryExistsThrowIfNotExists(packagesDirectory);
    
            if (!snapAppChannelReleases.Any())
            {
                throw new ArgumentException("Cannot be empty", nameof(snapAppChannelReleases));
            }

            var genesisSnapRelease = snapAppChannelReleases.GetGenesisRelease();
            if (!genesisSnapRelease.IsGenesis)
            {
                throw new FileNotFoundException("First release must be genesis release.", snapRelease.Filename);
            }

            if (!genesisSnapRelease.IsFull || genesisSnapRelease.IsDelta)
            {
                throw new FileNotFoundException("Expected genesis to be a full release.", snapRelease.Filename);
            }

            var deltaSnapReleasesToApply = snapAppChannelReleases.GetDeltaReleasesOlderThanOrEqualTo(snapRelease.Version).ToList();

            var compoundProgressSource = new RebuildPackageProgressSource();
            var totalFilesToRestore = genesisSnapRelease.Files.Count + 
                                      deltaSnapReleasesToApply
                                          .Sum(x => x.New.Count + x.Modified.Count);
            var totalFilesRestored = 0L;
            var totalProgressPercentage = 0;

            void UpdateRebuildProgress()
            {
                totalProgressPercentage = (int) Math.Floor((double) ++totalFilesRestored / totalFilesToRestore * 100);
                rebuildPackageProgressSource?.Raise(totalProgressPercentage, totalFilesRestored, totalFilesToRestore);
            }

            compoundProgressSource.Progress += tuple =>
            {
                if(tuple.filesRestored == 0) return;
                UpdateRebuildProgress();
            };

            rebuildPackageProgressSource?.Raise(0, 0, totalFilesToRestore);
 
            var (packageBuilder, genesisSnapApp) =
                await BuildPackageFromReleaseAsync(packagesDirectory, snapAppChannelReleases, genesisSnapRelease, compoundProgressSource, cancellationToken);

            if (!deltaSnapReleasesToApply.Any())
            {
                return (packageBuilder, genesisSnapApp, snapRelease);
            }
            
            if (!snapRelease.IsDelta)
            {
                throw new Exception($"Unable to rebuild full nupkg because release is not of type delta: {snapRelease.Filename}");
            }

            var reassembledFullSnapRelease = genesisSnapRelease.AsFullRelease(false);
            var reassembledFullSnapApp = genesisSnapApp.AsFullSnapApp(false);

            foreach (var deltaSnapRelease in deltaSnapReleasesToApply)
            {
                await ApplyDeltaPackageAsync(deltaSnapRelease);                
            }
            
            reassembledFullSnapRelease.Sort();

            var reassembledNupkgSha512Checksum = _snapCryptoProvider.Sha512(reassembledFullSnapRelease, packageBuilder);
            if (reassembledNupkgSha512Checksum != snapRelease.FullSha512Checksum)
            {
                throw new SnapReleaseChecksumMismatchException(snapRelease);
            }

            packageBuilder.Id = reassembledFullSnapRelease.UpstreamId;
            
            return (packageBuilder, reassembledFullSnapApp, reassembledFullSnapRelease);

            async Task ApplyDeltaPackageAsync(SnapRelease deltaRelease)
            {
                if (deltaRelease == null) throw new ArgumentNullException(nameof(deltaRelease));

                if (deltaRelease.IsGenesis)
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
                    foreach (var checksumNuspecTargetPath in deltaRelease.Deleted)
                    {
                        if (!packageBuilder.RemovePackageFile(checksumNuspecTargetPath, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new FileNotFoundException(
                                $"Unable to remove 'Deleted' file from genesis package builder: {reassembledFullSnapRelease.Filename}. " +
                                $"Target path: {checksumNuspecTargetPath}. " +
                                $"Nupkg: {deltaRelease.Filename}.");
                        }

                        var existingFullChecksum = reassembledFullSnapRelease.Files.SingleOrDefault(x => x.NuspecTargetPath == checksumNuspecTargetPath);
                        if (existingFullChecksum == null)
                        {
                            throw new FileNotFoundException(
                                $"Unable to remove 'Deleted' file from full release files list: {reassembledFullSnapRelease.Filename}. " +
                                $"Target path: {checksumNuspecTargetPath}. " +
                                $"Nupkg: {deltaRelease.Filename}.");
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

                        UpdateRebuildProgress();
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

                        var packageFile = packageBuilder.GetPackageFile(deltaChecksum.NuspecTargetPath, StringComparison.OrdinalIgnoreCase);
                        var packageFileStream = packageFile.GetStream();
                        packageFileStream.Seek(0, SeekOrigin.Begin);

                        var neverGenerateBsDiffThisAssembly =
                            NeverGenerateBsDiffsTheseAssemblies.SingleOrDefault(x =>
                                string.Equals(x, deltaChecksum.NuspecTargetPath, StringComparison.OrdinalIgnoreCase));

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

                            if (patchStream.Length == 0)
                            {
                                if (deltaChecksum.DeltaFilesize != 0)
                                {
                                    throw new Exception($"Expected delta file size to equal 0 (zero) when {nameof(patchStream)} " +
                                                        $"length is 0 (zero). Target path: {existingChecksum.NuspecTargetPath}.");
                                }

                                if (deltaChecksum.DeltaSha512Checksum != SnapConstants.Sha512EmptyFileChecksum)
                                {
                                    throw new Exception($"Expected delta file checksum to equal {SnapConstants.Sha512EmptyFileChecksum} when " +
                                                        $"{nameof(patchStream)} length is 0 (zero). Target path: {existingChecksum.NuspecTargetPath}.");
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
                            AddPackageFile(packageBuilder, outputStream, deltaChecksum.NuspecTargetPath, string.Empty, reassembledFullSnapRelease, true);
                            UpdateRebuildProgress();
                        }

                        packageBuilder.Populate(await packageArchiveReader.GetManifestMetadataAsync(cancellationToken));
                    }
                    
                    reassembledFullSnapRelease.Version = reassembledFullSnapApp.Version = packageBuilder.Version = deltaRelease.Version.ToNuGetVersion();
                    reassembledFullSnapRelease.Filename = deltaRelease.BuildNugetFullFilename();
                    reassembledFullSnapRelease.FullSha512Checksum = deltaRelease.FullSha512Checksum;
                    reassembledFullSnapRelease.FullFilesize = deltaRelease.FullFilesize;
                    reassembledFullSnapRelease.DeltaSha512Checksum = deltaRelease.DeltaSha512Checksum;
                    reassembledFullSnapRelease.DeltaFilesize = deltaRelease.DeltaFilesize;

                    // Nuspec properties                    
                    reassembledFullSnapRelease.ReleaseNotes = deltaRelease.ReleaseNotes;
                    reassembledFullSnapRelease.CreatedDateUtc = deltaRelease.CreatedDateUtc;
                    reassembledFullSnapRelease.Gc = deltaRelease.Gc;
                }
            }
        }

        async Task<(PackageBuilder packageBuilder, SnapApp snapApp)> BuildPackageFromReleaseAsync([NotNull] string packagesDirectory,
            [NotNull] ISnapAppChannelReleases snapAppChannelReleases, [NotNull] SnapRelease snapRelease, IRebuildPackageProgressSource rebuildPackageProgressSource = null,
            CancellationToken cancellationToken = default)
        {
            if (packagesDirectory == null) throw new ArgumentNullException(nameof(packagesDirectory));
            if (snapAppChannelReleases == null) throw new ArgumentNullException(nameof(snapAppChannelReleases));
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

                var filesRestored = 0;
                var filesToRestore = snapRelease.Files.Count;

                rebuildPackageProgressSource?.Raise(0, filesRestored, filesToRestore);

                foreach (var checksum in snapRelease.Files)
                {
                    var srcStream = await packageArchiveReader.GetStreamAsync(checksum.NuspecTargetPath, cancellationToken).ReadToEndAsync(cancellationToken);

                    AddPackageFile(packageBuilder, srcStream, checksum.NuspecTargetPath, string.Empty);

                    var progressPercentage = (int) Math.Floor((double) ++filesRestored / filesToRestore * 100);
                    rebuildPackageProgressSource?.Raise(progressPercentage, filesRestored, filesToRestore);
                }

                var releaseChecksum = _snapCryptoProvider.Sha512(snapRelease, packageBuilder);
                if (releaseChecksum != snapRelease.FullSha512Checksum)
                {
                    throw new SnapReleaseChecksumMismatchException(snapRelease);
                }
                
                return (packageBuilder, snapApp);
            }
        }

        async Task AddSnapAssetsAsync([NotNull] ISnapNuspecDetails snapNuspecDetails, [NotNull] ICoreRunLib coreRunLib, [NotNull] PackageBuilder packageBuilder,
            [NotNull] SnapApp snapApp, [NotNull] SnapRelease snapRelease, CancellationToken cancellationToken = default)
        {
            if (snapNuspecDetails == null) throw new ArgumentNullException(nameof(snapNuspecDetails));
            if (coreRunLib == null) throw new ArgumentNullException(nameof(coreRunLib));
            if (packageBuilder == null) throw new ArgumentNullException(nameof(packageBuilder));
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));

            // Icon (Windows has native platform support for icons)
            if (snapApp.Target.Os != OSPlatform.Windows 
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
            var snapDllAbsolutePath = _snapFilesystem.PathCombine(snapNuspecDetails.NuspecBaseDirectory, SnapConstants.SnapDllFilename);
            using (var snapDllAssemblyDefinition = await _snapFilesystem.FileReadAssemblyDefinitionAsync(snapDllAbsolutePath, cancellationToken))
            {
                var snapDllOptimizedMemoryStream = new MemoryStream();

                if (snapApp.Target.Framework.IsNetCoreAppSafe())
                {
                    using (var snapDllAssemblyDefinitionOptimized =
                        _snapAppWriter.OptimizeSnapDllForPackageArchive(snapDllAssemblyDefinition, snapApp.Target.Os))
                    {
                        snapDllAssemblyDefinitionOptimized.Write(snapDllOptimizedMemoryStream);
                    }
                }
                else
                {
                    snapDllAssemblyDefinition.Write(snapDllOptimizedMemoryStream);
                }

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

        (MemoryStream nuspecStream, List<(string filename, string targetPath)> packageFiles) BuildNuspec([NotNull] MemoryStream nuspecStream, [NotNull] Func<string, string> propertyProvider,
            [NotNull] string baseDirectory, [NotNull] SnapApp snapApp, [NotNull] SnapRelease snapRelease)
        {
            if (nuspecStream == null) throw new ArgumentNullException(nameof(nuspecStream));
            if (nuspecStream == null) throw new ArgumentNullException(nameof(nuspecStream));
            if (propertyProvider == null) throw new ArgumentNullException(nameof(propertyProvider));
            if (baseDirectory == null) throw new ArgumentNullException(nameof(baseDirectory));
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));

            const string nuspecXmlNs = "http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd";

            var nugetVersion = new NuGetVersion(snapApp.Version.ToFullString());
            var upstreamPackageId = snapApp.BuildNugetUpstreamId();
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
                    metadata.Add(new XElement("version", nugetVersion));
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

                snapApp.ReleaseNotes = 
                    snapRelease.ReleaseNotes = metadata.SingleOrDefault(XName.Get("releasenotes", nuspecXmlNs))?.Value;

                nuspecDocument.SingleOrDefault(XName.Get("files", nuspecXmlNs))?.Remove();

                var files = nuspecDocument.SingleOrDefault(XName.Get("files", nuspecXmlNs));
                var excludeAttribute = files?.Attribute("exclude");

                var defaultExcludePattern = new List<Glob>
                {
                    Glob.Parse("**/*.nuspec"),
                    Glob.Parse("**/*.pdb"),
                    Glob.Parse("**/*.dll.xml"),
                    Glob.Parse("**/*.log")
                };

                const char excludePatternDelimeter = ';';

                var excludePatterns = string.IsNullOrWhiteSpace(excludeAttribute?.Value) ? defaultExcludePattern :
                    excludeAttribute.Value.Contains(excludePatternDelimeter) ? excludeAttribute.Value.Split(excludePatternDelimeter)
                        .Where(x => !string.IsNullOrWhiteSpace(x)).Select(Glob.Parse).ToList() :
                    new List<Glob> {Glob.Parse(excludeAttribute.Value)};

                var allFiles = _snapFilesystem.DirectoryGetAllFilesRecursively(baseDirectory).ToList();
                foreach (var fileAbsolutePath in allFiles)
                {
                    var relativePath = fileAbsolutePath.Replace(baseDirectory, string.Empty).Substring(1);
                    var excludeFile = excludePatterns.Any(x => x.IsMatch(relativePath));
                    if (excludeFile)
                    {
                        continue;
                    }

                    packageFiles.Add((fileAbsolutePath, targetPath: _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, relativePath).ForwardSlashesSafe()));
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

            var genesisRelease = snapAppReleases.GetGenesisRelease(snapApp.GetDefaultChannelOrThrow());
            if (genesisRelease == null)
            {
                throw new Exception("Missing genesis release");
            }

            if (!genesisRelease.IsGenesis)
            {
                throw new Exception($"Genesis release is not a genesis release: {genesisRelease.Filename}");
            }

            if (!genesisRelease.IsFull || genesisRelease.IsDelta)
            {
                throw new Exception($"Genesis release must be full release: {genesisRelease.Filename}");
            }
            
            snapAppsReleases.Bump();

            var packageBuilder = new PackageBuilder
            {
                Id = snapApp.BuildNugetReleasesUpstreamId(),
                Version = snapAppsReleases.Version.ToNuGetVersion(),
                Description =
                    $"Snapx application database. This file contains release details for application: {snapApp.Id}. " +
                    $"Channels: {string.Join(", ", snapApp.Channels.Select(x => x.Name))}.",
                Authors = {"Snapx"}
            };

            foreach (var snapRelease in snapAppReleases)
            {
                if (snapRelease.IsFull)
                {
                    var genesisOrFull = snapRelease.IsGenesis ? "genesis" : "full";
                    
                    var expectedFilename = snapRelease.BuildNugetFullFilename();
                    if (snapRelease.Filename != expectedFilename)
                    {
                        throw new Exception($"Invalid {genesisOrFull} filename: {snapRelease.Filename}. Expected: {expectedFilename}");
                    }
                    
                    var expectedUpstreamId = snapRelease.BuildNugetFullUpstreamId();
                    if (snapRelease.UpstreamId != expectedUpstreamId)
                    {
                        throw new Exception($"Invalid {genesisOrFull} upstream id: {snapRelease.UpstreamId}. Expected: {expectedUpstreamId}");
                    }
                }
                else if (snapRelease.IsDelta)
                {
                    var expectedFilename = snapRelease.BuildNugetDeltaFilename();
                    if (snapRelease.Filename != expectedFilename)
                    {
                        throw new Exception($"Invalid delta filename: {snapRelease.Filename}. Expected: {expectedFilename}");
                    }

                    var expectedUpstreamId = snapRelease.BuildNugetDeltaUpstreamId();
                    if (snapRelease.UpstreamId != expectedUpstreamId)
                    {
                        throw new Exception($"Invalid upstream id: {snapRelease.UpstreamId}. Expected: {expectedUpstreamId}");
                    }
                }
                else
                {
                    throw new NotSupportedException($"Expected either delta or genesis release. Filename: {snapRelease.Filename}");
                }

                if (snapRelease.FullFilesize <= 0)
                {
                    throw new Exception($"Invalid file size: {snapRelease.FullSha512Checksum}. Must be greater than zero! Filename: {snapRelease.Filename}");
                }

                if (snapRelease.FullSha512Checksum == null || snapRelease.FullSha512Checksum.Length != 128)
                {
                    throw new Exception($"Invalid checksum: {snapRelease.FullSha512Checksum}. Filename: {snapRelease.Filename}");
                }
            }

            var snapsReleasesCompressedStream = new MemoryStream();            
            var snapAppsReleasesBytes = _snapAppWriter.ToSnapAppsReleases(snapAppsReleases);
            
            using (var snapsReleasesStream = new MemoryStream(snapAppsReleasesBytes))
            using (var writer = WriterFactory.Open(snapsReleasesCompressedStream, ArchiveType.Tar, new WriterOptions(CompressionType.BZip2)
            {
                LeaveStreamOpen = true
            }))
            {
                writer.Write(SnapConstants.ReleasesFilename, snapsReleasesStream);
            }
            
            AddPackageFile(packageBuilder, snapsReleasesCompressedStream, SnapConstants.NuspecRootTargetPath, SnapConstants.ReleasesFilename);

            var outputStream = new MemoryStream();
            packageBuilder.Save(outputStream);

            outputStream.Seek(0, SeekOrigin.Begin);
            return outputStream;            
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
            [NotNull] string nuspecTargetPath, [NotNull] string filename, SnapRelease snapRelease = null, bool replace = false)
        {
            if (packageBuilder == null) throw new ArgumentNullException(nameof(packageBuilder));
            if (srcStream == null) throw new ArgumentNullException(nameof(srcStream));
            if (nuspecTargetPath == null) throw new ArgumentNullException(nameof(nuspecTargetPath));
            if (filename == null) throw new ArgumentNullException(nameof(filename));

            if (!nuspecTargetPath.StartsWith(SnapConstants.NuspecRootTargetPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception($"Invalid {nameof(nuspecTargetPath)}: {nuspecTargetPath}. Must start with: {SnapConstants.NuspecRootTargetPath}");
            }

            if (nuspecTargetPath.EndsWith("/", StringComparison.Ordinal))
            {
                throw new Exception($"Invalid {nameof(nuspecTargetPath)}: {nuspecTargetPath}. Cannot end with a trailing slash.");
            }

            if (replace)
            {
                if (snapRelease == null)
                {
                    throw new ArgumentNullException(nameof(snapRelease));
                }

                if (!packageBuilder.RemovePackageFile(nuspecTargetPath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception($"Failed to replace: {nuspecTargetPath}. It does not exist in {nameof(packageBuilder)}");
                }

                var existingSnapReleaseChecksum = snapRelease.Files.SingleOrDefault(x => x.NuspecTargetPath == nuspecTargetPath);
                if (existingSnapReleaseChecksum == null)
                {
                    throw new Exception($"Failed to replace: {nuspecTargetPath}. It does not exist in {nameof(snapRelease)}");
                }

                snapRelease.Files.Remove(existingSnapReleaseChecksum);
            }

            var nuGetFramework = NuGetFramework.Parse(SnapConstants.NuspecTargetFrameworkMoniker);

            nuspecTargetPath = nuspecTargetPath.ForwardSlashesSafe();

            if (filename == string.Empty)
            {
                var lastSlashIndex = nuspecTargetPath.LastIndexOf("/", StringComparison.Ordinal);
                if (lastSlashIndex == -1)
                {
                    throw new Exception($"Expected target path to contain filename: {nuspecTargetPath}");
                }

                filename = nuspecTargetPath.Substring(lastSlashIndex + 1);
            }
            else
            {
                nuspecTargetPath = _snapFilesystem.PathCombine(nuspecTargetPath, filename).ForwardSlashesSafe();
            }

            if (srcStream.CanSeek)
            {
                srcStream.Seek(0, SeekOrigin.Begin);
            }

            if (snapRelease != null)
            {
                if (snapRelease.Files.Any(x => string.Equals(x.NuspecTargetPath, nuspecTargetPath, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new Exception($"File already added to {nameof(snapRelease)}. Target path: {nuspecTargetPath}");
                }

                snapRelease.Files.Add(new SnapReleaseChecksum
                {
                    NuspecTargetPath = nuspecTargetPath,
                    FullFilesize = srcStream.Length,
                    FullSha512Checksum = _snapCryptoProvider.Sha512(srcStream)
                });
            }

            var duplicatePackageFile = packageBuilder.GetPackageFile(nuspecTargetPath, StringComparison.OrdinalIgnoreCase);
            if (duplicatePackageFile != null)
            {
                throw new Exception($"File already added to {nameof(packageBuilder)}. Target path: {duplicatePackageFile.EffectivePath}");
            }

            packageBuilder.Files.Add(new InMemoryPackageFile(srcStream, nuGetFramework, nuspecTargetPath, filename));
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

        (Dictionary<string, string> properties, Func<string, string> propertiesResolverFunc) BuildNuspecProperties([NotNull] IReadOnlyDictionary<string, string> mixinNuspecProperties)
        {
            if (mixinNuspecProperties == null) throw new ArgumentNullException(nameof(mixinNuspecProperties));

            var nuspecProperties = new Dictionary<string, string>();

            foreach (var kv in mixinNuspecProperties)
            {
                if (!nuspecProperties.ContainsKey(kv.Key.ToLowerInvariant()))
                {
                    nuspecProperties.Add(kv.Key, kv.Value);
                }
            }

            string NuspecPropertyProvider(string propertyName)
            {
                return nuspecProperties.TryGetValue(propertyName, out var value) ? value : null;
            }

            return (nuspecProperties, NuspecPropertyProvider);
        }
             
    }
}
