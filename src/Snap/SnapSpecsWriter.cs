using System;
using System.Collections.Generic;
using System.Text;
using Mono.Cecil;
using Snap.Attributes;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Snap
{
    internal interface ISnapSpecsWriter
    {
        AssemblyDefinition BuildSnapAppSpecAssembly(SnapApp app, List<SnapFeed> feeds, string channelName);
        string ToSnapAppSpecYamlString(SnapApp app, List<SnapFeed> feeds, string channel);
    }

    internal sealed class SnapSpecsWriter : ISnapSpecsWriter
    {
        static readonly Serializer YamlSerializer = new SerializerBuilder()
            .WithNamingConvention(new CamelCaseNamingConvention())
            .WithTypeConverter(new SemanticVersionYamlTypeConverter())
            .WithTypeConverter(new UriYamlTypeConverter())
            .Build();

        public AssemblyDefinition BuildSnapAppSpecAssembly(SnapApp app, List<SnapFeed> feeds, string channelName)
        {
            var yamlSnapAppSpecStr = ToSnapAppSpecYamlString(app, feeds, channelName);

            const string snapAppSpecLibraryName = "SnapAppSpec";

            var assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition(snapAppSpecLibraryName, new Version(app.Version.Major, app.Version.Minor, app.Version.Patch)), snapAppSpecLibraryName, ModuleKind.Dll);

            var mainModule = assembly.MainModule;

            var attributeConstructor = mainModule.ImportReference(
                typeof(SnapSpecAttribute).GetConstructor(Type.EmptyTypes));

            assembly.CustomAttributes.Add(new CustomAttribute(attributeConstructor));

            var snapAppSpecEmbeddedResource = new EmbeddedResource("SnapAppSpec", ManifestResourceAttributes.Public, Encoding.UTF8.GetBytes(yamlSnapAppSpecStr));
            mainModule.Resources.Add(snapAppSpecEmbeddedResource);
 
            return assembly;
        }

        public string ToSnapAppSpecYamlString(SnapApp app, List<SnapFeed> feeds, string channel)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            if (feeds == null) throw new ArgumentNullException(nameof(feeds));
            var snapAppSpec = new SnapAppSpec
            {
                App = app,
                Feeds = feeds,
                Channel = channel
            };
            return YamlSerializer.Serialize(snapAppSpec);
        }
    }
}
