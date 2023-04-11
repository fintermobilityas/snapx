using System;
using System.IO;
using System.Threading.Tasks;
using Snap.Core;
using Xunit;

namespace Snap.Tests.Core
{
    public class SnapBinaryPatcherTests
    {
        static readonly Random Random = new();
        readonly ISnapBinaryPatcher _snapBinaryPatcher;

        public SnapBinaryPatcherTests()
        {
            _snapBinaryPatcher = new SnapBinaryPatcher(new BsdiffLib());
        }

        [Fact]
        public async Task TestBsDiff()
        {
            var baseFileData = new byte[] { 0, 2, 3, 5 };
            var newFileData = new byte[] { 0, 1, 2, 3, 10 };

            using var baseFileStream = new MemoryStream(baseFileData, 0, baseFileData.Length, true, true);
            using var newFileStream = new MemoryStream(newFileData, 0, newFileData.Length, true, true);
            await using var patchStream = new MemoryStream();
            _snapBinaryPatcher.Create(baseFileStream, newFileStream, patchStream);
            patchStream.Seek(0, SeekOrigin.Begin);
            
            await using var toPatchStream = new MemoryStream(baseFileData, 0, baseFileData.Length, true, true);
            await using var patchedStream = new MemoryStream();
            await _snapBinaryPatcher.ApplyAsync(toPatchStream, patchStream, patchedStream, default);
            
            Assert.Equal(newFileData, patchedStream.ToArray());
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

            using var baseFileStream = new MemoryStream(baseFileData, 0, baseFileData.Length, true, true);
            using var newFileStream = new MemoryStream(newFileData, 0, newFileData.Length, true, true);
            await using var patchStream = new MemoryStream();
            _snapBinaryPatcher.Create(baseFileStream, newFileStream, patchStream);
            patchStream.Seek(0, SeekOrigin.Begin);
            
            await using var toPatchStream = new MemoryStream(baseFileData, 0, baseFileData.Length, true, true);
            await using var patchedStream = new MemoryStream();
            await _snapBinaryPatcher.ApplyAsync(toPatchStream, patchStream, patchedStream, default);
            
            Assert.Equal(newFileData, patchedStream.ToArray());
        }

        [Fact]
        public async Task TestBsDiffWithoutExtraData()
        {
            var baseFileData = new byte[] { 1, 1, 1, 1 };
            var newFileData = new byte[] { 2, 1, 1, 1 };

            using var baseFileStream = new MemoryStream(baseFileData, 0, baseFileData.Length, true, true);
            using var newFileStream = new MemoryStream(newFileData, 0, newFileData.Length, true, true);
            await using var patchStream = new MemoryStream();
            _snapBinaryPatcher.Create(baseFileStream, newFileStream, patchStream);
            patchStream.Seek(0, SeekOrigin.Begin);
      
            await using var toPatchStream = new MemoryStream(baseFileData, 0, baseFileData.Length, true, true);
            await using var patchedStream = new MemoryStream();
            await _snapBinaryPatcher.ApplyAsync(toPatchStream, patchStream, patchedStream, default);

            Assert.Equal(newFileData, patchedStream.ToArray());
        }
    }
}
