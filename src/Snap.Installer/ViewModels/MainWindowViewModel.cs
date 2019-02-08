using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using JetBrains.Annotations;
using ReactiveUI;
using Snap.Core;
using Snap.Installer.Core;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace Snap.Installer.ViewModels
{
    internal sealed class MainWindowViewModel : ViewModelBase
    {
        readonly CancellationToken _cancellationToken;
        readonly List<Bitmap> _bitmaps;

        Bitmap _bitmap;
        string _statusText;
        double _progress;

        [UsedImplicitly]
        public double Progress
        {
            get => _progress;
            set => this.RaiseAndSetIfChanged(ref _progress, value);
        }

        [UsedImplicitly]
        public string StatusText
        {
            get => _statusText;
            set => this.RaiseAndSetIfChanged(ref _statusText, value);
        }        

        [UsedImplicitly]
        public Bitmap Bitmap
        {
            get => _bitmap;
            set => this.RaiseAndSetIfChanged(ref _bitmap, value);
        }

        public MainWindowViewModel([NotNull] ISnapInstallerEmbeddedResources snapInstallerEmbeddedResources, [NotNull] ISnapProgressSource progressSource, CancellationToken cancellationToken)
        {
            if (snapInstallerEmbeddedResources == null) throw new ArgumentNullException(nameof(snapInstallerEmbeddedResources));
            if (progressSource == null) throw new ArgumentNullException(nameof(progressSource));

            _bitmaps = snapInstallerEmbeddedResources.GifAnimation.Select(x => new Bitmap(new MemoryStream(x))).ToList();
            _cancellationToken = cancellationToken;

            StatusText = string.Empty;
            Progress = 0;

            progressSource.Progress += (sender, installationProgressPercentage) =>
            {
               Dispatcher.UIThread.InvokeAsync(() => Progress = installationProgressPercentage);
            };
            
            Task.Run(AnimateAsync);
        }

        public Task SetStatusTextAsync(string text)
        {
            return Dispatcher.UIThread.InvokeAsync(() => StatusText = text);
        }

        async Task AnimateAsync()
        {
            const int framePerMilliseconds = 40;

            async Task<bool> AnimateAsync()
            {
                try
                {
                    await Task.Delay(framePerMilliseconds, _cancellationToken);
                    return true;
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            }

            var bitmapCount = _bitmaps.Count; 
            if (bitmapCount <= 0)
            {
                throw new Exception("Unable to start animation, application does not contain any bitmaps.");
            }

            var bitmapIndex = 0;
            while (await AnimateAsync())
            {
                Bitmap = _bitmaps[bitmapIndex++];
                
                if (bitmapIndex < bitmapCount)
                {
                    continue;
                }

                bitmapIndex = 0;
            }
        }
        

        
    }
}
