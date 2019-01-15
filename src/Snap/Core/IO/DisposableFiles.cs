using System;
using System.Collections.Generic;
using System.IO;
using Snap.Logging;

namespace Snap.Core.IO
{
    internal class DisposableFiles : IDisposable
    {
        readonly IReadOnlyCollection<string> _filenames;
        readonly ILog _logger;

        public DisposableFiles(IReadOnlyCollection<string> filenames, ILog logger = null)
        {
            _filenames = filenames ?? throw new ArgumentNullException(nameof(filenames));
            _logger = logger;
        }

        public void Dispose()
        {
            foreach (var filename in _filenames)
            {
                try
                {
                    if (File.Exists(filename))
                    {
                        File.Delete(filename);
                    }
                }
                catch (Exception e)
                {
                    _logger?.ErrorException($"Unable to delete disposable file: {filename}.", e);
                }
            }
        }
    }
}
