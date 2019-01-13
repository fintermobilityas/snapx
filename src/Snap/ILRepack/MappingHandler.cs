using System;
using System.Collections.Generic;
using System.Linq;
using Fasterflect;
using Mono.Cecil;

namespace Snap.ILRepack
{
    internal class MappingHandler
    {
        struct Pair : IEquatable<Pair>
        {
            readonly string _scope;
            readonly string _name;
            public readonly IMetadataScope _metadataScope;

            public Pair(string scope, string name, IMetadataScope metadataScope)
            {
                _scope = scope;
                _name = name;
                _metadataScope = metadataScope;
            }

            public override int GetHashCode()
            {
                return _scope.GetHashCode() + _name.GetHashCode();
            }

            public bool Equals(Pair p)
            {
                return p._scope == _scope && p._name == _name;
            }
        }

        readonly IDictionary<Pair, TypeDefinition> _mappings = new Dictionary<Pair, TypeDefinition>();
        readonly IDictionary<Pair, TypeReference> _exportMappings = new Dictionary<Pair, TypeReference>();

        internal TypeDefinition GetRemappedType(TypeReference r)
        {
            if (r.Scope != null && _mappings.TryGetValue(GetTypeKey(r), out var other))
            {
                return other;
            }
            return null;
        }

        internal void StoreRemappedType(TypeDefinition orig, TypeDefinition renamed)
        {
            if (orig.Scope != null)
            {
                _mappings[GetTypeKey(orig)] = renamed;
            }
        }

        internal void StoreExportedType(IMetadataScope scope, string fullName, TypeReference exportedTo)
        {
            if (scope != null)
            {
                _exportMappings[GetTypeKey(scope, fullName)] = exportedTo;
            }
        }

        static Pair GetTypeKey(TypeReference reference)
        {
            return GetTypeKey(reference.Scope, reference.FullName);
        }

        static Pair GetTypeKey(IMetadataScope scope, string fullName)
        {
            return new Pair(GetScopeName(scope), fullName, scope);
        }

        internal static string GetScopeName(IMetadataScope scope)
        {
            switch (scope)
            {
                case AssemblyNameReference assemblyNameReference:
                    return assemblyNameReference.Name;
                case ModuleDefinition moduleDefinition:
                    return moduleDefinition.Assembly.Name.Name;
                default:
                    throw new Exception("Unsupported scope: " + scope);
            }
        }

        internal static string GetScopeFullName(IMetadataScope scope)
        {
            switch (scope)
            {
                case AssemblyNameReference assemblyNameReference:
                    return assemblyNameReference.FullName;
                case ModuleDefinition moduleDefinition:
                    return moduleDefinition.Assembly.Name.FullName;
                default:
                    throw new Exception("Unsupported scope: "+ scope);
            }
        }

        TypeReference GetRootReference(TypeReference type)
        {
            if (type.Scope == null || !_exportMappings.TryGetValue(GetTypeKey(type), out var other))
            {
                return null;
            }

            var next = GetRootReference(other);
            return next ?? other;
        }

        public TypeReference GetExportedRemappedType(TypeReference type)
        {
            var other = GetRootReference(type);
            if (other == null)
            {
                return null;
            }

            // ElementType is used when serializing the Assembly.
            // It should match the actual type (e.g., Boolean for System.Boolean). But because of forwarded types, this is not known at read time, thus having to fix it here.
            var etype = type.GetFieldValue("etype");
            if (etype != (object) 0x0)
            {
                other.SetFieldValue("etype", etype);
            }

            // when reading forwarded types, we don't know if they are value types, fix that later on
            if (type.IsValueType && !other.IsValueType)
                other.IsValueType = true;
            return other;
        }

        internal T GetOrigTypeScope<T>(TypeDefinition nt) where T : class, IMetadataScope
        {
            return _mappings.Where(p => p.Value == nt).Select(p => p.Key._metadataScope).FirstOrDefault() as T;
        }
    }
}
