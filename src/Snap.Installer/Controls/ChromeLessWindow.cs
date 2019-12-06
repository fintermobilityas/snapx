using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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

        protected override void OnTemplateApplied(TemplateAppliedEventArgs e)
        {
            var thisHandle = PlatformImpl.Handle.Handle;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                NativeMethodsWindows.FocusThisWindow(thisHandle);
            }
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
