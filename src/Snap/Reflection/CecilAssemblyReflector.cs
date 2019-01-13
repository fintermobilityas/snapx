using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Mono.Cecil;

namespace Snap.Reflection
{
    internal interface IAttributeReflector
    {
        IDictionary<string, string> Values { get; }
    }

    internal interface ITypeReflector
    {
        IEnumerable<IAttributeReflector> GetAttributes<T>() where T : Attribute;
        string FullName { get; }
        string Name { get; }
    }

    internal interface IAssemblyReflector
    {
        IEnumerable<IAttributeReflector> GetAttributes<T>() where T : Attribute;
        IAttributeReflector GetAttribute<T>() where T : Attribute;
        IEnumerable<ITypeReflector> GetTypes();
        string Location { get; }
        string FileName { get; }
        string FullName { get; }
    }

    internal class CecilAssemblyReflector : IAssemblyReflector
    {
        readonly AssemblyDefinition _assembly;

        public CecilAssemblyReflector([NotNull] AssemblyDefinition assembly)
        {
            _assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
        }

        public IEnumerable<IAttributeReflector> GetAttributes<T>() where T : Attribute
        {
            if (!_assembly.HasCustomAttributes)
            {
                return new IAttributeReflector[] { };
            }

            var expectedTypeName = typeof(T).Name;
            return _assembly.CustomAttributes
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
            var modules = _assembly.Modules;
            foreach (var module in modules)
            {
                var types = module.GetTypes();
                result.AddRange(types.Select(type => new CecilTypeReflector(type)).Cast<ITypeReflector>());
            }
            return result;
        }

        public string Location => _assembly.MainModule.FileName;

        public string FileName => _assembly.MainModule.Name;

        public string FullName => _assembly.FullName;
    }

    internal class CecilTypeReflector : ITypeReflector
    {
        readonly TypeDefinition _type;

        public CecilTypeReflector([NotNull] TypeDefinition type)
        {
            _type = type ?? throw new ArgumentNullException(nameof(type));
        }

        public IEnumerable<IAttributeReflector> GetAttributes<T>() where T : Attribute
        {
            if (!_type.HasCustomAttributes)
            {
                return new IAttributeReflector[] { };
            }

            var expectedTypeName = typeof(T).Name;
            return _type.CustomAttributes
                .Where(a => a.AttributeType.Name == expectedTypeName)
                .Select(a => new CecilAttributeReflector(a))
                .ToList();

        }

        public string FullName => _type.FullName;
        public string Name => _type.Name;
    }

    internal class CecilAttributeReflector : IAttributeReflector
    {
        readonly CustomAttribute _attribute;
        IDictionary<string, string> _values;

        public CecilAttributeReflector([NotNull] CustomAttribute attribute)
        {
            _attribute = attribute ?? throw new ArgumentNullException(nameof(attribute));
        }

        public IDictionary<string, string> Values
        {
            get
            {
                if (_values != null)
                {
                    return _values;
                }

                _values = new Dictionary<string, string>();
                var constructorArguments = _attribute.Constructor.Resolve().Parameters.Select(p => p.Name).ToList();
                var constructorParameters = _attribute.ConstructorArguments.Select(a => a.Value.ToString()).ToList();
                for (var i = 0; i < constructorArguments.Count; i++)
                {
                    _values.Add(constructorArguments[i], constructorParameters[i]);
                }
                foreach (var prop in _attribute.Properties)
                {
                    _values.Add(prop.Name, prop.Argument.Value.ToString());
                }
                return _values;
            }
        }
    }
}
