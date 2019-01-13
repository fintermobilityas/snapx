using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using NuGet.Versioning;
using Snap.Tests.Support.Extensions;
using Snap.Tests.Support.Misc;

namespace Snap.Tests.Support
{
    public class BaseFixture
    {
        public string WorkingDirectory => Directory.GetCurrentDirectory();

        public (SnapApp app, List<SnapFeed> feeds) BuildSnapAppSpec()
        {
            var snapApp = new SnapApp
            {
                Name = "mysnapapp",
                Version = new SemanticVersion(1, 0, 0),
                Channels = new List<SnapChannel>
                {
                    new SnapChannel
                    {
                        Name = "test",
                        Configurations = new List<SnapChannelConfiguration>
                        {
                            new SnapChannelConfiguration
                            {
                                TargetFramework = "net45",
                                Feed = "myget", 
                                RuntimeIdentifier = "test", 
                                MSBuildProperties = "test"
                            }
                        }
                    }
                }
            };

            var snapFeeds = new List<SnapFeed>
            {
                new SnapFeed
                {
                    Name = "myget",
                    SourceType = SnapFeedSourceType.Nuget,
                    SourceUri = new Uri("https://example.org"),
                    Username = "myusername",
                    Password = "mypassword"
                }
            };

            return (snapApp, snapFeeds);
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

        public void WriteAssemblies(string workingDirectory, bool disposeAssemblyDefinitions = false, params AssemblyDefinition[]  assemblyDefinitions)
        {
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
            if (assemblyDefinitions == null) throw new ArgumentNullException(nameof(assemblyDefinitions));

            WriteAssemblies(workingDirectory, assemblyDefinitions.ToList(), disposeAssemblyDefinitions);
        }

        public void WriteAndDisposeAssemblies(string workingDirectory, params AssemblyDefinition[]  assemblyDefinitions)
        {
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
            if (assemblyDefinitions == null) throw new ArgumentNullException(nameof(assemblyDefinitions));

            WriteAssemblies(workingDirectory, assemblyDefinitions.ToList(), true);
        }

        public IDisposable WithDisposableAssemblies(string workingDirectory, params AssemblyDefinition[] assemblyDefinitions)
        {
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
            if (assemblyDefinitions == null) throw new ArgumentNullException(nameof(assemblyDefinitions));

            WriteAndDisposeAssemblies(workingDirectory, assemblyDefinitions);

            return new DisposableFiles(assemblyDefinitions.Select(x => x.GetFullPath(workingDirectory)).ToList());
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
    }
}
