using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.EventEmitters;

namespace Snap.Core.Yaml.Emitters
{   
    // https://github.com/aaubry/YamlDotNet/pull/229/files

    internal class AbstractClassTagEventEmitter : ChainedEventEmitter
    {
        readonly IDictionary<Type, string> _tagMappings;

        public AbstractClassTagEventEmitter(IEventEmitter inner, [NotNull] IDictionary<string, Type> tagMappings) : base(inner)
        {
            if (tagMappings == null) throw new ArgumentNullException(nameof(tagMappings));
            _tagMappings = tagMappings.ToDictionary(x => x.Value, x => $"!{x.Key}");
        }

        public override void Emit(MappingStartEventInfo eventInfo, IEmitter emitter)
        {
            if(_tagMappings.ContainsKey(eventInfo.Source.Type))
            {
                eventInfo.Tag = _tagMappings[eventInfo.Source.Type];
            }
            base.Emit(eventInfo, emitter);
        }
    }
}

