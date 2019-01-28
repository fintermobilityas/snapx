using System.Diagnostics;
using System.Linq;
using Snap.NuGet;
using Xunit;

#if NET45
using Snap.Extensions;
#endif

namespace Snap.Tests.NuGet
{
    public class NugetServiceTests
    {
        [Fact]
        public void TestNuGetMachineWideSettings()
        {
            var packageSources = new NuGetMachineWideSettings();
            var configRoots = packageSources.Settings.GetConfigRoots();
            var configFilePaths = packageSources.Settings.GetConfigFilePaths();
            Assert.NotEmpty(configRoots);
            Assert.NotEmpty(configFilePaths);
        }
        
        [Fact]
        public void TestNuGetMachineWidePackageSources()
        {
            var feeds = new NuGetMachineWidePackageSources();
            Assert.NotNull(feeds.Items.SingleOrDefault(x => x.Name.StartsWith("nuget.org")));
        }
    }
}
