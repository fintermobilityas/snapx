using System;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Snap.Core.Yaml.TypeConverters;

internal sealed class UriYamlTypeConverter : IYamlTypeConverter
{
    public bool Accepts(Type type)
    {
        return type == typeof(Uri);
    }

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var uriStr = ((Scalar)parser.Current)?.Value;
        parser.MoveNext();
        Uri.TryCreate(uriStr, UriKind.Absolute, out var uri);
        return uri;
    }

    public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer serializer)
    {
        var uriStr = ((Uri)value)?.ToString() ?? string.Empty;
        emitter.Emit(new Scalar(uriStr));
    }
}