using System;
using System.Runtime.InteropServices;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Snap.Core.Yaml.TypeConverters;

internal sealed class OsPlatformYamlTypeConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(OSPlatform);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var osPlatform = ((Scalar)parser.Current)?.Value;
        parser.MoveNext();
        return TryCreateOsPlatform(osPlatform);
    }

    public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer serializer)
    {
        if (value is not OSPlatform osPlatform)
        {
            throw new ArgumentException("Value is not an OSPlatform", nameof(value));
        }

        var osPlatformStr = osPlatform.ToString().ToLowerInvariant();
        emitter.Emit(new Scalar(osPlatformStr));
    }
    
    static OSPlatform TryCreateOsPlatform(string osPlatform)
    {
        if (string.IsNullOrWhiteSpace(osPlatform))
        {
            osPlatform = "unknown";
        }

        return OSPlatform.Create(osPlatform.ToUpperInvariant());
    }
}