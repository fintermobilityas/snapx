using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using JetBrains.Annotations;
using Mono.Cecil;
using Snap.Core;

namespace Snap.Shared.Tests.Extensions
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedMember.Global")]
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

        public static string BuildRuntimeConfigFilename([NotNull] this AssemblyDefinition assemblyDefinition, [NotNull] ISnapFilesystem snapFilesystem, OSPlatform osPlatform = default)
        {
            if (assemblyDefinition == null) throw new ArgumentNullException(nameof(assemblyDefinition));
            if (snapFilesystem == null) throw new ArgumentNullException(nameof(snapFilesystem));
            var filename = assemblyDefinition.BuildRelativeFilename(osPlatform);
            return $"{snapFilesystem.PathGetFileNameWithoutExtension(filename)}.runtimeconfig.json";
        }
        
        public static MemoryStream BuildRuntimeConfig([NotNull] this AssemblyDefinition assemblyDefinition)
        {
            if (assemblyDefinition == null) throw new ArgumentNullException(nameof(assemblyDefinition));
            const string runtimeConfig = @"{
    ""runtimeOptions"": {
        ""frameworks"": [
            {
            ""name"": ""Microsoft.AspNetCore.App"",
            ""version"": ""3.1.1""
            }
        ]
    }
}";
            return new MemoryStream(Encoding.UTF8.GetBytes(runtimeConfig));
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
