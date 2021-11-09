using JetBrains.Annotations;
using Mono.Cecil;

namespace Snap.Reflection.Exceptions;

internal class CecilAutoPropertyNotFoundException : CecilReflectorException
{
    public CecilAutoPropertyNotFoundException([NotNull] AssemblyDefinition assemblyDefinition, string propertyName) : 
        base(assemblyDefinition, $"Unable to find auto property: {propertyName}")
    {

    }

}