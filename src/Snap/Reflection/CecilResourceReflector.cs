using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using JetBrains.Annotations;
using Mono.Cecil;
using Snap.Reflection.Exceptions;

namespace Snap.Reflection
{
    [SuppressMessage("ReSharper", "UnusedMemberInSuper.Global")]
    internal interface IResourceReflector
    {
        IEnumerable<Resource> GetResources();
        void RemoveOrThrow([NotNull] string name);
        void RemoveAllOrThrow(string @namespace);
    }

    internal class CecilResourceReflector : IResourceReflector
    {
        readonly AssemblyDefinition _assemblyDefinition;

        public CecilResourceReflector([NotNull] AssemblyDefinition assemblyDefinition)
        {
            _assemblyDefinition = assemblyDefinition ?? throw new ArgumentNullException(nameof(assemblyDefinition));
        }

        public IEnumerable<Resource> GetResources()
        {
            return _assemblyDefinition.MainModule.Resources;
        }

        public void RemoveOrThrow(string name)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            var resource = GetResources().SingleOrDefault(x => x.Name == name);
            if (resource == null)
            {
                throw new CecilResourceNotFoundException(_assemblyDefinition, name);
            }

            _assemblyDefinition.MainModule.Resources.Remove(resource);
        }

        public void RemoveAllOrThrow(string @namespace)
        {
            var resourcesRemoved = 0;
            foreach (var resource in GetResources().Where(x => @namespace == null || x.Name.StartsWith(@namespace)).ToList())
            {
                RemoveOrThrow(resource.Name);
                resourcesRemoved++;
            }

            if (resourcesRemoved <= 0)
            {
                throw new Exception($"Failed to remove any resources from assembly: {_assemblyDefinition.FullName}");
            }
        }
    }
}
