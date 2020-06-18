using System.Diagnostics.CodeAnalysis;
using CommandLine;
using JetBrains.Annotations;

namespace snapx.Options
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [Verb("demote", HelpText = "Demote one or multiple releases")]
    [UsedImplicitly]
    internal class DemoteOptions : BaseSubOptions
    {
        [Option('a',"app", HelpText = "Application id", Required = true)]
        public string Id { get; [UsedImplicitly] set; }
        [Option('r', "rid", HelpText = "Runtime identifier (RID), e.g win-x64.")]
        public string Rid { get; [UsedImplicitly] set; }
        [Option("from-version", HelpText = "Remove all releases newer than this version.")]
        public string FromVersion { get; set; }
        [Option("all", HelpText = "Remove all matching release")]
        public bool All { get; set; }
        [Option("lock-retries", HelpText = "The number of retries if a mutex fails to be acquired (default: 3). Specify -1 if you want to retry forever.")]
        public int LockRetries { get; set; } = 3;
        [Option("lock-token", HelpText = "Override default lock token")]
        public string LockToken { get; set; }
    }
}
