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
        private bool _leftLoop = true;
        private bool _rightLoop = true;
        public bool IsDiagnosticMode { get; set; } = false;

        public MasterOutputWindow(DisplayInfo targetDisplay)
        {
            InitializeComponent();
            TargetDisplay = targetDisplay;
            Logger.Log($"MasterOutputWindow constructor created for target display: {targetDisplay.DisplayLabel} ({targetDisplay.Width}x{targetDisplay.Height} @ {targetDisplay.Left},{targetDisplay.Top})");
        }

        public void PositionOnDisplay(DisplayInfo display)
        {
            TargetDisplay = display;
            this.WindowStartupLocation = WindowStartupLocation.Manual;
            this.Left = display.Left;
            this.Top = display.Top;
            this.Width = Math.Max(100, display.Width);
            this.Height = Math.Max(100, display.Height);

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            Logger.Log($"PositionOnDisplay called for {display.DisplayLabel}. HWND = 0x{hwnd.ToInt64():X}");

            if (hwnd != IntPtr.Zero)
            {
                bool success = NativeMethods.SetWindowPos(
                    hwnd,
                    NativeMethods.HWND_TOPMOST,
                    display.Left,
                    display.Top,
                    display.Width,
                    display.Height,
                    NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_FRAMECHANGED);

                Logger.Log($"SetWindowPos executed for MasterOutputWindow: success={success}, Left={display.Left}, Top={display.Top}, Width={display.Width}, Height={display.Height}");
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            if (TargetDisplay != null)
            {
                PositionOnDisplay(TargetDisplay);
            }
        }

        public void ApplyState(MasterCanvasState state, BitmapImage? leftBmp, BitmapImage? rightBmp)
        {
            Logger.Log($"ApplyState called on MasterOutputWindow. DiagnosticMode={IsDiagnosticMode}, LeftType={state.Left.MediaType}, RightType={state.Right.MediaType}");

            // --- DIAGNOSTIC OVERLAY ---
            if (IsDiagnosticMode)
            {
                DiagnosticOverlayCanvas.Visibility = Visibility.Visible;
                return;
            }
            else
            {
                DiagnosticOverlayCanvas.Visibility = Visibility.Collapsed;
            }

            // --- LEFT REGION ---
            _leftLoop = state.Left.IsLooping;
            if (state.Left.MediaType == MediaSourceType.Video && !string.IsNullOrEmpty(state.Left.MediaPath))
            {
                LeftMediaImage.Visibility = Visibility.Collapsed;
                LeftWebView.Visibility = Visibility.Collapsed;
                LeftVideoPlayer.Visibility = Visibility.Visible;
                LeftVideoPlayer.IsMuted = state.Left.IsMuted;
                LeftVideoPlayer.Volume = state.Left.Volume;
                if (LeftVideoPlayer.Source == null || LeftVideoPlayer.Source.LocalPath != state.Left.MediaPath)
                {
                    LeftVideoPlayer.Source = new Uri(state.Left.MediaPath, UriKind.Absolute);
                }
                if (state.Left.IsPlaying) LeftVideoPlayer.Play(); else LeftVideoPlayer.Pause();
            }
            else if (state.Left.MediaType == MediaSourceType.Website && !string.IsNullOrEmpty(state.Left.WebUrl))
            {
                LeftVideoPlayer.Stop();
                LeftVideoPlayer.Visibility = Visibility.Collapsed;
                LeftMediaImage.Visibility = Visibility.Collapsed;
                LeftWebView.Visibility = Visibility.Visible;
                NavigateWebView(LeftWebView, state.Left.WebUrl);
            }
            else
            {
                LeftVideoPlayer.Stop();
                LeftVideoPlayer.Visibility = Visibility.Collapsed;
                LeftWebView.Visibility = Visibility.Collapsed;
                LeftMediaImage.Visibility = Visibility.Visible;
                LeftMediaImage.Source = leftBmp;
            }
            FrameworkElement leftTarget = state.Left.MediaType == MediaSourceType.Video ? (FrameworkElement)LeftVideoPlayer : (state.Left.MediaType == MediaSourceType.Website ? (FrameworkElement)LeftWebView : LeftMediaImage);
            double leftMediaW = state.Left.MediaType == MediaSourceType.Video ? (LeftVideoPlayer.NaturalVideoWidth > 0 ? LeftVideoPlayer.NaturalVideoWidth : 1920) : (leftBmp?.PixelWidth > 0 ? leftBmp.PixelWidth : 860);
            double leftMediaH = state.Left.MediaType == MediaSourceType.Video ? (LeftVideoPlayer.NaturalVideoHeight > 0 ? LeftVideoPlayer.NaturalVideoHeight : 1080) : (leftBmp?.PixelHeight > 0 ? leftBmp.PixelHeight : 1720);
            LayoutTransformHelper.ApplyLayoutTransform(
                leftTarget,
                null,
                state.Left.ScaleMode,
                state.Left.Layout.ZoomPercent,
                state.Left.Layout.OffsetX,
                state.Left.Layout.OffsetY,
                state.Left.Calibration.RotationAngle,
                leftMediaW,
                leftMediaH,
                860,
                1720);

            LeftBlackoutOverlay.Visibility = state.Left.IsBlackout ? Visibility.Visible : Visibility.Collapsed;
            if (state.Left.IsBlackout) LeftWebView.Visibility = Visibility.Collapsed;

            // --- RIGHT REGION ---
            _rightLoop = state.Right.IsLooping;
            if (state.Right.MediaType == MediaSourceType.Video && !string.IsNullOrEmpty(state.Right.MediaPath))
            {
                RightMediaImage.Visibility = Visibility.Collapsed;
                RightWebView.Visibility = Visibility.Collapsed;
                RightVideoPlayer.Visibility = Visibility.Visible;
                RightVideoPlayer.IsMuted = state.Right.IsMuted;
                RightVideoPlayer.Volume = state.Right.Volume;
                if (RightVideoPlayer.Source == null || RightVideoPlayer.Source.LocalPath != state.Right.MediaPath)
                {
                    RightVideoPlayer.Source = new Uri(state.Right.MediaPath, UriKind.Absolute);
                }
                if (state.Right.IsPlaying) RightVideoPlayer.Play(); else RightVideoPlayer.Pause();
            }
            else if (state.Right.MediaType == MediaSourceType.Website && !string.IsNullOrEmpty(state.Right.WebUrl))
            {
                RightVideoPlayer.Stop();
                RightVideoPlayer.Visibility = Visibility.Collapsed;
                RightMediaImage.Visibility = Visibility.Collapsed;
                RightWebView.Visibility = Visibility.Visible;
                NavigateWebView(RightWebView, state.Right.WebUrl);
            }
            else
            {
                RightVideoPlayer.Stop();
                RightVideoPlayer.Visibility = Visibility.Collapsed;
                RightWebView.Visibility = Visibility.Collapsed;
                RightMediaImage.Visibility = Visibility.Visible;
                RightMediaImage.Source = rightBmp;
            }

            FrameworkElement rightTarget = state.Right.MediaType == MediaSourceType.Video ? (FrameworkElement)RightVideoPlayer : (state.Right.MediaType == MediaSourceType.Website ? (FrameworkElement)RightWebView : RightMediaImage);
            double rightMediaW = state.Right.MediaType == MediaSourceType.Video ? (RightVideoPlayer.NaturalVideoWidth > 0 ? RightVideoPlayer.NaturalVideoWidth : 1920) : (rightBmp?.PixelWidth > 0 ? rightBmp.PixelWidth : 860);
            double rightMediaH = state.Right.MediaType == MediaSourceType.Video ? (RightVideoPlayer.NaturalVideoHeight > 0 ? RightVideoPlayer.NaturalVideoHeight : 1080) : (rightBmp?.PixelHeight > 0 ? rightBmp.PixelHeight : 1720);
            LayoutTransformHelper.ApplyLayoutTransform(
                rightTarget,
                null,
                state.Right.ScaleMode,
                state.Right.Layout.ZoomPercent,
                state.Right.Layout.OffsetX,
                state.Right.Layout.OffsetY,
                state.Right.Calibration.RotationAngle,
                rightMediaW,
                rightMediaH,
                860,
                1720);

            RightBlackoutOverlay.Visibility = state.Right.IsBlackout ? Visibility.Visible : Visibility.Collapsed;
            if (state.Right.IsBlackout) RightWebView.Visibility = Visibility.Collapsed;

            // --- MIDDLE Y-OFFSET CALIBRATION ---
            Canvas.SetTop(MiddleCanvas, state.MiddleYOffset);

            // --- MASTER BLACKOUT ---
            MasterBlackoutOverlay.Visibility = state.IsMasterBlackout ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LeftVideoPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            if (_leftLoop)
            {
                LeftVideoPlayer.Position = TimeSpan.Zero;
                LeftVideoPlayer.Play();
            }
        }

        private void RightVideoPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            if (_rightLoop)
            {
                RightVideoPlayer.Position = TimeSpan.Zero;
                RightVideoPlayer.Play();
            }
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

        private async void NavigateWebView(Microsoft.Web.WebView2.Wpf.WebView2 webView, string url)
        {
            try
            {
                if (webView == null || string.IsNullOrWhiteSpace(url)) return;

                if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    url = "https://" + url;
                }

                if (webView.CoreWebView2 == null)
                {
                    await webView.EnsureCoreWebView2Async();
                }

                if (webView.Source == null || webView.Source.AbsoluteUri != url)
                {
                    webView.Source = new Uri(url);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"NavigateWebView Error ({url})", ex);
            }
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
