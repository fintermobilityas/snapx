using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Snap.Core;
using Snap.NuGet;
using Snap.Shared.Tests;
using Xunit;

namespace Snap.Tests.NuGet
{
    public class NugetServiceTests : IClassFixture<BaseFixture>
    {
        readonly BaseFixture _baseFixture;
        readonly ISnapFilesystem _snapFilesystem;

        public NugetServiceTests([NotNull] BaseFixture baseFixture)
        {
            _baseFixture = baseFixture ?? throw new ArgumentNullException(nameof(baseFixture));
            _snapFilesystem = new SnapFilesystem();
        }
        
        [Fact]
        public async Task TestNuGetMachineWideSettings()
        {
            await WriteNugetConfigToWorkingDirectoryAsync();
            
            var packageSources = new NuGetMachineWideSettings(_snapFilesystem, _baseFixture.WorkingDirectory);
            
            var configRoots = packageSources.Settings.GetConfigRoots();
            var configFilePaths = packageSources.Settings.GetConfigFilePaths();
            Assert.NotEmpty(configRoots);
            Assert.NotEmpty(configFilePaths);
        }
        
        [Fact]
        public async Task TestNuGetMachineWidePackageSources()
        {
            await WriteNugetConfigToWorkingDirectoryAsync();

            var feeds = new NuGetMachineWidePackageSources(_snapFilesystem, _baseFixture.WorkingDirectory);
            Assert.NotNull(feeds.Items.SingleOrDefault(x => x.Name.StartsWith("nuget.org")));
        }

        async Task WriteNugetConfigToWorkingDirectoryAsync()
        {
            const string nugetConfigXml =
                @"<?xml version=""1.0"" encoding=""utf-8""?><configuration><packageSources><add key=""nuget.org"" value=""https://api.nuget.org/v3/index.json"" /></packageSources><activePackageSource><add key=""All"" value=""(Aggregate source)"" /></activePackageSource> </configuration>";

            var dstFilename = _snapFilesystem.PathCombine(_baseFixture.WorkingDirectory, "nuget.config");
            await _snapFilesystem.FileWriteUtf8StringAsync(nugetConfigXml, dstFilename, CancellationToken.None);
        }
    }
}
