using System;
using System.Collections.Generic;
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
             base($"Checksum mismatch for filename: {checksum.Filename}. Nupkg: {release.Filename}. Nupkg file size: {release.Filesize}.")
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
                 $"Delta file size: {checksum.DeltaFilesize}. Patch file size: {patchStreamFilesize}. Nupkg: {release.Filename}. Nupkg file size: {release.Filesize}.")
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
            base($"Checksum mismatch for nupkg: {release.Filename}. File size: {release.Filesize}.")
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
        Task<MemoryStream> BuildPackageAsync([NotNull] ISnapPackageDetails packageDetails,
            [NotNull] ICoreRunLib coreRunLib, ILog logger = null, CancellationToken cancellationToken = default);
        Task<MemoryStream> RebuildPackageAsync([NotNull] string packagesDirectory, [NotNull] ISnapAppReleases existingAppReleases,
            [NotNull] SnapRelease snapRelease, SnapChannel snapChannel,
            ILog logger = null, CancellationToken cancellationToken = default);
        Task<MemoryStream> BuildFullPackageAsync([NotNull] ISnapPackageDetails packageDetails, [NotNull] SnapRelease snapRelease,
            ICoreRunLib coreRunLib, ILog logger = null, CancellationToken cancellationToken = default);
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

        public async Task<MemoryStream> BuildPackageAsync(ISnapPackageDetails packageDetails, ICoreRunLib coreRunLib,
            ILog logger = null, CancellationToken cancellationToken = default)
        {
            if (packageDetails == null) throw new ArgumentNullException(nameof(packageDetails));
            if (coreRunLib == null) throw new ArgumentNullException(nameof(coreRunLib));

            var snapRelease = new SnapRelease();
            var outputStream = new MemoryStream();
            
            var packageBuilder = await BuildPackageAsyncInternal(packageDetails, snapRelease, coreRunLib, logger, cancellationToken);
            var sha512Checksum = _snapCryptoProvider.Sha512(snapRelease, packageBuilder);
            packageBuilder.Save(outputStream);

            snapRelease.Filesize = outputStream.Length;
            snapRelease.Sha512Checksum = sha512Checksum;

            outputStream.Seek(0, SeekOrigin.Begin);
            
            snapRelease.Sort();

            return outputStream;
        }

        async Task<PackageBuilder> BuildPackageAsyncInternal([NotNull] ISnapPackageDetails packageDetails, [NotNull] SnapRelease snapRelease,
            [NotNull] ICoreRunLib coreRunLib, ILog logger = null,
            CancellationToken cancellationToken = default)
        {
            if (packageDetails == null) throw new ArgumentNullException(nameof(packageDetails));
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
            if (coreRunLib == null) throw new ArgumentNullException(nameof(coreRunLib));

            Validate(packageDetails);

            var snapApp = packageDetails.SnapApp;
            var snapChannel = snapApp.GetDefaultChannelOrThrow();
            var snapReleases = packageDetails.SnapAppsReleases;
            var packagesDirectory = packageDetails.PackagesDirectory;
            var snapAppReleases = snapReleases.GetReleases(snapApp);

            snapRelease.Id = snapApp.Id;
            snapRelease.Version = snapApp.Version;
            snapRelease.UpstreamId = snapApp.BuildNugetUpstreamPackageId();
            snapRelease.Version = snapApp.Version;
            snapRelease.Channels = new List<string> { snapChannel.Name };
            snapRelease.Target = new SnapTarget(snapApp.Target);
            snapApp.IsGenisis = snapRelease.IsGenisis = !snapAppReleases.Any();
            snapRelease.Filename = snapApp.BuildNugetLocalFilename();

            if (snapRelease.IsGenisis)
            {
                snapReleases.Releases.Add(snapRelease);
                return await BuildFullPackageAsyncInternal(packageDetails, snapRelease, coreRunLib, logger, cancellationToken);
            }

            var mostRecentRelease = snapAppReleases.GetMostRecentRelease(snapChannel);
            snapReleases.Releases.Add(snapRelease);

            var previousNupkgPackageBuilder = await RebuildFullPackageAsyncInternal(
                packagesDirectory, snapAppReleases, mostRecentRelease, snapChannel, logger, cancellationToken);

            var currentNupkgPackageBuilder = await BuildFullPackageAsyncInternal(
                packageDetails, snapRelease, coreRunLib, logger, cancellationToken);

            var deltaNupkgBuilder = await BuildDeltaPackageAsyncInternal(
                packagesDirectory,
                mostRecentRelease,
                snapRelease,
                previousNupkgPackageBuilder,
                currentNupkgPackageBuilder, logger, cancellationToken);
                
            return deltaNupkgBuilder;
        }

        async Task<PackageBuilder> BuildFullPackageAsyncInternal([NotNull] ISnapPackageDetails packageDetails, [NotNull] SnapRelease snapRelease,
            ICoreRunLib coreRunLib, ILog logger = null, CancellationToken cancellationToken = default)
        {
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
            EnsureCoreRunSupportsTargetOsPlatform(snapRelease.Target.Os);

            var alwaysRemoveTheseAssemblies = AlwaysRemoveTheseAssemblies.ToList();
            alwaysRemoveTheseAssemblies.Add(_snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(packageDetails.SnapApp));

            var (_, nuspecPropertiesResolver) = BuildNuspecProperties(packageDetails);

            var nuspecIntermediateStream = await _snapFilesystem
                .FileReadAsync(packageDetails.NuspecFilename, cancellationToken);

            var (nuspecStream, packageFiles) =
                GetNuspecStream(packageDetails, nuspecIntermediateStream,
                    nuspecPropertiesResolver, packageDetails.NuspecBaseDirectory, snapRelease, packageDetails.SnapApp);

            using (nuspecIntermediateStream)
            using (nuspecStream)
            {
                var packageBuilder = new PackageBuilder(nuspecStream, packageDetails.NuspecBaseDirectory, nuspecPropertiesResolver);
                packageBuilder.Files.Clear();

                foreach (var (filename, targetPath) in packageFiles)
                {
                    var srcStream = await _snapFilesystem.FileRead(filename).ReadToEndAsync(cancellationToken);
                    AddPackageFile(packageBuilder, srcStream, targetPath, string.Empty, snapRelease);
                }

                var mainExecutableFileName = _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(packageDetails.SnapApp);
                var mainExecutableTargetPath = _snapFilesystem.PathCombine(SnapConstants.NuspecRootTargetPath, mainExecutableFileName).ForwardSlashesSafe();
                var mainExecutablePackageFile = packageBuilder.GetPackageFile(mainExecutableTargetPath);
                if (mainExecutablePackageFile == null)
                {
                    throw new FileNotFoundException("Main executable is missing in nuspec", mainExecutableTargetPath);
                }

                AlwaysRemoveTheseAssemblies.ForEach(targetPath => packageBuilder.RemovePackageFile(targetPath));

                await AddPackageAssetsAsync(coreRunLib, packageBuilder, packageDetails.SnapApp, snapRelease, logger, cancellationToken);

                return packageBuilder;
            }
        }

        async Task<PackageBuilder> BuildDeltaPackageAsyncInternal([NotNull] string packagesDirectory, 
            [NotNull] SnapRelease previousRelease,
            [NotNull] SnapRelease deltaRelease,
            [NotNull] PackageBuilder previousFullNupkgPackageBuilder,
            [NotNull] PackageBuilder currentFullNupkgPackageBuilder,
            ILog logger = null, CancellationToken cancellationToken = default)
        {
            if (packagesDirectory == null) throw new ArgumentNullException(nameof(packagesDirectory));
            if (previousRelease == null) throw new ArgumentNullException(nameof(previousRelease));
            if (deltaRelease == null) throw new ArgumentNullException(nameof(deltaRelease));
            if (previousFullNupkgPackageBuilder == null) throw new ArgumentNullException(nameof(previousFullNupkgPackageBuilder));
            if (currentFullNupkgPackageBuilder == null) throw new ArgumentNullException(nameof(currentFullNupkgPackageBuilder));

            foreach (var currentChecksum in deltaRelease.Files)
            {
                var previousChecksum = previousRelease.Files
                    .SingleOrDefault(x => string.Equals(x.NuspecTargetPath, currentChecksum.NuspecTargetPath, StringComparison.InvariantCulture));
                      
                if (previousChecksum == null)
                {
                    deltaRelease.New.Add(currentChecksum);
                    continue;
                }
         
                if (previousChecksum.FullSha512Checksum.Equals(currentChecksum.FullSha512Checksum, StringComparison.InvariantCulture))
                {
                    deltaRelease.Unmodified.Add(currentChecksum);
                    previousRelease.Files.Remove(previousChecksum);
                    if (!currentFullNupkgPackageBuilder.RemovePackageFile(currentChecksum.NuspecTargetPath))
                    {
                        throw new Exception($"Failed to remove unmodified file: {currentChecksum.NuspecTargetPath}. Nupkg: {deltaRelease.Filename}");
                    }
                    continue;
                }

                var neverGenerateBsDiffThisAssembly =
                    NeverGenerateBsDiffsTheseAssemblies.SingleOrDefault(x => 
                        string.Equals(x, currentChecksum.NuspecTargetPath, StringComparison.InvariantCulture));
                if (neverGenerateBsDiffThisAssembly != null)
                {
                    goto modified;
                }

                var deltaStream = currentFullNupkgPackageBuilder.GetPackageFile(currentChecksum.NuspecTargetPath).GetStream();
                        
                using (var oldDataStream = await previousFullNupkgPackageBuilder.GetPackageFile(previousChecksum.NuspecTargetPath).GetStream().ReadToEndAsync(cancellationToken))
                using (var newDataStream = await deltaStream.ReadToEndAsync(cancellationToken))
                using (var patchStream = new MemoryStream())
                {
                    currentChecksum.FullSha512Checksum = _snapCryptoProvider.Sha512(newDataStream);
                    currentChecksum.FullFilesize = newDataStream.Length;

                    SnapBinaryPatcher.Create(oldDataStream.ToArray(), newDataStream.ToArray(), patchStream);

                    patchStream.Seek(0, SeekOrigin.Begin);
                    deltaStream.Seek(0, SeekOrigin.Begin);
                    deltaStream.SetLength(patchStream.Length);

                    await patchStream.CopyToAsync(deltaStream, cancellationToken);

                    currentChecksum.DeltaSha512Checksum = _snapCryptoProvider.Sha512(deltaStream);
                    currentChecksum.DeltaFilesize = deltaStream.Length;
                }
                
                modified:                
                deltaRelease.Modified.Add(currentChecksum);
                previousRelease.Files.Remove(previousChecksum);
            }

            foreach (var checksum in previousRelease.Files)
            {
                deltaRelease.Deleted.Add(checksum);
            }

            foreach (var checksum in deltaRelease.Unmodified)
            {
                deltaRelease.Files.Remove(checksum);
            }

            return currentFullNupkgPackageBuilder;
        }

        public async Task<MemoryStream> RebuildPackageAsync(string packagesDirectory, ISnapAppReleases existingAppReleases,
            SnapRelease snapRelease, [NotNull] SnapChannel snapChannel, ILog logger = null, CancellationToken cancellationToken = default)
        {
            if (packagesDirectory == null) throw new ArgumentNullException(nameof(packagesDirectory));
            if (existingAppReleases == null) throw new ArgumentNullException(nameof(existingAppReleases));
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));

            var outputStream = new MemoryStream();

            var packageBuilder = await RebuildFullPackageAsyncInternal(packagesDirectory, existingAppReleases, snapRelease, snapChannel, logger, cancellationToken);
            packageBuilder.Save(outputStream);
            outputStream.Seek(0, SeekOrigin.Begin);
            
            snapRelease.Sort();

            return outputStream;
        }

        public async Task<MemoryStream> BuildFullPackageAsync(ISnapPackageDetails packageDetails, SnapRelease snapRelease, [NotNull] ICoreRunLib coreRunLib, ILog logger = null,
            CancellationToken cancellationToken = default)
        {
            if (packageDetails == null) throw new ArgumentNullException(nameof(packageDetails));
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
            if (coreRunLib == null) throw new ArgumentNullException(nameof(coreRunLib));
            
            var outputStream = new MemoryStream();

            var packageBuilder = await BuildFullPackageAsyncInternal(packageDetails, snapRelease, coreRunLib, logger, cancellationToken);
            packageBuilder.Save(outputStream);
            outputStream.Seek(0, SeekOrigin.Begin);
            
            snapRelease.Sort();

            return outputStream;
        }

        async Task<PackageBuilder> RebuildFullPackageAsyncInternal(string packagesDirectory, ISnapAppReleases snapAppReleases, SnapRelease snapRelease,
            [NotNull] SnapChannel snapChannel,
            ILog logger = null, CancellationToken cancellationToken = default)
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

            var genisisRelease = snapAppReleases.First();
            if (!genisisRelease.IsGenisis)
            {
                throw new FileNotFoundException("First release must be genisis release.", snapRelease.Filename);
            }

            var packageBuilder = await BuildPackageFromReleaseAsync(packagesDirectory, snapAppReleases, genisisRelease, logger, cancellationToken);

            if (snapRelease.IsGenisis)
            {
                var genisisReleaseChecksum = _snapCryptoProvider.Sha512(snapRelease, packageBuilder);
                if (genisisReleaseChecksum != snapRelease.Sha512Checksum)
                {
                    throw new SnapReleaseChecksumMismatchException(snapRelease);
                }

                return packageBuilder;
            }

            var deltasToApply = snapAppReleases.GetDeltaReleasesNewerThan(snapChannel, genisisRelease.Version).ToList();
            if (!deltasToApply.Any())
            {
                return packageBuilder;
            }

            foreach (var deltaRelease in deltasToApply)
            {
               var deltaSnapApp = await ApplyDeltaPackageAsync(deltaRelease);
               packageBuilder.Id = deltaSnapApp.BuildNugetUpstreamPackageId();
               packageBuilder.Version = deltaSnapApp.Version.ToNuGetVersion();
            }

            if (packageBuilder.Id != snapRelease.UpstreamId)
            {
                throw new Exception(
                    $"Expected reassembled full nupkg to have the following upstream id: {snapRelease.UpstreamId} but was {packageBuilder.Id}");
            }

            if (packageBuilder.Version != snapRelease.Version)
            {
                throw new Exception(
                    $"Expected reassembled full nupkg to match delta version. Full nupkg version: {snapRelease.Version}. Delta version: {snapRelease.Version}");
            }

            return packageBuilder;

            async Task<SnapApp> ApplyDeltaPackageAsync(SnapRelease deltaRelease)
            {
                if (deltaRelease == null) throw new ArgumentNullException(nameof(deltaRelease));

                if (!deltaRelease.IsDelta)
                {
                    throw new Exception($"Expected to apply a delta release. Nupkg: {deltaRelease.Filename}");
                }

                var deltaAbsolutePath = _snapFilesystem.PathCombine(packagesDirectory, deltaRelease.Filename);
                _snapFilesystem.FileExistsThrowIfNotExists(deltaAbsolutePath);

                using (var packageArchiveReader = new PackageArchiveReader(_snapFilesystem.FileRead(deltaAbsolutePath)))
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
                        if (!packageBuilder.RemovePackageFile(checksum.Filename))
                        {
                            throw new FileNotFoundException($"Unable to delete file. Nupkg: {deltaRelease.Filename}", checksum.Filename);
                        }
                    }
  
                    foreach (var checksum in deltaRelease.New)
                    {
                        var srcStream = await packageArchiveReader.GetStream(checksum.NuspecTargetPath).ReadToEndAsync(cancellationToken);
                        var sha512Checksum = _snapCryptoProvider.Sha512(srcStream);
                        if (checksum.FullSha512Checksum != sha512Checksum)
                        {
                            throw new SnapReleaseFileChecksumMismatchException(checksum, snapRelease);
                        }

                        AddPackageFile(packageBuilder, srcStream, checksum.NuspecTargetPath, checksum.Filename, genisisRelease);
                    }

                    foreach (var checksum in deltaRelease.Modified)
                    {
                        var packageFile = packageBuilder.GetPackageFile(checksum.NuspecTargetPath);
                        var srcStream = packageFile.GetStream();
                        srcStream.Seek(0, SeekOrigin.Begin);

                        var neverGenerateBsDiffThisAssembly =
                            NeverGenerateBsDiffsTheseAssemblies.SingleOrDefault(x => string.Equals(x, checksum.NuspecTargetPath, StringComparison.InvariantCulture));

                        var outputStream = new MemoryStream((int) checksum.FullFilesize);
                        using (var patchStream = await packageArchiveReader.GetStream(checksum.NuspecTargetPath).ReadToEndAsync(cancellationToken))
                        {
                            string sha512Checksum;
                            if (neverGenerateBsDiffThisAssembly != null)
                            {
                                await patchStream.CopyToAsync(outputStream, cancellationToken);
                                
                                sha512Checksum = _snapCryptoProvider.Sha512(outputStream);
                                if (checksum.FullSha512Checksum != sha512Checksum)
                                {
                                    throw new SnapReleaseFileChecksumDeltaMismatchException(checksum, snapRelease, patchStream.Length);
                                }

                                goto done;
                            }
                            
                            sha512Checksum = _snapCryptoProvider.Sha512(patchStream);
                            if (checksum.DeltaSha512Checksum != sha512Checksum)
                            {
                                throw new SnapReleaseFileChecksumDeltaMismatchException(checksum, snapRelease, patchStream.Length);
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
                                
                            SnapBinaryPatcher.Apply(srcStream, OpenPatchStream, outputStream);

                            sha512Checksum = _snapCryptoProvider.Sha512(outputStream);
                            if (checksum.FullSha512Checksum != sha512Checksum)
                            {
                                throw new SnapReleaseFileChecksumMismatchException(checksum, snapRelease);
                            }

                            done:
                            if (!packageBuilder.Files.Remove(packageFile))
                            {
                                throw new FileNotFoundException($"Unable to replace file. Nupkg: {deltaRelease.Filename}", checksum.Filename);
                            }                            
                            AddPackageFile(packageBuilder, outputStream, checksum.NuspecTargetPath, string.Empty, deltaRelease);

                        }
                        
                        packageBuilder.Populate(await packageArchiveReader.GetManifestMetadataAsync(cancellationToken));
                    }
                    
                    return deltaSnapApp;
                }
            }
        }

        async Task<PackageBuilder> BuildPackageFromReleaseAsync([NotNull] string packagesDirectory,
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

                var checksum = _snapCryptoProvider.Sha512(snapRelease, packageArchiveReader, this);
                if (checksum != snapRelease.Sha512Checksum)
                {
                    throw new SnapReleaseChecksumMismatchException(snapRelease);
                }

                var packageBuilder = new PackageBuilder();
                packageBuilder.Populate(await packageArchiveReader.GetManifestMetadataAsync(cancellationToken));

                var packageFiles = await packageArchiveReader.GetFilesAsync(cancellationToken);

                foreach (var targetPath in packageFiles.Where(x => x.StartsWith(SnapConstants.NuspecRootTargetPath, StringComparison.InvariantCulture)))
                {
                    var srcStream = await packageArchiveReader.GetStreamAsync(targetPath, cancellationToken).ReadToEndAsync(cancellationToken);

                    AddPackageFile(packageBuilder, srcStream, targetPath, string.Empty, snapRelease);
                }

                return packageBuilder;
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
                var manifest = Manifest.ReadFrom(nuspecStreamRewritten, propertyProvider, true);

                var outputStream = new MemoryStream();
                manifest.Save(outputStream);
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

                if (checksum.Filesize <= 0)
                {
                    throw new Exception($"Invalid file size: {checksum.Sha512Checksum}. Must be greater than zero! Filename: {checksum.Filename}");
                }
                
                if (checksum.Sha512Checksum == null || checksum.Sha512Checksum.Length != 128)
                {
                    throw new Exception($"Invalid checksum: {checksum.Sha512Checksum}. Filename: {checksum.Filename}");
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

        void EnsureCoreRunSupportsTargetOsPlatform(OSPlatform targetOs)
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

        void AddPackageFile([NotNull] PackageBuilder packageBuilder, [NotNull] MemoryStream srcStream, 
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
    }
}
