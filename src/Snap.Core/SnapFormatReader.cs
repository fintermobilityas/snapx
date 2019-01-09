﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Versioning;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Snap.Core
{
    public sealed class SemanticVersionYamlTypeConverter : IYamlTypeConverter
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

    public sealed class UriYamlTypeConverter : IYamlTypeConverter
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
            var uriStr = ((Uri) value).ToString();
            emitter.Emit(new Scalar(uriStr));
        }
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public enum SnapFeedSourceType
    {
        Nuget
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public sealed class SnapFeed
    {
        public string Name { get; set; }
        [YamlMember(Alias = "type")]
        public SnapFeedSourceType SourceType { get; set; }
        [YamlMember(Alias = "source")]
        public Uri SourceUri { get; set; }
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public sealed class SnapChannel
    {
        public string Name { get; set; }
        public List<SnapChannelConfiguration> Configurations { get; set; }
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public sealed class SnapChannelConfiguration
    {
        [YamlMember(Alias = "framework")]
        public string TargetFramework { get; set; }
        [YamlMember(Alias = "rid")]
        public string RuntimeIdentifier { get; set; }
        public string Feed { get; set; }
        [YamlMember(Alias = "source")]
        public string SourceDirectory { get; set; }
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public sealed class SnapApp
    {
        public string Name { get; set; }
        public string Nuspec { get; set; }
        public SemanticVersion Version { get; set; }
        public List<SnapChannel> Channels { get; set; }

        public SnapApp()
        {
            Channels = new List<SnapChannel>();
        }
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public sealed class Snaps 
    {
        public List<SnapFeed> Feeds { get; set; }
        public List<SnapApp> Apps { get; set; }

        public Snaps()
        {
            Feeds = new List<SnapFeed>();
            Apps = new List<SnapApp>();
        }
    }

    public interface ISnapFormatReader
    {
        Task<Snaps> ReadFromDiskAsync(string snapPkgFilename, CancellationToken cancellationToken);
        Snaps ReadFromString(string snapPkgYamlContents);
    }

    public sealed class SnapFormatReader : ISnapFormatReader
    {        
        public async Task<Snaps> ReadFromDiskAsync(string snapPkgFilename, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(snapPkgFilename)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(snapPkgFilename));

            if (!File.Exists(snapPkgFilename))
            {
                return null;
            }

            var yaml = await File.ReadAllTextAsync(snapPkgFilename, cancellationToken);

            return Parse(yaml);
        }

        public Snaps ReadFromString(string snapPkgYamlContents)
        {
            if (string.IsNullOrWhiteSpace(snapPkgYamlContents)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(snapPkgYamlContents));

            return Parse(snapPkgYamlContents);
        }

        Snaps Parse(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(content));

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(new CamelCaseNamingConvention())
                .WithTypeConverter(new SemanticVersionYamlTypeConverter())
                .WithTypeConverter(new UriYamlTypeConverter())
                .Build();

            return deserializer.Deserialize<Snaps>(content);
        }
    }
}
