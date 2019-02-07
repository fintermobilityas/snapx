using System.Diagnostics.CodeAnalysis;
using CommandLine;

namespace Snap.Tool.Options
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [Verb("releasify", HelpText = "Create a new release for a given app")]
    internal class ReleasifyOptions : BaseSubOptions
    {
        [Option('a', "app", HelpText = "Application name", Required = true)]
        public string App { get; set; }
        [Option('r', "runtime-identifier", HelpText = "Runtime identifier target name, e.g win7-x64", Required = true)]
        public string Rid { get; set; }
        [Option('d', "publish-directory", HelpText = "Location on disk where current app has been published", Required = true)]
        public string PublishDirectory { get; set; }
        [Option('v', "version", HelpText = "New application version (Required only if we you don't have a bump strategy configured)")]
        public string Version { get; set; }
    }
}
