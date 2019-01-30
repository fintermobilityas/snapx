using System;
using System.Text;
using JetBrains.Annotations;
using Mono.Cecil;
using Snap.Attributes;
using Snap.Core.Models;
using Snap.Core.Yaml;
using Snap.Core.Yaml.Emitters;
using Snap.Core.Yaml.TypeConverters;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Snap.Core
{
    internal interface ISnapAppWriter
    {
        AssemblyDefinition BuildSnapAppAssembly(SnapApp snapsApp);
        string ToSnapAppYamlString(SnapApp snapApp);
        string ToSnapAppsYamlString(SnapApps snapApps);
    }

    internal sealed class SnapAppWriter : ISnapAppWriter
    {
        public const string SnapAppLibraryName = "SnapApp";
        public const string SnapAppDllFilename = SnapAppLibraryName + ".dll";

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
