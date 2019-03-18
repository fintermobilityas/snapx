using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using JetBrains.Annotations;
using Snap.Core.Models;
using Snap.Core.Yaml.NodeTypeResolvers;
using Snap.Core.Yaml.TypeConverters;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization.NodeTypeResolvers;

namespace Snap.Core
{
    internal interface ISnapAppReader
    {
        SnapApps BuildSnapAppsFromYamlString(string yamlString);
        SnapApp BuildSnapAppFromStream(MemoryStream stream);
        SnapApp BuildSnapAppFromYamlString(string yamlString);
        SnapAppsReleases BuildSnapAppsReleasesFromStream(MemoryStream stream);
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal sealed class SnapAppReader : ISnapAppReader
    {
        internal static readonly Dictionary<string, Type> AbstractClassTypeMappingsSnapApp = new Dictionary<string, Type>
        {
            { "nuget", typeof(SnapNugetFeed) }, 
            { "http", typeof(SnapHttpFeed)}
        };

        internal static readonly Dictionary<string, Type> AbstractClassTypeMappingsSnapApps = new Dictionary<string, Type>
        {
            { "nuget", typeof(SnapsNugetFeed) },
            { "http", typeof(SnapsHttpFeed)}
        };

        static readonly Deserializer DeserializerSnapApp = Build(new DeserializerBuilder()
            .WithNodeTypeResolver(new AbstractClassTypeResolver(AbstractClassTypeMappingsSnapApp),
                selector => selector.After<TagNodeTypeResolver>()
            )
        );

        static readonly Deserializer DeserializerSnapApps = Build(new DeserializerBuilder()
            .WithNodeTypeResolver(new AbstractClassTypeResolver(AbstractClassTypeMappingsSnapApps),
                selector => selector.After<TagNodeTypeResolver>()
            )
        );
 
        static Deserializer Build(DeserializerBuilder builder)
        {
            return builder.WithNamingConvention(new CamelCaseNamingConvention())
                .WithTypeConverter(new SemanticVersionYamlTypeConverter())
                .WithTypeConverter(new UriYamlTypeConverter())
                .WithTypeConverter(new OsPlatformYamlTypeConverter())
                .Build();
        }

        public SnapApps BuildSnapAppsFromStream([NotNull] MemoryStream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            return BuildSnapAppsFromYamlString(Encoding.UTF8.GetString(stream.ToArray()));
        }

        public SnapApp BuildSnapAppFromStream([NotNull] MemoryStream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            return BuildSnapAppFromYamlString(Encoding.UTF8.GetString(stream.ToArray()));
        }

        public SnapApp BuildSnapAppFromYamlString(string yamlString)
        {
            if (string.IsNullOrWhiteSpace(yamlString)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(yamlString));
            return DeserializerSnapApp.Deserialize<SnapApp>(yamlString);
        }

        public SnapAppsReleases BuildSnapAppsReleasesFromStream([NotNull] MemoryStream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            return BuildSnapAppsReleasesFromYamlString(Encoding.UTF8.GetString(stream.ToArray()));
        }

        public SnapAppsReleases BuildSnapAppsReleasesFromYamlString([NotNull] string yamlString)
        {
            if (string.IsNullOrWhiteSpace(yamlString)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(yamlString));
            return DeserializerSnapApp.Deserialize<SnapAppsReleases>(yamlString);
        }

        public SnapApps BuildSnapAppsFromYamlString([NotNull] string yamlString)
        {
            if (string.IsNullOrWhiteSpace(yamlString)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(yamlString));
            return DeserializerSnapApps.Deserialize<SnapApps>(yamlString);
        }
    }
}
