using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using NuGet.Versioning;
using Snap.Extensions;
using Snap.NuGet;
using YamlDotNet.Serialization;

namespace Snap.Core.Specs
{
    public sealed class SnapAppValidationException : Exception
    {
        public SnapApp App { get; }
        public string ParamName { get; }

        public SnapAppValidationException([NotNull] SnapApp app, [NotNull] string paramName, [NotNull] string message) : base(message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            App = app ?? throw new ArgumentNullException(nameof(app));
            ParamName = paramName ?? throw new ArgumentNullException(nameof(paramName));
        }
    }

    public sealed class SnapApp
    {
        public string Id { get; set; }
        public SemanticVersion Version { get; set; }
        public SnapSignature Signature { get; set; }
        public SnapChannel Channel { get; set; }
        public List<SnapChannel> Channels { get; set; }
        public SnapTarget Target { get; set; }
        public List<SnapFeed> Feeds { get; set; }

        [UsedImplicitly]
        public SnapApp()
        {
            Channels = new List<SnapChannel>();
            Feeds = new List<SnapFeed>();
        }

        /// <summary>
        /// Copy constructor. Integrity of app properties are not validated.
        /// </summary>
        /// <param name="app"></param>
        public SnapApp([NotNull] SnapApp app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));
            Id = app.Id;
            Version = app.Version;
            if (app.Signature != null)
            {
                Signature = new SnapSignature(app.Signature);
            }
            if (app.Channel != null)
            {
                Channel = new SnapChannel(app.Channel);
            }
            if (app.Target != null)
            {
                Target = new SnapTarget(app.Target);
            }
            Channels = app.Channels?.Select(x => new SnapChannel(x)).ToList();
            Feeds = app.Feeds?.Select(x => new SnapFeed(x)).ToList();
        }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Id))
            {
                throw new SnapAppValidationException(this, nameof(Id), "Cannot be null or whitespace.");
            }

            if (Version == null)
            {
                throw new SnapAppValidationException(this, nameof(Version), "Cannot be null.");
            }

            if (Version.IsPrerelease)
            {
                throw new SnapAppValidationException(this, nameof(Version), "Prereleases are not allowed. Use multiple channels instead, e.g. CI, test, staging, production.");
            }

            if (!string.IsNullOrEmpty(Version.Release))
            {
                throw new SnapAppValidationException(this, nameof(Version), "Release labels are not allowed. Use multiple channels instead, e.g. CI, test, staging, production.");
            }

            Signature?.Validate(this);

            if (Channel == null)
            {
                throw new SnapAppValidationException(this, nameof(Channel), "Cannot be null.");
            }

            Channel.Validate(this);

            if (Channels == null)
            {
                throw new SnapAppValidationException(this, nameof(Channels), "Cannot be null.");
            }

            Channels.ForEach(x => x.Validate(this));

            if (Target == null)
            {
                throw new SnapAppValidationException(this, nameof(Target), "Cannot be null.");
            }

            Target.Validate(this);

            if (Feeds == null)
            {
                throw new SnapAppValidationException(this, nameof(Feeds), "Cannot be null.");
            }

            Feeds.ForEach(x => x.Validate(this));
        }
    }

    public sealed class SnapTarget
    {
        public OSPlatform OsPlatform { get; set; }
        public SnapTargetFramework Framework { get; set; }

        [UsedImplicitly]
        public SnapTarget()
        {

        }

        public SnapTarget([NotNull] SnapTarget target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            OsPlatform = target.OsPlatform;
            Framework = new SnapTargetFramework(target.Framework);
        }

        public void Validate(SnapApp app)
        {
            if (!OsPlatform.IsSupportedOsVersion())
            {
                throw new SnapAppValidationException(app, nameof(OsPlatform), $"Unsupported os platform: {OsPlatform}.");
            }

            if (Framework == null)
            {
                throw new SnapAppValidationException(app, nameof(Framework), "Cannot be null.");
            }
            
            Framework.Validate(app);
        }
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public sealed class SnapFeed
    {
        public string Name { get; set; }
        public Uri SourceUri { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public NuGetProtocolVersion ProtocolVersion { get; set; }
        public string ApiKey { get; set; }

        [UsedImplicitly]
        public SnapFeed()
        {

        }

        public SnapFeed([NotNull] SnapFeed snapFeed)
        {
            if (snapFeed == null) throw new ArgumentNullException(nameof(snapFeed));
            Name = snapFeed.Name;
            SourceUri = snapFeed.SourceUri;
            Username = snapFeed.Username;
            Password = snapFeed.Password;
            ProtocolVersion = snapFeed.ProtocolVersion;
            ApiKey = snapFeed.ApiKey;
        }

        public void Validate(SnapApp app)
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                throw new SnapAppValidationException(app, nameof(Name), "Please provide a valid feed name.");
            }

            if (SourceUri == null)
            {
                throw new SnapAppValidationException(app, nameof(SourceUri), "Please provide a valid feed source.");
            }

            if (ProtocolVersion == NuGetProtocolVersion.NotSupported)
            {
                var validProtocolVersions = string.Join(",", Enum.GetNames(typeof(NuGetProtocolVersion)));
                throw new SnapAppValidationException(app, nameof(ProtocolVersion), $"Please provide a valid feed protocol version. Valid values are: {validProtocolVersions}.");
            }
        }
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public sealed class SnapChannel
    {
        static readonly Regex NameRegex = new Regex("^[A-Z-0-9]{3,15}", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public string Name { get; set; }
        public string Feed { get; set; }
        public string Update { get; set; }
        public string Publish { get; set; }

        [UsedImplicitly]
        public SnapChannel()
        {

        }

        public SnapChannel([NotNull] SnapChannel channel)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            Name = channel.Name;
            Feed = channel.Feed;
            Update = channel.Update;
            Publish = channel.Publish;
        }

        public void Validate(SnapApp app)
        {
            if (Name == null || !NameRegex.IsMatch(Name))
            {
                throw new SnapAppValidationException(app, nameof(Name), $"Please provide a valid channel name. Regex: {NameRegex}");
            }

            if (string.IsNullOrWhiteSpace(Feed))
            {
                throw new SnapAppValidationException(app, nameof(Feed), "Please provide a valid feed name.");
            }
        }
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public sealed class SnapTargetFramework
    {
        [YamlMember(Alias = "framework")]
        public string Name { get; set; }
        [YamlMember(Alias = "rid")]
        public string RuntimeIdentifier { get; set; }
        public string Nuspec { get; set; }
        public string Alias { get; set; }

        [UsedImplicitly]
        public SnapTargetFramework()
        {

        }

        public SnapTargetFramework([NotNull] SnapTargetFramework framework)
        {
            if (framework == null) throw new ArgumentNullException(nameof(framework));
            Name = framework.Name;
            RuntimeIdentifier = framework.RuntimeIdentifier;
            Nuspec = framework.Nuspec;
            Alias = framework.Alias;
        }

        public void Validate(SnapApp app)
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                throw new SnapAppValidationException(app, nameof(Name), "Please provide a target framework, e.g: netcoreapp2.2.");
            }

            if (string.IsNullOrWhiteSpace(RuntimeIdentifier))
            {
                throw new SnapAppValidationException(app, nameof(RuntimeIdentifier), "Please provide a runtime identifier, e.g. win7-x64.");
            }
        }
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public sealed class SnapSignature
    {
        [YamlMember(Alias = "name")]
        public string CertificateSubjectName { get; set; }
        public string Sha1 { get; set; }
        public string Sha256 { get; set; }

        [UsedImplicitly]
        public SnapSignature()
        {

        }

        public SnapSignature([NotNull] SnapSignature signature)
        {
            if (signature == null) throw new ArgumentNullException(nameof(signature));
            CertificateSubjectName = signature.CertificateSubjectName;
            Sha1 = signature.Sha1;
            Sha256 = signature.Sha256;
        }

        public void Validate(SnapApp app)
        {
            if (CertificateSubjectName == null)
            {
                throw new SnapAppValidationException(app, nameof(CertificateSubjectName), "Please provide a valid certificate subject name.");
            }

            if (string.IsNullOrWhiteSpace(Sha1) && string.IsNullOrWhiteSpace(Sha256))
            {
                throw new SnapAppValidationException(app, $"{nameof(Sha1)}/{nameof(Sha256)}", "Did you forgot to specify the SHA signature type? Please provide either a SHA1 or SHA256 signature.");
            }

            if (!string.IsNullOrWhiteSpace(Sha1) && !string.IsNullOrWhiteSpace(Sha256))
            {
                throw new SnapAppValidationException(app, $"{nameof(Sha1)}/{nameof(Sha256)}", 
                    "Please provide either a SHA1 or SHA256 signature, you cannot provide both. " +
                            "A SHA256 signature is recommended in favor of SHA1.");
            }
        }
    }
}
