using System.Diagnostics.CodeAnalysis;
using CommandLine;
using JetBrains.Annotations;

namespace snapx.Options
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [Verb("pack", HelpText = "Publish a new release")]
    [UsedImplicitly]
    internal class PackOptions : BaseSubOptions
    {
        [Option('a', "app", HelpText = "Application id", Required = true)]
        public string AppId { get; [UsedImplicitly] set; }
        [Option('r', "rid", HelpText = "Runtime identifier (RID), e.g win-x64", Required = true)]
        public string Rid { get; [UsedImplicitly] set; }
        [Option('v', "version", HelpText = "New application version", Required = true)]
        public string Version { get; [UsedImplicitly] set; }
        [Option('y', "yes", HelpText = "Yes (y) to all prompts")]
        public bool YesToAllPrompts { get; [UsedImplicitly] set; }
        [Option("gc", HelpText = "Removes all delta releases and creates a new full release")]
        public bool Gc { get; set; }
        [Option("db-version", HelpText = "Manually specify next db version. Has to be greater than current version.")]
        public int DbVersion { get; set; } = -1;
        [Option("lock-retries", HelpText = "The number of retries if a mutex fails to be acquired (default: 3). Specify -1 if you want to retry forever.")]
        public int LockRetries { get; set; } = 3;
        [Option("lock-token", HelpText = "Override default lock token")]
        public string LockToken { get; set; } 
        [Option("skip-installers", HelpText = "Skip building installers.")]
        public bool SkipInstallers { get; set; }
        [Option("release-notes", HelpText = "Overwrite release notes defined in YML manifest.")]
        public string ReleasesNotes { get; set; }
    }
}
