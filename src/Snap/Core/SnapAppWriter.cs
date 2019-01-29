using System;
using System.Text;
using JetBrains.Annotations;
using Mono.Cecil;
using Snap.Attributes;
using Snap.Core.Specs;
using Snap.Core.Yaml;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Snap.Logging;

namespace Snap.Core
{
    internal interface ISnapAppWriter
    {
        AssemblyDefinition BuildSnapAppAssembly(SnapApp snapApp);
        string ToSnapAppYamlString(SnapApp snapApp);
    }

    internal sealed class SnapAppWriter : ISnapAppWriter
    {
        static readonly ILog Logger = LogProvider.For<SnapAppWriter>();

        public const string SnapAppLibraryName = "SnapApp";
        public const string SnapAppDllFilename = SnapAppLibraryName + ".dll";

        static readonly Serializer YamlSerializer = new SerializerBuilder()
            .WithNamingConvention(new CamelCaseNamingConvention())
            .WithTypeConverter(new SemanticVersionYamlTypeConverter())
            .WithTypeConverter(new UriYamlTypeConverter())
            .WithTypeConverter(new OsPlatformYamlTypeConverter())
            .Build();

        public AssemblyDefinition BuildSnapAppAssembly([NotNull] SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));

            // Clone
            snapApp = new SnapApp(snapApp);

            // Prune sensitive information
            foreach (var feed in snapApp.Feeds)
            {
                // NB! Username/password is allowed but its the users responsibility to
                // ensure that the credentials can only be used for reading.
                feed.ApiKey = null;
            }

            snapApp.Validate();

            var yamlSnapAppStr = ToSnapAppYamlString(snapApp);

            var currentVersion = snapApp.Version;

            var assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition(SnapAppLibraryName, new Version(currentVersion.Major, currentVersion.Minor, currentVersion.Patch)), SnapAppLibraryName, ModuleKind.Dll);

            var mainModule = assembly.MainModule;

            var attributeConstructor = mainModule.ImportReference(
                typeof(SnapAppReleaseDetailsAttribute).GetConstructor(Type.EmptyTypes));

            assembly.CustomAttributes.Add(new CustomAttribute(attributeConstructor));

            var snapAppSpecEmbeddedResource = new EmbeddedResource(SnapAppLibraryName, ManifestResourceAttributes.Public, Encoding.UTF8.GetBytes(yamlSnapAppStr));
            mainModule.Resources.Add(snapAppSpecEmbeddedResource);

            return assembly;
        }

        public string ToSnapAppYamlString([NotNull] SnapApp snapApp)
        {
            if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
            return YamlSerializer.Serialize(snapApp);
        }
    }
}
