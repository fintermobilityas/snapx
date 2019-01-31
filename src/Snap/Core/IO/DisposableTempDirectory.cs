using System;
using System.IO;

namespace Snap.Core.IO
{
    internal sealed class DisposableTempDirectory : IDisposable
    {
        readonly ISnapFilesystem _filesystem;

        public string WorkingDirectory { get; }
        public DirectoryInfo WorkingDirectoryInfo => new DirectoryInfo(WorkingDirectory);

        public DisposableTempDirectory(string workingDirectory, ISnapFilesystem filesystem)
        {
            _filesystem = filesystem;
            WorkingDirectory = Path.Combine(workingDirectory, Guid.NewGuid().ToString());
            Directory.CreateDirectory(WorkingDirectory);
        }

        public void Dispose()
        {
            _filesystem.DeleteDirectoryOrJustGiveUpAsync(WorkingDirectory).Wait();
        }
    }
}
