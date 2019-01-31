using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using JetBrains.Annotations;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Snap.Attributes;
using Snap.Core.Models;
using Snap.Core.Resources;
using Snap.Core.UnitTest;
using Snap.Core.Yaml.Emitters;
using Snap.Core.Yaml.TypeConverters;
using Snap.Extensions;
using Snap.Reflection;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using EmbeddedResource = Mono.Cecil.EmbeddedResource;

namespace Snap.Core
{
    internal interface ISnapAppWriter
    {
        string SnapAppLibraryName { get; }
        string SnapDllFilename { get; }
        string SnapAppDllFilename { get; }
        AssemblyDefinition BuildSnapAppAssembly(SnapApp snapsApp);
        AssemblyDefinition OptimizeSnapDllForPackageArchive(AssemblyDefinition assemblyDefinition, OSPlatform osPlatform);
        string ToSnapAppYamlString(SnapApp snapApp);
        string ToSnapAppsYamlString(SnapApps snapApps);
    }

    internal sealed class SnapAppWriter : ISnapAppWriter
    {
        public string SnapAppLibraryName => "Snap.App";
        public string SnapDllFilename => "Snap.dll";
        public string SnapAppDllFilename => SnapAppLibraryName + ".dll";

        static readonly Serializer YamlSerializer = new SerializerBuilder()
            .WithNamingConvention(new CamelCaseNamingConvention())
            .WithTypeConverter(new SemanticVersionYamlTypeConverter())
            .WithTypeConverter(new UriYamlTypeConverter())
            .WithTypeConverter(new OsPlatformYamlTypeConverter())
            .WithEventEmitter(eventEmitter => new AbstractClassTagEventEmitter(eventEmitter, SnapAppReader.AbstractClassTypeMappings))
            .DisableAliases()
            .EmitDefaults()
            .Build();

        public AssemblyDefinition BuildSnapAppAssembly([NotNull] SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));

            // Clone
            snapApp = new SnapApp(snapApp);


            foreach (var channel in snapApp.Channels)
            {
                channel.PushFeed.ApiKey = null;
            }

            var snapAppYamlStr = ToSnapAppYamlString(snapApp);

            var currentVersion = snapApp.Version;

            var assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition(SnapAppLibraryName, new Version(currentVersion.Major,
                    currentVersion.Minor, currentVersion.Patch)), SnapAppLibraryName, ModuleKind.Dll);

            var mainModule = assembly.MainModule;

            var attributeConstructor = mainModule.ImportReference(
                typeof(SnapAppReleaseDetailsAttribute).GetConstructor(Type.EmptyTypes));

            assembly.CustomAttributes.Add(new CustomAttribute(attributeConstructor));

            var snapAppSpecEmbeddedResource = new EmbeddedResource(SnapAppLibraryName, ManifestResourceAttributes.Public, Encoding.UTF8.GetBytes(snapAppYamlStr));
            mainModule.Resources.Add(snapAppSpecEmbeddedResource);

            return assembly;
        }

        public AssemblyDefinition OptimizeSnapDllForPackageArchive([NotNull] AssemblyDefinition assemblyDefinition, OSPlatform osPlatform)
        {
            if (assemblyDefinition == null) throw new ArgumentNullException(nameof(assemblyDefinition));

            if (!osPlatform.IsSupportedOsVersion())
            {
                throw new PlatformNotSupportedException();
            }

            if (osPlatform.IsAnyOs())
            {
                return assemblyDefinition;
            }

            var cecilReflector = new CecilAssemblyReflector(assemblyDefinition);
            var cecilResourceFlector = cecilReflector.GetResourceReflector();

            void PruneResources()
            {
                var coreRunResourceName = "Snap.Resources.corerun.";
                if (osPlatform == OSPlatform.Windows)
                {
                    coreRunResourceName += "corerun";
                }
                else if (osPlatform == OSPlatform.Linux)
                {
                    coreRunResourceName += "corerun.exe";
                }

                cecilResourceFlector.RemoveOrThrow(coreRunResourceName);

                cecilReflector.RewriteOrThrow<SnapEmbeddedResources>(x => x.IsStripped, (typedDefinition, getterName, setterName, propertyDefinition) =>
                {                    
                    var getIlProcessor = propertyDefinition.GetMethod.Body.GetILProcessor();
                    getIlProcessor.Body.Instructions.Clear();
                    getIlProcessor.Emit(OpCodes.Ldc_I4_1);
                    getIlProcessor.Emit(OpCodes.Ret);
                });
            }

            PruneResources();

            return assemblyDefinition;
        }

        public string ToSnapAppYamlString([NotNull] SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            return YamlSerializer.Serialize(snapApp);
        }

        public string ToSnapAppsYamlString([NotNull] SnapApps snapApps)
        {
            if (snapApps == null) throw new ArgumentNullException(nameof(snapApps));
            return YamlSerializer.Serialize(snapApps);
        }
    }
}
