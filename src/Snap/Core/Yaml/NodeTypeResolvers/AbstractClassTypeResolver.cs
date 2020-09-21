using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Snap.Core.Yaml.NodeTypeResolvers
{
    // https://github.com/aaubry/YamlDotNet/issues/343#issuecomment-424882014

    public sealed class AbstractClassTypeResolver : INodeTypeResolver
    {
        readonly IDictionary<string, Type> _tagMappings;

        public AbstractClassTypeResolver([NotNull] Dictionary<string, Type> typesByName)
        {
            if (typesByName == null) throw new ArgumentNullException(nameof(typesByName));
            var tagMappings = typesByName.ToDictionary(kv => "!" + kv.Key, kv => kv.Value);
            _tagMappings = tagMappings;
        }

        bool INodeTypeResolver.Resolve(NodeEvent nodeEvent, ref Type currentType)
        {
            var typeName = nodeEvent.Tag; // this is what gets the "!MyDotnetClass" tag from the yaml
            if (string.IsNullOrEmpty(typeName))
            {
                return false;
            }

            var arrayType = false;
            if (typeName.EndsWith("[]")) // this handles tags for array types like "!MyDotnetClass[]"
            {
                arrayType = true;
                typeName = typeName[0..^2];
            }

            if (!_tagMappings.TryGetValue(typeName, out var predefinedType))
            {
                throw new YamlException(
                    $"I can't find the type '{nodeEvent.Tag}'. Is it spelled correctly? If there are" +
                    $" multiple types named '{nodeEvent.Tag}', you must used the fully qualified type name.");
            }

            currentType = arrayType ? predefinedType.MakeArrayType() : predefinedType;
            return true;
        }
    }
}
