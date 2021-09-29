using System;
using NuGet.Versioning;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Snap.Core.Yaml.TypeConverters;

internal sealed class SemanticVersionYamlTypeConverter : IYamlTypeConverter
{
    public bool Accepts(Type type)
    {
        return type == typeof(SemanticVersion);
    }

    public object ReadYaml(IParser parser, Type type)
    {
        var semanticVersionStr = ((Scalar)parser.Current)?.Value;
        parser.MoveNext();
        SemanticVersion.TryParse(semanticVersionStr, out var semanticVersion);
        return semanticVersion;
    }

    public void WriteYaml(IEmitter emitter, object value, Type type)
    {
        var semanticVersionStr = ((SemanticVersion)value)?.ToNormalizedString() ?? string.Empty;
        emitter.Emit(new Scalar(semanticVersionStr));
    }
}