using System;
using System.IO;
using System.Reflection;
using System.Text;
using Splat;

namespace Snap
{
    internal sealed class SnapSetupLogLogger : ILogger, IDisposable
    {
        readonly TextWriter _inner;
        readonly object _gate = 42;
        public LogLevel Level { get; set; }

        public SnapSetupLogLogger(bool saveInTemp)
        {
            for (var i=0; i < 10; i++) {
                try {
                    var dir = saveInTemp ?
                        Path.GetTempPath() :
                        Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

                    var file = Path.Combine(dir, $"SnapSetup.{i}.log".Replace(".0.log", ".log"));
                    var str = File.Open(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    _inner = new StreamWriter(str, Encoding.UTF8, 4096, false) { AutoFlush = true };
                    return;
                } catch (Exception ex) {
                    // Didn't work? Keep going
                    Console.Error.WriteLine("Couldn't open log file, trying new file: " + ex.ToString());
                }
            }

            _inner = Console.Error;
        }

        public void Write(string message, LogLevel logLevel)
        {
            if (logLevel < Level) {
                return;
            }

            lock (_gate) _inner.WriteLine("{0:yyyy-MM-dd HH:mm:ss}> {1}", DateTime.Now, message);
        }

        public void Dispose()
        {
            lock (_gate) {
                _inner.Flush();
                _inner.Dispose();
            }
        }
    }
}
