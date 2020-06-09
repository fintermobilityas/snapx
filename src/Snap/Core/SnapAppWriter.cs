using System;
using System.Runtime.InteropServices;
using System.Text;
using JetBrains.Annotations;
using MessagePack;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Snap.Attributes;
using Snap.Core.Models;
using Snap.Core.Resources;
using Snap.Core.Yaml.Emitters;
using Snap.Core.Yaml.TypeConverters;
using Snap.Extensions;
using Snap.Reflection;
using Snap.Resources;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.Converters;
using YamlDotNet.Serialization.NamingConventions;
using EmbeddedResource = Mono.Cecil.EmbeddedResource;

namespace Snap.Core
{
    internal interface ISnapAppWriter
    {
        AssemblyDefinition BuildSnapAppAssembly(SnapApp snapsApp);
        AssemblyDefinition OptimizeSnapDllForPackageArchive(AssemblyDefinition assemblyDefinition, OSPlatform osPlatform);
        string ToSnapAppYamlString(SnapApp snapApp);
        string ToSnapAppsYamlString(SnapApps snapApps);
        byte[] ToSnapAppsReleases(SnapAppsReleases snapAppsApps);
    }

    internal sealed class SnapAppWriter : ISnapAppWriter
    {      
        static readonly ISerializer YamlSerializerSnapApp = Build(new SerializerBuilder()
            .WithEventEmitter(eventEmitter => new AbstractClassTagEventEmitter(eventEmitter,
                SnapAppReader.AbstractClassTypeMappingsSnapApp)
            )
        );

        static readonly ISerializer YamlSerializerSnapApps = Build(new SerializerBuilder()
            .WithEventEmitter(eventEmitter => new AbstractClassTagEventEmitter(eventEmitter,
                SnapAppReader.AbstractClassTypeMappingsSnapApps)
            )
        );

        static ISerializer Build(SerializerBuilder serializerBuilder)
        {
            return serializerBuilder
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .WithTypeConverter(new SemanticVersionYamlTypeConverter())
                .WithTypeConverter(new OsPlatformYamlTypeConverter())
                .WithTypeConverter(new UriYamlTypeConverter())
                .WithTypeConverter(new DateTimeConverter(DateTimeKind.Utc))     
                .DisableAliases()
                .Build();
        }

        public AssemblyDefinition BuildSnapAppAssembly([NotNull] SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));

            snapApp = new SnapApp(snapApp);

            foreach (var channel in snapApp.Channels)
            {
                channel.PushFeed.ApiKey = null;
                channel.PushFeed.Username = null;
                channel.PushFeed.Password = null;

                // Prevent publishing nuget.org credentials.
                if (channel.UpdateFeed is SnapNugetFeed updateFeed
                    && updateFeed.Source.Host.IndexOf("nuget.org", StringComparison.OrdinalIgnoreCase) != -1)
                {
                    updateFeed.ApiKey = null;
                    updateFeed.Username = null;
                    updateFeed.Password = null;
                }
            }
                                                         
            var snapAppYamlStr = ToSnapAppYamlString(snapApp);
            var currentVersion = snapApp.Version;

            var assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition(SnapConstants.SnapAppLibraryName, new Version(currentVersion.Major,
                    currentVersion.Minor, currentVersion.Patch)), SnapConstants.SnapAppLibraryName, ModuleKind.Dll);

            var assemblyReflector = new CecilAssemblyReflector(assembly);

            var snapAppReleaseDetailsAttributeMethodDefinition = assemblyReflector.MainModule.ImportReference(
                typeof(SnapAppReleaseDetailsAttribute).GetConstructor(Type.EmptyTypes));

            assemblyReflector.AddCustomAttribute(new CustomAttribute(snapAppReleaseDetailsAttributeMethodDefinition));
            assemblyReflector.AddResource(new EmbeddedResource(SnapConstants.SnapAppLibraryName, ManifestResourceAttributes.Public, Encoding.UTF8.GetBytes(snapAppYamlStr)));

            return assembly;
        }

        public AssemblyDefinition OptimizeSnapDllForPackageArchive([NotNull] AssemblyDefinition assemblyDefinition, OSPlatform osPlatform)
        {
            if (assemblyDefinition == null) throw new ArgumentNullException(nameof(assemblyDefinition));

            if (!osPlatform.IsSupportedOsVersion())
            {
                throw new PlatformNotSupportedException();
            }

            var cecilReflector = new CecilAssemblyReflector(assemblyDefinition);
            var cecilResourceReflector = cecilReflector.GetResourceReflector();
            
            cecilResourceReflector.RemoveAllOrThrow(typeof(SnapEmbeddedResourcesTypeRoot).Namespace);

            cecilReflector.RewriteOrThrow<SnapEmbeddedResources>(x => x.IsOptimized, (typedDefinition, getterName, setterName, propertyDefinition) =>
            {                    
                var getIlProcessor = propertyDefinition.GetMethod.Body.GetILProcessor();
                getIlProcessor.Body.Instructions.Clear();
                getIlProcessor.Emit(OpCodes.Ldc_I4_1);
                getIlProcessor.Emit(OpCodes.Ret);
            });

            return assemblyDefinition;
        }

        public string ToSnapAppYamlString([NotNull] SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            return YamlSerializerSnapApp.Serialize(snapApp);
        }

        public string ToSnapAppsYamlString([NotNull] SnapApps snapApps)
        {
            if (snapApps == null) throw new ArgumentNullException(nameof(snapApps));
            return YamlSerializerSnapApps.Serialize(snapApps);
        }

        public byte[] ToSnapAppsReleases([NotNull] SnapAppsReleases snapAppsApps)
        {
            if (snapAppsApps == null) throw new ArgumentNullException(nameof(snapAppsApps));
            return MessagePackSerializer.Serialize(snapAppsApps, MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray));
        }
    }
}
