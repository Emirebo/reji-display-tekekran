using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using RejiDisplay.Helpers;
using RejiDisplay.Models;

namespace RejiDisplay
{
    public partial class IdentifyOverlayWindow : Window
    {
        private readonly DispatcherTimer _timer;

        public IdentifyOverlayWindow(DisplayInfo display, string roleText)
        {
            InitializeComponent();

            TxtNumber.Text = $"EKRAN {display.DisplayIndex}";
            TxtDetails.Text = $"{display.Width} x {display.Height} @ ({display.Left}, {display.Top})";
            TxtStatus.Text = roleText;

            PositionOnDisplay(display);

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3.5)
            };
            _timer.Tick += (s, e) =>
            {
                _timer.Stop();
                Close();
            };
            _timer.Start();
        }

        private void PositionOnDisplay(DisplayInfo display)
        {
            this.Left = display.Left;
            this.Top = display.Top;
            this.Width = display.Width;
            this.Height = display.Height;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, (int)this.Left, (int)this.Top, (int)this.Width, (int)this.Height, NativeMethods.SWP_SHOWWINDOW);
            }
        }
    }
}
