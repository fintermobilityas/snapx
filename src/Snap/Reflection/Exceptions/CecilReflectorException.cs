using System;
using JetBrains.Annotations;
using Mono.Cecil;

namespace Snap.Reflection.Exceptions;

internal abstract class CecilReflectorException : Exception
{
    public AssemblyDefinition AssemblyDefinition { [UsedImplicitly] get; }

    protected CecilReflectorException([NotNull] AssemblyDefinition assemblyDefinition, [NotNull] string message) : base($"{message}. Assembly: {assemblyDefinition.FullName}.")
    {
        if (message == null) throw new ArgumentNullException(nameof(message));
        AssemblyDefinition = assemblyDefinition ?? throw new ArgumentNullException(nameof(assemblyDefinition));
    }
}