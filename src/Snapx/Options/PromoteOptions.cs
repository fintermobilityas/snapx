using System.Collections.Generic;
using CommandLine;
using CommandLine.Text;
using JetBrains.Annotations;

namespace snapx.Options
{
    [Verb("promote", HelpText = "Promote a snap to next release channel. E.g.: test -> staging -> production")]
    [UsedImplicitly]
    internal class PromoteOptions : BaseSubOptions
    {
        const int DefaultLockRetries = 3;

        [Option('r', "rid",
            HelpText = "The runtime identifier (RID), e.g win-x64", 
            Required = true)]
        public string Rid { get; [UsedImplicitly] set; }

        [Option('c', "channel",
            HelpText = "The base channel to promote from.", 
            Required = true)]
        public string Channel { get; [UsedImplicitly] set; }

        [Option("all",
            HelpText = "Promote to all remaining channels.")]
        public bool ToAllRemainingChannels { get; [UsedImplicitly] set; }

        [Option("lock-retries",
            Default = DefaultLockRetries,
            HelpText = "The number of retries if a mutex fails to be acquired. Set -1 if you want to retry forever.")]
        public int LockRetries { get; set; } = DefaultLockRetries;

        [Option("lock-token",
            HelpText = "Override lock token.")]
        public string LockToken { get; set; }

        [Option('y', "yes",
            HelpText = "Yes (y) to all prompts")]
        public bool YesToAllPrompts { get; [UsedImplicitly] set; }

        [Option("skip-installers",
            HelpText = "Skip building installers.")]
        public bool SkipInstallers { get; set; }

        [Value(0, HelpText = "Application id", Required = true)]
        public string Id { get; [UsedImplicitly] set; }

        [Usage(ApplicationAlias = "snapx")]
        [UsedImplicitly]
        public static IEnumerable<Example> Examples
        {
            get
            {
                yield return new Example("Promote current win-x64 release from test to staging", new PromoteOptions
                {
                    Id = "demoapp",
                    Channel = "test",
                    Rid = "win-x64"
                });
                yield return new Example("Promote current win-x64 release from staging to production", new PromoteOptions
                {
                    Id = "demoapp",
                    Channel = "staging",
                    Rid = "win-x64"
                });
                yield return new Example("Promote current win-x64 release from test to staging, production", new PromoteOptions
                {
                    Id = "demoapp",
                    Channel = "test",
                    Rid = "win-x64",
                    ToAllRemainingChannels = true
                });
            }
        }
    }
}
