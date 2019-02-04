using System;
using System.Collections;
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
using JetBrains.Annotations;
using Mono.Cecil;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using Snap.Core.Models;
using Snap.Core.Resources;
using Snap.Extensions;
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
        public IAsyncPackageCoreReader PreviousNupkgAsyncPackageCoreReader { get; set; }
        public IAsyncPackageCoreReader CurrentNupkgAsyncPackageCoreReader { get; set; }

        SnapPackDeltaReport()
        {
            New = new List<SnapPackFileChecksum>();
            Modified = new List<SnapPackFileChecksum>();
            Unmodified = new List<SnapPackFileChecksum>();
            Deleted = new List<SnapPackFileChecksum>();
        }

        public SnapPackDeltaReport(
            [NotNull] SnapApp previousSnapApp,
            [NotNull] SnapApp currentSnapApp,
            [NotNull] string previousNugpkgFilename,
            [NotNull] string currentNupkgFilename,
            [NotNull] IAsyncPackageCoreReader previousNupkgAsyncPackageCoreReader, [NotNull] IAsyncPackageCoreReader currentNupkgAsyncPackageCoreReader) : this()
        {
            PreviousSnapApp = previousSnapApp ?? throw new ArgumentNullException(nameof(previousSnapApp));
            CurrentSnapApp = currentSnapApp ?? throw new ArgumentNullException(nameof(currentSnapApp));
            PreviousNupkgFilename = previousNugpkgFilename ?? throw new ArgumentNullException(nameof(previousNugpkgFilename));
            CurrentNupkgFilename = currentNupkgFilename ?? throw new ArgumentNullException(nameof(currentNupkgFilename));
            PreviousNupkgAsyncPackageCoreReader = previousNupkgAsyncPackageCoreReader ?? throw new ArgumentNullException(nameof(previousNupkgAsyncPackageCoreReader));
            CurrentNupkgAsyncPackageCoreReader = currentNupkgAsyncPackageCoreReader ?? throw new ArgumentNullException(nameof(currentNupkgAsyncPackageCoreReader));
        }

        public void SortAndVerifyIntegrity()
        {
            void ThrowIfNotUnique(string collectioName, IEnumerable<SnapPackFileChecksum> lhss, params List<SnapPackFileChecksum>[] rhss)
            {
                if (collectioName == null) throw new ArgumentNullException(nameof(collectioName));
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
                    $"Duplicate delta report items detected. Source collection: {collectioName}. " +
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
        SnapApp App { get; set; }
        string NuspecFilename { get; }
        string NuspecBaseDirectory { get; }
        ISnapProgressSource SnapProgressSource { get; set; }
        IReadOnlyDictionary<string, string> NuspecProperties { get; set; }
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
        IReadOnlyCollection<string> NeverGenerateBsDiffsTheseAssemblies { get; }
        string NuspecTargetFrameworkMoniker { get; }
        string NuspecRootTargetPath { get; }
        string SnapNuspecTargetPath { get; }
        string SnapUniqueTargetPathFolderName { get; }
        string ChecksumManifestFilename { get; }
        Task<MemoryStream> BuildFullPackageAsync(ISnapPackageDetails packageDetails, CancellationToken cancellationToken = default);
        Task<SnapPackDeltaReport> BuildDeltaReportAsync(
            [NotNull] string previousNupkgAbsolutePath, [NotNull] string currentNupkgAbsolutePath, CancellationToken cancellationToken = default);
        Task<MemoryStream> BuildDeltaPackageAsync([NotNull] string previousNupkgAbsolutePath, [NotNull] string currentNupkgAbsolutePath,
            ISnapProgressSource progressSource = null, CancellationToken cancellationToken = default);
        Task<MemoryStream> ReassambleFullPackageAsync([NotNull] string deltaNupkgAbsolutePath, [NotNull] string currentNupkgAbsolutePath,
            ISnapProgressSource progressSource = null, CancellationToken cancellationToken = default);
        Task<SnapApp> GetSnapAppAsync(IAsyncPackageCoreReader asyncPackageCoreReader, CancellationToken cancellationToken = default);
        IEnumerable<SnapPackFileChecksum> ParseChecksumManifest(string content);
        Task<int> CountNonNugetFilesAsync(IAsyncPackageCoreReader asyncPackageCoreReader, CancellationToken cancellationToken);
        Task<IEnumerable<string>> GetFilesAsync([NotNull] IAsyncPackageCoreReader asyncPackageCoreReader, CancellationToken cancellationToken);
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

        public async Task<MemoryStream> BuildFullPackageAsync(ISnapPackageDetails packageDetails, CancellationToken cancellationToken = default)
        {
            if (packageDetails == null) throw new ArgumentNullException(nameof(packageDetails));

            var (_, nuspecPropertiesResolver) = BuildNuspecProperties(packageDetails);

            var outputStream = new MemoryStream();
            var progressSource = packageDetails.SnapProgressSource;

            progressSource?.Raise(0);

            using (var nuspecIntermediateStream = await _snapFilesystem.FileReadAsync(packageDetails.NuspecFilename, cancellationToken))
            using (var nuspecStream = RewriteNuspec(packageDetails, nuspecIntermediateStream, nuspecPropertiesResolver, packageDetails.NuspecBaseDirectory))
            {
                progressSource?.Raise(50);

                var packageBuilder = new PackageBuilder(nuspecStream, packageDetails.NuspecBaseDirectory, nuspecPropertiesResolver);

                EnsureCoreRunSupportsThisPlatform();
                AlwaysRemoveTheseAssemblies.ForEach(targetPath => packageBuilder.Files.Remove(new PhysicalPackageFile {TargetPath = targetPath}));
                await AddSnapAssemblies(packageBuilder, packageDetails.App, cancellationToken);
                await AddChecksumManifestAsync(packageBuilder, cancellationToken);

                packageBuilder.Save(outputStream);

                outputStream.Seek(0, SeekOrigin.Begin);

                progressSource?.Raise(100);

                return outputStream;
            }
        }

        public async Task<SnapPackDeltaReport> BuildDeltaReportAsync(string previousNupkgAbsolutePath, string currentNupkgAbsolutePath, CancellationToken cancellationToken = default)
        {
            if (previousNupkgAbsolutePath == null) throw new ArgumentNullException(nameof(previousNupkgAbsolutePath));
            if (currentNupkgAbsolutePath == null) throw new ArgumentNullException(nameof(currentNupkgAbsolutePath));

            var previousNupkgStream = _snapFilesystem.FileRead(previousNupkgAbsolutePath);
            var currentNupkgMemoryStream = _snapFilesystem.FileRead(currentNupkgAbsolutePath);
            var previousNupkgPackageArchiveReader = new PackageArchiveReader(previousNupkgStream);
            var currentNupkgPackageArchiveReader = new PackageArchiveReader(currentNupkgMemoryStream);
            
            var previousSnapApp = await GetSnapAppAsync(previousNupkgPackageArchiveReader, cancellationToken);
            if (previousSnapApp == null)
            {
                throw new Exception($"Unable to build snap app from previous nupkg: {previousNupkgAbsolutePath}.");
            }

            var currentSnapApp = await GetSnapAppAsync(currentNupkgPackageArchiveReader, cancellationToken);
            if (currentSnapApp == null)
            {
                throw new Exception($"Unable to build snap app from current nupkg: {currentNupkgAbsolutePath}.");
            }

            if (previousSnapApp.Version > currentSnapApp.Version)
            {
                var message = $"You cannot build a delta package based on version {previousSnapApp.Version} " +
                              $"as it is a later version than {currentSnapApp.Version}";
                throw new Exception(message);
            }

            if (previousSnapApp.Target.Os != currentSnapApp.Target.Os)
            {
                var message = $"You cannot build a delta package between two packages that target different operating systems. " +
                              $"Previous os: {previousSnapApp.Target.Os}. Current os: {currentSnapApp.Target.Os}.";
                throw new Exception(message);
            }
            
            if (previousSnapApp.Target.Rid != currentSnapApp.Target.Rid)
            {
                var message = $"You cannot build a delta package between two packages that target different runtime identifiers. " +
                              $"Previous rid: {previousSnapApp.Target.Rid}. Current rid: {currentSnapApp.Target.Rid}.";
                throw new Exception(message);
            }

            var previousChecksums = (await GetChecksumsManifestAsync(previousNupkgPackageArchiveReader, cancellationToken)).ToList();
            var currentChecksums = (await GetChecksumsManifestAsync(currentNupkgPackageArchiveReader, cancellationToken)).ToList();

            var deltaReport = new SnapPackDeltaReport(
                previousSnapApp,
                currentSnapApp,
                previousNupkgAbsolutePath,
                currentNupkgAbsolutePath,
                previousNupkgPackageArchiveReader,
                currentNupkgPackageArchiveReader);

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

        public async Task<MemoryStream> BuildDeltaPackageAsync(
            string previousNupkgAbsolutePath, 
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
                    $"Unknown error building delta report between previous and current nupkg. Previous: {previousNupkgAbsolutePath}. Current: {currentNupkgAbsolutePath}.");
            }

            progressSource?.Raise(30);

            using (deltaReport)
            {
                var currentManifestData = await deltaReport.CurrentNupkgAsyncPackageCoreReader.GetManifestMetadataAsync(cancellationToken);
                if (currentManifestData == null)
                {
                    throw new Exception($"Unable to extract manifest data from current nupkg: {currentNupkgAbsolutePath}.");
                }

                progressSource?.Raise(50);

                var snapApp = new SnapApp(deltaReport.CurrentSnapApp)
                {
                    DeltaSrcFilename = deltaReport.PreviousSnapApp.BuildNugetLocalFilename(),
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
                        
                        throw new InvalidOperationException($"Fatal error! Expected to replace assembly: {neverGenerateBsDiffThisAssembly}.");
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

                return outputStream;
            }
        }

        public Task<MemoryStream> ReassambleFullPackageAsync(string deltaNupkgAbsolutePath, string currentNupkgAbsolutePath, ISnapProgressSource progressSource = null,
            CancellationToken cancellationToken = default)
        {
            if (deltaNupkgAbsolutePath == null) throw new ArgumentNullException(nameof(deltaNupkgAbsolutePath));
            if (currentNupkgAbsolutePath == null) throw new ArgumentNullException(nameof(currentNupkgAbsolutePath));

            throw new NotImplementedException();
        }

        public async Task<IEnumerable<SnapPackFileChecksum>> GetChecksumsManifestAsync(
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

        public MemoryStream RewriteNuspec([NotNull] ISnapPackageDetails packageDetails, MemoryStream memoryStream,
            [NotNull] Func<string, string> propertyProvider, [NotNull] string baseDirectory)
        {
            if (packageDetails == null) throw new ArgumentNullException(nameof(packageDetails));
            if (memoryStream == null) throw new ArgumentNullException(nameof(memoryStream));
            if (propertyProvider == null) throw new ArgumentNullException(nameof(propertyProvider));
            if (baseDirectory == null) throw new ArgumentNullException(nameof(baseDirectory));

            var nuspec = Manifest.ReadFrom(memoryStream, propertyProvider, true);

            nuspec.Metadata.Id = packageDetails.App.BuildNugetUpstreamPackageId();

            if (!nuspec.Files.Any())
            {
                throw new Exception("Nuspec does not contain any files.");
            }

            foreach (var file in nuspec.Files)
            {
                var targetPath = file.Source?.Replace(baseDirectory, string.Empty).ForwardSlashesSafe() ?? "/";
                if (!targetPath.StartsWith(_snapFilesystem.DirectorySeparatorChar.ToString()))
                {
                    targetPath = $"{_snapFilesystem.DirectorySeparatorChar}{targetPath}";
                }

                file.Target = $"$anytarget${targetPath}";
            }

            var outputStream = new MemoryStream();
            nuspec.Save(outputStream);
            outputStream.Seek(0, SeekOrigin.Begin);

            return outputStream;
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
                        throw new FileNotFoundException($"corerun is missing in Snap assembly. Target os: {OSPlatform.Linux}.");
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
                using (var shaStream = await inputStream.ReadToEndAsync(cancellationToken, leaveSrcStreamOpen: true))
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
                throw new Exception("It's illegal to add snap assemblies to a delta package.");
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
        }

        InMemoryPackageFile BuildInMemoryPackageFile(MemoryStream memoryStream, string targetPath, string filename)
        {
            if (memoryStream == null) throw new ArgumentNullException(nameof(memoryStream));

            memoryStream.Seek(0, SeekOrigin.Begin);

            var nuGetFramework = NuGetFramework.Parse(NuspecTargetFrameworkMoniker);
            targetPath = _snapFilesystem.PathCombine(targetPath, filename);

            return new InMemoryPackageFile(memoryStream, targetPath, nuGetFramework);
        }

        static string BuildSnapNuspecUniqueFolderName()
        {
            var guidStr = typeof(SnapPack).Assembly.GetCustomAttribute<GuidAttribute>()?.Value;
            Guid.TryParse(guidStr, out var assemblyGuid);
            if (assemblyGuid == Guid.Empty)
            {
                throw new Exception("Fatal error! Assembly guid is empty.");
            }

            return assemblyGuid.ToString("N");
        }

        void ValidatePackageDetails([NotNull] ISnapPackageDetails packageDetails)
        {
            if (packageDetails == null) throw new ArgumentNullException(nameof(packageDetails));

            if (packageDetails.NuspecBaseDirectory == null ||
                !_snapFilesystem.DirectoryExists(packageDetails.NuspecBaseDirectory))
            {
                throw new DirectoryNotFoundException($"Unable to find base directory: {packageDetails.NuspecBaseDirectory}.");
            }

            if (!_snapFilesystem.FileExists(packageDetails.NuspecFilename))
            {
                throw new FileNotFoundException($"Unable to find nuspec filename: {packageDetails.NuspecFilename}.");
            }

            if (packageDetails.App == null)
            {
                throw new Exception("Snap app cannot be null.");
            }

            if (packageDetails.App.Version == null)
            {
                throw new Exception("Snap app version cannot be null.");
            }

            if (!packageDetails.App.IsValidAppName())
            {
                throw new Exception($"Snap app id is invalid: {packageDetails.App.Id}.");
            }

            if (!packageDetails.App.IsValidChannelName())
            {
                throw new Exception($"Invalid channel name: {packageDetails.App.GetCurrentChannelOrThrow().Name}. App id: {packageDetails.App.Id}.");
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
