using System;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using JetBrains.Annotations;
using ReactiveUI;
using Snap.Core;
using Snap.Installer.Controls;
using Snap.Installer.Core;

namespace Snap.Installer.ViewModels
{
    internal sealed class AvaloniaMainWindowViewModel : ViewModelBase, IMainWindowViewModel
    {
        [NotNull] readonly ISnapInstallerEmbeddedResources _snapInstallerEmbeddedResources;
        [NotNull] readonly Action _onFirstFrameAnimatedCallback;
        GifAnimationControl _gifGifAnimationControl;
        string _statusText;
        double _progress;
        Brush _statusTextBrush;
        
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
        public Brush StatusTextBrush
        {
            get => _statusTextBrush;
            set => this.RaiseAndSetIfChanged(ref _statusTextBrush, value);
        }

        [UsedImplicitly]
        public GifAnimationControl GifAnimation
        {
            get => _gifGifAnimationControl;
            set => this.RaiseAndSetIfChanged(ref _gifGifAnimationControl, value);
        }

        public ReactiveCommand<Unit, Unit> CancelCommand { get; set; }

        public AvaloniaMainWindowViewModel([NotNull] ISnapInstallerEmbeddedResources snapInstallerEmbeddedResources, 
            [NotNull] ISnapProgressSource progressSource, [NotNull] Action onFirstFrameAnimatedCallback)
        {
            if (progressSource == null) throw new ArgumentNullException(nameof(progressSource));

            _snapInstallerEmbeddedResources = snapInstallerEmbeddedResources ?? throw new ArgumentNullException(nameof(snapInstallerEmbeddedResources));
            _onFirstFrameAnimatedCallback = onFirstFrameAnimatedCallback ?? throw new ArgumentNullException(nameof(onFirstFrameAnimatedCallback));

            StatusText = string.Empty;
            Progress = 0;
            StatusTextBrush = (Brush) Brush.Parse("#fff");

            progressSource.Progress = installationProgressPercentage =>
            {
               Dispatcher.UIThread.InvokeAsync(() => Progress = installationProgressPercentage);
            };
        }

        public Task SetStatusTextAsync(string text)
        {
            return Dispatcher.UIThread.InvokeAsync(() => StatusText = text);
        }

        public Task SetErrorAsync()
        {
            return Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusTextBrush = (Brush) Brush.Parse("#B80F0A");
            });
        }

        public void OnInitialized()
        {
            GifAnimation.AddImages(_snapInstallerEmbeddedResources
                .GifAnimation.Select(x => new Bitmap(new MemoryStream(x))));

            GifAnimation.Run(TimeSpan.FromMilliseconds(66), _onFirstFrameAnimatedCallback);
        }
    }
}
