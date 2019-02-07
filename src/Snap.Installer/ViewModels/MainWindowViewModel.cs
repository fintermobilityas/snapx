using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using ReactiveUI;
using Snap.Installer.Core;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace Snap.Installer.ViewModels
{
    internal class MainWindowViewModel : ViewModelBase
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

        public MainWindowViewModel([NotNull] IInstallerEmbeddedResources installerEmbeddedResources, CancellationToken cancellationToken)
        {
            if (installerEmbeddedResources == null) throw new ArgumentNullException(nameof(installerEmbeddedResources));

            _bitmaps = installerEmbeddedResources.GifAnimation.Select(x => new Bitmap(new MemoryStream(x))).ToList();
            _cancellationToken = cancellationToken;
            
            StatusText = "Please wait while unpacking application...";
            Progress = 30;
            
            Task.Run(AnimateAsync);
        }

        async Task AnimateAsync()
        {
            var index = 0;
            const int delay = 40;
            while (await MoveNextAsync(delay))
            {
                Bitmap = _bitmaps[index++];
                
                if (index < _bitmaps.Count)
                {
                    continue;
                }

                index = 0;
            }
        }
        
        async Task<bool> MoveNextAsync(int delay)
        {
            try
            {
                await Task.Delay(delay, _cancellationToken);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
        
    }
}
