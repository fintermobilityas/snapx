using CommandLine;
using JetBrains.Annotations;

namespace snapx.Options
{
    [Verb("lock", HelpText = "Lock management")]
    [UsedImplicitly]
    public class LockOptions
    {
        [Option('a', "app", HelpText = "Application id", Required = true)]
        public string Id { get; [UsedImplicitly] set; }
        [Option("release", HelpText = "Release of lock for application")]
        public bool Release { get; [UsedImplicitly] set; }
    }
}
