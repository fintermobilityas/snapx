using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using JetBrains.Annotations;
using Mono.Cecil;
using Snap.Extensions;
using Snap.Reflection.Exceptions;

namespace Snap.Reflection
{
    
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedMemberInSuper.Global")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal interface IAssemblyReflector
    {
        string Location { get; }
        string FileName { get; }
        string FullName { get; }
        ModuleDefinition MainModule {get;}
        IEnumerable<IAttributeReflector> GetAttributes<T>() where T : Attribute;
        IAttributeReflector GetAttribute<T>() where T : Attribute;
        IEnumerable<ITypeReflector> GetTypes();
        CecilResourceReflector GetResourceReflector();
        void AddResource(EmbeddedResource embeddedResource);
        void AddCustomAttribute(CustomAttribute attribute);
        void RewriteOrThrow<TSource>(Expression<Func<TSource, object>> selector, Action<TypeDefinition, string, string, PropertyDefinition> rewriter);
    }

    internal class CecilAssemblyReflector : IAssemblyReflector
    {
        readonly AssemblyDefinition _assemblyDefinition;
        
        public string Location => _assemblyDefinition.MainModule.FileName;
        public string FileName => _assemblyDefinition.MainModule.Name;
        public string FullName => _assemblyDefinition.FullName;
        public ModuleDefinition MainModule => _assemblyDefinition.MainModule;

        public CecilAssemblyReflector([NotNull] AssemblyDefinition assemblyDefinition)
        {
            _assemblyDefinition = assemblyDefinition ?? throw new ArgumentNullException(nameof(assemblyDefinition));
        }

        public IEnumerable<IAttributeReflector> GetAttributes<T>() where T : Attribute
        {
            if (!_assemblyDefinition.HasCustomAttributes)
            {
                return new IAttributeReflector[] { };
            }

            var expectedTypeName = typeof(T).Name;
            return _assemblyDefinition.CustomAttributes
                .Where(a => a.AttributeType.Name == expectedTypeName)
                .Select(a => new CecilAttributeReflector(a))
                .ToList();
        }

        public IAttributeReflector GetAttribute<T>() where T : Attribute
        {
            return GetAttributes<T>().SingleOrDefault();
        }

        public IEnumerable<ITypeReflector> GetTypes()
        {
            var result = new List<ITypeReflector>();
            var modules = _assemblyDefinition.Modules;
            foreach (var module in modules)
            {
                var types = module.GetTypes();
                result.AddRange(types.Select(type => new CecilTypeReflector(type)));
            }
            return result;
        }

        public CecilResourceReflector GetResourceReflector()
        {
            return new CecilResourceReflector(_assemblyDefinition);
        }

        public void AddCustomAttribute([NotNull] CustomAttribute attribute)
        {
            if (attribute == null) throw new ArgumentNullException(nameof(attribute));
            _assemblyDefinition.CustomAttributes.Add(attribute);
        }

        public void AddResource([NotNull] EmbeddedResource embeddedResource)
        {
            if (embeddedResource == null) throw new ArgumentNullException(nameof(embeddedResource));
            _assemblyDefinition.MainModule.Resources.Add(embeddedResource);
        }

        public void RewriteOrThrow<TSource>([NotNull] Expression<Func<TSource, object>> selector,
            [NotNull] Action<TypeDefinition, string, string, PropertyDefinition> rewriter)
        {
            if (selector == null) throw new ArgumentNullException(nameof(selector));
            if (rewriter == null) throw new ArgumentNullException(nameof(rewriter));

            var (typeDefinition, autoPropertyDefinition, setterName, getterName) = _assemblyDefinition.ResolveAutoProperty(selector);
            if (autoPropertyDefinition == null)
            {
                throw new CecilAutoPropertyNotFoundException(_assemblyDefinition, selector.BuildMemberName());
            }

            rewriter(typeDefinition, getterName, setterName, autoPropertyDefinition);
        }
    }
}
