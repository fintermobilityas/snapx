using Snap.AnyOS;
using Snap.AnyOS.Windows;
using Snap.Core;
using Snap.Shared.Tests;
using Xunit;

#if PLATFORM_WINDOWS
using System.Collections.Generic;
using System.Linq;
using Snap.Core.IO;
using Snap.Reflection;
using Snap.Shared.Tests.Extensions;
#endif

namespace Snap.Tests.AnyOS.Windows
{
    public class SnapOsWindowsTests : IClassFixture<BaseFixture>
    {
        readonly BaseFixture _baseFixture;
        readonly ISnapFilesystem _snapFilesystem;
        readonly ISnapOs _snapOs;

        public SnapOsWindowsTests(BaseFixture baseFixture)
        {
            _baseFixture = baseFixture;
            _snapFilesystem = new SnapFilesystem();
            _snapOs = new SnapOs(new SnapOsWindows(_snapFilesystem, new SnapOsProcessManager(), new SnapOsSpecialFoldersWindows()));
        }

        [Fact]
        public void TestDistroType()
        {
            Assert.Equal(SnapOsDistroType.Windows, _snapOs.DistroType);
        }

#if PLATFORM_WINDOWS
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
#endif
        
    }
}
