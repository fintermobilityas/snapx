using System.Diagnostics.CodeAnalysis;
using CommandLine;
using JetBrains.Annotations;

namespace snapx.Options
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [Verb("promote", HelpText = "Promote a snap to next release channel. E.g.: test -> staging -> production")]
    [UsedImplicitly]
    internal class PromoteOptions : BaseSubOptions
    {
        [Option('a', "app", HelpText = "Application id", Required = true)]
        public string Id { get; [UsedImplicitly] set; }
        [Option('r', "rid", HelpText = "Runtime identifier (RID), e.g win-x64", Required = true)]
        public string Rid { get; [UsedImplicitly] set; }
        [Option('c', "channel", HelpText = "Channel name", Required = true)]
        public string Channel { get; [UsedImplicitly] set; }
        [Option("all", HelpText = "Promote to remaining channels")]
        public bool ToAllRemainingChannels { get; [UsedImplicitly] set; }
        [Option("lock-retries", HelpText = "The number of retries if a mutex fails to be acquired (default: 3). Specify -1 if you want to retry forever.")]
        public int LockRetries { get; set; } = 3;
        [Option("lock-token", HelpText = "Override default lock token")]
        public string LockToken { get; set; }
    }
}
