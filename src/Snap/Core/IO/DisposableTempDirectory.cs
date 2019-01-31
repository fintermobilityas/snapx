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
            WorkingDirectory = filesystem.PathCombine(workingDirectory, Guid.NewGuid().ToString());
            filesystem.DirectoryCreate(WorkingDirectory);
        }

        public void Dispose()
        {
            _filesystem.DirectoryDeleteOrJustGiveUpAsync(WorkingDirectory).Wait();
        }
    }
}
