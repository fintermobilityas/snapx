using Mono.Cecil;

namespace Snap.Reflection.Exceptions;

internal class CecilResourceNotFoundException(AssemblyDefinition assemblyDefinition, string resourceName)
    : CecilReflectorException(assemblyDefinition,
        $"Unable to find resource with name: {resourceName}. Assembly: {assemblyDefinition.FullName}");