using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using JetBrains.Annotations;
using ReactiveUI;
using Snap.Core;
using Snap.Installer.Core;

namespace Snap.Installer.ViewModels
{
    internal sealed class AvaloniaMainWindowViewModel : ViewModelBase, IMainWindowViewModel
    {
        [NotNull] readonly ISnapInstallerEmbeddedResources _snapInstallerEmbeddedResources;
        readonly Action _onFirstFrameAnimatedCallback;
        readonly CancellationToken _cancellationToken;
        readonly List<Bitmap> _bitmaps;

        Bitmap _bitmap;
        string _statusText;
        double _progress;

        public bool Headless => false;

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

        public AvaloniaMainWindowViewModel([NotNull] ISnapInstallerEmbeddedResources snapInstallerEmbeddedResources, 
            [NotNull] ISnapProgressSource progressSource, [NotNull] Action onFirstFrameAnimatedCallback, CancellationToken cancellationToken)
        {
            if (progressSource == null) throw new ArgumentNullException(nameof(progressSource));

            _bitmaps = new List<Bitmap>();
            _snapInstallerEmbeddedResources = snapInstallerEmbeddedResources ?? throw new ArgumentNullException(nameof(snapInstallerEmbeddedResources));
            _onFirstFrameAnimatedCallback = onFirstFrameAnimatedCallback ?? throw new ArgumentNullException(nameof(onFirstFrameAnimatedCallback));
            _cancellationToken = cancellationToken;

            StatusText = string.Empty;
            Progress = 0;

            progressSource.Progress = installationProgressPercentage =>
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

            var streams = _snapInstallerEmbeddedResources.GifAnimation.ToList();

            var bitmapCount = streams.Count;
            if (bitmapCount <= 0)
            {
                throw new Exception("Unable to start animation, application does not contain any bitmaps.");
            }

            var bitmapIndex = 0;
            var addBitmap = true;
            while (await AnimateAsync())
            {
                if (addBitmap)
                {
                    _bitmaps.Add(new Bitmap(new MemoryStream(streams[bitmapIndex])));
                }
                
                Bitmap = _bitmaps[bitmapIndex++];
                
                if (addBitmap && bitmapIndex == 1)
                {
                    _onFirstFrameAnimatedCallback();
                }

                if (bitmapIndex < bitmapCount)
                {
                    continue;
                }

                addBitmap = false;
                bitmapIndex = 0;
            }
        }
        

        
    }
}
