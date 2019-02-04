using System.Diagnostics.CodeAnalysis;
using CommandLine;
using NuGet.Versioning;

namespace Snap.Tool.Options
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [Verb("releasify", HelpText = "Create a new release for a given app.")]
    internal class ReleasifyOptions : BaseSubOptions
    {
        [Option('a', "app", HelpText = "Application name.", Required = true)]
        public string App { get; set; }
        [Option('d', "publish-directory", HelpText = "Location on disk where current app has been published.", Required = true)]
        public string PublishDirectory { get; set; }
        [Option('v', "version", HelpText = "New application version.", Required = true)]
        public SemanticVersion Version { get; set; }
    }
}
