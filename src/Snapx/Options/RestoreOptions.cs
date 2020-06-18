using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommandLine;
using CommandLine.Text;
using JetBrains.Annotations;
using Snap.Core;

namespace snapx.Options
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [Verb("restore", HelpText = "Restore missing or corrupt packages")]
    [UsedImplicitly]
    internal class RestoreOptions : BaseSubOptions
    {
        [Option('r', "rid",
            HelpText = "The runtime identifier (RID), e.g win-x64. If left unspecified all runtime identifiers will be restored.")]
        public string Rid { get; [UsedImplicitly] set; }

        [Option('i', "build-installers", 
            HelpText = "Build installers.")]
        public bool BuildInstallers { get; set; }

        [Option("rc|restore-concurrency", 
            HelpText = "The number of concurrent restores.", 
            Default = 4)]
        public int RestoreConcurrency { get; set; }

        [Option("dc|download-concurrency", 
            HelpText = "The number of concurrent downloads for missing packages.",
            Default = 4)]
        public int DownloadConcurrency { get; set; }
        
        [Value(0, 
            HelpText = "The application id to restore. Leave this value empty if you want to restore all applications.")]
        public string Id { get; [UsedImplicitly] set; }

        public SnapPackageManagerRestoreType RestoreStrategyType { get; set; } = SnapPackageManagerRestoreType.Default;

        [Usage(ApplicationAlias = "snapx")]
        public static IEnumerable<Example> Examples
        {
            get
            {
                yield return new Example("Restore packages for all applications", new RestoreOptions());
                yield return new Example("Restore packages for all applications and build installers", new RestoreOptions
                {
                    BuildInstallers = true
                });
                yield return new Example("Restore packages for demoapp application", new RestoreOptions
                {
                    Id = "demoapp"
                });
                yield return new Example("Restore packages for demoapp win-x64 application", new RestoreOptions
                {
                    Id = "demoapp",
                    Rid = "win-x64"
                });
                yield return new Example("Restore packages for demoapp win-x64 application and build installers", new RestoreOptions
                {
                    Id = "demoapp",
                    Rid = "win-x64",
                    BuildInstallers = true
                });
            }
        }
    }
}
