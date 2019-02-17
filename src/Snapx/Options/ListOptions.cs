using CommandLine;
using JetBrains.Annotations;

namespace snapx.Options
{
    [Verb("list", HelpText = "Display release summary for one or multiple applications")]
    [UsedImplicitly]
    internal class ListOptions : BaseSubOptions
    {
        [Option("id", HelpText = "Application id")]
        public string Id { get; [UsedImplicitly] set; }
    }
}
