using System;
using System.IO;

namespace Snap.Core.IO
{
    internal sealed class DisposableTempDirectory : IDisposable
    {
        readonly ISnapFilesystem _filesystem;

        public string AbsolutePath { get; }

        public DisposableTempDirectory(string workingDirectory, ISnapFilesystem filesystem)
        {
            _filesystem = filesystem;
            AbsolutePath = Path.Combine(workingDirectory, Guid.NewGuid().ToString());
            Directory.CreateDirectory(AbsolutePath);
        }

        public void Dispose()
        {
            _filesystem.DeleteDirectoryOrJustGiveUpAsync(AbsolutePath).Wait();
        }
    }
}
