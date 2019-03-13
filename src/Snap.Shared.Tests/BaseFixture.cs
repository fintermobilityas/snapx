using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Versioning;
using Snap.Core;
using Snap.Core.IO;
using Snap.Core.Models;
using Snap.Core.Resources;
using Snap.NuGet;
using Snap.Shared.Tests.Extensions;
using Xunit;

namespace Snap.Shared.Tests
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    [SuppressMessage("ReSharper", "MemberCanBeMadeStatic.Global")]
    [UsedImplicitly]
    public class BaseFixture
    {
        static readonly Random RandomSource = new Random();

        public string WorkingDirectory => Directory.GetCurrentDirectory();
        public string NugetTempDirectory => Path.Combine(WorkingDirectory, "nuget");

        internal DisposableTempDirectory WithDisposableTempDirectory([NotNull] ISnapFilesystem filesystem)
        {
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            return new DisposableTempDirectory(WorkingDirectory, filesystem);
        }

        public SnapApp BuildSnapApp(string id = "demoapp", bool isGenisis = false, string rid = null, OSPlatform osPlatform = default)
        {
            var pushFeed = new SnapNugetFeed
            {
                Name = "nuget.org",
                Source = new Uri(NuGetConstants.V3FeedUrl),
                ProtocolVersion = NuGetProtocolVersion.V3,
                ApiKey = "myapikey"
            };

            var updateFeedNuget = new SnapNugetFeed
            {
                Name = "nuget.org",
                Source = new Uri(NuGetConstants.V3FeedUrl),
                ProtocolVersion = NuGetProtocolVersion.V3,
                Username = "myusername",
                Password = "mypassword"
            };

            var updateFeedHttp = new SnapHttpFeed
            {
                Source = new Uri("https://mydynamicupdatefeed.com")
            };

            var testChannel = new SnapChannel
            {
                Name = "test",
                PushFeed = pushFeed,
                UpdateFeed = updateFeedNuget,
                Current = true
            };

            var stagingChannel = new SnapChannel
            {
                Name = "staging",
                PushFeed = pushFeed,
                UpdateFeed = updateFeedHttp
            };

            var productionChannel = new SnapChannel
            {
                Name = "production",
                PushFeed = pushFeed,
                UpdateFeed = updateFeedNuget
            };

            if (osPlatform == default)
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    osPlatform = OSPlatform.Windows;
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    osPlatform = OSPlatform.Linux;
                }                
            }
            
            if (osPlatform != OSPlatform.Windows && osPlatform != OSPlatform.Linux)
            {
                throw new NotSupportedException($"Unsupported OS platform: {osPlatform}");
            }

            var snapApp = new SnapApp
            {
                Id = id,
                Version = new SemanticVersion(1, 0, 0),
                IsGenisis = isGenisis,
                Channels = new List<SnapChannel>
                {
                    testChannel,
                    stagingChannel,
                    productionChannel
                },
                Target = new SnapTarget
                {
                    Os = osPlatform,
                    Framework = "netcoreapp2.1",
                    Rid = rid ?? "win-x64",
                    Nuspec = "test.nuspec",
                    Shortcuts = new List<SnapShortcutLocation>
                    {
                        SnapShortcutLocation.Desktop,
                        SnapShortcutLocation.Startup,
                        SnapShortcutLocation.StartMenu
                    },
                    PersistentAssets = new List<string>
                    {
                        "application.json"
                    }
                }
            };

            return snapApp;
        }

        public SnapApps BuildSnapApps()
        {
            var snapApp = BuildSnapApp();

            return new SnapApps
            {
                Schema = 1,
                Channels = snapApp.Channels.Select(x => new SnapsChannel(x)).ToList(),
                Apps = new List<SnapsApp> {new SnapsApp(snapApp)},
                Generic = new SnapAppsGeneric
                {
                    Nuspecs = "snap/nuspecs",
                    Packages = "snap/packages"
                }
            };
        }

        public void WriteAssemblies(string workingDirectory, List<AssemblyDefinition> assemblyDefinitions, bool disposeAssemblyDefinitions = false)
        {
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
            if (assemblyDefinitions == null) throw new ArgumentNullException(nameof(assemblyDefinitions));

            foreach (var assemblyDefinition in assemblyDefinitions)
            {
                assemblyDefinition.Write(Path.Combine(workingDirectory, assemblyDefinition.BuildRelativeFilename()));

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

        internal IDisposable WithDisposableAssemblies(string workingDirectory, [NotNull] ISnapFilesystem filesystem,
            params AssemblyDefinition[] assemblyDefinitions)
        {
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (assemblyDefinitions == null) throw new ArgumentNullException(nameof(assemblyDefinitions));

            WriteAndDisposeAssemblies(workingDirectory, assemblyDefinitions);

            return new DisposableFiles(filesystem, assemblyDefinitions.Select(x => x.GetFullPath(filesystem, workingDirectory)).ToArray());
        }

        public AssemblyDefinition BuildLibrary(string libraryName, bool randomVersion = false, IReadOnlyCollection<AssemblyDefinition> references = null)
        {
            if (libraryName == null) throw new ArgumentNullException(nameof(libraryName));

            var version = randomVersion
                ? new Version(
                    RandomSource.Next(0, 1000),
                    RandomSource.Next(0, 1000),
                    RandomSource.Next(0, 1000),
                    RandomSource.Next(0, 1000))
                : new Version(1, 0, 0, 0);

            var assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition(libraryName, version), libraryName, ModuleKind.Dll);

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

        public AssemblyDefinition BuildExecutable(string applicationName, bool randomVersion = false,
            List<AssemblyDefinition> references = null, int exitCode = 0)
        {
            if (applicationName == null) throw new ArgumentNullException(nameof(applicationName));

            var version = randomVersion
                ? new Version(
                    RandomSource.Next(0, 1000),
                    RandomSource.Next(0, 1000),
                    RandomSource.Next(0, 1000),
                    RandomSource.Next(0, 1000))
                : new Version(1, 0, 0, 0);

            var hideConsoleWindow = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ModuleKind.Windows : ModuleKind.Console;

            var assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition(applicationName, version), applicationName,
                hideConsoleWindow);

            var mainModule = assembly.MainModule;

            var programType = new TypeDefinition(applicationName, "Program",
                TypeAttributes.Class | TypeAttributes.Public, mainModule.TypeSystem.Object);

            mainModule.Types.Add(programType);

            var ctor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.HideBySig
                                                                             | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                mainModule.TypeSystem.Void);

            var il = ctor.Body.GetILProcessor();
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Call, mainModule.ImportReference(method: typeof(object).GetConstructor(Array.Empty<Type>()))));
            il.Append(il.Create(OpCodes.Nop));
            il.Append(il.Create(OpCodes.Ret));
            programType.Methods.Add(ctor);

            var mainMethod = new MethodDefinition("Main",
                MethodAttributes.Public | MethodAttributes.Static, mainModule.TypeSystem.Int32);
            programType.Methods.Add(mainMethod);

            var argsParameter = new ParameterDefinition("args",
                ParameterAttributes.None, mainModule.ImportReference(typeof(string[])));
            mainMethod.Parameters.Add(argsParameter);

            var stringJoinMethodReference = mainModule.ImportReference(
                typeof(string).GetMethod(nameof(string.Join), new[]
                {
                    typeof(string),
                    typeof(object[])
                }));

            var systemConsoleWriteLineMethodReference = mainModule.ImportReference(
                typeof(Console).GetMethod(nameof(Console.WriteLine), new[]
                {
                    typeof(string),
                    typeof(object),
                    typeof(object)
                }));

            il = mainMethod.Body.GetILProcessor();
            // Console.WriteLine("Arguments({0}):{1}", args.Length, string.Join(",", args));
            il.Append(il.Create(OpCodes.Nop));
            il.Append(il.Create(OpCodes.Ldstr, "Arguments({0}):{1}"));
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Ldlen));
            il.Append(il.Create(OpCodes.Conv_I4));
            il.Append(il.Create(OpCodes.Box, mainModule.TypeSystem.Int32));
            il.Append(il.Create(OpCodes.Ldstr, ","));
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Call, stringJoinMethodReference));
            il.Append(il.Create(OpCodes.Call, systemConsoleWriteLineMethodReference));
            il.Append(il.Create(OpCodes.Nop));
            // return exitCode;
            il.Append(il.Create(OpCodes.Ldc_I4, exitCode));
            il.Append(il.Create(OpCodes.Ret));

            assembly.EntryPoint = mainMethod;

            if (references == null)
            {
                references = new List<AssemblyDefinition>();
            }

            foreach (var assemblyDefinition in references)
            {
                mainModule.AssemblyReferences.Add(assemblyDefinition.Name);
            }

            return assembly;
        }

        public AssemblyDefinition BuildSnapExecutable([NotNull] SnapApp snapApp, bool randomVersion = false, List<AssemblyDefinition> references = null)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            return BuildExecutable(snapApp.Id, randomVersion, references);
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

        internal void VerifyChecksums([NotNull] ISnapCryptoProvider snapCryptoProvider, ISnapEmbeddedResources snapEmbeddedResources, 
            [NotNull] ISnapFilesystem filesystem, [NotNull] ISnapPack snapPack, PackageArchiveReader packageArchiveReader, [NotNull] SnapApp snapApp, [NotNull] SnapRelease snapRelease,
            [NotNull] string baseDirectory, string appDirectory, [NotNull] List<string> expectedDiskLayout)
        {
            if (snapCryptoProvider == null) throw new ArgumentNullException(nameof(snapCryptoProvider));
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (snapPack == null) throw new ArgumentNullException(nameof(snapPack));
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (snapRelease == null) throw new ArgumentNullException(nameof(snapRelease));
            if (baseDirectory == null) throw new ArgumentNullException(nameof(baseDirectory));
            if (expectedDiskLayout == null) throw new ArgumentNullException(nameof(expectedDiskLayout));
            if (expectedDiskLayout.Count == 0) throw new ArgumentException("Value cannot be an empty collection.", nameof(expectedDiskLayout));
            
            Assert.Equal(expectedDiskLayout.Count, snapRelease.Files.Count);

            var coreRunExe = snapEmbeddedResources.GetCoreRunExeFilenameForSnapApp(snapApp);
                                 
            var snapReleaseChecksum = snapCryptoProvider.Sha512(snapRelease, packageArchiveReader, snapPack);
            Assert.Equal(snapRelease.Sha512Checksum, snapReleaseChecksum);
                                 
            for (var i = 0; i < expectedDiskLayout.Count; i++)
            {
                var expectedChecksum = snapRelease.Files[i];

                string diskAbsoluteFilename;
                var targetPath = expectedChecksum.NuspecTargetPath;
                if (targetPath.StartsWith(SnapConstants.NuspecAssetsTargetPath))
                {
                    targetPath = targetPath.Substring(SnapConstants.NuspecAssetsTargetPath.Length + 1);
                    var isCoreRunExe = targetPath.EndsWith(coreRunExe);                      
                    diskAbsoluteFilename = filesystem.PathCombine(isCoreRunExe ? baseDirectory : appDirectory, targetPath);
                } else if (targetPath.StartsWith(SnapConstants.NuspecRootTargetPath))
                {
                    targetPath = targetPath.Substring(SnapConstants.NuspecRootTargetPath.Length + 1);
                    diskAbsoluteFilename = filesystem.PathCombine(appDirectory, targetPath);
                }
                else
                {
                    throw new Exception($"Unexpected file: {targetPath}");
                }
                
                Assert.NotNull(diskAbsoluteFilename);
                Assert.True(filesystem.FileExists(diskAbsoluteFilename), $"File does not exist: {diskAbsoluteFilename}");

                using (var fileStream = filesystem.FileRead(diskAbsoluteFilename))                
                {
                    var diskSha512Checksum = snapCryptoProvider.Sha512(fileStream);
                    
                    Assert.Equal(expectedChecksum.DeltaSha512Checksum ?? expectedChecksum.FullSha512Checksum, diskSha512Checksum);
                }
            }
        }
    }
}
