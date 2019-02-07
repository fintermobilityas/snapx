using Avalonia.Controls;
using Avalonia.Input;

namespace Snap.Installer.Controls
{
    internal class ChromeLessWindow : Window
    {
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            BeginMoveDrag();

            base.OnPointerPressed(e);
        }
    }
}
