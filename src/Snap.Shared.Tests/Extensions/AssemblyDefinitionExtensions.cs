using System;
using System.IO;
using System.Runtime.InteropServices;
using Mono.Cecil;

namespace Snap.Shared.Tests.Extensions
{
    internal static class AssemblyDefinitionExtensions
    {
        public static string BuildRelativeFilename(this AssemblyDefinition assemblyDefinition)
        {
            if (assemblyDefinition == null) throw new ArgumentNullException(nameof(assemblyDefinition));
            var assemblyName = $"{assemblyDefinition.Name.Name}";
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                assemblyName = assemblyDefinition.MainModule.Kind == ModuleKind.Dll ? $"{assemblyName}.dll" : $"{assemblyName}.exe";
            }
            else
            {
                assemblyName = assemblyDefinition.MainModule.Kind == ModuleKind.Dll ? $"{assemblyName}.dll" : $"{assemblyName}";
            }

            return assemblyName;
        }

        public static string GetFullPath(this AssemblyDefinition assemblyDefinition, string workingDirectory)
        {
            if (assemblyDefinition == null) throw new ArgumentNullException(nameof(assemblyDefinition));
            return Path.Combine(workingDirectory, assemblyDefinition.BuildRelativeFilename());
        }
    }
}
