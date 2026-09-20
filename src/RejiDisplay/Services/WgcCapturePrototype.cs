using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using RejiDisplay.Helpers;

namespace RejiDisplay.Services
{
    public class WgcCapturePrototype : IDisposable
    {
        [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", SetLastError = true, ExactSpelling = true)]
        private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        public bool IsRunning { get; private set; }
        public int CapturedFrameCount => _frameCount;
        public double MeasuredFps { get; private set; }
        public double AverageLatencyMs { get; private set; }
        public string TargetDisplayDevice { get; private set; } = string.Empty;
        public string StatusMessage { get; private set; } = string.Empty;

        private ID3D11Device? _d3dDevice;
        private ID3D11DeviceContext? _d3dContext;
        private IDirect3DDevice? _winrtDevice;
        private Direct3D11CaptureFramePool? _framePool;
        private GraphicsCaptureSession? _session;
        private GraphicsCaptureItem? _captureItem;

        private int _frameCount;
        private double _totalLatencyMs;
        private System.Diagnostics.Stopwatch? _fpsTimer;

        public static IntPtr GetMonitorHandleForDevice(string targetDeviceName)
        {
            string cleanTarget = (targetDeviceName ?? string.Empty).Trim('\0', ' ');
            IntPtr foundHandle = IntPtr.Zero;

            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr data) =>
            {
                var mi = new MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
                if (GetMonitorInfo(hMon, ref mi))
                {
                    string cleanDevice = (mi.szDevice ?? string.Empty).Trim('\0', ' ');
                    if (string.Equals(cleanDevice, cleanTarget, StringComparison.OrdinalIgnoreCase))
                    {
                        foundHandle = hMon;
                        return false;
                    }
                }
                return true;
            }, IntPtr.Zero);

            return foundHandle;
        }

        public bool StartTest(string targetDeviceName)
        {
            TargetDisplayDevice = (targetDeviceName ?? string.Empty).Trim('\0', ' ');
            Logger.Log($"[WGC_PROTOTYPE] Starting isolated WGC feasibility test for target display '{TargetDisplayDevice}'");

            try
            {
                if (!GraphicsCaptureSession.IsSupported())
                {
                    StatusMessage = "Windows.Graphics.Capture is not supported on this OS version.";
                    Logger.Log($"[WGC_PROTOTYPE_FAILURE] {StatusMessage}");
                    return false;
                }

                IntPtr hMonitor = GetMonitorHandleForDevice(TargetDisplayDevice);
                if (hMonitor == IntPtr.Zero)
                {
                    StatusMessage = $"Monitor handle (HMONITOR) for '{TargetDisplayDevice}' could not be resolved.";
                    Logger.Log($"[WGC_PROTOTYPE_FAILURE] {StatusMessage}");
                    return false;
                }

                Logger.Log($"[WGC_PROTOTYPE] Resolved HMONITOR 0x{hMonitor.ToString("X")}");

                var hrDev = D3D11.D3D11CreateDevice(
                    null,
                    DriverType.Hardware,
                    DeviceCreationFlags.BgraSupport,
                    new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 },
                    out _d3dDevice,
                    out _d3dContext);

                if (hrDev.Failure || _d3dDevice == null || _d3dContext == null)
                {
                    StatusMessage = $"D3D11CreateDevice failed: HRESULT 0x{hrDev.Code:X8}";
                    Logger.Log($"[WGC_PROTOTYPE_FAILURE] {StatusMessage}");
                    return false;
                }

                using var dxgiDevice = _d3dDevice.QueryInterface<IDXGIDevice>();
                int hrInterop = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out IntPtr pUnknown);
                if (hrInterop != 0 || pUnknown == IntPtr.Zero)
                {
                    StatusMessage = $"CreateDirect3D11DeviceFromDXGIDevice failed: HRESULT 0x{hrInterop:X8}";
                    Logger.Log($"[WGC_PROTOTYPE_FAILURE] {StatusMessage}");
                    return false;
                }

                _winrtDevice = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(pUnknown);
                Marshal.Release(pUnknown);

                _captureItem = CreateCaptureItemForMonitor(hMonitor);
                if (_captureItem == null)
                {
                    StatusMessage = $"GraphicsCaptureItem creation failed for HMONITOR 0x{hMonitor.ToString("X")}";
                    Logger.Log($"[WGC_PROTOTYPE_FAILURE] {StatusMessage}");
                    return false;
                }

                Logger.Log($"[WGC_PROTOTYPE] Created GraphicsCaptureItem: Name='{_captureItem.DisplayName}', Size={_captureItem.Size.Width}x{_captureItem.Size.Height}");

