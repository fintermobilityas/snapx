using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Mono.Cecil;
using NuGet.Configuration;
using NuGet.Versioning;
using Snap.Core;
using Snap.Core.IO;
using Snap.Core.Specs;
using Snap.NuGet;
using Snap.Shared.Tests.Extensions;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace Snap.Shared.Tests
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public class BaseFixture
    {
        public string WorkingDirectory => Directory.GetCurrentDirectory();

        public SnapApp BuildSnapApp()
        {
            var publishFeed = new SnapFeed
            {
                Name = "nuget.org (publish)",
                SourceUri = new Uri(NuGetConstants.V3FeedUrl),
                ProtocolVersion = NuGetProtocolVersion.V3,
                ApiKey = "myapikey"
            };
            
            var updateFeed = new SnapFeed
            {
                Name = "nuget.org (update)",
                SourceUri = new Uri(NuGetConstants.V3FeedUrl),
                ProtocolVersion = NuGetProtocolVersion.V3,
                Username = "myusername",
                Password = "mypassword"
            };

            var testChannel = new SnapChannel
            {
                Name = "test",
                Feed = publishFeed.Name,
                Publish = publishFeed.Name,
                Update = updateFeed.Name
            };

            var productionChannel = new SnapChannel
            {
                Name = "production",
                Feed = publishFeed.Name,
                Publish = publishFeed.Name,
                Update = updateFeed.Name
            };

            return new SnapApp
            {
                Id = "demoapp",
                Version = new SemanticVersion(1, 0, 0),
                Feeds = new List<SnapFeed> {publishFeed, updateFeed },
                Signature = new SnapSignature()
                {
                    CertificateSubjectName = "mycompany",
                    Sha256 = "311FE3FEED16B9CD8DF0F8B1517BE5CB86048707DF4889BA8DC37D4D68866D02"
                },
                Channel = testChannel,
                Channels = new List<SnapChannel> { testChannel, productionChannel },
                Target = new SnapTarget
                {
                    OsPlatform = OSPlatform.Windows,
                    Framework = new SnapTargetFramework
                    {
                        Name = "netcoreapp2.1",
                        RuntimeIdentifier = "win7-x64",
                        Alias = "demoapp-win7-x64",
                        Nuspec = "test.nuspec"
                    }
                }
            };
        }

        public void WriteAssemblies(string workingDirectory, List<AssemblyDefinition> assemblyDefinitions, bool disposeAssemblyDefinitions = false)
        {
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
            if (assemblyDefinitions == null) throw new ArgumentNullException(nameof(assemblyDefinitions));

            foreach (var assemblyDefinition in assemblyDefinitions)
            {
                assemblyDefinition.Write(Path.Combine(workingDirectory, assemblyDefinition.GetRelativeFilename()));

                if (disposeAssemblyDefinitions)
                {
                    assemblyDefinition.Dispose();
                }
            }
        }

        public void WriteAssemblies(string workingDirectory, bool disposeAssemblyDefinitions = false, params AssemblyDefinition[] assemblyDefinitions)
        {
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
            if (assemblyDefinitions == null) throw new ArgumentNullException(nameof(assemblyDefinitions));

            WriteAssemblies(workingDirectory, assemblyDefinitions.ToList(), disposeAssemblyDefinitions);
        }

        public void WriteAndDisposeAssemblies(string workingDirectory, params AssemblyDefinition[] assemblyDefinitions)
        {
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
            if (assemblyDefinitions == null) throw new ArgumentNullException(nameof(assemblyDefinitions));

            WriteAssemblies(workingDirectory, assemblyDefinitions.ToList(), true);
        }

        internal IDisposable WithDisposableAssemblies(string workingDirectory, ISnapFilesystem filesystem, params AssemblyDefinition[] assemblyDefinitions)
        {
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
            if (assemblyDefinitions == null) throw new ArgumentNullException(nameof(assemblyDefinitions));

            WriteAndDisposeAssemblies(workingDirectory, assemblyDefinitions);

            return new DisposableFiles(filesystem, assemblyDefinitions.Select(x => x.GetFullPath(workingDirectory)).ToArray());
        }

        public AssemblyDefinition BuildEmptyLibrary(string libraryName, IReadOnlyCollection<AssemblyDefinition> references = null)
        {
            if (libraryName == null) throw new ArgumentNullException(nameof(libraryName));

            var assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition(libraryName, new Version(1, 0, 0, 0)), libraryName, ModuleKind.Dll);

            var mainModule = assembly.MainModule;

            if (references == null)
            {
                return assembly;
            }

            foreach (var assemblyDefinition in references)
            {
                mainModule.AssemblyReferences.Add(assemblyDefinition.Name);
            }

            return assembly;
        }

        public AssemblyDefinition BuildEmptyExecutable(string applicationName, IReadOnlyCollection<AssemblyDefinition> references = null)
        {
            if (applicationName == null) throw new ArgumentNullException(nameof(applicationName));

            var assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition(applicationName, new Version(1, 0, 0, 0)), applicationName, ModuleKind.Console);

            var mainModule = assembly.MainModule;

            if (references == null)
            {
                return assembly;
            }

            foreach (var assemblyDefinition in references)
            {
                mainModule.AssemblyReferences.Add(assemblyDefinition.Name);
            }

            return assembly;
        }

        public AssemblyDefinition BuildLibrary(string libraryName, string className, IReadOnlyCollection<AssemblyDefinition> references = null)
        {
            if (libraryName == null) throw new ArgumentNullException(nameof(libraryName));
            if (className == null) throw new ArgumentNullException(nameof(className));

            var assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition(libraryName, new Version(1, 0, 0, 0)), libraryName, ModuleKind.Dll);

            var mainModule = assembly.MainModule;

            var simpleClass = new TypeDefinition(libraryName, className,
                TypeAttributes.Class | TypeAttributes.Public, mainModule.TypeSystem.Object);

            mainModule.Types.Add(simpleClass);

            if (references == null)
            {
                return assembly;
            }

            foreach (var assemblyDefinition in references)
            {
                mainModule.AssemblyReferences.Add(assemblyDefinition.Name);
            }

            return assembly;
        }

        internal async Task<(MemoryStream memoryStream, SnapPackageDetails packageDetails)> BuildTestNupkgAsync([NotNull] ISnapFilesystem filesystem,
            [NotNull] ISnapPack snapPack, ISnapProgressSource progressSource = null, CancellationToken cancellationToken = default)
        {
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (snapPack == null) throw new ArgumentNullException(nameof(snapPack));

            const string nuspecContent = @"<?xml version=""1.0""?>
<package xmlns=""http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd"">
    <metadata>
        <id>Youpark</id>
        <title>Youpark</title>
        <version>$version$</version>
        <authors>Youpark AS</authors>
        <requireLicenseAcceptance>false</requireLicenseAcceptance>
        <description>Youpark</description>
    </metadata>
    <files> 
		<file src=""$nuspecbasedirectory$\test.dll"" target=""lib\net45"" />						    
		<file src=""$nuspecbasedirectory$\subdirectory\test2.dll"" target=""lib\net45\subdirectory"" />						    
    </files>
</package>";

            using (var tempDirectory = new DisposableTempDirectory(WorkingDirectory, filesystem))
            {
                var snapPackDetails = new SnapPackageDetails
                {
                    NuspecFilename = Path.Combine(tempDirectory.AbsolutePath, "test.nuspec"),
                    NuspecBaseDirectory = tempDirectory.AbsolutePath,
                    SnapProgressSource = progressSource,
                    App = BuildSnapApp()
                };

                var subDirectory = Path.Combine(snapPackDetails.NuspecBaseDirectory, "subdirectory");
                filesystem.CreateDirectory(subDirectory);

                using (var emptyLibraryAssemblyDefinition = BuildEmptyLibrary("test"))
                {
                    var testDllFilename = Path.Combine(snapPackDetails.NuspecBaseDirectory,
                        emptyLibraryAssemblyDefinition.GetRelativeFilename());
                    emptyLibraryAssemblyDefinition.Write(testDllFilename);
                }

                using (var emptyLibraryAssemblyDefinition = BuildEmptyLibrary("test2"))
                {
                    var testDllFilename = Path.Combine(subDirectory, emptyLibraryAssemblyDefinition.GetRelativeFilename());
                    emptyLibraryAssemblyDefinition.Write(testDllFilename);
                }

                await filesystem.WriteStringContentAsync(nuspecContent, snapPackDetails.NuspecFilename, cancellationToken);

                var nupkgMemoryStream = snapPack.Pack(snapPackDetails);
                return (nupkgMemoryStream, snapPackDetails);
            }
        }
    }
}
