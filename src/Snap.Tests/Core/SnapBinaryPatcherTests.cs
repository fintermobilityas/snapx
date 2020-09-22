using System;
using System.IO;
using System.Threading.Tasks;
using Snap.Core;
using Xunit;

namespace Snap.Tests.Core
{
    public class SnapBinaryPatcherTests
    {
        static readonly Random Random = new Random();
        readonly ISnapBinaryPatcher _snapBinaryPatcher;

        public SnapBinaryPatcherTests()
        {
            _snapBinaryPatcher = new SnapBinaryPatcher();
        }

        [Fact]
        public async Task TestBsDiff()
        {
            var baseFileData = new byte[] { 0, 2, 3, 5 };
            var newFileData = new byte[] { 0, 1, 2, 3, 10 };

            await using var patchOut = new MemoryStream();
            await _snapBinaryPatcher.CreateAsync(baseFileData, newFileData, patchOut, default);
            var patchData = patchOut.ToArray();

            await using var toPatch = new MemoryStream(baseFileData);
            await using var patched = new MemoryStream();
            await _snapBinaryPatcher.ApplyAsync(toPatch, async () => await Task.FromResult(new MemoryStream(patchData)), patched, default);

            Assert.Equal(newFileData, patched.ToArray());
        }

        [Fact]
        public async Task TestBsDiffRandomNoise()
        {
            var baseFileData = new byte[1024 * 1024];
            Random.NextBytes(baseFileData);
            var newFileData = new byte[1024 * 1024];
            baseFileData.CopyTo(newFileData, 0);

            for (var i = 0; i < newFileData.Length; i++)
            {
                if (Random.NextDouble() > 0.5)
                {
                    newFileData[i] = (byte)Random.Next();
                }
            }

            await using var patchOut = new MemoryStream();
            await _snapBinaryPatcher.CreateAsync(baseFileData, newFileData, patchOut, default);
            var patchData = patchOut.ToArray();

            await using var toPatch = new MemoryStream(baseFileData);
            await using var patched = new MemoryStream();
            await _snapBinaryPatcher.ApplyAsync(toPatch, async () => await Task.FromResult(new MemoryStream(patchData)), patched, default);

            Assert.Equal(newFileData, patched.ToArray());
        }

        [Fact]
        public async Task TestBsDiffWithoutExtraData()
        {
            var baseFileData = new byte[] { 1, 1, 1, 1 };
            var newFileData = new byte[] { 2, 1, 1, 1 };

            await using var patchOut = new MemoryStream();
            await _snapBinaryPatcher.CreateAsync(baseFileData, newFileData, patchOut, default);
            var patchData = patchOut.ToArray();

            await using var toPatch = new MemoryStream(baseFileData);
            await using var patched = new MemoryStream();
            await _snapBinaryPatcher.ApplyAsync(toPatch, async () => await Task.FromResult(new MemoryStream(patchData)), patched, default);

            Assert.Equal(newFileData, patched.ToArray());
        }
    }
}