                _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                    _winrtDevice,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    2,
                    _captureItem.Size);

                _framePool.FrameArrived += OnFrameArrived;

                _session = _framePool.CreateCaptureSession(_captureItem);
                _session.IsCursorCaptureEnabled = false;

                _frameCount = 0;
                _totalLatencyMs = 0;
                _fpsTimer = System.Diagnostics.Stopwatch.StartNew();

                _session.StartCapture();
                IsRunning = true;

                StatusMessage = $"WGC Capture Active for {_captureItem.DisplayName} ({_captureItem.Size.Width}x{_captureItem.Size.Height})";
                Logger.Log($"[WGC_PROTOTYPE_SUCCESS] {StatusMessage}");

                return true;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Exception in WGC prototype initialization: {ex.GetType().Name} - {ex.Message}";
                Logger.LogError("[WGC_PROTOTYPE_FAILURE] Exception", ex);
                Stop();
                return false;
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateForMonitorDelegate(IntPtr pThis, IntPtr hMonitor, ref Guid riid, out IntPtr ppv);

        [DllImport("api-ms-win-core-winrt-l1-1-0.dll")]
        private static extern int RoGetActivationFactory(IntPtr hstr, ref Guid iid, out IntPtr factory);

        [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll")]
        private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string sourceString, int length, out IntPtr hstring);

        [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll")]
        private static extern int WindowsDeleteString(IntPtr hstring);

        [DllImport("api-ms-win-core-winrt-l1-1-0.dll")]
        private static extern int RoInitialize(int initType);

        private static GraphicsCaptureItem? CreateCaptureItemForMonitor(IntPtr hMonitor)
        {
            try
            {
                RoInitialize(1); // RO_INIT_MULTITHREADED
            }
            catch { }

            string runtimeClass = "Windows.Graphics.Capture.GraphicsCaptureItem";
            int hrString = WindowsCreateString(runtimeClass, runtimeClass.Length, out IntPtr hString);
            if (hrString != 0 || hString == IntPtr.Zero)
            {
                Logger.Log($"[WGC_INTEROP] WindowsCreateString failed with HRESULT 0x{hrString:X8}");
                return null;
            }

            try
            {
                Guid actGuid = new Guid("00000035-0000-0000-C000-000000000046");
                int hrFactory = RoGetActivationFactory(hString, ref actGuid, out IntPtr pFactory);
                if (hrFactory != 0 || pFactory == IntPtr.Zero)
                {
                    Logger.Log($"[WGC_INTEROP] RoGetActivationFactory failed with HRESULT 0x{hrFactory:X8}");
                    return null;
                }

                try
                {
                    Guid interopGuid = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
                    int hrQuery = Marshal.QueryInterface(pFactory, ref interopGuid, out IntPtr pInterop);
                    Logger.Log($"[WGC_INTEROP] QueryInterface(IGraphicsCaptureItemInterop) returned HRESULT 0x{hrQuery:X8}, pInterop=0x{pInterop.ToString("X")}");

                    if (hrQuery != 0 || pInterop == IntPtr.Zero) return null;

                    try
                    {
                        IntPtr vtable = Marshal.ReadIntPtr(pInterop);
                        IntPtr pCreateForMonitor = Marshal.ReadIntPtr(vtable, 4 * IntPtr.Size);
                        var createForMonitorFunc = Marshal.GetDelegateForFunctionPointer<CreateForMonitorDelegate>(pCreateForMonitor);

                        Guid iunkGuid = new Guid("00000000-0000-0000-C000-000000000046");
                        int hrCreate = createForMonitorFunc(pInterop, hMonitor, ref iunkGuid, out IntPtr pItem);
                        Logger.Log($"[WGC_INTEROP] VTable CreateForMonitor returned HRESULT 0x{hrCreate:X8}, pItem=0x{pItem.ToString("X")}");

                        if (hrCreate != 0 || pItem == IntPtr.Zero) return null;

                        try
                        {
                            return WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(pItem);
                        }
                        finally
                        {
                            Marshal.Release(pItem);
                        }
                    }
                    finally
                    {
                        Marshal.Release(pInterop);
                    }
                }
                catch (Exception exInterop)
                {
                    Logger.LogError("[WGC_INTEROP] Exception in VTable CreateForMonitor dispatch", exInterop);
                    return null;
                }
                finally
                {
                    Marshal.Release(pFactory);
                }
            }
            finally
            {
                WindowsDeleteString(hString);
            }
        }

        public bool TryPollFrame(out double latencyMs)
        {
            latencyMs = 0;
            if (_framePool == null) return false;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var frame = _framePool.TryGetNextFrame();
            sw.Stop();
            if (frame != null)
            {
                _frameCount++;
                latencyMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                _totalLatencyMs += latencyMs;
                return true;
            }
            return false;
        }

        private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            var frameSw = System.Diagnostics.Stopwatch.StartNew();
            using var frame = sender.TryGetNextFrame();
            frameSw.Stop();

            if (frame != null)
            {
                _frameCount++;
                _totalLatencyMs += frameSw.Elapsed.TotalMilliseconds;

                if (_fpsTimer != null && _fpsTimer.Elapsed.TotalSeconds >= 1.0)
                {
                    MeasuredFps = Math.Round(_frameCount / _fpsTimer.Elapsed.TotalSeconds, 1);
                    AverageLatencyMs = Math.Round(_totalLatencyMs / Math.Max(1, _frameCount), 2);
                }
            }
        }

        public void Stop()
        {
            IsRunning = false;
            if (_framePool != null)
            {
                try { _framePool.FrameArrived -= OnFrameArrived; } catch { }
            }
            try { _session?.Dispose(); } catch { }
            _session = null;
            try { _framePool?.Dispose(); } catch { }
            _framePool = null;
            try { _winrtDevice?.Dispose(); } catch { }
            _winrtDevice = null;
            _d3dContext?.Dispose();
            _d3dContext = null;
            _d3dDevice?.Dispose();
            _d3dDevice = null;
            _captureItem = null;

            if (_fpsTimer != null)
            {
                _fpsTimer.Stop();
                if (_fpsTimer.Elapsed.TotalSeconds > 0)
                {
                    MeasuredFps = Math.Round(_frameCount / _fpsTimer.Elapsed.TotalSeconds, 1);
                    AverageLatencyMs = Math.Round(_totalLatencyMs / Math.Max(1, _frameCount), 2);
                }
            }

            Logger.Log($"[WGC_PROTOTYPE] Capture stopped. Total Frames: {_frameCount}, Measured FPS: {MeasuredFps}, Avg Latency: {AverageLatencyMs} ms");
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
