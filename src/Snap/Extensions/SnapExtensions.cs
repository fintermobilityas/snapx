using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using JetBrains.Annotations;
using Mono.Cecil;
using Snap.Attributes;
using Snap.Reflection;

namespace Snap.Extensions
{
    public static class SnapExtensions
    {
        internal static SnapAppSpec GetSnapAppSpec(this AssemblyDefinition assemblyDefinition, ISnapSpecsReader snapSpecsReader)
        {
            var snapSpecAttribute = new CecilAssemblyReflector(assemblyDefinition).GetAttribute<SnapSpecAttribute>();
            if (snapSpecAttribute == null)
            {
                throw new Exception($"Unable to find {nameof(SnapSpecAttribute)} in assembly {assemblyDefinition.FullName}.");
            }

            const string snapAppSpecResourceName = "SnapAppSpec";
            var snapSpecResource = assemblyDefinition.MainModule.Resources.SingleOrDefault(x => x.Name == snapAppSpecResourceName);
            if (!(snapSpecResource is EmbeddedResource snapSpecEmbeddedResource))
            {
                throw new Exception($"Unable to find {snapAppSpecResourceName} in assembly {assemblyDefinition.FullName}.");
            }

            using (var inputStream = snapSpecEmbeddedResource.GetResourceStream())
            using (var outputStream = new MemoryStream())
            {
                inputStream.CopyTo(outputStream);
                outputStream.Seek(0, SeekOrigin.Begin);

                var yamlString = Encoding.UTF8.GetString(outputStream.ToArray());
                var snapAppSpec = snapSpecsReader.GetSnapAppSpecFromYamlString(yamlString);

                return snapAppSpec;
            }
        }

        public static SnapAppSpec GetSnapAppSpec([NotNull] this Assembly assembly)
        {
            if (assembly == null) throw new ArgumentNullException(nameof(assembly));

            var snapSpecDllDirectory = Path.GetDirectoryName(assembly.Location);
            if (snapSpecDllDirectory == null)
            {
                throw new Exception($"Unable to find snap spec directory from assembly location. Assembly name: {assembly.FullName}.");
            }

            var snapSpecDllFilename = Path.Combine(snapSpecDllDirectory, "Snap.Spec.dll");
            
            using (var assemblyDefinition = AssemblyDefinition.ReadAssembly(snapSpecDllFilename))
            {
                return assemblyDefinition.GetSnapAppSpec(new SnapSpecsReader());
            }
        }
    }
}
