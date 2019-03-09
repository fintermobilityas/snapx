using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Fasterflect;
using JetBrains.Annotations;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NuGet.Configuration;
using NuGet.Versioning;
using Snap.Core;
using Snap.Core.IO;
using Snap.Core.Models;
using Snap.Core.Resources;
using Snap.NuGet;
using Snap.Reflection;
using Snap.Shared.Tests.Extensions;

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

        public SnapApp BuildSnapApp(string appId = "demoapp")
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

            OSPlatform osPlatform;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                osPlatform = OSPlatform.Windows;
            } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                osPlatform = OSPlatform.Linux;
            }
            else
            {
                throw new NotSupportedException();
            }
            
            return new SnapApp
            {
                Id = appId,
                Version = new SemanticVersion(1, 0, 0),
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
                    Rid = "win-x64",
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
        }

        public SnapApps BuildSnapApps()
        {
            var snapApp = BuildSnapApp();

            return new SnapApps
            {
                Schema = 1,
                Channels = snapApp.Channels.Select(x => new SnapsChannel(x)).ToList(),
                Apps = new List<SnapsApp> { new SnapsApp(snapApp) },
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

        internal IDisposable WithDisposableAssemblies(string workingDirectory, [NotNull] ISnapFilesystem filesystem, params AssemblyDefinition[] assemblyDefinitions)
        {
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (assemblyDefinitions == null) throw new ArgumentNullException(nameof(assemblyDefinitions));

            WriteAndDisposeAssemblies(workingDirectory, assemblyDefinitions);

            return new DisposableFiles(filesystem, assemblyDefinitions.Select(x => x.GetFullPath(filesystem, workingDirectory)).ToArray());
        }

        public AssemblyDefinition BuildEmptyLibrary(string libraryName, bool randomVersion = false, IReadOnlyCollection<AssemblyDefinition> references = null)
        {
            if (libraryName == null) throw new ArgumentNullException(nameof(libraryName));
            
            var version = randomVersion ? new Version(
                RandomSource.Next(0, 1000), 
                RandomSource.Next(0, 1000), 
                RandomSource.Next(0, 1000), 
                RandomSource.Next(0, 1000)) : 
                new Version(1, 0, 0, 0);
            
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

        public AssemblyDefinition BuildEmptyExecutable(string applicationName, bool randomVersion = false, 
            List<AssemblyDefinition> references = null, int exitCode = 0)
        {
            if (applicationName == null) throw new ArgumentNullException(nameof(applicationName));

            var version = randomVersion ? new Version(
                    RandomSource.Next(0, 1000), 
                    RandomSource.Next(0, 1000), 
                    RandomSource.Next(0, 1000), 
                    RandomSource.Next(0, 1000)) : 
                new Version(1, 0, 0, 0);

            var hideConsoleWindow = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? 
                ModuleKind.Windows : ModuleKind.Console;

            var assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition(applicationName, version), applicationName, 
                hideConsoleWindow);
             
            var mainModule = assembly.MainModule;

            var programType = new TypeDefinition(applicationName, "Program",
                TypeAttributes.Class | TypeAttributes.Public, mainModule.TypeSystem.Object);

            mainModule.Types.Add(programType);

            var ctor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.HideBySig
                | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, mainModule.TypeSystem.Void);

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
                typeof(string).GetMethod(nameof(string.Join), new []
                {
                    typeof(string), 
                    typeof(object[])
                }));
            var systemConsoleWriteLineMethodReference = mainModule.ImportReference(
                typeof(Console).GetMethod(nameof(Console.WriteLine), new []
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

        public AssemblyDefinition BuildSnapAwareEmptyExecutable([NotNull] SnapApp snapApp, bool randomVersion = false, List<AssemblyDefinition> references = null)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            var testExeAssemblyDefinition = BuildEmptyExecutable(snapApp.Id, randomVersion, references);
            var testExeAssemblyDefinitionReflector = new CecilAssemblyReflector(testExeAssemblyDefinition);
            testExeAssemblyDefinitionReflector.SetSnapAware();
            return testExeAssemblyDefinition;
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

        internal async Task<(MemoryStream memoryStream, SnapPackageDetails packageDetails, string checksum)> BuildInMemoryFullPackageAsync(
            [NotNull] SnapApp snapApp, [NotNull] ICoreRunLib coreRunLib, [NotNull] ISnapFilesystem filesystem, 
            [NotNull] ISnapPack snapPack, [NotNull] ISnapEmbeddedResources snapEmbeddedResources, [NotNull] Dictionary<string, object> nuspecFilesLayout, 
            ISnapProgressSource progressSource = null, CancellationToken cancellationToken = default)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            if (coreRunLib == null) throw new ArgumentNullException(nameof(coreRunLib));
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (snapPack == null) throw new ArgumentNullException(nameof(snapPack));
            if (snapEmbeddedResources == null) throw new ArgumentNullException(nameof(snapEmbeddedResources));
            if (nuspecFilesLayout == null) throw new ArgumentNullException(nameof(nuspecFilesLayout));

            var (coreRunMemoryStream, _, _) = snapEmbeddedResources.GetCoreRunForSnapApp(snapApp, filesystem, coreRunLib);
            coreRunMemoryStream.Dispose();
            
            const string nuspecContent = @"<?xml version=""1.0""?>
<package xmlns=""http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd"">
    <metadata>
        <title>Random Title</title>
        <authors>Peter Rekdal Sunde</authors>
    </metadata>
</package>";

            using (var tempDirectory = new DisposableTempDirectory(WorkingDirectory, filesystem))
            {
                var snapPackDetails = new SnapPackageDetails
                {
                    NuspecFilename = Path.Combine(tempDirectory.WorkingDirectory, "test.nuspec"),
                    NuspecBaseDirectory = tempDirectory.WorkingDirectory,
                    SnapProgressSource = progressSource,
                    App = snapApp
                };

                foreach (var (key, value) in nuspecFilesLayout)
                {
                    var dstFilename = filesystem.PathCombine(snapPackDetails.NuspecBaseDirectory, key);
                    var directory = filesystem.PathGetDirectoryName(dstFilename);
                    filesystem.DirectoryCreateIfNotExists(directory);
                    switch (value)
                    {
                        case AssemblyDefinition assemblyDefinition:
                            assemblyDefinition.Write(dstFilename);                
                            break;
                        case MemoryStream memoryStream:
                            await filesystem.FileWriteAsync(memoryStream, dstFilename, cancellationToken);
                            memoryStream.Seek(0, SeekOrigin.Begin);
                            break;
                        default:
                            throw new NotSupportedException($"{key}: {value?.GetType().FullName}");
                    }
                }

                await filesystem.FileWriteUtf8StringAsync(nuspecContent, snapPackDetails.NuspecFilename, cancellationToken);

                var (nupkgMemoryStream, checksum) = await snapPack.BuildFullPackageAsync(snapPackDetails, coreRunLib, cancellationToken: cancellationToken);
                return (nupkgMemoryStream, snapPackDetails, checksum);
            }
        }

    }
}
