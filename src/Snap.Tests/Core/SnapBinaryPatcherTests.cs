using System.IO;
using Snap.Core;
using Xunit;

namespace Snap.Tests.Core
{
    public class SnapBinaryPatcherTests
    {
        [Fact]
        public void HandleBsDiffWithoutExtraData()
        {
            var baseFileData = new byte[] { 1, 1, 1, 1 };
            var newFileData = new byte[] { 2, 1, 1, 1 };

            byte[] patchData;

            using (var patchOut = new MemoryStream())
            {
                SnapBinaryPatcher.Create(baseFileData, newFileData, patchOut);
                patchData = patchOut.ToArray();
            }

            using (var toPatch = new MemoryStream(baseFileData))
            using (var patched = new MemoryStream())
            {
                SnapBinaryPatcher.Apply(toPatch, () => new MemoryStream(patchData), patched);

                Assert.Equal(newFileData, patched.ToArray());
            }
        }
    }
}
