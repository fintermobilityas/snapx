using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using JetBrains.Annotations;
using Mono.Cecil;
using NuGet.Frameworks;
using NuGet.Packaging;
using Snap.Core.Models;
using Snap.Core.Resources;
using Snap.Extensions;
using Snap.NuGet;

namespace Snap.Core
{
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
        string NuspecTargetFrameworkMoniker { get; }
        string NuspecRootTargetPath { get; }
        string SnapNuspecTargetPath { get; }
        string SnapUniqueTargetPathFolderName { get; }
        Task<MemoryStream> PackAsync(ISnapPackageDetails packageDetails, CancellationToken cancellationToken = default);
        Task<SnapApp> GetSnapAppFromPackageArchiveReaderAsync(PackageArchiveReader packageArchiveReader, CancellationToken cancellationToken = default);
        MemoryStream RewriteNuspec([NotNull] MemoryStream memoryStream, Func<string, string> propertyProvider,
            string baseDirectory);
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
            _snapFilesystem.PathCombine(NuspecRootTargetPath, _snapAppWriter.SnapDllFilename),
            _snapFilesystem.PathCombine(NuspecRootTargetPath, _snapAppWriter.SnapAppDllFilename)
        };

        public string NuspecTargetFrameworkMoniker { get; }
        public string NuspecRootTargetPath { get; }
        public string SnapNuspecTargetPath { get; }
        public string SnapUniqueTargetPathFolderName { get; }

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
            NuspecRootTargetPath = snapFilesystem.PathCombine("lib", NuspecTargetFrameworkMoniker);
            SnapNuspecTargetPath = snapFilesystem.PathCombine(NuspecRootTargetPath, SnapUniqueTargetPathFolderName);
        }
       
        public async Task<MemoryStream> PackAsync(ISnapPackageDetails packageDetails, CancellationToken cancellationToken = default)
        {
            if (packageDetails == null) throw new ArgumentNullException(nameof(packageDetails));

            if (packageDetails.NuspecBaseDirectory == null || !_snapFilesystem.DirectoryExists(packageDetails.NuspecBaseDirectory))
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

            var nuspecProperties = new Dictionary<string, string>
            {
                {"version", packageDetails.App.Version.ToFullString()},
                {"basedirectory", packageDetails.NuspecBaseDirectory},
                {"anytarget", NuspecRootTargetPath }
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
            
            var progressSource = packageDetails.SnapProgressSource;

            progressSource?.Raise(0);

            var outputStream = new MemoryStream();

            using (var nuspecIntermediateStream = await _snapFilesystem.FileReadAsync(packageDetails.NuspecFilename, cancellationToken))
            using (var nuspecStream = RewriteNuspec(nuspecIntermediateStream, NuspecPropertyProvider, packageDetails.NuspecBaseDirectory))
            {
                progressSource?.Raise(50);

                var packageBuilder = new PackageBuilder(nuspecStream, packageDetails.NuspecBaseDirectory, NuspecPropertyProvider);

                EnsureCoreRunSupportsThisPlatform();
                AlwaysRemoveTheseAssemblies.ForEach(targetPath => packageBuilder.Files.Remove(new PhysicalPackageFile { TargetPath = targetPath }));
                await AddSnapAssemblies(packageBuilder, packageDetails, cancellationToken);
                await AddChecksumManifestAsync(packageBuilder, cancellationToken);
  
                packageBuilder.Save(outputStream);

                outputStream.Seek(0, SeekOrigin.Begin);

                progressSource?.Raise(100);

                return outputStream;
            }
        }

        public async Task<SnapApp> GetSnapAppFromPackageArchiveReaderAsync([NotNull] PackageArchiveReader packageArchiveReader, CancellationToken cancellationToken = default)
        {
            if (packageArchiveReader == null) throw new ArgumentNullException(nameof(packageArchiveReader));
            
            var targetPath = _snapFilesystem.PathCombine(SnapNuspecTargetPath, _snapAppWriter.SnapAppDllFilename);
            using (var intermediateStream = packageArchiveReader.GetStream(targetPath))
            using (var actualStream = await intermediateStream.ReadToEndAsync(cancellationToken))
            using (var assemblyDefinition = AssemblyDefinition.ReadAssembly(actualStream, new ReaderParameters(ReadingMode.Immediate)))
            {
                var snapApp =  assemblyDefinition.GetSnapApp(_snapAppReader, _snapAppWriter);
                return snapApp;
            }
        }

        public MemoryStream RewriteNuspec(MemoryStream memoryStream, [NotNull] Func<string, string> propertyProvider,
            [NotNull] string baseDirectory)
        {
            if (memoryStream == null) throw new ArgumentNullException(nameof(memoryStream));
            if (propertyProvider == null) throw new ArgumentNullException(nameof(propertyProvider));
            if (baseDirectory == null) throw new ArgumentNullException(nameof(baseDirectory));

            var nuspec = Manifest.ReadFrom(memoryStream, propertyProvider, true);

            foreach (var file in nuspec.Files.Where(x => !string.IsNullOrWhiteSpace(x.Source)))
            {
                var targetPath = file.Source.Replace(baseDirectory, string.Empty).ForwardSlashesSafe();
                if(!targetPath.StartsWith(_snapFilesystem.DirectorySeparator))
                {
                    targetPath = $"{_snapFilesystem.DirectorySeparator}{targetPath}";
                }
                file.Target = $"$anytarget${targetPath}";
            }
            
            var outputStream = new MemoryStream();
            nuspec.Save(outputStream);
            outputStream.Seek(0, SeekOrigin.Begin);
            
            return outputStream;
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
                string effectivePath;
                switch (x)
                {
                    case PhysicalPackageFile physicalPackageFile:
                        stream = physicalPackageFile.GetStream();
                        effectivePath = physicalPackageFile.TargetPath;
                        break;
                    case InMemoryPackageFile inMemoryPackage:
                        stream = inMemoryPackage.GetStream();
                        effectivePath = inMemoryPackage.EffectivePath;
                        break;
                    default:
                        throw new NotSupportedException($"Unknown package file type: {x.GetType().FullName}");
                }

                return (stream, effectivePath);
            }).OrderBy(x => x.effectivePath);
            
            foreach (var (inputStream, effectivePath) in packageFiles)
            {
                using (var shaStream = await inputStream.ReadToEndAsync(cancellationToken, leaveOpen: true))
                {
                    stringBuilder.Append($"{effectivePath}:{_snapCryptoProvider.Sha1(shaStream)}");
                    stringBuilder.Append(_snapFilesystem.FixedNewlineChar);
                }

                inputStream.Seek(0, SeekOrigin.Begin);
            }
            
            var checksumStream = new MemoryStream(Encoding.UTF8.GetBytes(stringBuilder.ToString()));                        
            packageBuilder.Files.Add(BuildInMemoryPackageFile(checksumStream, _snapFilesystem.PathCombine(SnapNuspecTargetPath, "checksum"), "checksum"));
        }
        
        async Task AddSnapAssemblies([NotNull] PackageBuilder packageBuilder, [NotNull] ISnapPackageDetails packageDetails, CancellationToken cancellationToken)
        {
            if (packageBuilder == null) throw new ArgumentNullException(nameof(packageBuilder));
            if (packageDetails == null) throw new ArgumentNullException(nameof(packageDetails));

            // Snap.dll
            using (var snapDllAssemblyDefinition = await _snapFilesystem.FileReadAssemblyDefinitionAsync(typeof(SnapPack).Assembly.Location, cancellationToken))
            using (var snapDllAssemblyDefinitionOptimized = _snapAppWriter.OptimizeSnapDllForPackageArchive(snapDllAssemblyDefinition, packageDetails.App.Target.Os))
            {
                var snapDllOptimizedMemoryStream = new MemoryStream();
                snapDllAssemblyDefinitionOptimized.Write(snapDllOptimizedMemoryStream);
                snapDllOptimizedMemoryStream.Seek(0, SeekOrigin.Begin);

                packageBuilder.Files.Add(BuildInMemoryPackageFile(snapDllOptimizedMemoryStream, SnapNuspecTargetPath, _snapAppWriter.SnapDllFilename));
            }

            // Snap.App.dll
            using (var snapAppDllAssembly = _snapAppWriter.BuildSnapAppAssembly(packageDetails.App))
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
    }
}
