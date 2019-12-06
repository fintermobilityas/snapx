using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using JetBrains.Annotations;
using MessagePack;
using Snap.Core.Models;
using Snap.Core.Yaml.NodeTypeResolvers;
using Snap.Core.Yaml.TypeConverters;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.Converters;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization.NodeTypeResolvers;

namespace Snap.Core
{
    internal interface ISnapAppReader
    {
        SnapApps BuildSnapAppsFromYamlString(string yamlString);
        SnapApp BuildSnapAppFromStream(MemoryStream stream);
        SnapApp BuildSnapAppFromYamlString(string yamlString);
        Task<SnapAppsReleases> BuildSnapAppsReleasesFromStreamAsync(MemoryStream stream);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "UnusedMember.Global")]
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

        static readonly IDeserializer DeserializerSnapApp = Build(new DeserializerBuilder()
            .WithNodeTypeResolver(new AbstractClassTypeResolver(AbstractClassTypeMappingsSnapApp),
                selector => selector.After<TagNodeTypeResolver>()
            )
        );

        static readonly IDeserializer DeserializerSnapApps = Build(new DeserializerBuilder()
            .WithNodeTypeResolver(new AbstractClassTypeResolver(AbstractClassTypeMappingsSnapApps),
                selector => selector.After<TagNodeTypeResolver>()
            )
        );
 
        static IDeserializer Build(DeserializerBuilder builder)
        {
            return builder.WithNamingConvention(CamelCaseNamingConvention.Instance)
                .WithTypeConverter(new SemanticVersionYamlTypeConverter())
                .WithTypeConverter(new UriYamlTypeConverter())
                .WithTypeConverter(new OsPlatformYamlTypeConverter())
                .WithTypeConverter(new DateTimeConverter(DateTimeKind.Utc))     
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

        public Task<SnapAppsReleases> BuildSnapAppsReleasesFromStreamAsync([NotNull] MemoryStream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            return MessagePackSerializer.DeserializeAsync<SnapAppsReleases>(stream);
        }

        public SnapApps BuildSnapAppsFromYamlString([NotNull] string yamlString)
        {
            if (string.IsNullOrWhiteSpace(yamlString)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(yamlString));
            return DeserializerSnapApps.Deserialize<SnapApps>(yamlString);
        }
    }
}
