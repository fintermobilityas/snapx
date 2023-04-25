using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace Snap.Installer.Windows;

internal class CustomChromeWindow : Window
{
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        Focus();
        base.OnApplyTemplate(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        BeginMoveDrag(e);
        base.OnPointerPressed(e);
    }
        
    protected override void HandleWindowStateChanged(WindowState state) => WindowState = WindowState.Normal;
}
