using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using JetBrains.Annotations;

namespace Snap.Installer.Controls
{
    internal class ChromeLessWindow : Window
    {
        [UsedImplicitly]
        public int FixedWidth { get; set; } = 800;
        [UsedImplicitly]
        public int FixedHeight { get; set; } = 600;
        
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            BeginMoveDrag();

            base.OnPointerPressed(e);
        }
        
        protected override void HandleResized(Size clientSize)
        {
            if (FixedWidth > 0 && FixedHeight > 0 
                               && clientSize != new Size(FixedWidth, FixedHeight))
            {
                return;
            }
            
            base.HandleResized(clientSize);
        }

        protected override void HandleWindowStateChanged(WindowState state)
        {
            WindowState = WindowState.Normal;
        }
    }
}
