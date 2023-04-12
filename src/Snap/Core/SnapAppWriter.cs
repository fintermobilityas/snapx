using System;
using System.Text;
using JetBrains.Annotations;
using MessagePack;
using Mono.Cecil;
using Snap.Attributes;
using Snap.Core.Models;
using Snap.Core.Yaml.Emitters;
using Snap.Core.Yaml.TypeConverters;
using Snap.Reflection;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.Converters;
using YamlDotNet.Serialization.NamingConventions;
using EmbeddedResource = Mono.Cecil.EmbeddedResource;

namespace Snap.Core;

internal interface ISnapAppWriter
{
    AssemblyDefinition BuildSnapAppAssembly(SnapApp snapsApp);
    string ToSnapAppYamlString(SnapApp snapApp);
    string ToSnapAppsYamlString(SnapApps snapApps);
    byte[] ToSnapAppsReleases(SnapAppsReleases snapAppsApps);
}

internal sealed class SnapAppWriter : ISnapAppWriter
{      
    static readonly ISerializer YamlSerializerSnapApp = Build(new SerializerBuilder()
        .WithEventEmitter(eventEmitter => new AbstractClassTagEventEmitter(eventEmitter,
            SnapAppReader.AbstractClassTypeMappingsSnapApp)
        )
    );

    static readonly ISerializer YamlSerializerSnapApps = Build(new SerializerBuilder()
        .WithEventEmitter(eventEmitter => new AbstractClassTagEventEmitter(eventEmitter,
            SnapAppReader.AbstractClassTypeMappingsSnapApps)
        )
    );

    static ISerializer Build(SerializerBuilder serializerBuilder)
    {
        return serializerBuilder
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeConverter(new SemanticVersionYamlTypeConverter())
            .WithTypeConverter(new OsPlatformYamlTypeConverter())
            .WithTypeConverter(new UriYamlTypeConverter())
            .WithTypeConverter(new DateTimeConverter(DateTimeKind.Utc))     
            .DisableAliases()
            .Build();
    }

    public AssemblyDefinition BuildSnapAppAssembly([NotNull] SnapApp snapApp)
    {
        if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));

        snapApp = new SnapApp(snapApp);

        foreach (var channel in snapApp.Channels)
        {
            if (channel.PushFeed == null)
            {
                throw new Exception($"{nameof(channel.PushFeed)} cannot be null. Channel: {channel.Name}. Application id: {snapApp.Id}");
            }

            if (channel.UpdateFeed == null)
            {
                throw new Exception($"{nameof(channel.UpdateFeed)} cannot be null. Channel: {channel.Name}. Application id: {snapApp.Id}");
            }

            channel.PushFeed.ApiKey = null;
            channel.PushFeed.Username = null;
            channel.PushFeed.Password = null;

            if (channel.UpdateFeed.Source == null)
            {
                throw new Exception(
                    $"Update feed {nameof(channel.UpdateFeed.Source)} cannot be null. Channel: {channel.Name}. Application id: {snapApp.Id}");
            }
            
            if (channel.UpdateFeed is SnapNugetFeed updateFeed)
            {
                updateFeed.ApiKey = null;
                
                // Prevent publishing nuget.org credentials.
                if (updateFeed.Source.Host.IndexOf("nuget.org", StringComparison.OrdinalIgnoreCase) != -1)
                {
                    updateFeed.Username = null;
                    updateFeed.Password = null;
                }
            }
        }
                                                         
        var snapAppYamlStr = ToSnapAppYamlString(snapApp);
        var currentVersion = snapApp.Version;

        var assembly = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition(SnapConstants.SnapAppLibraryName, new Version(currentVersion.Major,
                currentVersion.Minor, currentVersion.Patch)), SnapConstants.SnapAppLibraryName, ModuleKind.Dll);

        var assemblyReflector = new CecilAssemblyReflector(assembly);

        var snapAppReleaseDetailsAttributeMethodDefinition = assemblyReflector.MainModule.ImportReference(
            typeof(SnapAppReleaseDetailsAttribute).GetConstructor(Type.EmptyTypes));

        assemblyReflector.AddCustomAttribute(new CustomAttribute(snapAppReleaseDetailsAttributeMethodDefinition));
        assemblyReflector.AddResource(new EmbeddedResource(SnapConstants.SnapAppLibraryName, ManifestResourceAttributes.Public, Encoding.UTF8.GetBytes(snapAppYamlStr)));

        return assembly;
    }

    public string ToSnapAppYamlString([NotNull] SnapApp snapApp)
    {
        if (snapApp == null) throw new ArgumentNullException(nameof(snapApp));
        return YamlSerializerSnapApp.Serialize(snapApp);
    }

    public string ToSnapAppsYamlString([NotNull] SnapApps snapApps)
    {
        if (snapApps == null) throw new ArgumentNullException(nameof(snapApps));
        return YamlSerializerSnapApps.Serialize(snapApps);
    }

    public byte[] ToSnapAppsReleases([NotNull] SnapAppsReleases snapAppsApps)
    {
        if (snapAppsApps == null) throw new ArgumentNullException(nameof(snapAppsApps));
        return MessagePackSerializer.Serialize(snapAppsApps, MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray));
    }
}
