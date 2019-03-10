using CommandLine;
using JetBrains.Annotations;

namespace snapx.Options
{
    [Verb("list", HelpText = "Show published applications release graph summary")]
    [UsedImplicitly]
    internal class ListOptions : BaseSubOptions
    {
        [Option("id", HelpText = "Application id")]
        public string Id { get; [UsedImplicitly] set; }
    }
}
