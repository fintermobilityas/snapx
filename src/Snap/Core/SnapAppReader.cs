using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using Snap.Core.Models;
using Snap.Core.Yaml.NodeTypeResolvers;
using Snap.Core.Yaml.TypeConverters;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization.NodeTypeResolvers;

namespace Snap.Core
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal interface ISnapAppReader
    {
        SnapApps BuildSnapAppsFromStream(MemoryStream stream);
        SnapApps BuildSnapAppsFromYamlString(string yamlString);
        SnapApp BuildSnapAppFromStream(MemoryStream stream);
        SnapApp BuildSnapAppFromYamlString(string yamlString);
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal sealed class SnapAppReader : ISnapAppReader
    {
        internal static readonly Dictionary<string, Type> AbstractClassTypeMappings = new Dictionary<string, Type>
        {
            { "NugetFeed", typeof(SnapNugetFeed) }, // SnapFeed
            { "HttpFeed", typeof(SnapHttpFeed)} // SnapFeed
        };

        static readonly Deserializer Deserializer = new DeserializerBuilder()
            .WithNamingConvention(new CamelCaseNamingConvention())
            .WithTypeConverter(new SemanticVersionYamlTypeConverter())
            .WithTypeConverter(new UriYamlTypeConverter())
            .WithTypeConverter(new OsPlatformYamlTypeConverter())
            .WithNodeTypeResolver(new AbstractClassTypeResolver(AbstractClassTypeMappings), selector => selector.After<TagNodeTypeResolver>())
            .Build();

        public SnapApps BuildSnapAppsFromStream(MemoryStream stream)
        {
            return BuildSnapAppsFromYamlString(Encoding.UTF8.GetString(stream.ToArray()));
        }

        public SnapApp BuildSnapAppFromStream(MemoryStream stream)
        {
            return BuildSnapAppFromYamlString(Encoding.UTF8.GetString(stream.ToArray()));
        }

        public SnapApp BuildSnapAppFromYamlString(string yamlString)
        {
            if (string.IsNullOrWhiteSpace(yamlString)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(yamlString));
            return Deserializer.Deserialize<SnapApp>(yamlString);
        }

        public SnapApps BuildSnapAppsFromYamlString(string yamlString)
        {
            return Deserializer.Deserialize<SnapApps>(yamlString);
        }
    }
}
