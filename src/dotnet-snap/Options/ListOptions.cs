using CommandLine;

namespace Snap.Options
{
    [Verb("list", HelpText = "Show installed apps, feeds etc.")]
    class ListOptions : BaseSubOptions
    {
        [Option('d', "Directory", HelpText = "Path to snap folder (Can be relative or absolute)", Required = false)]
        public string Directory { get; set; }
        [Option('a', "apps", HelpText = "List available apps", Required = false)]
        public bool Apps { get; set; }
        [Option('f', "feeds", HelpText = "List available feeds", Required = false)]
        public bool Feeds { get; set; }
    }
}
