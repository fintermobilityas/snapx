using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Mono.Cecil;
using Snap.Reflection.Exceptions;

namespace Snap.Reflection
{
    internal interface IResourceReflector
    {
        IEnumerable<Resource> GetResources();
        void RemoveOrThrow([NotNull] string name);
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
    }
}
