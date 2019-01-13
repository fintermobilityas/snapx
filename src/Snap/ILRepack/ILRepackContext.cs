using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Mono.Cecil;

namespace Snap.ILRepack
{
    internal interface IRepackContext
    {
        AssemblyDefinition TargetAssemblyDefinition { get; }
        MappingHandler MappingHandler { get; }
        ReflectionHelper ReflectionHelper { get; }

        string FixTypeName(string assemblyName, string typeName);
        string FixAssemblyName(string assemblyName);
        string FixStr(string content);
        TypeDefinition GetMergedTypeFromTypeRef(TypeReference reference);
    }

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    internal sealed class ILRepackContext : IRepackContext
    {
        public AssemblyDefinition TargetAssemblyDefinition { get; }
        public MappingHandler MappingHandler { get;  }
        public ReflectionHelper ReflectionHelper { get; }

        public ILRepackContext(AssemblyDefinition assemblyDefinition)
        {
            TargetAssemblyDefinition = assemblyDefinition ?? throw new ArgumentNullException(nameof(assemblyDefinition));
            MappingHandler = new MappingHandler();
            ReflectionHelper = new ReflectionHelper(this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string FixTypeName(string assemblyName, string typeName)
        {
            return typeName;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string FixAssemblyName(string assemblyName)
        {
            return assemblyName;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string FixStr(string content)
        {
            return content;
        }

        public TypeDefinition GetMergedTypeFromTypeRef(TypeReference reference)
        {
            return MappingHandler.GetRemappedType(reference);
        }
    }
}
