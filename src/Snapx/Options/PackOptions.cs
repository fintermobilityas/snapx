using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommandLine;
using CommandLine.Text;
using JetBrains.Annotations;

namespace snapx.Options
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [Verb("pack", HelpText = "Publish a new release")]
    [UsedImplicitly]
    internal class PackOptions : BaseSubOptions
    {
        const int DefaultDbVersion = -1;
        const int DefaultLockRetries = 3;

        [Option('r', "rid", 
            HelpText = "Runtime identifier (RID), e.g win-x64",
            Required = true)]
        public string Rid { get; [UsedImplicitly] set; }

        [Option('v', "version",
            HelpText = "The application version.", Required = true)]
        public string Version { get; [UsedImplicitly] set; }

        [Option('y', "yes",
            HelpText = "Yes (y) to all prompts")]
        public bool YesToAllPrompts { get; [UsedImplicitly] set; }

        [Option("gc",
            HelpText = "Removes all delta releases and creates a new full release")]
        public bool Gc { get; set; }

        [Option("db-version",
            HelpText = "Manually specify next db version. Has to be greater than current version.",
            Default = DefaultDbVersion)]
        public int DbVersion { get; set; } = DefaultDbVersion;

        [Option("lock-retries",
            HelpText =
                "The number of retries if a mutex fails to be acquired (default: 3). Specify -1 if you want to retry forever.",
            Default = DefaultLockRetries)]
        public int LockRetries { get; set; } = DefaultLockRetries;

        [Option("lock-token", 
            HelpText = "Override default lock token")]
        public string LockToken { get; set; } 

        [Option("skip-installers", 
            HelpText = "Skip building installers.")]
        public bool SkipInstallers { get; set; }

        [Option("release-notes", 
            HelpText = "Overwrite release notes defined in YML manifest.")]
        public string ReleasesNotes { get; set; }

        [Value(0,
            HelpText = "The application id.", 
            Required = true)]
        public string Id { get; [UsedImplicitly] set; }

        [Usage(ApplicationAlias = "snapx")]
        public static IEnumerable<Example> Examples
        {
            get
            {
                yield return new Example("Publish a new release for win-x64", new PackOptions
                {
                    Id = "demoapp",
                    Rid = "win-x64",
                    Version = "1.0.0-prerelease",
                    ReleasesNotes = "My first release :)",
                });
                yield return new Example("Publish a new release for win-x64 and remove all previous releases", new PackOptions
                {
                    Id = "demoapp",
                    Rid = "win-x64",
                    Version = "1.0.0-prerelease",
                    Gc = true
                });
                yield return new Example("Publish a new release for win-x64 (non-interactive front-end, e.g Github Actions)", new PackOptions
                {
                    Id = "demoapp",
                    Rid = "win-x64",
                    Version = "1.0.0-prerelease",
                    ReleasesNotes = "My first release :)",
                    YesToAllPrompts = true
                });
            }
        }
    }
}
