using System.Collections.Generic;
using System.Linq;
using Snap.AnyOS;
using Snap.AnyOS.Unix;
using Snap.Core;
using Snap.Core.IO;
using Snap.Reflection;
using Snap.Shared.Tests;
using Snap.Shared.Tests.Extensions;
using Xunit;

namespace Snap.Tests.AnyOS
{
    public class SnapOsUnixTests : IClassFixture<BaseFixture>
    {
        readonly BaseFixture _baseFixture;
        readonly ISnapFilesystem _snapFilesystem;
        readonly ISnapOs _snapOs;

        public SnapOsUnixTests(BaseFixture baseFixture)
        {
            _baseFixture = baseFixture;
            _snapFilesystem = new SnapFilesystem();
            _snapOs = new SnapOs(new SnapOsUnix(_snapFilesystem));
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
    }
}
