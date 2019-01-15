using System;
using System.Collections.Generic;
using JetBrains.Annotations;

namespace Snap.Core.IO
{
    internal class DisposableFiles : IDisposable
    {
        readonly IReadOnlyCollection<string> _filenames;
        readonly ISnapFilesystem _filesystem;

        public DisposableFiles([NotNull] ISnapFilesystem filesystem, [NotNull] params string[] filenames)
        {
            _filenames = filenames ?? throw new ArgumentNullException(nameof(filenames));
            _filesystem = filesystem ?? throw new ArgumentNullException(nameof(filesystem));
        }

        public void Dispose()
        {
            foreach (var filename in _filenames)
            {
                _filesystem.DeleteFileHarder(filename, true);
            }
        }
    }
}
