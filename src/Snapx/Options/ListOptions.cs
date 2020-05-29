using CommandLine;
using JetBrains.Annotations;

namespace snapx.Options
{
    [Verb("list", true, HelpText = "Show published applications release graph summary")]
    [UsedImplicitly]
    internal class ListOptions : BaseSubOptions
    {
        [Option('a', "app", HelpText = "Application id")]
        public string Id { get; [UsedImplicitly] set; }
    }
}
