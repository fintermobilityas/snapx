using System.Collections.Generic;
using CommandLine;
using CommandLine.Text;
using JetBrains.Annotations;

namespace snapx.Options
{
    [Verb("demote", HelpText = "Demote one or multiple releases")]
    [UsedImplicitly]
    internal class DemoteOptions : BaseSubOptions
    {
        const int DefaultLockRetries = 3;

        [Option('r', "rid", 
            HelpText = "The Runtime identifier (RID), e.g win-x64.")]
        public string Rid { get; [UsedImplicitly] set; }

        [Option("from-version", 
            HelpText = "Remove all releases newer than this version."
        )]
        public string FromVersion { get; set; }

        [Option("remove-all",
            HelpText = "Remove all matching releases.")]
        public bool RemoveAll { get; set; }

        [Option("lock-retries",
            Default = DefaultLockRetries,
            HelpText = "The number of retries if a mutex fails to be acquired. Set -1 if you want to retry forever.")]
        public int LockRetries { get; set; } = DefaultLockRetries;

        [Option("lock-token", 
            HelpText = "Override lock token.")]
        public string LockToken { get; set; }

        [Value(0,
            HelpText = "The Application id.",
            Required = true)]
        public string Id { get; [UsedImplicitly] set; }

        [Usage(ApplicationAlias = "snapx")]
        [UsedImplicitly]
        public static IEnumerable<Example> Examples
        {
            get
            {
                yield return new Example("Remove current release from all runtime identifiers", new DemoteOptions
                {
                    Id = "demoapp"
                });
                yield return new Example("Remove current release from win-x64", new DemoteOptions
                {
                    Id = "demoapp",
                    Rid = "win-x64"
                });
                yield return new Example("Remove all releases from win-x64", new DemoteOptions
                {
                    Id = "demoapp",
                    Rid = "win-x64"
                });
                yield return new Example("Remove all releases from all runtime identifiers", new DemoteOptions
                {
                    Id = "demoapp",
                    RemoveAll = true
                });
                yield return new Example("Remove all releases from all runtime identifiers greater than --from-version", new DemoteOptions
                {
                    Id = "demoapp",
                    FromVersion = "1.0.0",
                    RemoveAll = true
                });
            }
        }
    }
}
