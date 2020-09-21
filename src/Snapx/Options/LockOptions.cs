using System.Collections.Generic;
using CommandLine;
using CommandLine.Text;
using JetBrains.Annotations;

namespace snapx.Options
{
    [Verb("lock", HelpText = "Lock management")]
    [UsedImplicitly]
    public class LockOptions
    {
        [Option("release", HelpText = "Force release lock specified in snapx.yml.", Required = true)]
        public bool Release { get; [UsedImplicitly] set; }

        [Option("token", HelpText = "Input lock token to release.")]
        public string Token { get; [UsedImplicitly] set; }

        [Value(0,
            HelpText = "The application id",
            Required = true)]
        public string Id { get; [UsedImplicitly] set; }

        [Usage(ApplicationAlias = "snapx")]
        [UsedImplicitly]
        public static IEnumerable<Example> Examples
        {
            get
            {
                yield return new Example("Release demoapp lock token", new LockOptions
                {
                    Id = "demoapp",
                    Release = true
                });
                yield return new Example("Release demoapp using custom input lock token", new LockOptions
                {
                    Id = "demoapp",
                    Release = true,
                    Token = "abc123"
                });
            }
        }
    }
}
