using System.IO;
using System.Threading.Tasks;
using Snap.Core;
using Xunit;

namespace Snap.Tests.Core
{
    public class SnapBinaryPatcherTests
    {
        [Fact]
        public async Task HandleBsDiffWithoutExtraData()
        {
            var baseFileData = new byte[] { 1, 1, 1, 1 };
            var newFileData = new byte[] { 2, 1, 1, 1 };

            byte[] patchData;

            await using (var patchOut = new MemoryStream())
            {
                await SnapBinaryPatcher.CreateAsync(baseFileData, newFileData, patchOut, default);
                patchData = patchOut.ToArray();
            }

            await using var toPatch = new MemoryStream(baseFileData);
            await using var patched = new MemoryStream();
            await SnapBinaryPatcher.ApplyAsync(toPatch, async () => await Task.FromResult(new MemoryStream(patchData)), patched, default);

            Assert.Equal(newFileData, patched.ToArray());
        }
    }
}
