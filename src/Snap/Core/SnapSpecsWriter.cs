using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ILRepacking;
using JetBrains.Annotations;
using Mono.Cecil;
using Snap.AnyOS.Windows;
using Snap.Attributes;
using Snap.Core.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Snap.Extensions;
using Snap.Logging;

namespace Snap.Core
{
    internal interface ISnapSpecsWriter
    {
        AssemblyDefinition BuildSnapAppSpecAssembly(SnapAppSpec snapAppSpec);
        string ToSnapAppSpecYamlString(SnapAppSpec snapAppSpec);
    }

    internal sealed class SnapSpecsWriter : ISnapSpecsWriter
    {
        static readonly ILog Logger = LogProvider.For<SnapSpecsWriter>();

        public const string SnapAppSpecLibraryName = "SnapAppSpec";
        public const string SnapAppSpecDllFilename = SnapAppSpecLibraryName + ".dll";
        public const string SnapUpdateDllFilename = "Snap.Update.dll";
        public const string SnapUpdateDllUnitTestFilename = "Snap.Update.UnitTest.dll";
        public const string SnapUpdateExeFilename = "Update.exe";
        public const string SnapDllFilename = "Snap.dll";
        public const string SnapDllILRepackedUnitTestFilename = "Snap.ILRepacked.UnitTest.dll";

        static readonly Serializer YamlSerializer = new SerializerBuilder()
            .WithNamingConvention(new CamelCaseNamingConvention())
            .WithTypeConverter(new SemanticVersionYamlTypeConverter())
            .WithTypeConverter(new UriYamlTypeConverter())
            .Build();

        public AssemblyDefinition BuildSnapAppSpecAssembly([NotNull] SnapAppSpec snapAppSpec)
        {
            if (snapAppSpec == null) throw new ArgumentNullException(nameof(snapAppSpec));

            var yamlSnapAppSpecStr = ToSnapAppSpecYamlString(snapAppSpec);

            var currentVersion = snapAppSpec.Version;

            var assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition(SnapAppSpecLibraryName, new Version(currentVersion.Major, currentVersion.Minor, currentVersion.Patch)), SnapAppSpecLibraryName, ModuleKind.Dll);

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
