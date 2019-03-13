using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using JetBrains.Annotations;
using Mono.Cecil;
using Snap.Core;

namespace Snap.Shared.Tests.Extensions
{
    internal static class AssemblyDefinitionExtensions
    {
        public static string BuildRelativeFilename(this AssemblyDefinition assemblyDefinition, OSPlatform osPlatform = default)
        {
            if (assemblyDefinition == null) throw new ArgumentNullException(nameof(assemblyDefinition));
            var assemblyName = $"{assemblyDefinition.Name.Name}";
            
            if (osPlatform != default && osPlatform == OSPlatform.Windows || osPlatform == default && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                assemblyName = assemblyDefinition.MainModule.Kind == ModuleKind.Dll ? $"{assemblyName}.dll" : $"{assemblyName}.exe";
            }
            else
            {
                assemblyName = assemblyDefinition.MainModule.Kind == ModuleKind.Dll ? $"{assemblyName}.dll" : $"{assemblyName}";
            }

            return assemblyName;
        }

        public static string BuildRuntimeSettingsRelativeFilename([NotNull] this AssemblyDefinition assemblyDefinition, OSPlatform osPlatform = default)
        {
            if (assemblyDefinition == null) throw new ArgumentNullException(nameof(assemblyDefinition));
            var filename = assemblyDefinition.BuildRelativeFilename(osPlatform);
            return $"{filename}.runtimesettings.json";
        }
        
        public static MemoryStream BuildRuntimeSettings([NotNull] this AssemblyDefinition assemblyDefinition)
        {
            if (assemblyDefinition == null) throw new ArgumentNullException(nameof(assemblyDefinition));
            const string runtimeConfigSettings = @"
{
  ""runtimeOptions"": {
            ""tfm"": ""netcoreapp2.1"",
            ""framework"": {
                ""name"": ""Microsoft.NETCore.App"",
                ""version"": ""2.1.0""
            }
        }
    }
";
            return new MemoryStream(Encoding.UTF8.GetBytes(runtimeConfigSettings));
        }

        public static string GetFullPath(this AssemblyDefinition assemblyDefinition, [NotNull] ISnapFilesystem filesystem, [NotNull] string workingDirectory, OSPlatform osPlatform = default)
        {
            if (assemblyDefinition == null) throw new ArgumentNullException(nameof(assemblyDefinition));
            if (filesystem == null) throw new ArgumentNullException(nameof(filesystem));
            if (workingDirectory == null) throw new ArgumentNullException(nameof(workingDirectory));
            return filesystem.PathCombine(workingDirectory, assemblyDefinition.BuildRelativeFilename(osPlatform));
        }
    }
}
