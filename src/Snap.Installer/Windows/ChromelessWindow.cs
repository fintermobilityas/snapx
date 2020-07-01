using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace Snap.Installer.Windows
{
    internal class ChromelessWindow : Window
    {
        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            var thisHandle = PlatformImpl.Handle.Handle;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                NativeMethodsWindows.FocusThisWindow(thisHandle);
            }

            base.OnApplyTemplate(e);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            BeginMoveDrag(e);

            base.OnPointerPressed(e);
        }
        
        protected override void HandleWindowStateChanged(WindowState state)
        {
            WindowState = WindowState.Normal;
        }

        protected static class NativeMethodsWindows
        {
            [DllImport("user32", SetLastError = true, EntryPoint = "SetActiveWindow")]
            static extern IntPtr SetActiveWindow(IntPtr hWnd);
            [DllImport("user32", SetLastError = true, EntryPoint = "SetForegroundWindow")]
            static extern bool SetForegroundWindow(IntPtr hWnd);

            public static void FocusThisWindow(IntPtr hWnd)
            {
                SetActiveWindow(hWnd);
                SetForegroundWindow(hWnd);
            }
        }
    }
}
