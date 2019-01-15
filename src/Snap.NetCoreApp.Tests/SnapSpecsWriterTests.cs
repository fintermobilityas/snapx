using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mono.Cecil;
using Snap.AnyOS.Windows;
using Snap.Core;
using Snap.Core.IO;
using Snap.Extensions;
using Snap.Shared.Tests;
using Xunit;

namespace Snap.Tests
{
    public class SnapSpecsWriterTests : IClassFixture<BaseFixture>
    {
        readonly BaseFixture _baseFixture;
        readonly ISnapSpecsWriter _snapSpecsWriter;
        readonly ISnapSpecsReader _snapSpecsReader;
        readonly SnapFilesystem _snapFilesystem;

        public SnapSpecsWriterTests(BaseFixture baseFixture)
        {
            _baseFixture = baseFixture ?? throw new ArgumentNullException(nameof(baseFixture));
            _snapSpecsWriter = new SnapSpecsWriter();
            _snapSpecsReader = new SnapSpecsReader();
            _snapFilesystem = new SnapFilesystem();
        }
               
        #if NETCOREAPP
        [Fact]
        public async Task TestBuildNetCoreUpdateExe()
        {
            var workingDirectory = _baseFixture.WorkingDirectory;

            var updateExeAbsoluteFilename = await _snapSpecsWriter.ConvertUpdateExeToConsoleApplicationUnitTest(workingDirectory, _snapFilesystem, CancellationToken.None);
            using (new DisposableFiles(_snapFilesystem, updateExeAbsoluteFilename))
            {
                Assert.NotNull(updateExeAbsoluteFilename);
                Assert.True(_snapFilesystem.FileExists(updateExeAbsoluteFilename));
                Assert.Equal(SnapSpecsWriter.SnapUpdateExeFilename, Path.GetFileName(updateExeAbsoluteFilename));

                using (var updateExeMemoryStreamPe = await _snapFilesystem.FileReadAsync(updateExeAbsoluteFilename, CancellationToken.None))
                {
                    var (subSystemType, _, _) = updateExeMemoryStreamPe.GetPeDetails();
                    Assert.Equal(PeUtility.SubSystemType.IMAGE_SUBSYSTEM_WINDOWS_GUI, subSystemType);
                }

                var (updateExeMemoryStream, updateExePdbMemoryStream) = await _snapSpecsWriter.MergeSnapCoreIntoUpdateExeAsyncUnitTest(workingDirectory, _snapFilesystem, CancellationToken.None);
                using (updateExeMemoryStream)
                using (updateExePdbMemoryStream)
                {
                    Assert.NotNull(updateExeMemoryStream);
                    Assert.Equal(0, updateExeMemoryStream.Position);
                    Assert.True(updateExeMemoryStream.CanRead);

                    Assert.NotNull(updateExePdbMemoryStream);
                    Assert.Equal(0, updateExePdbMemoryStream.Position);
                    Assert.True(updateExePdbMemoryStream.CanRead);

                    using (var updateExeAssemblyDefinition = AssemblyDefinition.ReadAssembly(updateExeMemoryStream))
                    {
                        var references = updateExeAssemblyDefinition.MainModule.AssemblyReferences.ToList();
                        Assert.Single(references);
                        Assert.Equal("mscorlib.dll", references[0].FullName);
                    }
                }
            }                                  
        }
        #endif

    }
}
