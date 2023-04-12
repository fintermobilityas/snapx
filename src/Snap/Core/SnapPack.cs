using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using JetBrains.Annotations;
using Mono.Cecil;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using SharpCompress.Common;
using SharpCompress.Writers;
using Snap.Core.Models;
using Snap.Extensions;
using Snap.NuGet;
using Snap.Reflection;

namespace Snap.Core
{
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

    internal interface ISnapPack
    {
        IReadOnlyCollection<string> AlwaysRemoveTheseAssemblies { get; }
        IReadOnlyCollection<string> NeverGenerateBsDiffsTheseAssemblies { get; }

        Task<(MemoryStream fullNupkgMemoryStream, SnapApp fullSnapApp, SnapRelease fullSnapRelease, MemoryStream deltaNupkgMemoryStream, SnapApp deltaSnapApp, SnapRelease deltaSnapRelease)> 
            BuildPackageAsync([NotNull] ISnapPackageDetails packageDetails, [NotNull] ILibPal libPal, CancellationToken cancellationToken = default);
        Task<(SnapApp fullSnapApp, SnapRelease fullSnapRelease)> RebuildPackageAsync([NotNull] string packagesDirectory,
            [NotNull] ISnapAppChannelReleases snapAppChannelReleases, [NotNull] SnapRelease snapRelease,
            IRebuildPackageProgressSource rebuildPackageProgressSource = null, ISnapFilesystem filesystem = default,
            CancellationToken cancellationToken = default);
        MemoryStream BuildEmptyReleasesPackage([NotNull] SnapApp snapApp, [NotNull] SnapAppsReleases snapAppsReleases);
        MemoryStream BuildReleasesPackage([NotNull] SnapApp snapApp, [NotNull] SnapAppsReleases snapAppsReleases, int? version = null);
        Task<SnapApp> GetSnapAppAsync([NotNull] IAsyncPackageCoreReader asyncPackageCoreReader, CancellationToken cancellationToken = default);
        Task<MemoryStream> GetSnapAssetAsync([NotNull] IAsyncPackageCoreReader asyncPackageCoreReader, [NotNull] string filename,
            CancellationToken cancellationToken = default);
    }

    internal sealed class SnapPack : ISnapPack
    {
        readonly ISnapFilesystem _snapFilesystem;
        readonly ISnapAppReader _snapAppReader;
        readonly ISnapAppWriter _snapAppWriter;
        readonly ISnapCryptoProvider _snapCryptoProvider;
        readonly ISnapBinaryPatcher _snapBinaryPatcher;
        readonly SemanticVersion _snapDllVersion;

        static readonly Regex IsSnapDotnetRuntimesFileRegex =
            new(@"^runtimes\/(.+?)\/native\/([libsnap|snapstub]+)$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

        public IReadOnlyCollection<string> AlwaysRemoveTheseAssemblies => new List<string>
        {
            _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, SnapConstants.SnapDllFilename).ForwardSlashesSafe(),
            _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, SnapConstants.SnapAppDllFilename).ForwardSlashesSafe()
        };

