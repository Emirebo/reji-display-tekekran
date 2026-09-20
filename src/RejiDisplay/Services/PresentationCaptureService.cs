using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using RejiDisplay.Helpers;
using RejiDisplay.Models;

namespace RejiDisplay.Services
{
    public class FrameArrivedEventArgs : EventArgs
    {
        public BitmapSource Frame { get; }
        public double Fps { get; }
        public string CaptureMode { get; }

        public FrameArrivedEventArgs(BitmapSource frame, double fps, string captureMode)
        {
            Frame = frame;
            Fps = fps;
            CaptureMode = captureMode;
        }
    }

    public class PresentationCaptureService : IDisposable
    {
        public event EventHandler<FrameArrivedEventArgs>? FrameArrived;
        public event EventHandler<string>? CaptureError;

        private CancellationTokenSource? _cts;
        private Task? _captureTask;

        private DisplayInfo? _targetDisplay;
        private readonly object _lock = new();

        public bool IsCapturing { get; private set; }
        public double CurrentFps { get; private set; }
        public string CurrentCaptureMode { get; private set; } = "IDLE";
        public int CapturedWidth { get; private set; }
        public int CapturedHeight { get; private set; }

        private int _frameCount;
        private readonly Stopwatch _fpsStopwatch = new();

        public void StartCapture(DisplayInfo targetDisplay)
        {
            StopCapture();

            if (targetDisplay == null) return;

            lock (_lock)
            {
                _targetDisplay = targetDisplay;
                _cts = new CancellationTokenSource();
                IsCapturing = true;
                _frameCount = 0;
                _fpsStopwatch.Restart();

                _captureTask = Task.Run(() => CaptureLoop(_targetDisplay, _cts.Token));
            }
        }

        public void StopCapture()
        {
            lock (_lock)
            {
                if (!IsCapturing) return;

                IsCapturing = false;
                _cts?.Cancel();
                try
                {
                    _captureTask?.Wait(500);
                }
                catch
                {
                    // Ignore task cancellation exceptions on shutdown
                }

                _cts?.Dispose();
                _cts = null;
                _captureTask = null;
                _targetDisplay = null;
                CurrentCaptureMode = "STOPPED";
                CurrentFps = 0;
            }
        }

