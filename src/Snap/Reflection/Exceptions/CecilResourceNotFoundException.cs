using Mono.Cecil;

namespace Snap.Reflection.Exceptions;

internal class CecilResourceNotFoundException : CecilReflectorException
{
    public CecilResourceNotFoundException(AssemblyDefinition assemblyDefinition, string resourceName) : base(assemblyDefinition, 
        $"Unable to find resource with name: {resourceName}. Assembly: {assemblyDefinition.FullName}")
    {

    }
}