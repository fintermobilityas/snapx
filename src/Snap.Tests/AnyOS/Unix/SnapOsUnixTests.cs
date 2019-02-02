#if PLATFORM_UNIX
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Snap.AnyOS;
using Snap.AnyOS.Unix;
using Snap.Core;
using Snap.Core.IO;
using Snap.Reflection;
using Snap.Shared.Tests;
using Snap.Shared.Tests.Extensions;
using Xunit;

namespace Snap.Tests.AnyOS.Unix
{
    public class SnapOsUnixTests : IClassFixture<BaseFixture>
    {
        readonly BaseFixture _baseFixture;
        readonly ISnapFilesystem _snapFilesystem;
        readonly ISnapOs _snapOs;
        readonly SnapOsUnix _snapOsUnix;

        public SnapOsUnixTests(BaseFixture baseFixture)
        {
            _baseFixture = baseFixture;
            _snapFilesystem = new SnapFilesystem();
            _snapOsUnix = new SnapOsUnix(_snapFilesystem, new SnapOsProcessManager(), new SnapOsSpecialFoldersUnix());
            _snapOs = new SnapOs(_snapOsUnix);
        }

        [Fact]
        public void TestGetAllSnapAwareApps()
        {
            using (var assemblyDefinitionExe = _baseFixture.BuildEmptyExecutable("myexe"))
            using (var assemblyDefinitionDll = _baseFixture.BuildEmptyLibrary("mydll"))
            using (var assemblyDefinitionNotSnapAwareExe = _baseFixture.BuildEmptyExecutable("mynotsnapwareexe"))
            using (var assemblyDefinitionNotSnapAwareDll = _baseFixture.BuildEmptyLibrary("mynotsnapawaredll"))
            using (var tmpDir = new DisposableTempDirectory(_baseFixture.WorkingDirectory, _snapFilesystem))
            {
                var reflectorSnapAwareExe = new CecilAssemblyReflector(assemblyDefinitionExe);
                var reflectorSnapAwareExePath = _snapFilesystem.PathCombine(tmpDir.WorkingDirectory, assemblyDefinitionExe.BuildRelativeFilename());
                reflectorSnapAwareExe.SetSnapAware();


                var reflectorSnapAwareDll = new CecilAssemblyReflector(assemblyDefinitionDll);
                var reflectorSnapAwareDllPath = _snapFilesystem.PathCombine(tmpDir.WorkingDirectory, assemblyDefinitionDll.BuildRelativeFilename());
                reflectorSnapAwareDll.SetSnapAware();

                assemblyDefinitionExe.Write(reflectorSnapAwareExePath);
                assemblyDefinitionDll.Write(reflectorSnapAwareDllPath);
                                
                var notSnapAwareExePath = _snapFilesystem.PathCombine(tmpDir.WorkingDirectory, assemblyDefinitionNotSnapAwareExe.BuildRelativeFilename());
                var notSnapAwareDllPath = _snapFilesystem.PathCombine(tmpDir.WorkingDirectory, assemblyDefinitionNotSnapAwareDll.BuildRelativeFilename());
                assemblyDefinitionNotSnapAwareExe.Write(notSnapAwareExePath);
                assemblyDefinitionNotSnapAwareDll.Write(notSnapAwareDllPath);

                var snapAwareApps = _snapOs.GetAllSnapAwareApps(tmpDir.WorkingDirectory).OrderBy(x => x).ToList();
                Assert.Equal(2, snapAwareApps.Count);

                var expectedSnapAwareApps = new List<string>
                {
                    reflectorSnapAwareExePath,
                    reflectorSnapAwareDllPath
                }.OrderBy(x => x).ToList();

                Assert.Equal(expectedSnapAwareApps, snapAwareApps);
            }
        }

        [Fact]
        public void TestDistroType()
        {
            // If this test case fails then please submit a PR that returns the correct unix distro type :)
            Assert.Equal(SnapOsDistroType.Ubuntu, _snapOs.DistroType);
        }

        [Fact]
        public async Task TestParseLsbRelease()
        {
            var lsbRelease = @"
No LSB modules are available.
Distributor ID:	Ubuntu
Description:	Ubuntu 18.10
Release:	18.10
Codename:	cosmic";

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var (exitCode, maybeLsbRelease) = await _snapOs.OsProcess.RunAsync("lsb_release", "-a", CancellationToken.None);
                if (exitCode == 0 && !string.IsNullOrWhiteSpace(maybeLsbRelease))
                {
                    lsbRelease = maybeLsbRelease;
                }
            } 

            var (distributorId, description, release, codeName) = _snapOsUnix.ParseLsbRelease(lsbRelease);
            Assert.Equal("Ubuntu", distributorId);
            Assert.Equal("Ubuntu 18.10", description);
            Assert.Equal("18.10", release);
            Assert.Equal("cosmic", codeName);
        }
        
#if PLATFORM_UNIX
        [Fact]
        public async Task TestSnapOsUnixNativeMethods_Chmod()
        {
            await _snapFilesystem.FileWriteStringContentAsync("yolo", "test.txt", CancellationToken.None);

            Assert.Equal(0,NativeMethodsUnix.chmod("test.txt", 755));            
        }

        [Fact]
        public async Task TestGetProcessesAsync()
        {
            var processes = await _snapOs.GetProcessesAsync(CancellationToken.None);
            Assert.NotEmpty(processes);
        }
#endif

    }
          
}
#endif