        public IReadOnlyCollection<string> NeverGenerateBsDiffsTheseAssemblies => new List<string>
        {
            _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapAppDllFilename).ForwardSlashesSafe()
        };

        public SnapPack(ISnapFilesystem snapFilesystem,
            [NotNull] ISnapAppReader snapAppReader, 
            [NotNull] ISnapAppWriter snapAppWriter,
            [NotNull] ISnapCryptoProvider snapCryptoProvider, 
            [NotNull] ISnapBinaryPatcher snapBinaryPatcher)
        {
            _snapFilesystem = snapFilesystem ?? throw new ArgumentNullException(nameof(snapFilesystem));
            _snapAppReader = snapAppReader ?? throw new ArgumentNullException(nameof(snapAppReader));
            _snapAppWriter = snapAppWriter ?? throw new ArgumentNullException(nameof(snapAppWriter));
            _snapCryptoProvider = snapCryptoProvider ?? throw new ArgumentNullException(nameof(snapCryptoProvider));
            _snapBinaryPatcher = snapBinaryPatcher ?? throw new ArgumentNullException(nameof(snapBinaryPatcher));
        
            var informationalVersion = typeof(Snapx).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            _snapDllVersion = !SemanticVersion.TryParse(informationalVersion, out var currentVersion) ? null : currentVersion;
        }

        public async Task<(MemoryStream fullNupkgMemoryStream, SnapApp fullSnapApp, SnapRelease fullSnapRelease, MemoryStream deltaNupkgMemoryStream, SnapApp deltaSnapApp, SnapRelease deltaSnapRelease)> 
            BuildPackageAsync(ISnapPackageDetails packageDetails, ILibPal libPal, CancellationToken cancellationToken = default)
        {
            if (packageDetails == null) throw new ArgumentNullException(nameof(packageDetails));
            if (libPal == null) throw new ArgumentNullException(nameof(libPal));

            var (fullNupkgPackageBuilder, fullSnapApp, fullSnapRelease, deltaNupkgPackageBuilder, deltaSnapApp, deltaSnapRelease) =
                await BuildPackageAsyncInternal(packageDetails, libPal, cancellationToken);

            fullSnapRelease.Sort();            
            
            var fullNupkgMemoryStream = new MemoryStream();
            
            fullSnapRelease.FullSha256Checksum = _snapCryptoProvider.Sha256(fullSnapRelease, fullNupkgPackageBuilder);

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

            deltaSnapRelease.DeltaSha256Checksum =
                fullSnapRelease.DeltaSha256Checksum = _snapCryptoProvider.Sha256(deltaSnapRelease, deltaNupkgPackageBuilder);
                
            deltaNupkgPackageBuilder.Save(deltaNupkgMemoryStream);

            deltaSnapRelease.DeltaFilesize = 
                fullSnapRelease.DeltaFilesize = deltaNupkgMemoryStream.Length;
            deltaSnapRelease.FullSha256Checksum = fullSnapRelease.FullSha256Checksum;
            deltaSnapRelease.FullFilesize = fullSnapRelease.FullFilesize;

            packageDetails.SnapAppsReleases.Add(deltaSnapRelease);

            deltaNupkgMemoryStream.Seek(0, SeekOrigin.Begin);

            return (fullNupkgMemoryStream, fullSnapApp, fullSnapRelease, deltaNupkgMemoryStream, deltaSnapApp, deltaSnapRelease);
        }

        async Task<(PackageBuilder fullNupkgPackageBuilder, SnapApp fullSnapApp, SnapRelease fullSnapRelease, PackageBuilder deltaNupkgBuilder, SnapApp deltaSnapApp, SnapRelease deltaSnapRelease)>
            BuildPackageAsyncInternal(
                [NotNull] ISnapPackageDetails packageDetails, 
                [NotNull] ILibPal libPal,
                CancellationToken cancellationToken = default)
        {
            if (packageDetails == null) throw new ArgumentNullException(nameof(packageDetails));
            if (libPal == null) throw new ArgumentNullException(nameof(libPal));

            Validate(packageDetails);

            var packagesDirectory = packageDetails.PackagesDirectory;
            var snapAppsReleases = packageDetails.SnapAppsReleases;
            var snapAppChannelReleases = snapAppsReleases.GetReleases(packageDetails.SnapApp, 
                packageDetails.SnapApp.GetDefaultChannelOrThrow());

            var snapAppMetadataOnly = new SnapApp(packageDetails.SnapApp)
            {
                IsFull = false,
                IsGenesis = false
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
                        snapReleaseMetadataOnly, libPal, cancellationToken);

                genesisSnapRelease.Channels = genesisSnapApp.Channels.Select(x => x.Name).ToList();

                await using (nuspecMemoryStream)
                {
                    return (genesisPackageBuilder, genesisSnapApp, genesisSnapRelease, null, null, null);                    
                }
            }
            
            var previousSnapRelease = snapAppChannelReleases.GetMostRecentRelease();

            var (previousNupkgPackageBuilder, _, _) = await RebuildFullPackageAsyncInternal(
                packagesDirectory, snapAppChannelReleases, previousSnapRelease, cancellationToken: cancellationToken);

            var (currentFullNupkgPackageBuilder, currentNuspecMemoryStream, currentNuspecPropertiesResolver, 
                currentFullSnapApp, currentFullSnapRelease) = await BuildFullPackageAsyncInternal(
                    packageDetails, snapAppChannelReleases, snapAppMetadataOnly, snapReleaseMetadataOnly, libPal, cancellationToken);

            var deltaSnapApp = currentFullSnapApp.AsDeltaSnapApp();
            var deltaSnapRelease = currentFullSnapRelease.AsDeltaRelease();

            await using (currentNuspecMemoryStream)
            {
                var deltaNupkgBuilder = await BuildDeltaPackageAsyncInternal(packageDetails,
                    previousSnapRelease,
                    currentFullSnapRelease,
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
            BuildFullPackageAsyncInternal([NotNull] ISnapPackageDetails snapPackageDetails, [NotNull] ISnapAppChannelReleases snapAppChannelReleases, SnapApp snapAppMetadataOnly,
                [NotNull] SnapRelease snapReleaseMetadataOnly, [NotNull] ILibPal libPal, CancellationToken cancellationToken = default)
        {
            if (snapPackageDetails == null) throw new ArgumentNullException(nameof(snapPackageDetails));
            if (snapAppChannelReleases == null) throw new ArgumentNullException(nameof(snapAppChannelReleases));
            if (snapReleaseMetadataOnly == null) throw new ArgumentNullException(nameof(snapReleaseMetadataOnly));
            if (libPal == null) throw new ArgumentNullException(nameof(libPal));

            var isGenesis = !snapAppChannelReleases.Any();

            var fullSnapApp = snapAppMetadataOnly.AsFullSnapApp(isGenesis);
            var fullSnapRelease = snapReleaseMetadataOnly.AsFullRelease(isGenesis);
            
            var (_, nuspecPropertiesResolver) = BuildNuspecProperties(snapPackageDetails.NuspecProperties);

            var version = fullSnapApp.Version;
            var description = fullSnapApp.Description ?? "snapx application";
            var authors = fullSnapApp.Authors ?? "snapx";
            var upstreamPackageId = fullSnapApp.BuildNugetUpstreamId();

            var nuspecXml = $@"<?xml version=""1.0""?>
<package xmlns=""http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd"">
    <metadata>
        <id>{upstreamPackageId}</id>
        <title>{fullSnapApp.Id}</title>
        <authors>{authors}</authors>
        <version>{version}</version>
        <releaseNotes>{fullSnapApp.ReleaseNotes}</releaseNotes>
        <repository url=""{fullSnapApp.RepositoryUrl}"" type=""{fullSnapApp.RepositoryType}"" />
        <description>{description}</description>
    </metadata>
</package>";


            var nuspecIntermediateStream = new MemoryStream(Encoding.UTF8.GetBytes(nuspecXml));

            var (nuspecStream, packageFiles) = BuildNuspec(nuspecIntermediateStream, fullSnapApp, nuspecPropertiesResolver, snapPackageDetails.NuspecBaseDirectory);

            var nuspecStreamCpy = new MemoryStream(nuspecStream.ToArray()); // PackageBuilder closes stream

            await using (nuspecStream)
            await using (nuspecIntermediateStream)
            {
                var packageBuilder = new PackageBuilder(nuspecStream, snapPackageDetails.NuspecBaseDirectory, nuspecPropertiesResolver);
                packageBuilder.Files.Clear(); // NB! We are _NOT_ loading files twice into memory.    
                
                foreach (var (filename, targetPath) in packageFiles)
                {
                    var srcStream = await _snapFilesystem.FileRead(filename).ReadToEndAsync(cancellationToken: cancellationToken);
                    AddPackageFile(packageBuilder, srcStream, targetPath, string.Empty, fullSnapRelease);
                }

                var mainExecutableFileName = fullSnapApp.GetStubExeFilename();
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
                    await using var tmpDir = _snapFilesystem.WithDisposableTempDirectory();
                    await using var mainExecutableStream = await mainExecutablePackageFile.GetStream().ReadToEndAsync(cancellationToken: cancellationToken);
                    var mainExecutableTempFilename = _snapFilesystem.PathCombine(tmpDir.WorkingDirectory, mainExecutableFileName);
                    await using (var mainExecutableTmpStream = _snapFilesystem.FileWrite(mainExecutableTempFilename))
                    {
                        await mainExecutableStream.CopyToAsync(mainExecutableTmpStream, cancellationToken);                                        
                    }

                    if (!libPal.SetIcon(mainExecutableTempFilename, fullSnapApp.Target.Icon))
                    {
                        throw new Exception($"Failed to update icon for executable {mainExecutableTempFilename}. Icon: {fullSnapApp.Target.Icon}.");
                    }

                    var mainExecutableByteArray = await _snapFilesystem.FileReadAllBytesAsync(mainExecutableTempFilename, cancellationToken);
                    var mainExecutableStreamWithIcon = new MemoryStream(mainExecutableByteArray);
                       
                    AddPackageFile(packageBuilder, mainExecutableStreamWithIcon, 
                        mainExecutablePackageFile.EffectivePath, string.Empty, fullSnapRelease, true);
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

                    using var snapAssemblyDefinition = AssemblyDefinition.ReadAssembly(packageFile.GetStream());
                    var cecil = new CecilAssemblyReflector(snapAssemblyDefinition);
                    var snapAssemblyInformationalVersionAttribute = cecil
                        .GetAttribute<AssemblyInformationalVersionAttribute>();

                    if (snapAssemblyInformationalVersionAttribute == null)
                    {
                        throw new Exception($"Failed to get assembly version from {targetPath}.");
                    }

                    var snapAssemblyInformationVersionValue = snapAssemblyInformationalVersionAttribute.Values.First().Value;
                        
                    if (!SemanticVersion.TryParse(snapAssemblyInformationVersionValue, out var snapAssemblyVersion))
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
                });

                await AddSnapAssetsAsync(snapPackageDetails, packageBuilder, fullSnapApp, fullSnapRelease, cancellationToken);

                return (packageBuilder, nuspecStreamCpy, nuspecPropertiesResolver, fullSnapApp, fullSnapRelease);
            }
        }

        async Task<PackageBuilder> BuildDeltaPackageAsyncInternal(
            [NotNull] ISnapPackageDetails snapPackageDetails,
            [NotNull] SnapRelease previousFullSnapRelease,
            [NotNull] SnapRelease currentFullSnapRelease,
            [NotNull] SnapRelease currentDeltaSnapRelease,
            [NotNull] PackageBuilder previousFullNupkgPackageBuilder,
            [NotNull] PackageBuilder currentFullNupkgPackageBuilder,
            [NotNull] MemoryStream currentFullNupkgNuspecMemoryStream,
            [NotNull] Func<string, string> currentFullNupkgNuspecPropertiesResolverFn, CancellationToken cancellationToken = default)
        {
            if (snapPackageDetails == null) throw new ArgumentNullException(nameof(snapPackageDetails));
            if (previousFullSnapRelease == null) throw new ArgumentNullException(nameof(previousFullSnapRelease));
            if (previousFullNupkgPackageBuilder == null) throw new ArgumentNullException(nameof(previousFullNupkgPackageBuilder));
            if (currentFullNupkgPackageBuilder == null) throw new ArgumentNullException(nameof(currentFullNupkgPackageBuilder));
            if (currentFullNupkgNuspecMemoryStream == null) throw new ArgumentNullException(nameof(currentFullNupkgNuspecMemoryStream));
            if (currentFullNupkgNuspecPropertiesResolverFn == null) throw new ArgumentNullException(nameof(currentFullNupkgNuspecPropertiesResolverFn));

            async Task AddNoBsDiffPackageFileAsync(PackageBuilder packageBuilder, SnapReleaseChecksum checksum, IPackageFile packageFile)
            {
                if (packageBuilder == null) throw new ArgumentNullException(nameof(packageBuilder));
                if (checksum == null) throw new ArgumentNullException(nameof(checksum));
                if (packageFile == null) throw new ArgumentNullException(nameof(packageFile));
                
                if (checksum.DeltaSha256Checksum != null)
                {
                    throw new Exception(
                        $"Expected {nameof(checksum.DeltaSha256Checksum)} to be null. " +
                        $"Filename: {checksum.Filename}. " +
                        $"Nupkg: {currentFullSnapRelease.Filename}. ");
                }

                if (checksum.DeltaFilesize != 0)
                {
                    throw new Exception($"Expected {nameof(checksum.DeltaFilesize)} to be 0 (zero). " +
                                        $"Filename: {checksum.Filename}. " +
                                        $"Nupkg: {currentFullSnapRelease.Filename}. ");
                }

                var srcStream = await packageFile.GetStream().ReadToEndAsync(cancellationToken: cancellationToken);
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
                snapPackageDetails.NuspecBaseDirectory,
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
                    var srcStream = await currentPackageFile.GetStream().ReadToEndAsync(cancellationToken: cancellationToken);
                    AddPackageFile(deltaNupkgPackageBuilder, srcStream, currentChecksum.NuspecTargetPath, string.Empty);
                    continue;
                }
                
                if (string.Equals(previousChecksum.FullSha256Checksum, currentChecksum.FullSha256Checksum, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(previousChecksum.DeltaSha256Checksum, currentChecksum.DeltaSha256Checksum, StringComparison.OrdinalIgnoreCase))
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

                await using var oldDataStream = await previousPackageFile.GetStream().ReadToEndAsync(cancellationToken: cancellationToken);
                await using var newDataStream = await currentPackageFile.GetStream().ReadToEndAsync(cancellationToken: cancellationToken);
                var patchStream = new MemoryStream();

                if (newDataStream.Length > 0
                    && oldDataStream.Length > 0)
                {
                    _snapBinaryPatcher.Diff(oldDataStream, newDataStream, patchStream);
                } else if (newDataStream.Length > 0)
                {
                    await newDataStream.CopyToAsync(patchStream, cancellationToken);
                }
            
                currentChecksum.DeltaSha256Checksum = _snapCryptoProvider.Sha256(patchStream);
                currentChecksum.DeltaFilesize = patchStream.Length;

                if (currentChecksum.DeltaFilesize == 0 
                    && currentChecksum.DeltaSha256Checksum != SnapConstants.Sha256EmptyFileChecksum)
                {
                    throw new Exception($"Expected empty file checksum to equal {SnapConstants.Sha256EmptyFileChecksum}. Target path: {currentChecksum.NuspecTargetPath}.");
                }

                AddPackageFile(deltaNupkgPackageBuilder, patchStream, currentChecksum.NuspecTargetPath, string.Empty);
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

        public async Task<(SnapApp fullSnapApp, SnapRelease fullSnapRelease)> RebuildPackageAsync(string packagesDirectory,
            ISnapAppChannelReleases snapAppChannelReleases, SnapRelease snapRelease, IRebuildPackageProgressSource rebuildPackageProgressSource = null,
            ISnapFilesystem filesystem = default, CancellationToken cancellationToken = default)
        {
            if (packagesDirectory == null) throw new ArgumentNullException(nameof(packagesDirectory));
            if (snapAppChannelReleases == null) throw new ArgumentNullException(nameof(snapAppChannelReleases));
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));

            

            var (packageBuilder, fullSnapApp, fullSnapRelease) =
                await RebuildFullPackageAsyncInternal(packagesDirectory, snapAppChannelReleases, snapRelease, rebuildPackageProgressSource, cancellationToken);
            await using var filestream=filesystem.FileWrite(filesystem.PathCombine(packagesDirectory, fullSnapRelease.Filename));
            packageBuilder.Save(filestream);

            snapRelease.Sort();

            return (fullSnapApp, fullSnapRelease);
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
                await BuildPackageFromReleaseAsync(packagesDirectory, genesisSnapRelease, compoundProgressSource, cancellationToken);

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
                await ApplyDeltaPackageAsync(deltaSnapRelease, true);                
            }
            
            reassembledFullSnapRelease.Sort();

            var reassembledNupkgSha256Checksum = _snapCryptoProvider.Sha256(reassembledFullSnapRelease, packageBuilder);
            if (reassembledNupkgSha256Checksum != snapRelease.FullSha256Checksum)
            {
                throw new SnapReleaseChecksumMismatchException(snapRelease);
            }

            packageBuilder.Id = reassembledFullSnapRelease.UpstreamId;
            
            return (packageBuilder, reassembledFullSnapApp, reassembledFullSnapRelease);

            async Task ApplyDeltaPackageAsync(SnapRelease deltaRelease, bool skipChecksum = false)
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
                using var packageArchiveReader = new PackageArchiveReader(deltaNupkgMemoryStream);
                foreach (var checksumNuspecTargetPath in deltaRelease.Deleted)
                {
                    if (!packageBuilder.RemovePackageFile(checksumNuspecTargetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new FileNotFoundException(
                            $"Unable to remove 'Deleted' file from genesis package builder: {reassembledFullSnapRelease.Filename}. " +
                            $"Target path: {checksumNuspecTargetPath}. " +
                            $"Nupkg: {deltaRelease.Filename}.");
                    }
                    var existingFullChecksum = reassembledFullSnapRelease.Files.SingleOrDefault(x => string.Equals(x.NuspecTargetPath, checksumNuspecTargetPath, StringComparison.OrdinalIgnoreCase));
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
                    var srcStream = await packageArchiveReader.GetStream(deltaChecksum.NuspecTargetPath).ReadToEndAsync(cancellationToken: cancellationToken);
                    if (!skipChecksum)
                    {
                        var sha256Checksum = _snapCryptoProvider.Sha256(srcStream);
                        if (deltaChecksum.FullSha256Checksum != sha256Checksum)
                        {
                            throw new SnapReleaseFileChecksumMismatchException(deltaChecksum, snapRelease);
                        }
                    }

                    var existingFullChecksum = reassembledFullSnapRelease.Files.SingleOrDefault(x => string.Equals(x.NuspecTargetPath, deltaChecksum.NuspecTargetPath, StringComparison.OrdinalIgnoreCase)  );
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
                    var existingChecksum = reassembledFullSnapRelease.Files.SingleOrDefault(x => string.Equals(x.NuspecTargetPath, deltaChecksum.NuspecTargetPath, StringComparison.OrdinalIgnoreCase));
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
                    await using (var patchStream = await packageArchiveReader.GetStream(deltaChecksum.NuspecTargetPath).ReadToEndAsync(cancellationToken: cancellationToken))
                    {
                        string sha256Checksum;
                        if (neverGenerateBsDiffThisAssembly != null)
                        {
                            await patchStream.CopyToAsync(outputStream, cancellationToken);

                            if (!skipChecksum)
                            {
                                sha256Checksum = _snapCryptoProvider.Sha256(outputStream);
                                if (deltaChecksum.FullSha256Checksum != sha256Checksum)
                                {
                                    throw new SnapReleaseFileChecksumDeltaMismatchException(deltaChecksum, snapRelease, patchStream.Length);
                                }
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

                            if (deltaChecksum.DeltaSha256Checksum != SnapConstants.Sha256EmptyFileChecksum)
                            {
                                throw new Exception($"Expected delta file checksum to equal {SnapConstants.Sha256EmptyFileChecksum} when " +
                                                    $"{nameof(patchStream)} length is 0 (zero). Target path: {existingChecksum.NuspecTargetPath}.");
                            }

                            goto done;
                        }

                        if (!skipChecksum)
                        {
                            sha256Checksum = _snapCryptoProvider.Sha256(patchStream);
                            if (deltaChecksum.DeltaSha256Checksum != sha256Checksum)
                            {
                                throw new SnapReleaseFileChecksumDeltaMismatchException(deltaChecksum, snapRelease, patchStream.Length);
                            }
                        }
                        
                        _snapBinaryPatcher.Patch((MemoryStream)packageFileStream, patchStream, outputStream, cancellationToken);

                        if (!skipChecksum)
                        {
                            sha256Checksum = _snapCryptoProvider.Sha256(outputStream);
                            if (deltaChecksum.FullSha256Checksum != sha256Checksum)
                            {
                                throw new SnapReleaseFileChecksumMismatchException(deltaChecksum, snapRelease);
                            }
                        }

                        done:
                        AddPackageFile(packageBuilder, outputStream, deltaChecksum.NuspecTargetPath, string.Empty, reassembledFullSnapRelease, true);
                        UpdateRebuildProgress();
                    }

                    packageBuilder.Populate(await packageArchiveReader.GetManifestMetadataAsync(cancellationToken));
                }
                    
                reassembledFullSnapRelease.Version = reassembledFullSnapApp.Version = packageBuilder.Version = deltaRelease.Version.ToNuGetVersion();
                reassembledFullSnapRelease.Filename = deltaRelease.BuildNugetFullFilename();
                reassembledFullSnapRelease.FullSha256Checksum = deltaRelease.FullSha256Checksum;
                reassembledFullSnapRelease.FullFilesize = deltaRelease.FullFilesize;
                reassembledFullSnapRelease.DeltaSha256Checksum = deltaRelease.DeltaSha256Checksum;
                reassembledFullSnapRelease.DeltaFilesize = deltaRelease.DeltaFilesize;

                // Nuspec properties                    
                reassembledFullSnapRelease.ReleaseNotes = deltaRelease.ReleaseNotes;
                reassembledFullSnapRelease.CreatedDateUtc = deltaRelease.CreatedDateUtc;
                reassembledFullSnapRelease.Gc = deltaRelease.Gc;
            }
        }

        async Task<(PackageBuilder packageBuilder, SnapApp snapApp)> BuildPackageFromReleaseAsync([NotNull] string packagesDirectory, [NotNull] SnapRelease snapRelease,
            IRebuildPackageProgressSource rebuildPackageProgressSource = null, CancellationToken cancellationToken = default)
        {
            if (packagesDirectory == null) throw new ArgumentNullException(nameof(packagesDirectory));
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));

            var nupkgAbsolutePath = _snapFilesystem.PathCombine(packagesDirectory, snapRelease.Filename);
            _snapFilesystem.FileExistsThrowIfNotExists(nupkgAbsolutePath);

            await using var inputStream = _snapFilesystem.FileRead(nupkgAbsolutePath);
            using var packageArchiveReader = new PackageArchiveReader(inputStream);
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
                var srcStream = await packageArchiveReader.GetStreamAsync(checksum.NuspecTargetPath, cancellationToken).ReadToEndAsync(cancellationToken: cancellationToken);

                AddPackageFile(packageBuilder, srcStream, checksum.NuspecTargetPath, string.Empty);

                var progressPercentage = (int) Math.Floor((double) ++filesRestored / filesToRestore * 100);
                rebuildPackageProgressSource?.Raise(progressPercentage, filesRestored, filesToRestore);
            }

            var releaseChecksum = _snapCryptoProvider.Sha256(snapRelease, packageBuilder);
            if (releaseChecksum != snapRelease.FullSha256Checksum)
            {
                throw new SnapReleaseChecksumMismatchException(snapRelease);
            }
                
            return (packageBuilder, snapApp);
        }

        async Task AddSnapAssetsAsync([NotNull] ISnapPackageDetails snapNuspecDetails, [NotNull] PackageBuilder packageBuilder,
            [NotNull] SnapApp snapApp, [NotNull] SnapRelease snapRelease, CancellationToken cancellationToken = default)
        {
            if (snapNuspecDetails == null) throw new ArgumentNullException(nameof(snapNuspecDetails));
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
            AddPackageFile(packageBuilder, await _snapFilesystem.FileReadAsync(snapDllAbsolutePath, cancellationToken),
                SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapDllFilename, snapRelease);

            // Stub executable
            var (stubExeFileStream, stubExeFileName) = snapApp.GetStubExeStream(_snapFilesystem, AppContext.BaseDirectory);
            AddPackageFile(packageBuilder, stubExeFileStream, SnapConstants.NuspecAssetsTargetPath, stubExeFileName, snapRelease);
            
            // Snap.App.dll
            using var snapAppDllAssembly = _snapAppWriter.BuildSnapAppAssembly(snapApp);
            var snapAppMemoryStream = new MemoryStream();
            snapAppDllAssembly.Write(snapAppMemoryStream);

            AddPackageFile(packageBuilder, snapAppMemoryStream, SnapConstants.NuspecAssetsTargetPath, SnapConstants.SnapAppDllFilename, snapRelease);
        }

        (MemoryStream nuspecStream, List<(string filename, string targetPath)> packageFiles) BuildNuspec([NotNull] MemoryStream nuspecStream, SnapApp snapApp, [NotNull] Func<string, string> propertyProvider, [NotNull] string baseDirectory)
        {
            if (nuspecStream == null) throw new ArgumentNullException(nameof(nuspecStream));
            if (nuspecStream == null) throw new ArgumentNullException(nameof(nuspecStream));
            if (propertyProvider == null) throw new ArgumentNullException(nameof(propertyProvider));
            if (baseDirectory == null) throw new ArgumentNullException(nameof(baseDirectory));

            const string nuspecXmlNs = "http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd";

            var packageFiles = new List<(string filename, string targetPath)>();

            MemoryStream RewriteNuspecStreamWithEssentials()
            {
                XDocument nuspecDocument;
                try
                {
                    nuspecDocument = new NuspecReader(nuspecStream, new DefaultFrameworkNameProvider(), true).Xml;
                }
                catch (Exception e)
                {
                    throw new Exception("Failed to parse nuspec", e);
                }

                var metadata = nuspecDocument.SingleOrDefault(XName.Get("metadata", nuspecXmlNs));
                if (metadata == null)
                {
                    throw new Exception("The required element 'metadata' is missing from the nuspec");
                }

                nuspecDocument.SingleOrDefault(XName.Get("files", nuspecXmlNs))?.Remove();
                
                var allFiles = _snapFilesystem.DirectoryGetAllFilesRecursively(baseDirectory).ToList();
                var libPalFilename = snapApp.GetLibPalFilename();
                var libBsdiffFilename = snapApp.GetLibBsdiffFilename();
                foreach (var fileAbsolutePath in allFiles)
                {
                    var relativePath = fileAbsolutePath.Replace(baseDirectory, string.Empty)[1..];

                    var match = IsSnapDotnetRuntimesFileRegex.Match(relativePath.ForwardSlashesSafe());
                    if (match.Success && match.Groups.Count == 5) 
                    {
                        var rid = match.Groups[1].Value;
                        if (!string.Equals(rid, snapApp.Target.Rid, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var filename = match.Groups[5].Value;
                        if (string.Equals(filename, libPalFilename, StringComparison.OrdinalIgnoreCase) || 
                            string.Equals(filename, libBsdiffFilename, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }
                    
                    packageFiles.Add((fileAbsolutePath, targetPath: _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, relativePath).ForwardSlashesSafe()));
                }

                var rewrittenNuspecStream = new MemoryStream();
                nuspecDocument.Save(rewrittenNuspecStream);
                rewrittenNuspecStream.Seek(0, SeekOrigin.Begin);

                return rewrittenNuspecStream;
            }

            using var nuspecStreamRewritten = RewriteNuspecStreamWithEssentials();
            var manifestMetadata = Manifest.ReadFrom(nuspecStreamRewritten, propertyProvider, true);
                
            var outputStream = new MemoryStream();
            manifestMetadata.Save(outputStream);
            outputStream.Seek(0, SeekOrigin.Begin);

            return (outputStream, packageFiles);
        }

        public MemoryStream BuildEmptyReleasesPackage(SnapApp snapApp, SnapAppsReleases snapAppsReleases)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (snapAppsReleases == null) throw new ArgumentNullException(nameof(snapAppsReleases));

            var snapAppReleases = snapAppsReleases.GetReleases(snapApp);
            if (snapAppReleases.Any())
            {
                throw new Exception($"Expected {nameof(snapAppsReleases)} to not contain any releases.");
            }

            snapAppsReleases.Bump();
            snapAppsReleases.PackId = Guid.NewGuid();
            snapAppsReleases.PackVersion = null;

            if (SemanticVersion.TryParse(Assembly
                    .GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                    out var snapxVersion))
            {
                snapAppsReleases.PackVersion = snapxVersion;
            }

            var packageBuilder = new PackageBuilder
            {
                Id = snapApp.BuildNugetReleasesUpstreamId(),
                Version = snapAppsReleases.Version.ToNuGetVersion(),
                Description =
                    $"Snapx application database. This file contains release details for application: {snapApp.Id}. " +
                    $"Channels: {string.Join(", ", snapApp.Channels.Select(x => x.Name))}.",
                Authors = {"Snapx"}
            };

            var snapsReleasesCompressedStream = new MemoryStream();            
            var snapAppsReleasesBytes = _snapAppWriter.ToSnapAppsReleases(snapAppsReleases);
            
            using (var snapsReleasesStream = new MemoryStream(snapAppsReleasesBytes))
            {
                using var writer = WriterFactory.Open(snapsReleasesCompressedStream, ArchiveType.Tar, new WriterOptions(CompressionType.BZip2)
                {
                    LeaveStreamOpen = true
                });
                writer.Write(SnapConstants.ReleasesFilename, snapsReleasesStream);
            }

            AddPackageFile(packageBuilder, snapsReleasesCompressedStream, SnapConstants.NuspecRootTargetPath, SnapConstants.ReleasesFilename);

            var outputStream = new MemoryStream();
            packageBuilder.Save(outputStream);

            outputStream.Seek(0, SeekOrigin.Begin);

            return outputStream;            
        }

        public MemoryStream BuildReleasesPackage(SnapApp snapApp, SnapAppsReleases snapAppsReleases, int? version = null)
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

            snapAppsReleases.Bump(version);
            snapAppsReleases.PackId = Guid.NewGuid();
            snapAppsReleases.PackVersion = null;

            if (SemanticVersion.TryParse(Assembly
                    .GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                    out var snapxVersion))
            {
                snapAppsReleases.PackVersion = snapxVersion;
            }

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
                    throw new Exception($"Invalid file size: {snapRelease.FullSha256Checksum}. Must be greater than zero! Filename: {snapRelease.Filename}");
                }

                if (snapRelease.FullSha256Checksum == null || snapRelease.FullSha256Checksum.Length != 64)
                {
                    throw new Exception($"Invalid checksum: {snapRelease.FullSha256Checksum}. Filename: {snapRelease.Filename}");
                }
            }
            
            var snapsReleasesCompressedStream = new MemoryStream();            
            var snapAppsReleasesBytes = _snapAppWriter.ToSnapAppsReleases(snapAppsReleases);
            
            using (var snapsReleasesStream = new MemoryStream(snapAppsReleasesBytes))
            {
                using var writer = WriterFactory.Open(snapsReleasesCompressedStream, ArchiveType.Tar, new WriterOptions(CompressionType.BZip2)
                {
                    LeaveStreamOpen = true
                });
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
            await using var assemblyStream = await GetSnapAssetAsync(asyncPackageCoreReader, SnapConstants.SnapAppDllFilename, cancellationToken);
            using var assemblyDefinition = AssemblyDefinition.ReadAssembly(assemblyStream, new ReaderParameters(ReadingMode.Immediate));
            var snapApp = assemblyDefinition.GetSnapApp(_snapAppReader);
            return snapApp;
        }

        public async Task<MemoryStream> GetSnapAssetAsync(IAsyncPackageCoreReader asyncPackageCoreReader, string filename,
            CancellationToken cancellationToken = default)
        {
            if (asyncPackageCoreReader == null) throw new ArgumentNullException(nameof(asyncPackageCoreReader));
            if (filename == null) throw new ArgumentNullException(nameof(filename));

            var targetPath = _snapFilesystem.PathCombine(SnapConstants.NuspecAssetsTargetPath, filename);

            return await asyncPackageCoreReader.GetStreamAsync(targetPath, cancellationToken).ReadToEndAsync(cancellationToken: cancellationToken);
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

                var existingSnapReleaseChecksum = snapRelease.Files.SingleOrDefault(x => string.Equals(x.NuspecTargetPath, nuspecTargetPath,StringComparison.OrdinalIgnoreCase));
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

                filename = nuspecTargetPath[(lastSlashIndex + 1)..];
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
                    FullSha256Checksum = _snapCryptoProvider.Sha256(srcStream)
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

            _snapFilesystem.DirectoryExistsThrowIfNotExists(packageDetails.NuspecBaseDirectory);
            _snapFilesystem.DirectoryExistsThrowIfNotExists(packageDetails.PackagesDirectory);
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
