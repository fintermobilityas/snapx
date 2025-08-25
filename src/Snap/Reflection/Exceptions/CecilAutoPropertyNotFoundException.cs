using JetBrains.Annotations;
using Mono.Cecil;

namespace Snap.Reflection.Exceptions;

internal class CecilAutoPropertyNotFoundException([NotNull] AssemblyDefinition assemblyDefinition, string propertyName)
    : CecilReflectorException(assemblyDefinition, $"Unable to find auto property: {propertyName}");