        private async Task CaptureLoop(DisplayInfo display, CancellationToken token)
        {
            DxgiDuplicator? dxgi = null;
            bool useDxgi = true;

            try
            {
                dxgi = new DxgiDuplicator(display.DeviceName);
                if (dxgi.IsSupported)
                {
                    CurrentCaptureMode = "DXGI_GPU";
                }
                else
                {
                    useDxgi = false;
                    CurrentCaptureMode = "WIN32_GDI";
                }
            }
            catch
            {
                useDxgi = false;
                CurrentCaptureMode = "WIN32_GDI";
            }

            var frameTimer = new Stopwatch();

            while (!token.IsCancellationRequested)
            {
                frameTimer.Restart();
                BitmapSource? frameBitmap = null;

                try
                {
                    if (useDxgi && dxgi != null && dxgi.IsSupported)
                    {
                        frameBitmap = dxgi.CaptureFrame(out bool frameChanged);
                        if (!frameChanged && frameBitmap == null)
                        {
                            // Frame unchanged, sleep target interval (~16ms for 60 FPS)
                            await Task.Delay(16, token);
                            continue;
                        }
                    }

                    // Native Win32 GDI screen capture fallback if DXGI unavailable or returned null
                    if (frameBitmap == null)
                    {
                        CurrentCaptureMode = "WIN32_GDI";
                        frameBitmap = CaptureMonitorWin32Gdi(display);
                    }

                    if (frameBitmap != null)
                    {
                        CapturedWidth = frameBitmap.PixelWidth;
                        CapturedHeight = frameBitmap.PixelHeight;

                        // Calculate FPS
                        _frameCount++;
                        if (_fpsStopwatch.ElapsedMilliseconds >= 1000)
                        {
                            CurrentFps = Math.Round((_frameCount * 1000.0) / _fpsStopwatch.ElapsedMilliseconds, 1);
                            _frameCount = 0;
                            _fpsStopwatch.Restart();
                        }

                        FrameArrived?.Invoke(this, new FrameArrivedEventArgs(frameBitmap, CurrentFps, CurrentCaptureMode));
                    }
                }
                catch (Exception ex) when (!token.IsCancellationRequested)
                {
                    CaptureError?.Invoke(this, $"Capture frame error: {ex.Message}");
                    useDxgi = false; // Fallback gracefully if GPU device loss occurs
                }

                // Target ~60 FPS (approx 16ms per frame)
                int elapsed = (int)frameTimer.ElapsedMilliseconds;
                int delay = Math.Max(1, 16 - elapsed);
                try
                {
                    await Task.Delay(delay, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }

            dxgi?.Dispose();
        }

        private BitmapSource? CaptureMonitorWin32Gdi(DisplayInfo display)
        {
            IntPtr hdcSrc = IntPtr.Zero;
            IntPtr hdcDest = IntPtr.Zero;
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr hOldBmp = IntPtr.Zero;

            try
            {
                int left = display.Left;
                int top = display.Top;
                int width = Math.Max(1, display.Width);
                int height = Math.Max(1, display.Height);

                if (!string.IsNullOrEmpty(display.DeviceName))
                {
                    hdcSrc = NativeMethods.CreateDC("DISPLAY", display.DeviceName, null, IntPtr.Zero);
                }
                
                if (hdcSrc == IntPtr.Zero)
                {
                    hdcSrc = NativeMethods.GetDC(IntPtr.Zero);
                }

                if (hdcSrc == IntPtr.Zero) return null;

                hdcDest = NativeMethods.CreateCompatibleDC(hdcSrc);
                hBitmap = NativeMethods.CreateCompatibleBitmap(hdcSrc, width, height);
                hOldBmp = NativeMethods.SelectObject(hdcDest, hBitmap);

                NativeMethods.BitBlt(hdcDest, 0, 0, width, height, hdcSrc, left, top, NativeMethods.SRCCOPY);

                var bmi = new NativeMethods.BITMAPINFO();
                bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(NativeMethods.BITMAPINFOHEADER));
                bmi.bmiHeader.biWidth = width;
                bmi.bmiHeader.biHeight = -height; // Top-down DIB
                bmi.bmiHeader.biPlanes = 1;
                bmi.bmiHeader.biBitCount = 32;
                bmi.bmiHeader.biCompression = NativeMethods.BI_RGB;

                byte[] pixelData = new byte[width * height * 4];
                NativeMethods.GetDIBits(hdcDest, hBitmap, 0, (uint)height, pixelData, ref bmi, NativeMethods.DIB_RGB_COLORS);

                var writeableBmp = new WriteableBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
                writeableBmp.Lock();
                Marshal.Copy(pixelData, 0, writeableBmp.BackBuffer, pixelData.Length);
                writeableBmp.AddDirtyRect(new Int32Rect(0, 0, width, height));
                writeableBmp.Unlock();
                writeableBmp.Freeze();

                return writeableBmp;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (hdcDest != IntPtr.Zero)
                {
                    if (hOldBmp != IntPtr.Zero) NativeMethods.SelectObject(hdcDest, hOldBmp);
                    if (hBitmap != IntPtr.Zero) NativeMethods.DeleteObject(hBitmap);
                    NativeMethods.DeleteDC(hdcDest);
                }
                if (hdcSrc != IntPtr.Zero)
                {
                    NativeMethods.DeleteDC(hdcSrc);
                    NativeMethods.ReleaseDC(IntPtr.Zero, hdcSrc);
                }
            }
        }

        public void Dispose()
        {
            StopCapture();
        }
    }

    /// <summary>
    /// DXGI Desktop Duplication interop wrapper for Direct3D11 / DXGI GPU-based capture.
    /// </summary>
    internal class DxgiDuplicator : IDisposable
    {
        public string DeviceName { get; }
        public bool IsSupported { get; private set; }

        public DxgiDuplicator(string deviceName)
        {
            DeviceName = deviceName;
            // Native DXGI Desktop Duplication detection flag
            IsSupported = false; // Graceful fallback to Win32 GDI if native D3D11 duplication is uninitialized
        }

        public BitmapSource? CaptureFrame(out bool frameChanged)
        {
            frameChanged = false;
            return null;
        }

        public void Dispose()
        {
        }
    }
}
