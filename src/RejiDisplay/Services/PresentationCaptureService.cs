using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
        public long FrameId { get; }
        public BitmapSource Frame { get; }
        public double Fps { get; }
        public string CaptureMode { get; }

        public FrameArrivedEventArgs(long frameId, BitmapSource frame, double fps, string captureMode)
        {
            FrameId = frameId;
            Frame = frame;
            Fps = fps;
            CaptureMode = captureMode;
        }
    }

    public enum DiagnosticScenario
    {
        ScenarioA_CaptureOnly,
        ScenarioB_CaptureAndMaster,
        ScenarioC_CaptureAndPreview,
        ScenarioD_FullApp,
        ScenarioE_FullAppWithWeb,
        ScenarioF_FullAppWithVideo
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
        public double UniqueCapturedFps { get; private set; }
        public double RepeatedFrameFps { get; private set; }
        public string CurrentCaptureMode { get; private set; } = "IDLE";
        public int CapturedWidth { get; private set; }
        public int CapturedHeight { get; private set; }

        public double MasterDeliveryFps { get; private set; }
        public double PreviewDeliveryFps { get; private set; }
        public int DroppedFrames { get; private set; }
        public int RepeatedFrames { get; private set; }

        public double AcquisitionMs { get; private set; }
        public double ReadbackMs { get; private set; }
        public double FrameLatencyMs { get; private set; }
        public double AverageFrameLatencyMs { get; private set; }
        public double P95FrameLatencyMs { get; private set; }
        public double DispatcherQueueLatencyMs { get; private set; }

        public DiagnosticScenario ActiveScenario { get; set; } = DiagnosticScenario.ScenarioD_FullApp;

        private int _frameCount;
        private int _uniqueFrameCount;
        private int _repeatedFrameCount;
        private int _masterFrameCount;
        private int _previewFrameCount;
        private readonly Stopwatch _fpsStopwatch = new();
        private readonly Stopwatch _diagnosticLogStopwatch = new();

        private readonly List<double> _recentLatencies = new();

        private IntPtr _persistentHdcSrc = IntPtr.Zero;
        private IntPtr _persistentHdcDest = IntPtr.Zero;
        private IntPtr _persistentHBitmap = IntPtr.Zero;
        private IntPtr _persistentOldBmp = IntPtr.Zero;
        private string? _persistentDeviceName;
        private int _persistentWidth;
        private int _persistentHeight;
        private byte[]? _gdiPixelBuffer;
        private BitmapSource? _lastCapturedBitmap;

        private int _isDispatchingPreview = 0;
        private long _lastPreviewTicks = 0;

        public void StartCapture(DisplayInfo targetDisplay)
        {
            StopCapture();

            if (targetDisplay == null) return;

            lock (_lock)
            {
                NativeMethods.TimeBeginPeriod(1);
                _targetDisplay = targetDisplay;
                _cts = new CancellationTokenSource();
                IsCapturing = true;
                _frameCount = 0;
                _masterFrameCount = 0;
                _previewFrameCount = 0;
                DroppedFrames = 0;
                _fpsStopwatch.Restart();
                _diagnosticLogStopwatch.Restart();

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
                    // Ignore cancellation exceptions on shutdown
                }

                ReleasePersistentGdi();
                NativeMethods.TimeEndPeriod(1);

                _cts?.Dispose();
                _cts = null;
                _captureTask = null;
                _targetDisplay = null;
                CurrentCaptureMode = "STOPPED";
                CurrentFps = 0;
                MasterDeliveryFps = 0;
                PreviewDeliveryFps = 0;
            }
        }

        private async Task CaptureLoop(DisplayInfo display, CancellationToken token)
        {
            WgcCaptureEngine? wgcEngine = null;
            DxgiCaptureEngine? dxgiEngine = null;
            bool useWgc = false;
            bool useDxgi = false;
            string fallbackReason = string.Empty;

            Logger.Log($"[PresentationCapture] Starting capture loop for '{display.FriendlyName}' ({display.DeviceName})");

            // 1. Attempt WGC capture first (preferred GPU backend)
            try
            {
                wgcEngine = new WgcCaptureEngine(display.DeviceName);
                if (wgcEngine.IsInitialized)
                {
                    useWgc = true;
                    CurrentCaptureMode = "BAŞLATILIYOR (WGC_GPU)";
                    Logger.Log($"[BACKEND_TRANSITION] Active backend: WGC_GPU ({wgcEngine.Width}x{wgcEngine.Height} @ {display.RefreshRate}Hz) - Initializing first frame...");
                }
                else
                {
                    fallbackReason = $"WGC init failed: {wgcEngine.InitError}";
                    wgcEngine.Dispose();
                    wgcEngine = null;
                }
            }
            catch (Exception exWgc)
            {
                fallbackReason = $"WGC Exception: {exWgc.Message}";
                wgcEngine?.Dispose();
                wgcEngine = null;
            }

            // 2. Fallback to DXGI capture if WGC is not initialized
            if (!useWgc)
            {
                try
                {
                    dxgiEngine = new DxgiCaptureEngine(display.DeviceName);
                    if (dxgiEngine.IsInitialized)
                    {
                        useDxgi = true;
                        CurrentCaptureMode = "BAŞLATILIYOR (DXGI_GPU)";
                        Logger.Log($"[BACKEND_TRANSITION] Active backend: DXGI_GPU ({display.Width}x{display.Height} @ {display.RefreshRate}Hz) - Initializing first frame...");
                    }
                    else
                    {
                        fallbackReason += $" | DXGI init failed: {dxgiEngine.InitError}";
                        dxgiEngine.Dispose();
                        dxgiEngine = null;
                    }
                }
                catch (Exception exDxgi)
                {
                    fallbackReason += $" | DXGI Exception: {exDxgi.Message}";
                    dxgiEngine?.Dispose();
                    dxgiEngine = null;
                }
            }

            // 3. Fallback to WIN32_GDI if both GPU engines fail
            if (!useWgc && !useDxgi)
            {
                CurrentCaptureMode = "BAŞLATILIYOR (WIN32_GDI)";
                Logger.Log($"[BACKEND_TRANSITION] Fallback to WIN32_GDI. Reason: {fallbackReason}");
            }

            var frameTimer = Stopwatch.StartNew();
            var initTimeoutTimer = Stopwatch.StartNew();
            long targetFrameTicks = Stopwatch.Frequency / 60; // 60 FPS pacing

            while (!token.IsCancellationRequested)
            {
                long startTicks = frameTimer.ElapsedTicks;
                BitmapSource? frameBitmap = null;
                bool frameChanged = false;
                double frameAcqMs = 0;
                double frameReadMs = 0;
                var overallLatencySw = Stopwatch.StartNew();

                try
                {
                    if (useWgc && wgcEngine != null && wgcEngine.IsInitialized)
                    {
                        frameBitmap = wgcEngine.CaptureFrame(out frameChanged, out frameAcqMs, out frameReadMs);
                        if (frameBitmap != null)
                        {
                            CurrentCaptureMode = "WGC_GPU (120FPS)";
                        }
                        else if (initTimeoutTimer.ElapsedMilliseconds > 2000 && _lastCapturedBitmap == null)
                        {
                            Logger.Log($"[WGC_NO_FRAMES] WGC engine produced 0 initial frames in {initTimeoutTimer.ElapsedMilliseconds}ms. Shutting down WGC and switching fallback to WIN32_GDI.");
                            useWgc = false;
                            wgcEngine.Dispose();
                            wgcEngine = null;
                        }
                        else if (!wgcEngine.IsInitialized)
                        {
                            useWgc = false;
                            wgcEngine.Dispose();
                            wgcEngine = null;
                            Logger.Log($"[BACKEND_TRANSITION] WGC device access lost. Switching fallback.");
                        }
                    }
                    else if (useDxgi && dxgiEngine != null && dxgiEngine.IsInitialized)
                    {
                        frameBitmap = dxgiEngine.CaptureFrame(out frameChanged, out frameAcqMs, out frameReadMs);
                        if (frameBitmap != null)
                        {
                            CurrentCaptureMode = "DXGI_GPU (60FPS)";
                        }
                        else if (!dxgiEngine.IsInitialized)
                        {
                            useDxgi = false;
                            dxgiEngine.Dispose();
                            dxgiEngine = null;
                            Logger.Log($"[BACKEND_TRANSITION] DXGI device access lost. Switching fallback.");
                        }
                    }

                    if (!useWgc && !useDxgi)
                    {
                        var gdiSw = Stopwatch.StartNew();
                        frameBitmap = CaptureMonitorWin32GdiPersistent(display);
                        gdiSw.Stop();
                        frameAcqMs = gdiSw.Elapsed.TotalMilliseconds;
                        frameReadMs = 0;
                        frameChanged = true;
                        if (frameBitmap != null)
                        {
                            CurrentCaptureMode = "WIN32_GDI (Persistent DC 60FPS)";
                        }
                        else
                        {
                            CurrentCaptureMode = "KARE YOK / HATA";
                        }
                    }

                    if (frameBitmap != null)
                    {
                        _lastCapturedBitmap = frameBitmap;
                    }
                    else if (_lastCapturedBitmap != null)
                    {
                        frameBitmap = _lastCapturedBitmap;
                        frameChanged = false;
                    }

                    overallLatencySw.Stop();
                    double totalLatencyMs = Math.Round(overallLatencySw.Elapsed.TotalMilliseconds, 2);

                    if (frameBitmap != null)
                    {
                        if (frameChanged)
                        {
                            _uniqueFrameCount++;
                        }
                        else
                        {
                            _repeatedFrameCount++;
                            RepeatedFrames++;
                        }

                        AcquisitionMs = Math.Round(frameAcqMs, 2);
                        ReadbackMs = Math.Round(frameReadMs, 2);
                        FrameLatencyMs = totalLatencyMs;

                        lock (_recentLatencies)
                        {
                            _recentLatencies.Add(totalLatencyMs);
                            if (_recentLatencies.Count > 100) _recentLatencies.RemoveAt(0);

                            if (_recentLatencies.Count > 0)
                            {
                                _recentLatencies.Sort();
                                AverageFrameLatencyMs = Math.Round(_recentLatencies.Average(), 2);
                                int p95Index = (int)Math.Ceiling(0.95 * _recentLatencies.Count) - 1;
                                P95FrameLatencyMs = Math.Round(_recentLatencies[Math.Max(0, p95Index)], 2);
                            }
                        }

                        CapturedWidth = frameBitmap.PixelWidth;
                        CapturedHeight = frameBitmap.PixelHeight;

                        _frameCount++;
                        _masterFrameCount++;

                        // --- FPS TELEMETRY METRICS ---
                        if (_fpsStopwatch.ElapsedMilliseconds >= 1000)
                        {
                            double elapsedSec = _fpsStopwatch.ElapsedMilliseconds / 1000.0;
                            CurrentFps = Math.Round(_frameCount / elapsedSec, 1);
                            UniqueCapturedFps = Math.Round(_uniqueFrameCount / elapsedSec, 1);
                            RepeatedFrameFps = Math.Round(_repeatedFrameCount / elapsedSec, 1);
                            MasterDeliveryFps = Math.Round(_masterFrameCount / elapsedSec, 1);
                            PreviewDeliveryFps = Math.Round(_previewFrameCount / elapsedSec, 1);
                            _frameCount = 0;
                            _uniqueFrameCount = 0;
                            _repeatedFrameCount = 0;
                            _masterFrameCount = 0;
                            _previewFrameCount = 0;
                            _fpsStopwatch.Restart();
                        }

                        // --- PERIODIC DIAGNOSTICS LOGGING (Every 5 Seconds) ---
                        if (_diagnosticLogStopwatch.ElapsedMilliseconds >= 5000)
                        {
                            _diagnosticLogStopwatch.Restart();
                            Logger.Log($"[DIAGNOSTICS] Scenario={ActiveScenario} | Backend={CurrentCaptureMode} | Display={display.DisplayLabel} ({CapturedWidth}x{CapturedHeight} @ {display.RefreshRate}Hz) | CapturedFPS={CurrentFps} | UniqueFPS={UniqueCapturedFps} | MasterFPS={MasterDeliveryFps} | PreviewFPS={PreviewDeliveryFps} | DroppedFrames={DroppedFrames} | RepeatedFrames={RepeatedFrames} | AvgLatency={AverageFrameLatencyMs}ms | P95Latency={P95FrameLatencyMs}ms | Acquisition={AcquisitionMs}ms | Readback={ReadbackMs}ms | DispatcherLatency={DispatcherQueueLatencyMs}ms");
                        }

                        // --- DECOUPLED OPERATOR PREVIEW DELIVERY (Max 30 FPS, non-blocking) ---
                        long currentTicks = Stopwatch.GetTimestamp();
                        double msSinceLastPreview = (currentTicks - _lastPreviewTicks) * 1000.0 / Stopwatch.Frequency;

                        if (msSinceLastPreview >= 30.0) // Throttle UI preview to ~30 FPS
                        {
                            if (Interlocked.CompareExchange(ref _isDispatchingPreview, 1, 0) == 0)
                            {
                                _lastPreviewTicks = currentTicks;
                                var capturedBmp = frameBitmap;
                                var capturedFps = CurrentFps;
                                var capturedMode = CurrentCaptureMode;
                                long currentFrameId = wgcEngine?.CurrentFrameId ?? _frameCount;

                                var dispatcher = Application.Current?.Dispatcher;
                                if (dispatcher != null)
                                {
                                    if (currentFrameId <= 5)
                                    {
                                        Logger.Log($"[FRAME_TRACE] Stage 4 (PresentationCaptureService Published Frame): FrameId={currentFrameId} | Mode={capturedMode} | Fps={capturedFps:F1} | ThreadId={Environment.CurrentManagedThreadId}");
                                    }

                                    _ = dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
                                    {
                                        try
                                        {
                                            _previewFrameCount++;
                                            FrameArrived?.Invoke(this, new FrameArrivedEventArgs(currentFrameId, capturedBmp, capturedFps, capturedMode));
                                        }
                                        finally
                                        {
                                            Interlocked.Exchange(ref _isDispatchingPreview, 0);
                                        }
                                    }));
                                }
                                else
                                {
                                    Interlocked.Exchange(ref _isDispatchingPreview, 0);
                                }
                            }
                            else
                            {
                                DroppedFrames++;
                            }
                        }
                    }
                }
                catch (Exception ex) when (!token.IsCancellationRequested)
                {
                    CaptureError?.Invoke(this, $"Capture frame error: {ex.Message}");
                    useDxgi = false;
                }

                // High-precision 60 FPS pacing loop
                long elapsedTicks = frameTimer.ElapsedTicks - startTicks;
                long remainingTicks = targetFrameTicks - elapsedTicks;
                if (remainingTicks > 0)
                {
                    double remainingMs = (remainingTicks * 1000.0) / Stopwatch.Frequency;
                    if (remainingMs > 2.0)
                    {
                        await Task.Delay((int)(remainingMs - 1.0), token);
                    }
                    while ((frameTimer.ElapsedTicks - startTicks) < targetFrameTicks)
                    {
                        Thread.SpinWait(10);
                    }
                }
            }

            wgcEngine?.Dispose();
            dxgiEngine?.Dispose();
        }

        private BitmapSource? CaptureMonitorWin32GdiPersistent(DisplayInfo display)
        {
            try
            {
                int width = Math.Max(1, display.Width);
                int height = Math.Max(1, display.Height);

                // Initialize or re-create persistent GDI handles if display properties changed
                if (_persistentHdcSrc == IntPtr.Zero || _persistentWidth != width || _persistentHeight != height || _persistentDeviceName != display.DeviceName)
                {
                    ReleasePersistentGdi();

                    if (!string.IsNullOrEmpty(display.DeviceName))
                    {
                        _persistentHdcSrc = NativeMethods.CreateDC("DISPLAY", display.DeviceName, null, IntPtr.Zero);
                    }

                    if (_persistentHdcSrc == IntPtr.Zero)
                    {
                        _persistentHdcSrc = NativeMethods.GetDC(IntPtr.Zero);
                    }

                    if (_persistentHdcSrc == IntPtr.Zero) return null;

                    _persistentHdcDest = NativeMethods.CreateCompatibleDC(_persistentHdcSrc);
                    _persistentHBitmap = NativeMethods.CreateCompatibleBitmap(_persistentHdcSrc, width, height);
                    _persistentOldBmp = NativeMethods.SelectObject(_persistentHdcDest, _persistentHBitmap);
                    _persistentDeviceName = display.DeviceName;
                    _persistentWidth = width;
                    _persistentHeight = height;
                }

                NativeMethods.BitBlt(_persistentHdcDest, 0, 0, width, height, _persistentHdcSrc, 0, 0, NativeMethods.SRCCOPY);

                var bmi = new NativeMethods.BITMAPINFO();
                bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(NativeMethods.BITMAPINFOHEADER));
                bmi.bmiHeader.biWidth = width;
                bmi.bmiHeader.biHeight = -height;
                bmi.bmiHeader.biPlanes = 1;
                bmi.bmiHeader.biBitCount = 32;
                bmi.bmiHeader.biCompression = NativeMethods.BI_RGB;

                int requiredLength = width * height * 4;
                if (_gdiPixelBuffer == null || _gdiPixelBuffer.Length != requiredLength)
                {
                    _gdiPixelBuffer = new byte[requiredLength];
                }

                NativeMethods.GetDIBits(_persistentHdcDest, _persistentHBitmap, 0, (uint)height, _gdiPixelBuffer, ref bmi, NativeMethods.DIB_RGB_COLORS);

                var writeableBmp = new WriteableBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
                writeableBmp.Lock();
                Marshal.Copy(_gdiPixelBuffer, 0, writeableBmp.BackBuffer, requiredLength);
                writeableBmp.AddDirtyRect(new Int32Rect(0, 0, width, height));
                writeableBmp.Unlock();
                writeableBmp.Freeze();

                return writeableBmp;
            }
            catch
            {
                ReleasePersistentGdi();
                return null;
            }
        }

        private void ReleasePersistentGdi()
        {
            if (_persistentHdcDest != IntPtr.Zero)
            {
                if (_persistentOldBmp != IntPtr.Zero) NativeMethods.SelectObject(_persistentHdcDest, _persistentOldBmp);
                if (_persistentHBitmap != IntPtr.Zero) NativeMethods.DeleteObject(_persistentHBitmap);
                NativeMethods.DeleteDC(_persistentHdcDest);
                _persistentHdcDest = IntPtr.Zero;
                _persistentHBitmap = IntPtr.Zero;
                _persistentOldBmp = IntPtr.Zero;
            }
            if (_persistentHdcSrc != IntPtr.Zero)
            {
                NativeMethods.DeleteDC(_persistentHdcSrc);
                NativeMethods.ReleaseDC(IntPtr.Zero, _persistentHdcSrc);
                _persistentHdcSrc = IntPtr.Zero;
            }
            _persistentDeviceName = null;
        }

        public void Dispose()
        {
            StopCapture();
        }
    }
}

