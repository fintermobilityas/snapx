using System;
using System.Text;
using JetBrains.Annotations;
using Mono.Cecil;
using Snap.Attributes;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Snap.Core
{
    internal interface ISnapSpecsWriter
    {
        AssemblyDefinition BuildSnapAppSpecAssembly(SnapAppSpec snapAppSpec);
        string ToSnapAppSpecYamlString(SnapAppSpec snapAppSpec);
    }

    internal sealed class SnapSpecsWriter : ISnapSpecsWriter
    {
        static readonly Serializer YamlSerializer = new SerializerBuilder()
            .WithNamingConvention(new CamelCaseNamingConvention())
            .WithTypeConverter(new SemanticVersionYamlTypeConverter())
            .WithTypeConverter(new UriYamlTypeConverter())
            .Build();

        public AssemblyDefinition BuildSnapAppSpecAssembly([NotNull] SnapAppSpec snapAppSpec)
        {
            if (snapAppSpec == null) throw new ArgumentNullException(nameof(snapAppSpec));

            var yamlSnapAppSpecStr = ToSnapAppSpecYamlString(snapAppSpec);

            const string snapAppSpecLibraryName = "SnapAppSpec";

            var currentVersion = snapAppSpec.Version;

            var assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition(snapAppSpecLibraryName, new Version(currentVersion.Major, currentVersion.Minor, currentVersion.Patch)), snapAppSpecLibraryName, ModuleKind.Dll);

            var mainModule = assembly.MainModule;

            var attributeConstructor = mainModule.ImportReference(
                typeof(SnapSpecAttribute).GetConstructor(Type.EmptyTypes));

            assembly.CustomAttributes.Add(new CustomAttribute(attributeConstructor));

            var snapAppSpecEmbeddedResource = new EmbeddedResource("SnapAppSpec", ManifestResourceAttributes.Public, Encoding.UTF8.GetBytes(yamlSnapAppSpecStr));
            mainModule.Resources.Add(snapAppSpecEmbeddedResource);
 
            return assembly;
        }

        public string ToSnapAppSpecYamlString([NotNull] SnapAppSpec snapAppSpec)
        {
            if (snapAppSpec == null) throw new ArgumentNullException(nameof(snapAppSpec));
            return YamlSerializer.Serialize(snapAppSpec);
        }
    }
}
