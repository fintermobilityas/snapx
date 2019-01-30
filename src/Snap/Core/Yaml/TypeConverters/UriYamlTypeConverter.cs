using System;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Snap.Core.Yaml.TypeConverters
{
    internal sealed class UriYamlTypeConverter : IYamlTypeConverter
    {
        public bool Accepts(Type type)
        {
            return type == typeof(Uri);
        }

        public object ReadYaml(IParser parser, Type type)
        {
            var uriStr = ((Scalar)parser.Current).Value;
            parser.MoveNext();
            Uri.TryCreate(uriStr, UriKind.Absolute, out var uri);
            return uri;
        }

        public void WriteYaml(IEmitter emitter, object value, Type type)
        {
            var uriStr = ((Uri)value).ToString();
            emitter.Emit(new Scalar(uriStr));
        }
    }
}
