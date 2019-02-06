using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
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
    [SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
    internal class SnapPackDeltaReport : IDisposable
    {
        public List<SnapPackFileChecksum> New { get; protected set; }
        public List<SnapPackFileChecksum> Modified { get; protected set; }
        public List<SnapPackFileChecksum> Unmodified { get; protected set; }
        public List<SnapPackFileChecksum> Deleted { get; protected set; }
        public string PreviousNupkgFilename { get; set; }
        public string CurrentNupkgFilename { get; set; }
        public SnapApp PreviousSnapApp { get; }
        public SnapApp CurrentSnapApp { get; }
        public IAsyncPackageCoreReader PreviousNupkgAsyncPackageCoreReader { get; }
        public IAsyncPackageCoreReader CurrentNupkgAsyncPackageCoreReader { get; }
        public string PreviousNupkgSha1Checksum { get; }
        public string CurrentNupkgSha1Checksum { get; }
        public List<SnapPackFileChecksum> PreviousNupkgFileChecksums { get; set; }
        public List<SnapPackFileChecksum> CurrentNupkgFileChecksums { get; set; }

        SnapPackDeltaReport()
        {
            New = new List<SnapPackFileChecksum>();
            Modified = new List<SnapPackFileChecksum>();
            Unmodified = new List<SnapPackFileChecksum>();
            Deleted = new List<SnapPackFileChecksum>();
            PreviousNupkgFileChecksums = new List<SnapPackFileChecksum>();
            CurrentNupkgFileChecksums = new List<SnapPackFileChecksum>();
        }

        public SnapPackDeltaReport(
            [NotNull] string previousNupkgSha1Checksum,
            [NotNull] string currentNupkgSha1Checksum,
            [NotNull] SnapApp previousSnapApp,
            [NotNull] SnapApp currentSnapApp,
            [NotNull] string previousNugpkgFilename,
            [NotNull] string currentNupkgFilename,
            [NotNull] IAsyncPackageCoreReader previousNupkgAsyncPackageCoreReader, 
            [NotNull] IAsyncPackageCoreReader currentNupkgAsyncPackageCoreReader,
            [NotNull] List<SnapPackFileChecksum> previousNupkgFileChecksums,
            [NotNull] List<SnapPackFileChecksum> currentNupkgFileChecksums) : this()
        {
            PreviousNupkgSha1Checksum = previousNupkgSha1Checksum ?? throw new ArgumentNullException(nameof(previousNupkgSha1Checksum));
            CurrentNupkgSha1Checksum = currentNupkgSha1Checksum ?? throw new ArgumentNullException(nameof(currentNupkgSha1Checksum));
            PreviousSnapApp = previousSnapApp ?? throw new ArgumentNullException(nameof(previousSnapApp));
            CurrentSnapApp = currentSnapApp ?? throw new ArgumentNullException(nameof(currentSnapApp));
            PreviousNupkgFilename = previousNugpkgFilename ?? throw new ArgumentNullException(nameof(previousNugpkgFilename));
            CurrentNupkgFilename = currentNupkgFilename ?? throw new ArgumentNullException(nameof(currentNupkgFilename));
            PreviousNupkgAsyncPackageCoreReader = previousNupkgAsyncPackageCoreReader ?? throw new ArgumentNullException(nameof(previousNupkgAsyncPackageCoreReader));
            CurrentNupkgAsyncPackageCoreReader = currentNupkgAsyncPackageCoreReader ?? throw new ArgumentNullException(nameof(currentNupkgAsyncPackageCoreReader));
            PreviousNupkgFileChecksums = previousNupkgFileChecksums ?? throw new ArgumentNullException(nameof(previousNupkgFileChecksums));
            CurrentNupkgFileChecksums = currentNupkgFileChecksums ?? throw new ArgumentNullException(nameof(currentNupkgFileChecksums));
        }

        public void SortAndVerifyIntegrity()
        {
            void ThrowIfNotUnique(string collectionName, IEnumerable<SnapPackFileChecksum> lhss, params List<SnapPackFileChecksum>[] rhss)
            {
                if (collectionName == null) throw new ArgumentNullException(nameof(collectionName));
                if (lhss == null) throw new ArgumentNullException(nameof(lhss));
                if (rhss == null) throw new ArgumentNullException(nameof(rhss));
                
                var duplicates = rhss
                    .SelectMany(rhs => rhs)
                    .SelectMany(rhs => lhss.Where(lhs => lhs.TargetPath.Equals(rhs.TargetPath, StringComparison.Ordinal)))
                    .ToList();
                
                if (!duplicates.Any())
                {
                    return;
                }
                
                var fileChecksum = duplicates.FirstOrDefault();
                
                throw new Exception(
                    $"Duplicate delta report items detected. Source collection: {collectionName}. " +
                            $"Filename: {fileChecksum.TargetPath}. " +
                            $"Duplicates: {duplicates.Count}. ");
            }
            
            // Todo: Remove me after reviewing whether delta reporter have sufficient test coverage.
            ThrowIfNotUnique(nameof(New), New, Modified, Unmodified, Deleted);
            ThrowIfNotUnique(nameof(Modified), Modified, New, Unmodified, Deleted);
            ThrowIfNotUnique(nameof(Unmodified), Unmodified, New, Modified, Deleted);
            ThrowIfNotUnique(nameof(Deleted), Deleted, New, Modified, Unmodified);

            New = New.OrderBy(x => x.TargetPath).ToList();
            Modified = Modified.OrderBy(x => x.TargetPath).ToList();
            Unmodified = Unmodified.OrderBy(x => x.TargetPath).ToList();
            Deleted = Deleted.OrderBy(x => x.TargetPath).ToList();
        }

        public void Dispose()
        {
            PreviousNupkgAsyncPackageCoreReader?.Dispose();
            CurrentNupkgAsyncPackageCoreReader?.Dispose();
        }
    }

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    [DebuggerDisplay("{TargetPath} - {Sha1Checksum}")]
    internal struct SnapPackFileChecksum : IEquatable<SnapPackFileChecksum>
    {
        public readonly string TargetPath;
        public readonly string Sha1Checksum;
        public readonly string Filename;

        public static SnapPackFileChecksum Empty => new SnapPackFileChecksum();

        public static bool operator ==(SnapPackFileChecksum lhs, SnapPackFileChecksum rhs)
        {
            return lhs.Equals(rhs);
        }

        public static bool operator !=(SnapPackFileChecksum lhs, SnapPackFileChecksum rhs)
        {
            return !(lhs == rhs);
        }

        public SnapPackFileChecksum([NotNull] string effectivePath, [NotNull] string filename, [NotNull] string sha1Checksum)
        {
            TargetPath = effectivePath ?? throw new ArgumentNullException(nameof(effectivePath));
            Filename = filename ?? throw new ArgumentNullException(nameof(filename));
            Sha1Checksum = sha1Checksum ?? throw new ArgumentNullException(nameof(sha1Checksum));
        }

        public bool Equals(SnapPackFileChecksum other)
        {
            return string.Equals(TargetPath, other.TargetPath)
                   && string.Equals(Sha1Checksum, other.Sha1Checksum) 
                   && string.Equals(Filename, other.Filename);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            return obj is SnapPackFileChecksum other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (TargetPath != null ? TargetPath.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Sha1Checksum != null ? Sha1Checksum.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Filename != null ? Filename.GetHashCode() : 0);
                return hashCode;
            }
        }
    }

    internal interface ISnapPackageDetails
    {
        SnapApp App { get; }
        string NuspecFilename { get; }
        string NuspecBaseDirectory { get; }
        ISnapProgressSource SnapProgressSource { get; }
        IReadOnlyDictionary<string, string> NuspecProperties { get; }
    }

    internal sealed class SnapPackageDetails : ISnapPackageDetails
    {
        public SnapApp App { get; set; }
        public string NuspecFilename { get; set; }
        public string NuspecBaseDirectory { get; set; }
        public ISnapProgressSource SnapProgressSource { get; set; }
        public IReadOnlyDictionary<string, string> NuspecProperties { get; set; }
    }

    internal interface ISnapPack
    {
        IReadOnlyCollection<string> AlwaysRemoveTheseAssemblies { get; }
        string NuspecTargetFrameworkMoniker { get; }
        string NuspecRootTargetPath { get; }
        string SnapNuspecTargetPath { get; }
        string SnapUniqueTargetPathFolderName { get; }
        string ChecksumManifestFilename { get; }
        Task<MemoryStream> BuildFullPackageAsync(ISnapPackageDetails packageDetails, ILog logger = null, CancellationToken cancellationToken = default);
        Task<SnapPackDeltaReport> BuildDeltaReportAsync(
            [NotNull] string previousNupkgAbsolutePath, [NotNull] string currentNupkgAbsolutePath, CancellationToken cancellationToken = default);
        Task<(MemoryStream memoryStream, SnapApp snapApp)> BuildDeltaPackageAsync([NotNull] string previousNupkgAbsolutePath, [NotNull] string currentNupkgAbsolutePath,
            ISnapProgressSource progressSource = null, CancellationToken cancellationToken = default);
        Task<(MemoryStream outputStream, SnapApp nextSnapApp)> ReassambleFullPackageAsync([NotNull] string deltaNupkgAbsolutePath, [NotNull] string fullNupkgAbsolutePath,
            ISnapProgressSource progressSource = null, CancellationToken cancellationToken = default);
        Task<SnapApp> GetSnapAppAsync(IAsyncPackageCoreReader asyncPackageCoreReader, CancellationToken cancellationToken = default);
        IEnumerable<SnapPackFileChecksum> ParseChecksumManifest(string content);
        Task<int> CountNonNugetFilesAsync(IAsyncPackageCoreReader asyncPackageCoreReader, CancellationToken cancellationToken);
        Task<IEnumerable<string>> GetFilesAsync([NotNull] IAsyncPackageCoreReader asyncPackageCoreReader, CancellationToken cancellationToken);
        Task<IEnumerable<SnapPackFileChecksum>> GetChecksumManifestAsync([NotNull] IAsyncPackageCoreReader asyncPackageCoreReader, CancellationToken cancellationToken);
    }

    internal sealed class SnapPack : ISnapPack
    {
        readonly ISnapFilesystem _snapFilesystem;
        readonly ISnapAppReader _snapAppReader;
        readonly ISnapAppWriter _snapAppWriter;
        readonly ISnapCryptoProvider _snapCryptoProvider;
        readonly ISnapEmbeddedResources _snapEmbeddedResources;

        public IReadOnlyCollection<string> AlwaysRemoveTheseAssemblies => new List<string>
        {
            _snapFilesystem.PathCombine(NuspecRootTargetPath, _snapAppWriter.SnapDllFilename).ForwardSlashesSafe(),
            _snapFilesystem.PathCombine(NuspecRootTargetPath, _snapAppWriter.SnapAppDllFilename).ForwardSlashesSafe()
        };

        public IReadOnlyCollection<string> NeverGenerateBsDiffsTheseAssemblies => new List<string>
        {
            _snapFilesystem.PathCombine(SnapNuspecTargetPath, _snapAppWriter.SnapAppDllFilename).ForwardSlashesSafe()
        };

        public string NuspecTargetFrameworkMoniker { get; }
        public string NuspecRootTargetPath { get; }
        public string SnapNuspecTargetPath { get; }
        public string SnapUniqueTargetPathFolderName { get; }
        public string ChecksumManifestFilename { get; }

        public SnapPack(ISnapFilesystem snapFilesystem, [NotNull] ISnapAppReader snapAppReader, [NotNull] ISnapAppWriter snapAppWriter,
            [NotNull] ISnapCryptoProvider snapCryptoProvider, [NotNull] ISnapEmbeddedResources snapEmbeddedResources)
        {
            _snapFilesystem = snapFilesystem ?? throw new ArgumentNullException(nameof(snapFilesystem));
            _snapAppReader = snapAppReader ?? throw new ArgumentNullException(nameof(snapAppReader));
            _snapAppWriter = snapAppWriter ?? throw new ArgumentNullException(nameof(snapAppWriter));
            _snapCryptoProvider = snapCryptoProvider ?? throw new ArgumentNullException(nameof(snapCryptoProvider));
            _snapEmbeddedResources = snapEmbeddedResources ?? throw new ArgumentNullException(nameof(snapEmbeddedResources));

            SnapUniqueTargetPathFolderName = BuildSnapNuspecUniqueFolderName();
            NuspecTargetFrameworkMoniker = NuGetFramework.AnyFramework.Framework;
            NuspecRootTargetPath = snapFilesystem.PathCombine("lib", NuspecTargetFrameworkMoniker).ForwardSlashesSafe();
            SnapNuspecTargetPath = snapFilesystem.PathCombine(NuspecRootTargetPath, SnapUniqueTargetPathFolderName).ForwardSlashesSafe();
            ChecksumManifestFilename = "Snap.Checksum.Manifest";
        }

        public async Task<MemoryStream> BuildFullPackageAsync(ISnapPackageDetails packageDetails, ILog logger = null, CancellationToken cancellationToken = default)
        {
            if (packageDetails == null) throw new ArgumentNullException(nameof(packageDetails));

            var sw = new Stopwatch();
            sw.Restart();
            
            packageDetails.SnapProgressSource?.Raise(0);
            logger?.Info($"Building nuspec properties");
            
            var (_, nuspecPropertiesResolver) = BuildNuspecProperties(packageDetails);

            var outputStream = new MemoryStream();
            var progressSource = packageDetails.SnapProgressSource;

            var alwaysRemoveTheseAssemblies = AlwaysRemoveTheseAssemblies.ToList();
            alwaysRemoveTheseAssemblies.Add(_snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(packageDetails.App));

            logger?.Info($"Assemblies that will be replaced in nupkg: {string.Join(",", alwaysRemoveTheseAssemblies)}");

            progressSource?.Raise(10);

            logger?.Info($"Rewriting nuspec: {packageDetails.NuspecFilename}.");

            using (var nuspecIntermediateStream = await _snapFilesystem.FileReadAsync(packageDetails.NuspecFilename, cancellationToken))
            using (var nuspecStream = RewriteNuspec(packageDetails, nuspecIntermediateStream, nuspecPropertiesResolver, packageDetails.NuspecBaseDirectory))
            {
                progressSource?.Raise(30);

                logger?.Info($"Building nupkg using base directory: {packageDetails.NuspecBaseDirectory}");

                var packageBuilder = new PackageBuilder(nuspecStream, packageDetails.NuspecBaseDirectory, nuspecPropertiesResolver);

                var mainExecutableTargetPath = _snapFilesystem.PathCombine(NuspecRootTargetPath, _snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(packageDetails.App)).ForwardSlashesSafe();
                var mainExecutablePackageFile = packageBuilder.GetPackageFile(mainExecutableTargetPath);
                if (mainExecutablePackageFile == null)
                {
                    throw new Exception($"Main executable is missing in nuspec: {mainExecutableTargetPath}");
                }

                progressSource?.Raise(40);
                EnsureCoreRunSupportsThisPlatform();
                
                progressSource?.Raise(50);
                logger?.Info($"Replacing assemblies in nupkg: {string.Join(",", alwaysRemoveTheseAssemblies)}");
                AlwaysRemoveTheseAssemblies.ForEach(targetPath => packageBuilder.Files.Remove(new PhysicalPackageFile {TargetPath = targetPath}));
                
                progressSource?.Raise(60);
                logger?.Info($"Adding snap assemblies");
                await AddSnapAssemblies(packageBuilder, packageDetails.App, cancellationToken);
 
                progressSource?.Raise(70);
                logger?.Info($"Adding checkingsum manifest");
                await AddChecksumManifestAsync(packageBuilder, cancellationToken);

                logger?.Info($"Saving nupkg to stream");
                progressSource?.Raise(80);
                
                packageBuilder.Save(outputStream);
                outputStream.Seek(0, SeekOrigin.Begin);

                progressSource?.Raise(100);
                logger?.Info($"Nupkg has been successfully releasified: {packageDetails.App.BuildNugetLocalFilename()} in {sw.Elapsed.TotalSeconds:F1}s.");

                return outputStream;
            }
        }

        public async Task<SnapPackDeltaReport> BuildDeltaReportAsync(string previousNupkgAbsolutePath, string currentNupkgAbsolutePath, CancellationToken cancellationToken = default)
        {
            if (previousNupkgAbsolutePath == null) throw new ArgumentNullException(nameof(previousNupkgAbsolutePath));
            if (currentNupkgAbsolutePath == null) throw new ArgumentNullException(nameof(currentNupkgAbsolutePath));

            var previousNupkgStream = _snapFilesystem.FileRead(previousNupkgAbsolutePath);
            var currentNupkgMemoryStream = _snapFilesystem.FileRead(currentNupkgAbsolutePath);

            var previousNupkgSha1Checksum = _snapCryptoProvider.Sha1(previousNupkgStream);
            var currentNupkgSha1Checksum = _snapCryptoProvider.Sha1(currentNupkgMemoryStream);

            var previousNupkgPackageArchiveReader = new PackageArchiveReader(previousNupkgStream);
            var currentNupkgPackageArchiveReader = new PackageArchiveReader(currentNupkgMemoryStream);
            
            var previousSnapApp = await GetSnapAppAsync(previousNupkgPackageArchiveReader, cancellationToken);
            if (previousSnapApp == null)
            {
                throw new Exception($"Unable to build snap app from previous nupkg: {previousNupkgAbsolutePath}");
            }

            var currentSnapApp = await GetSnapAppAsync(currentNupkgPackageArchiveReader, cancellationToken);
            if (currentSnapApp == null)
            {
                throw new Exception($"Unable to build snap app from current nupkg: {currentNupkgAbsolutePath}");
            }

            if (previousSnapApp.Version > currentSnapApp.Version)
            {
                var message = $"You cannot build a delta package based on version {previousSnapApp.Version} " +
                              $"as it is a later version than {currentSnapApp.Version}";
                throw new Exception(message);
            }

            if (previousSnapApp.Target.Os != currentSnapApp.Target.Os)
            {
                var message = "You cannot build a delta package between two packages that target different operating systems. " +
                              $"Previous os: {previousSnapApp.Target.Os}. Current os: {currentSnapApp.Target.Os}";
                throw new Exception(message);
            }
            
            if (previousSnapApp.Target.Rid != currentSnapApp.Target.Rid)
            {
                var message = "You cannot build a delta package between two packages that target different runtime identifiers. " +
                              $"Previous rid: {previousSnapApp.Target.Rid}. Current rid: {currentSnapApp.Target.Rid}";
                throw new Exception(message);
            }

            var previousChecksums = (await GetChecksumManifestAsync(previousNupkgPackageArchiveReader, cancellationToken)).ToList();
            var currentChecksums = (await GetChecksumManifestAsync(currentNupkgPackageArchiveReader, cancellationToken)).ToList();

            var deltaReport = new SnapPackDeltaReport(
                previousNupkgSha1Checksum,
                currentNupkgSha1Checksum,
                previousSnapApp,
                currentSnapApp,
                previousSnapApp.BuildNugetLocalFilename(),
                currentSnapApp.BuildNugetLocalFilename(),
                previousNupkgPackageArchiveReader,
                currentNupkgPackageArchiveReader,
                previousChecksums,
                currentChecksums);

            foreach (var current in currentChecksums)
            {
                var neverGenerateBsDiffThisAssembly =
                    NeverGenerateBsDiffsTheseAssemblies.SingleOrDefault(x => string.Equals(x, current.TargetPath, StringComparison.Ordinal));
                var previous = previousChecksums.SingleOrDefault(x => string.Equals(x.TargetPath, current.TargetPath, StringComparison.Ordinal));
                if (neverGenerateBsDiffThisAssembly != null)
                {
                    deltaReport.New.Add(current);
                    goto next;
                }
                
                if (previous == SnapPackFileChecksum.Empty)
                {
                    deltaReport.New.Add(current);
                    goto next;
                }

                if (!current.Sha1Checksum.Equals(previous.Sha1Checksum, StringComparison.Ordinal))
                {
                    deltaReport.Modified.Add(current);
                    goto next;
                }

                deltaReport.Unmodified.Add(current);
                
                next:
                previousChecksums.Remove(previous);
            }

            foreach (var previous in previousChecksums)
            {
                deltaReport.Deleted.Add(previous);                
            }

            deltaReport.SortAndVerifyIntegrity();

            return deltaReport;
        }

        public async Task<(MemoryStream memoryStream, SnapApp snapApp)> BuildDeltaPackageAsync(string previousNupkgAbsolutePath,
            string currentNupkgAbsolutePath,
            ISnapProgressSource progressSource = null,
            CancellationToken cancellationToken = default)
        {
            if (previousNupkgAbsolutePath == null) throw new ArgumentNullException(nameof(previousNupkgAbsolutePath));
            if (currentNupkgAbsolutePath == null) throw new ArgumentNullException(nameof(currentNupkgAbsolutePath));

            progressSource?.Raise(0);
            
            var deltaReport = await BuildDeltaReportAsync(previousNupkgAbsolutePath, currentNupkgAbsolutePath, cancellationToken);
            if (deltaReport == null)
            {
                throw new Exception(
                    $"Unknown error building delta report between previous and current nupkg. Previous: {previousNupkgAbsolutePath}. Current: {currentNupkgAbsolutePath}");
            }

            if (deltaReport.PreviousNupkgSha1Checksum == deltaReport.CurrentNupkgSha1Checksum)
            {
                throw new Exception("Unable to build delta package because previous and current nupkg is the same nupkg. " +
                                    $"Previous: {previousNupkgAbsolutePath} ({deltaReport.PreviousNupkgSha1Checksum}). " +
                                    $"Current: {currentNupkgAbsolutePath} ({deltaReport.CurrentNupkgSha1Checksum}). ");
            }

            progressSource?.Raise(30);

            using (deltaReport)
            {
                var currentManifestData = await deltaReport.CurrentNupkgAsyncPackageCoreReader.GetManifestMetadataAsync(cancellationToken);
                if (currentManifestData == null)
                {
                    throw new Exception($"Unable to extract manifest data from current nupkg: {currentNupkgAbsolutePath}");
                }

                progressSource?.Raise(50);

                var snapApp = new SnapApp(deltaReport.CurrentSnapApp)
                {
                    DeltaReport = new SnapAppDeltaReport(deltaReport)
                };

                var packageBuilder = new PackageBuilder();
                packageBuilder.Populate(currentManifestData);
                packageBuilder.Id = snapApp.BuildNugetUpstreamPackageId();
                
                progressSource?.Raise(60);

                // New
                foreach (var file in deltaReport.New)
                {
                    MemoryStream srcStream;

                    var neverGenerateBsDiffThisAssembly =
                        NeverGenerateBsDiffsTheseAssemblies.SingleOrDefault(x => string.Equals(x, file.TargetPath, StringComparison.Ordinal));
                    if (neverGenerateBsDiffThisAssembly != null)
                    {
                        var isSnapAppDllTargetPath = neverGenerateBsDiffThisAssembly.EndsWith(_snapAppWriter.SnapAppDllFilename, StringComparison.Ordinal);
                        if (isSnapAppDllTargetPath)
                        {
                            using (var snapAppDllAssembly = _snapAppWriter.BuildSnapAppAssembly(snapApp))
                            {
                                srcStream = new MemoryStream();
                                
                                snapAppDllAssembly.Write(srcStream);
                                srcStream.Seek(0, SeekOrigin.Begin);
                                
                                packageBuilder.Files.Add(BuildInMemoryPackageFile(srcStream, file.TargetPath, string.Empty));

                                continue;
                            }
                        }
                        
                        throw new InvalidOperationException($"Fatal error! Expected to replace assembly: {neverGenerateBsDiffThisAssembly}");
                    }
   
                    srcStream = await deltaReport.CurrentNupkgAsyncPackageCoreReader.GetStreamAsync(file.TargetPath, cancellationToken).ReadToEndAsync(cancellationToken, true);
                    packageBuilder.Files.Add(BuildInMemoryPackageFile(srcStream, file.TargetPath, string.Empty));
                }
                
                progressSource?.Raise(70);
                
                // Modified               
                foreach (var file in deltaReport.Modified)
                {
                    var deltaStream = new MemoryStream();
                    using (var oldDataStream = await deltaReport.PreviousNupkgAsyncPackageCoreReader.GetStreamAsync(file.TargetPath, cancellationToken).ReadToEndAsync(cancellationToken, true))
                    using (var newDataStream = await deltaReport.CurrentNupkgAsyncPackageCoreReader.GetStreamAsync(file.TargetPath, cancellationToken).ReadToEndAsync(cancellationToken, true))
                    {
                        SnapBinaryPatcher.Create(oldDataStream.ToArray(), newDataStream.ToArray(), deltaStream);
                    }
                    deltaStream.Seek(0, SeekOrigin.Begin);
                    packageBuilder.Files.Add(BuildInMemoryPackageFile(deltaStream, file.TargetPath, string.Empty));
                }
                
                EnsureCoreRunSupportsThisPlatform();
   
                await AddChecksumManifestAsync(packageBuilder, cancellationToken);

                progressSource?.Raise(80);
                
                var outputStream = new MemoryStream();
                packageBuilder.Save(outputStream);

                outputStream.Seek(0, SeekOrigin.Begin);

                progressSource?.Raise(100);

                return (outputStream, snapApp);
            }
        }

        public async Task<(MemoryStream outputStream, SnapApp nextSnapApp)> ReassambleFullPackageAsync(string deltaNupkgAbsolutePath, string fullNupkgAbsolutePath,
            ISnapProgressSource progressSource = null,
            CancellationToken cancellationToken = default)
        {
            if (deltaNupkgAbsolutePath == null) throw new ArgumentNullException(nameof(deltaNupkgAbsolutePath));
            if (fullNupkgAbsolutePath == null) throw new ArgumentNullException(nameof(fullNupkgAbsolutePath));

            progressSource?.Raise(0);
            
            var deltaNupkgStream = _snapFilesystem.FileRead(deltaNupkgAbsolutePath);
            var fullNupkgStream = _snapFilesystem.FileRead(fullNupkgAbsolutePath);

            // Checksumming has to happen before reading nupkgs because
            // the reader is not immutable and modifies the stream somehow.
            var fullNupkgSha1Checksum = _snapCryptoProvider.Sha1(fullNupkgStream);

            progressSource?.Raise(10);

            var deltaCoreReader = new PackageArchiveReader(deltaNupkgStream);
            var fullNupkgCoreReader = new PackageArchiveReader(fullNupkgStream);

            var deltaSnapApp = await GetSnapAppAsync(deltaCoreReader, cancellationToken);

            if (!deltaSnapApp.Delta)
            {
                throw new Exception("The delta package specified is not a delta package. " +                                     
                                    $"Delta nupkg: {deltaNupkgAbsolutePath}. " +
                                    $"Full nupkg: {fullNupkgAbsolutePath}. ");
            }

            var fullSnapApp = await GetSnapAppAsync(fullNupkgCoreReader, cancellationToken);

            if (deltaSnapApp.DeltaReport.FullNupkgFilename != fullSnapApp.BuildNugetLocalFilename())
            {
                throw new Exception(
                    "Current full nupkg filename does not match delta package filename. " +
                    $"Expected: {deltaSnapApp.DeltaReport.FullNupkgFilename} but was {fullSnapApp.BuildNugetLocalFilename()}. " +
                    $"Delta nupkg: {deltaNupkgAbsolutePath}. " +
                    $"Full nupkg: {fullNupkgAbsolutePath}. ");
            }

            if (fullNupkgSha1Checksum != deltaSnapApp.DeltaReport.FullNupkgSha1Checksum)
            {
                throw new Exception("Checksum mismatch for specified full nupkg. " +
                                    $"Expected SHA1 checksum: {deltaSnapApp.DeltaReport.FullNupkgSha1Checksum} but was {fullNupkgSha1Checksum}. " +
                                    $"Delta nupkg: {deltaNupkgAbsolutePath}. " +
                                    $"Full nupkg: {fullNupkgAbsolutePath}. ");
            }

            progressSource?.Raise(30);
            
            var reassembledSnapApp = new SnapApp(deltaSnapApp)
            {
                DeltaReport = null
            };
            
            var currentManifestData = await deltaCoreReader.GetManifestMetadataAsync(cancellationToken);
            if (currentManifestData == null)
            {
                throw new Exception($"Unable to extract manifest data from current nupkg: {deltaNupkgAbsolutePath}");
            }
            
            var packageBuilder = new PackageBuilder();
            packageBuilder.Populate(currentManifestData);
            packageBuilder.Id = reassembledSnapApp.BuildNugetUpstreamPackageId();

            progressSource?.Raise(50);

            // New
            foreach (var targetPath in deltaSnapApp.DeltaReport.New)
            {
                var srcStream = await deltaCoreReader.GetStreamAsync(targetPath, cancellationToken).ReadToEndAsync(cancellationToken, true);
                packageBuilder.Files.Add(BuildInMemoryPackageFile(srcStream, targetPath, string.Empty));
            }
            
            // Modified
            foreach (var targetPath in deltaSnapApp.DeltaReport.Modified)
            {
                var deltaStream = new MemoryStream();
                using (var oldDataStream = await fullNupkgCoreReader.GetStreamAsync(targetPath, cancellationToken).ReadToEndAsync(cancellationToken, true))
                using (var patchDataStream = await deltaCoreReader.GetStreamAsync(targetPath, cancellationToken).ReadToEndAsync(cancellationToken, true))
                {
                    SnapBinaryPatcher.Apply(oldDataStream, () => patchDataStream.DuplicateStream(), deltaStream);
                }
                deltaStream.Seek(0, SeekOrigin.Begin);
                packageBuilder.Files.Add(BuildInMemoryPackageFile(deltaStream, targetPath, string.Empty));
            }

            progressSource?.Raise(60);

            // Unmodified
            foreach (var targetPath in deltaSnapApp.DeltaReport.Unmodified)
            {
                var srcStream = await fullNupkgCoreReader.GetStreamAsync(targetPath, cancellationToken).ReadToEndAsync(cancellationToken, true);
                packageBuilder.Files.Add(BuildInMemoryPackageFile(srcStream, targetPath, string.Empty));
            }

            progressSource?.Raise(70);

            EnsureCoreRunSupportsThisPlatform();
            
            await AddChecksumManifestAsync(packageBuilder, cancellationToken);

            progressSource?.Raise(80);
                
            var outputStream = new MemoryStream();
            packageBuilder.Save(outputStream);

            outputStream.Seek(0, SeekOrigin.Begin);

            progressSource?.Raise(100);
            
            return (outputStream, nextSnapApp: reassembledSnapApp);
        }

        public async Task<IEnumerable<SnapPackFileChecksum>> GetChecksumManifestAsync(
            [NotNull] IAsyncPackageCoreReader asyncPackageCoreReader, CancellationToken cancellationToken)
        {
            if (asyncPackageCoreReader == null) throw new ArgumentNullException(nameof(asyncPackageCoreReader));
            var targetPath = _snapFilesystem.PathCombine(SnapNuspecTargetPath, ChecksumManifestFilename);
            using (var inputStream = await asyncPackageCoreReader.GetStreamAsync(targetPath, cancellationToken).ReadToEndAsync(cancellationToken, true))
            using (var streamReader = new StreamReader(inputStream))
            {
                var checksumManifestUtf8Content = await streamReader.ReadToEndAsync();
                return ParseChecksumManifest(checksumManifestUtf8Content);
            }
        }

        public async Task<SnapApp> GetSnapAppAsync([NotNull] IAsyncPackageCoreReader asyncPackageCoreReader, CancellationToken cancellationToken = default)
        {
            if (asyncPackageCoreReader == null) throw new ArgumentNullException(nameof(asyncPackageCoreReader));

            var targetPath = _snapFilesystem.PathCombine(SnapNuspecTargetPath, _snapAppWriter.SnapAppDllFilename);
            
            using (var assemblyStream = await asyncPackageCoreReader.GetStreamAsync(targetPath, cancellationToken).ReadToEndAsync(cancellationToken, true))
            using (var assemblyDefinition = AssemblyDefinition.ReadAssembly(assemblyStream, new ReaderParameters(ReadingMode.Immediate)))
            {
                var snapApp = assemblyDefinition.GetSnapApp(_snapAppReader, _snapAppWriter);
                return snapApp;
            }
        }

        MemoryStream RewriteNuspec([NotNull] ISnapPackageDetails packageDetails, MemoryStream nuspecStream,
            [NotNull] Func<string, string> propertyProvider, [NotNull] string baseDirectory)
        {
            if (packageDetails == null) throw new ArgumentNullException(nameof(packageDetails));
            if (nuspecStream == null) throw new ArgumentNullException(nameof(nuspecStream));
            if (propertyProvider == null) throw new ArgumentNullException(nameof(propertyProvider));
            if (baseDirectory == null) throw new ArgumentNullException(nameof(baseDirectory));

            const string nuspecXmlNs = "http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd";
            
            var nugetVersion = new NuGetVersion(packageDetails.App.Version.ToFullString());
            var upstreamPackageId = packageDetails.App.BuildNugetUpstreamPackageId();

            MemoryStream RewriteNuspecStreamWithEssentials()
            {
                var nuspecDocument = XmlUtility.LoadSafe(nuspecStream);
                if (nuspecDocument == null)
                {
                    throw new Exception("Failed to parse nuspec");
                }

                var metadata = nuspecDocument.Descendants(XName.Get("metadata", nuspecXmlNs)).SingleOrDefault();
                if (metadata == null)
                {
                    throw new Exception("The required element 'metadata' is missing from the nuspec");
                }

                var id = metadata.Descendants(XName.Get("id", nuspecXmlNs)).SingleOrDefault();
                if (id != null)
                {
                    id.Value = upstreamPackageId;
                }
                else
                {
                    metadata.Add(new XElement("id", upstreamPackageId));
                }

                var title = metadata.Descendants(XName.Get("title", nuspecXmlNs)).SingleOrDefault();
                if (title == null)
                {
                    throw new Exception($"The required element 'description' is missing from the nuspec");
                }
                
                var version = metadata.Descendants(XName.Get("version", nuspecXmlNs)).SingleOrDefault();
                if (version == null)
                {
                    metadata.Add(new XElement("version", nugetVersion.ToFullString()));
                }
                else
                {
                    version.Value = nugetVersion.ToFullString();
                }

                var description = metadata.Descendants(XName.Get("description", nuspecXmlNs)).SingleOrDefault();
                if (description == null)
                {
                    metadata.Add(new XElement("description", title.Value));
                }

                var files = nuspecDocument.Descendants(XName.Get("files", nuspecXmlNs)).SingleOrDefault();
                if (files == null)
                {
                    throw new Exception($"The required element 'description' is missing from the nuspec");
                }

                foreach (var file in files.Descendants())
                {                    
                    var targetAttribute = file.Attribute("target");
                    if (targetAttribute == null)
                    {
                        file.SetAttributeValue("target", "$anytarget$");
                        continue;
                    }

                    var targetAttributeValue = !targetAttribute.Value.ForwardSlashesSafe().StartsWith("/") ? $"/{targetAttribute.Value}" : targetAttribute.Value.ForwardSlashesSafe();
                    file.SetAttributeValue("target", $"$anytarget${targetAttributeValue}");
                }
                
                var rewrittenNuspecStream = new MemoryStream();
                nuspecDocument.Save(rewrittenNuspecStream);
                rewrittenNuspecStream.Seek(0, SeekOrigin.Begin);

                return rewrittenNuspecStream;
            }

            using (var nuspecStreamRewritten = RewriteNuspecStreamWithEssentials())
            {
                var manifest = Manifest.ReadFrom(nuspecStreamRewritten, propertyProvider, true);

                if (!manifest.Files.Any())
                {
                    throw new Exception("Nuspec does not contain any files");
                }

                var outputStream = new MemoryStream();
                manifest.Save(outputStream);
                outputStream.Seek(0, SeekOrigin.Begin);

                return outputStream;
            }                      
        }

        public IEnumerable<SnapPackFileChecksum> ParseChecksumManifest([NotNull] string content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            return content
                .Split(_snapFilesystem.FixedNewlineChar)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Split(':'))
                .Select(x => new SnapPackFileChecksum(x[0], x[1], x[2]))
                .OrderBy(x => x.TargetPath);
        }

        public async Task<int> CountNonNugetFilesAsync([NotNull] IAsyncPackageCoreReader asyncPackageCoreReader, CancellationToken cancellationToken)
        {
            if (asyncPackageCoreReader == null) throw new ArgumentNullException(nameof(asyncPackageCoreReader));
            return (await GetFilesAsync(asyncPackageCoreReader, cancellationToken)).Count(x => x.StartsWith(NuspecRootTargetPath));
        }

        public async Task<IEnumerable<string>> GetFilesAsync(IAsyncPackageCoreReader asyncPackageCoreReader, CancellationToken cancellationToken)
        {
            if (asyncPackageCoreReader == null) throw new ArgumentNullException(nameof(asyncPackageCoreReader));
            return (await asyncPackageCoreReader.GetFilesAsync(cancellationToken));
        }
        
        void EnsureCoreRunSupportsThisPlatform()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
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

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
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

        async Task AddChecksumManifestAsync([NotNull] PackageBuilder packageBuilder, CancellationToken cancellationToken)
        {
            if (packageBuilder == null) throw new ArgumentNullException(nameof(packageBuilder));

            var stringBuilder = new StringBuilder();

            var packageFiles = packageBuilder.Files.Select(x =>
            {
                Stream stream;
                string targetPath;
                string filename;
                switch (x)
                {
                    case PhysicalPackageFile physicalPackageFile:
                        stream = physicalPackageFile.GetStream();
                        targetPath = physicalPackageFile.TargetPath;
                        filename = _snapFilesystem.PathGetFileName(targetPath);
                        break;
                    case InMemoryPackageFile inMemoryPackage:
                        stream = inMemoryPackage.GetStream();
                        targetPath = inMemoryPackage.EffectivePath;
                        filename = _snapFilesystem.PathGetFileName(targetPath);
                        break;
                    default:
                        throw new NotSupportedException($"Unknown package file type: {x.GetType().FullName}");
                }

                return (stream, filename, targetPath: targetPath.ForwardSlashesSafe());
            }).OrderBy(x => x.targetPath);

            foreach (var (inputStream, filename, targetPath) in packageFiles)
            {
                using (var shaStream = await inputStream.ReadToEndAsync(cancellationToken, true))
                {
                    stringBuilder.Append($"{targetPath}:{filename}:{_snapCryptoProvider.Sha1(shaStream)}");
                    stringBuilder.Append(_snapFilesystem.FixedNewlineChar);
                }

                inputStream.Seek(0, SeekOrigin.Begin);
            }

            var checksumStream = new MemoryStream(Encoding.UTF8.GetBytes(stringBuilder.ToString()));
            packageBuilder.Files.Add(BuildInMemoryPackageFile(checksumStream, SnapNuspecTargetPath, ChecksumManifestFilename));
        }

        async Task AddSnapAssemblies([NotNull] PackageBuilder packageBuilder, [NotNull] SnapApp snapApp, CancellationToken cancellationToken)
        {
            if (packageBuilder == null) throw new ArgumentNullException(nameof(packageBuilder));
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));

            if (snapApp.Delta)
            {
                throw new Exception("It's illegal to add snap assemblies to a delta package");
            }
            
            // Snap.dll
            using (var snapDllAssemblyDefinition = await _snapFilesystem.FileReadAssemblyDefinitionAsync(typeof(SnapPack).Assembly.Location, cancellationToken))
            using (var snapDllAssemblyDefinitionOptimized =
                _snapAppWriter.OptimizeSnapDllForPackageArchive(snapDllAssemblyDefinition, snapApp.Target.Os))
            {
                var snapDllOptimizedMemoryStream = new MemoryStream();
                snapDllAssemblyDefinitionOptimized.Write(snapDllOptimizedMemoryStream);
                snapDllOptimizedMemoryStream.Seek(0, SeekOrigin.Begin);

                packageBuilder.Files.Add(BuildInMemoryPackageFile(snapDllOptimizedMemoryStream, SnapNuspecTargetPath, _snapAppWriter.SnapDllFilename));
            }     
            
            // Snap.App.dll
            using (var snapAppDllAssembly = _snapAppWriter.BuildSnapAppAssembly(snapApp))
            {
                var snapAppMemoryStream = new MemoryStream();
                snapAppDllAssembly.Write(snapAppMemoryStream);

                packageBuilder.Files.Add(BuildInMemoryPackageFile(snapAppMemoryStream, SnapNuspecTargetPath, _snapAppWriter.SnapAppDllFilename));
            }
            
            // Corerun
            var (coreRunStream, coreRunFilename, _) = _snapEmbeddedResources.GetCoreRunForSnapApp(snapApp);
            packageBuilder.Files.Add(BuildInMemoryPackageFile(coreRunStream, SnapNuspecTargetPath, coreRunFilename));
            
        }

        InMemoryPackageFile BuildInMemoryPackageFile(MemoryStream memoryStream, string targetPath, string filename)
        {
            if (memoryStream == null) throw new ArgumentNullException(nameof(memoryStream));

            memoryStream.Seek(0, SeekOrigin.Begin);

            var nuGetFramework = NuGetFramework.Parse(NuspecTargetFrameworkMoniker);
            targetPath = _snapFilesystem.PathCombine(targetPath, filename).ForwardSlashesSafe();

            return new InMemoryPackageFile(memoryStream, targetPath, nuGetFramework);
        }

        static string BuildSnapNuspecUniqueFolderName()
        {
            var guidStr = typeof(SnapPack).Assembly.GetCustomAttribute<GuidAttribute>()?.Value;
            Guid.TryParse(guidStr, out var assemblyGuid);
            if (assemblyGuid == Guid.Empty)
            {
                throw new Exception("Fatal error! Assembly guid is empty");
            }

            return assemblyGuid.ToString("N");
        }

        void ValidatePackageDetails([NotNull] ISnapPackageDetails packageDetails)
        {
            if (packageDetails == null) throw new ArgumentNullException(nameof(packageDetails));

            if (packageDetails.NuspecBaseDirectory == null ||
                !_snapFilesystem.DirectoryExists(packageDetails.NuspecBaseDirectory))
            {
                throw new DirectoryNotFoundException($"Unable to find base directory: {packageDetails.NuspecBaseDirectory}");
            }

            if (!_snapFilesystem.FileExists(packageDetails.NuspecFilename))
            {
                throw new FileNotFoundException($"Unable to find nuspec filename: {packageDetails.NuspecFilename}");
            }

            if (packageDetails.App == null)
            {
                throw new Exception("Snap app cannot be null");
            }

            if (packageDetails.App.Version == null)
            {
                throw new Exception("Snap app version cannot be null");
            }

            if (!packageDetails.App.IsValidAppId())
            {
                throw new Exception($"Snap id is invalid: {packageDetails.App.Id}");
            }

            if (!packageDetails.App.IsValidChannelName())
            {
                throw new Exception($"Invalid channel name: {packageDetails.App.GetCurrentChannelOrThrow().Name}. Snap id: {packageDetails.App.Id}");
            }
        }

        (Dictionary<string, string> properties, Func<string, string> propertiesResolverFunc) BuildNuspecProperties([NotNull] ISnapPackageDetails packageDetails)
        {
            if (packageDetails == null) throw new ArgumentNullException(nameof(packageDetails));

            ValidatePackageDetails(packageDetails);

            var nuspecProperties = new Dictionary<string, string>
            {
                {"version", packageDetails.App.Version.ToFullString()},
                {"basedirectory", packageDetails.NuspecBaseDirectory},
                {"anytarget", NuspecRootTargetPath}
            };

            if (packageDetails.NuspecProperties != null)
            {
                foreach (var pair in packageDetails.NuspecProperties)
                {
                    if (!nuspecProperties.ContainsKey(pair.Key.ToLowerInvariant()))
                    {
                        nuspecProperties.Add(pair.Key, pair.Value);
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
