using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NuGet.Configuration;
using NuGet.Versioning;
using Snap.Core;
using Snap.Core.IO;
using Snap.Core.Models;
using Snap.Extensions;
using Snap.NuGet;
using Snap.Shared.Tests.Extensions;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace Snap.Shared.Tests
{
    [UsedImplicitly]
    public class BaseFixture
    {
        static readonly Random RandomSource = new Random();

        public string WorkingDirectory => Directory.GetCurrentDirectory();

        public OSPlatform OsPlatform
        {
            get
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return OSPlatform.Windows;
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    return OSPlatform.Linux;
                }

                throw new PlatformNotSupportedException();
            }
        }

        internal DisposableDirectory WithDisposableTempDirectory([NotNull] ISnapFilesystem filesystem)
        {
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            return new DisposableDirectory(WorkingDirectory, filesystem);
        }

        public SnapApp Bump([NotNull] SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            return new SnapApp(snapApp) { Version = snapApp.Version.BumpMajor() };
        }

        public SnapApp BuildSnapApp(string id = "demoapp", bool isGenesis = false,
            string localPackageSourceDirectory = null)
        {
            var pushFeed = new SnapNugetFeed
            {
                Name = "nuget.org",
                Source = new Uri(localPackageSourceDirectory ?? NuGetConstants.V3FeedUrl),
                ProtocolVersion = NuGetProtocolVersion.V3,
                ApiKey = "myapikey"
            };

            var updateFeedNuget = new SnapNugetFeed
            {
                Name = "nuget.org",
                Source = new Uri(localPackageSourceDirectory ?? NuGetConstants.V3FeedUrl),
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

            var snapApp = new SnapApp
            {
                Id = id,
                MainExe = id,
                InstallDirectoryName = id,
                SuperVisorId = Guid.NewGuid().ToString(),
                Version = new SemanticVersion(1, 0, 0),
                IsGenesis = isGenesis,
                IsFull = isGenesis,
                Channels = new List<SnapChannel>
                {
                    testChannel,
                    stagingChannel,
                    productionChannel
                },
                Target = new SnapTarget
                {
                    Framework = "netcoreapp2.1",
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

            snapApp.SetRidUsingCurrentOsPlatformAndProcessArchitecture();

            return snapApp;
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

        public AssemblyDefinition BuildLibrary(string libraryName, SemanticVersion semanticVersion = null, bool randomVersion = false, IReadOnlyCollection<AssemblyDefinition> assemblyReferencesDefinitions = null)
        {
            if (libraryName == null) throw new ArgumentNullException(nameof(libraryName));

            if (semanticVersion == null)
            {
                semanticVersion = randomVersion
                    ? new SemanticVersion(
                        RandomSource.Next(0, 1000),
                        RandomSource.Next(0, 1000),
                        RandomSource.Next(0, 1000))
                    : new SemanticVersion(1, 0, 0);
            }

            var assemblyVersion = new Version(semanticVersion.Major, semanticVersion.Minor, semanticVersion.Patch, 0);
            
            var assemblyDefinition = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition(libraryName, assemblyVersion), libraryName, ModuleKind.Dll);

            var mainModule = assemblyDefinition.MainModule;

            AddVersioningAttributes(assemblyDefinition, semanticVersion);

            if (assemblyReferencesDefinitions == null)
            {
                return assemblyDefinition;
            }

            foreach (var assemblyReferenceDefinition in assemblyReferencesDefinitions)
            {
                mainModule.AssemblyReferences.Add(assemblyReferenceDefinition.Name);
            }

            return assemblyDefinition;
        }

        public AssemblyDefinition BuildExecutable(string applicationName, bool randomVersion = false,
            List<AssemblyDefinition> assemblyReferencesDefinitions = null, int exitCode = 0)
        {
            if (applicationName == null) throw new ArgumentNullException(nameof(applicationName));

            var semanticVersion = randomVersion
                ? new SemanticVersion(
                    RandomSource.Next(0, 1000),
                    RandomSource.Next(0, 1000),
                    RandomSource.Next(0, 1000))
                : new SemanticVersion(1, 0, 0);

            var hideConsoleWindow = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ModuleKind.Windows : ModuleKind.Console;

            var assemblyDefinition = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition(applicationName, new Version(semanticVersion.Major, semanticVersion.Minor, semanticVersion.Patch, 0)), applicationName,
                hideConsoleWindow);

            var mainModule = assemblyDefinition.MainModule;

            var programType = new TypeDefinition(applicationName, "Program",
                TypeAttributes.Class | TypeAttributes.Public, mainModule.TypeSystem.Object);

            mainModule.Types.Add(programType);

            var ctor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.HideBySig
                                                                             | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                mainModule.TypeSystem.Void);

            var il = ctor.Body.GetILProcessor();
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Call, mainModule.ImportReference(typeof(object).GetConstructor(Array.Empty<Type>()))));
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

            assemblyDefinition.EntryPoint = mainMethod;

            AddVersioningAttributes(assemblyDefinition, semanticVersion);
                        
            assemblyReferencesDefinitions ??= new List<AssemblyDefinition>();

            foreach (var assemblyReferenceDefinition in assemblyReferencesDefinitions)
            {
                mainModule.AssemblyReferences.Add(assemblyReferenceDefinition.Name);
            }

            return assemblyDefinition;
        }

        public AssemblyDefinition BuildSnapExecutable([NotNull] SnapApp snapApp, bool randomVersion = true, List<AssemblyDefinition> references = null)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            return BuildExecutable(snapApp.MainExe ?? snapApp.Id, randomVersion, references);
        }

        public AssemblyDefinition BuildLibrary(string libraryName, string className, IReadOnlyCollection<AssemblyDefinition> assemblyReferencesDefinitions = null)
        {
            if (libraryName == null) throw new ArgumentNullException(nameof(libraryName));
            if (className == null) throw new ArgumentNullException(nameof(className));

            var assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition(libraryName, new Version(1, 0, 0, 0)), libraryName, ModuleKind.Dll);

            var mainModule = assembly.MainModule;

            var simpleClass = new TypeDefinition(libraryName, className,
                TypeAttributes.Class | TypeAttributes.Public, mainModule.TypeSystem.Object);

            mainModule.Types.Add(simpleClass);

            if (assemblyReferencesDefinitions == null)
            {
                return assembly;
            }

            foreach (var assemblyDefinition in assemblyReferencesDefinitions)
            {
                mainModule.AssemblyReferences.Add(assemblyDefinition.Name);
            }

            return assembly;
        }

        static void AddVersioningAttributes([NotNull] AssemblyDefinition assemblyDefinition, [NotNull] SemanticVersion semanticVersion)
        {
            if (assemblyDefinition == null) throw new ArgumentNullException(nameof(assemblyDefinition));
            if (semanticVersion == null) throw new ArgumentNullException(nameof(semanticVersion));

            var mainModule = assemblyDefinition.MainModule;
            
            var assemblyInformationalVersionAttributeMethodReference = mainModule.ImportReference(
                typeof(AssemblyInformationalVersionAttribute).GetConstructor(new []{ typeof(string) }));
            var assemblyInformationVersionCustomAttribute = new CustomAttribute(assemblyInformationalVersionAttributeMethodReference);
            assemblyInformationVersionCustomAttribute.ConstructorArguments.Add(new CustomAttributeArgument(mainModule.TypeSystem.String, semanticVersion.ToString()));            
            assemblyDefinition.CustomAttributes.Add(assemblyInformationVersionCustomAttribute);
        } 

    }
}
