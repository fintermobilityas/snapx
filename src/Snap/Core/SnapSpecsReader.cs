using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using JetBrains.Annotations;
using NuGet.Versioning;
using Snap.NuGet;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Snap.Core
{
    internal sealed class SemanticVersionYamlTypeConverter : IYamlTypeConverter
    {
        public bool Accepts(Type type)
        {
            return type == typeof(SemanticVersion);
        }

        public object ReadYaml(IParser parser, Type type)
        {
            var semanticVersionStr = ((Scalar)parser.Current).Value;
            parser.MoveNext();
            return SemanticVersion.Parse(semanticVersionStr);
        }

        public void WriteYaml(IEmitter emitter, object value, Type type)
        {
            var semanticVersionStr = ((SemanticVersion)value).ToFullString();
            emitter.Emit(new Scalar(semanticVersionStr));
        }
    }

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

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public sealed class SnapFeed
    {
        public string Name { get; set; }
        [YamlMember(Alias = "source")]
        public Uri SourceUri { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        [YamlMember(Alias = "version")]
        public NuGetProtocolVersion ProtocolVersion { get; set; }
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public sealed class SnapChannel
    {
        public string Name { get; set; }
        public string Feed { get; set; }
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public sealed class SnapTargetFramework
    {
        public string Framework { get; set; }
        [YamlMember(Alias = "rid")]
        public string RuntimeIdentifier { get; set; }
        public string OsPlatform { get; set; }
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public sealed class SnapApp
    {
        public string Id { get; set; }
        public SemanticVersion Version { get; set; }
        public List<SnapChannel> Channels { get; set; }
        public List<SnapTargetFramework> Configurations { get; set; }

        public SnapApp()
        {
            Channels = new List<SnapChannel>();
        }
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public sealed class SnapAppsSpec
    {
        public List<SnapFeed> Feeds { get; set; }
        public List<SnapApp> Apps { get; set; }

        public SnapAppsSpec()
        {
            Feeds = new List<SnapFeed>();
            Apps = new List<SnapApp>();
        }
    }

    public sealed class SnapAppSpec
    {
        public string Id { get; set; }
        public SemanticVersion Version { get; set; }
        public SnapChannel Channel { get; set; }
        public SnapFeed Feed { get; set; }
        public SnapTargetFramework TargetFramework { get; set; }
        public List<SnapChannel> AvailableChannels { get; set; }
        public bool IsDelta { get; set; }
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal interface ISnapSpecsReader
    {
        SnapAppsSpec GetSnapAppsSpecFromStream(MemoryStream stream);
        SnapAppsSpec GetSnapAppsSpecFromYamlString(string yamlString);
        SnapAppSpec GetSnapAppSpecFromYamlString(string yamlString);
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal sealed class SnapSpecsReader : ISnapSpecsReader
    {
        static readonly Deserializer Deserializer = new DeserializerBuilder()
            .WithNamingConvention(new CamelCaseNamingConvention())
            .WithTypeConverter(new SemanticVersionYamlTypeConverter())
            .WithTypeConverter(new UriYamlTypeConverter())
            .Build();

        public SnapAppsSpec GetSnapAppsSpecFromStream(MemoryStream stream)
        {
            return GetSnapAppsSpecFromYamlString(Encoding.UTF8.GetString(stream.ToArray()));
        }

        public SnapAppsSpec GetSnapAppsSpecFromYamlString(string yamlString)
        {
            if (string.IsNullOrWhiteSpace(yamlString)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(yamlString));

            return DeserializeSnapAppsSpec(yamlString);
        }

        public SnapAppSpec GetSnapAppSpecFromYamlString([NotNull] string yamlString)
        {
            if (string.IsNullOrWhiteSpace(yamlString)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(yamlString));
            return Deserializer.Deserialize<SnapAppSpec>(yamlString);
        }

        SnapAppsSpec DeserializeSnapAppsSpec(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(content));

            return Deserializer.Deserialize<SnapAppsSpec>(content);
        }
    }
}
