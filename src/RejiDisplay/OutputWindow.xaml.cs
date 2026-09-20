using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RejiDisplay.Helpers;
using RejiDisplay.Models;

namespace RejiDisplay
{
    public partial class OutputWindow : Window
    {
        public DisplayInfo TargetDisplay { get; private set; }
        private ScaleMode _scaleMode = ScaleMode.Fit;

        public OutputWindow(DisplayInfo targetDisplay)
        {
            InitializeComponent();
            TargetDisplay = targetDisplay;
            PositionOnDisplay(targetDisplay);
        }

        public void PositionOnDisplay(DisplayInfo display)
        {
            TargetDisplay = display;
            this.Left = display.Left;
            this.Top = display.Top;
            this.Width = display.Width;
            this.Height = display.Height;

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, display.Left, display.Top, display.Width, display.Height, NativeMethods.SWP_SHOWWINDOW);
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero && TargetDisplay != null)
            {
                NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, TargetDisplay.Left, TargetDisplay.Top, TargetDisplay.Width, TargetDisplay.Height, NativeMethods.SWP_SHOWWINDOW);
            }
        }

        public void SetImage(BitmapImage? bitmap, ScaleMode scaleMode)
        {
            _scaleMode = scaleMode;
            MediaImage.Source = bitmap;
            ApplyScaleMode(scaleMode);
        }

        public void SetScaleMode(ScaleMode scaleMode)
        {
            _scaleMode = scaleMode;
            ApplyScaleMode(scaleMode);
        }

        private void ApplyScaleMode(ScaleMode scaleMode)
        {
            switch (scaleMode)
            {
                case ScaleMode.Fit:
                    MediaImage.Stretch = Stretch.Uniform;
                    break;
                case ScaleMode.Fill:
                    MediaImage.Stretch = Stretch.UniformToFill;
                    break;
                case ScaleMode.Stretch:
                    MediaImage.Stretch = Stretch.Fill;
                    break;
            }
        }

        public void SetBlackout(bool isBlackout)
        {
            BlackoutOverlay.Visibility = isBlackout ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
