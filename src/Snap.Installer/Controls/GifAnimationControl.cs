using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using JetBrains.Annotations;

namespace Snap.Installer.Controls;

public class GifAnimationControl : Control
{
    readonly List<Bitmap> _bitmaps;
    DispatcherTimer _dispatcherTimer;
    TimeSpan _delayTimespan;
    int _bitmapindex;
    Action _onFirstDrawAction;
    bool _isFirstDraw;

    public GifAnimationControl()
    {
        _bitmaps = [];
    }

    public void AddImages([NotNull] IEnumerable<Bitmap> bitmaps)
    {
        if (bitmaps == null) throw new ArgumentNullException(nameof(bitmaps));
        _bitmaps.AddRange(bitmaps);
            
        if (_bitmaps.Count == 0)
        {
            throw new ArgumentException("Value cannot be an empty collection.", nameof(bitmaps));
        }
    }

    public void Run(TimeSpan delayTimeSpan, [NotNull] Action onFirstDrawAction)
    {
        _delayTimespan = delayTimeSpan;
        _isFirstDraw = true;
        _onFirstDrawAction = onFirstDrawAction ?? throw new ArgumentNullException(nameof(onFirstDrawAction));

        _dispatcherTimer = new DispatcherTimer(_delayTimespan, DispatcherPriority.Render,
            (sender, args) => InvalidateVisual());
        _dispatcherTimer.Start();
    }

    public override void Render(DrawingContext context)
    {
        if (!_bitmaps.Any())
        {
            goto done;
        }

        var bitmap = _bitmaps[_bitmapindex++];

        context.DrawImage(bitmap, new Rect(0, 0, bitmap.Size.Width, bitmap.Size.Height));

        if (_bitmapindex == 1
            && _isFirstDraw)
        {
            _onFirstDrawAction();
            _isFirstDraw = false;
        }

        if (_bitmapindex < _bitmaps.Count)
        {
            goto done;
        }

        _bitmapindex = 0;

        done:
        base.Render(context);
    }

}
