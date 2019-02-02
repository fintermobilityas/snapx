using System;
using System.IO;
using Mono.Cecil;

namespace Snap.Shared.Tests.Extensions
{
    public static class AssemblyDefinitionExtensions
    {
        public static string BuildRelativeFilename(this AssemblyDefinition assemblyDefinition)
        {
            if (assemblyDefinition == null) throw new ArgumentNullException(nameof(assemblyDefinition));
            return assemblyDefinition.MainModule.Kind == ModuleKind.Dll ? $"{assemblyDefinition.Name.Name}.dll" : $"{assemblyDefinition.Name.Name}.exe";
        }

        public static string GetFullPath(this AssemblyDefinition assemblyDefinition, string workingDirectory)
        {
            if (assemblyDefinition == null) throw new ArgumentNullException(nameof(assemblyDefinition));
            return Path.Combine(workingDirectory, assemblyDefinition.BuildRelativeFilename());
        }
    }
}
