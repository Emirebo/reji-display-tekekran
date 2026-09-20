using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RejiDisplay.Helpers;
using RejiDisplay.Models;

namespace RejiDisplay
{
    public partial class MasterOutputWindow : Window
    {
        public DisplayInfo TargetDisplay { get; private set; }

        public MasterOutputWindow(DisplayInfo targetDisplay)
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

        public void ApplyState(MasterCanvasState state, BitmapImage? leftBmp, BitmapImage? rightBmp)
        {
            // Left Region
            LeftMediaImage.Source = leftBmp;
            ApplyScaleMode(LeftMediaImage, state.Left.ScaleMode);
            LeftBlackoutOverlay.Visibility = state.Left.IsBlackout ? Visibility.Visible : Visibility.Collapsed;

            // Right Region
            RightMediaImage.Source = rightBmp;
            ApplyScaleMode(RightMediaImage, state.Right.ScaleMode);
            RightBlackoutOverlay.Visibility = state.Right.IsBlackout ? Visibility.Visible : Visibility.Collapsed;

            // Middle Y-Offset Calibration
            Canvas.SetTop(MiddleCanvas, state.MiddleYOffset);

            // Master Blackout
            MasterBlackoutOverlay.Visibility = state.IsMasterBlackout ? Visibility.Visible : Visibility.Collapsed;
        }

        public void UpdateMiddleCaptureFrame(BitmapSource? frameBitmap)
        {
            if (frameBitmap != null)
            {
                MiddleCaptureImage.Source = frameBitmap;
                MiddleFallbackText.Visibility = Visibility.Collapsed;
            }
            else
            {
                MiddleCaptureImage.Source = null;
                MiddleFallbackText.Visibility = Visibility.Visible;
            }
        }

        public void SetMasterBlackout(bool isBlackout)
        {
            MasterBlackoutOverlay.Visibility = isBlackout ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ApplyScaleMode(Image image, ScaleMode mode)
        {
            switch (mode)
            {
                case ScaleMode.Fit:
                    image.Stretch = Stretch.Uniform;
                    break;
                case ScaleMode.Fill:
                    image.Stretch = Stretch.UniformToFill;
                    break;
                case ScaleMode.Stretch:
                    image.Stretch = Stretch.Fill;
                    break;
            }
        }
    }
}
