using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using RejiDisplay.Helpers;

namespace RejiDisplay.Services
{
    public class WgcCaptureEngine : IDisposable
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

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateForMonitorDelegate(IntPtr pThis, IntPtr hMonitor, ref Guid riid, out IntPtr ppv);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetInterfaceDelegate(IntPtr pThis, ref Guid riid, out IntPtr ppv);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int QueryInterfaceDelegate(IntPtr pThis, ref Guid riid, out IntPtr ppv);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetIidsDelegate(IntPtr pThis, out uint count, out IntPtr pIids);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void SetMultithreadProtectedDelegate(IntPtr pThis, [MarshalAs(UnmanagedType.Bool)] bool bMTProtect);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void CopyResourceDelegate(IntPtr pContext, IntPtr pDstResource, IntPtr pSrcResource);

        [StructLayout(LayoutKind.Sequential)]
        private struct D3D11_TEXTURE2D_DESC
        {
            public uint Width;
            public uint Height;
            public uint MipLevels;
            public uint ArraySize;
            public uint Format;
            public uint SampleDescCount;
            public uint SampleDescQuality;
            public uint Usage;
            public uint BindFlags;
            public uint CPUAccessFlags;
            public uint MiscFlags;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GetDescDelegate(IntPtr pThis, out D3D11_TEXTURE2D_DESC pDesc);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GetDeviceDelegate(IntPtr pThis, out IntPtr ppDevice);

        [ComImport]
        [Guid("30D5A829-7FA4-4026-83BB-D75BAE4EA99E")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IDirect3DDxgiInterfaceAccess
        {
            [PreserveSig]
            int GetInterface([In] ref Guid riid, out IntPtr ppv);
        }

        [ComImport]
        [Guid("30D5A829-7FA4-4026-83BB-D75BAE4EA99E")]
        [InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
        public interface IDirect3DDxgiInterfaceAccess_Inspectable
        {
            [PreserveSig]
            int GetInterface([In] ref Guid riid, out IntPtr ppv);
        }

        [ComImport]
        [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IDirect3DDxgiInterfaceAccess_Alt_Unknown
        {
            [PreserveSig]
            int GetInterface([In] ref Guid riid, out IntPtr ppv);
        }

        [ComImport]
        [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
        [InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
        public interface IDirect3DDxgiInterfaceAccess_Alt_Inspectable
        {
            [PreserveSig]
            int GetInterface([In] ref Guid riid, out IntPtr ppv);
        }

        private static bool ValidateTexture(IntPtr pCandidate, out IntPtr pValidTexture)
        {
            pValidTexture = IntPtr.Zero;
            if (pCandidate == IntPtr.Zero) return false;

            try
            {
                Guid iidTex2D = new Guid("6F15806F-9114-4D77-967A-FD56951163B9");
                int hr = Marshal.QueryInterface(pCandidate, ref iidTex2D, out pValidTexture);
                Marshal.Release(pCandidate);
                return (hr == 0 && pValidTexture != IntPtr.Zero);
            }
            catch
            {
                try { Marshal.Release(pCandidate); } catch { }
                return false;
            }
        }

        private static readonly Guid IID_IDirect3DDxgiInterfaceAccess = new Guid("30D5A829-7FA4-4026-83BB-D75BAE4EA99E");
        private static readonly Guid IID_IDirect3DDxgiInterfaceAccess_Alt = new Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");

        [DllImport("api-ms-win-core-winrt-l1-1-0.dll")]
        private static extern int RoGetActivationFactory(IntPtr hstr, ref Guid iid, out IntPtr factory);

        [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll")]
        private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string sourceString, int length, out IntPtr hstring);

        [DllImport("api-ms-win-core-winrt-string-l1-1-0.dll")]
        private static extern int WindowsDeleteString(IntPtr hstring);

        [DllImport("api-ms-win-core-winrt-l1-1-0.dll")]
        private static extern int RoInitialize(int initType);

        private static readonly Guid IID_ID3D11Texture2D = new Guid("6f15806f-9114-4d77-967a-fd56951163b9");

        public bool IsInitialized { get; private set; }
        public string InitError { get; private set; } = string.Empty;
        public int Width { get; private set; }
        public int Height { get; private set; }
        public string TargetDeviceName { get; private set; } = string.Empty;
        public long CurrentFrameId => _globalFrameId;

        private ID3D11Device? _d3dDevice;
        private ID3D11DeviceContext? _d3dContext;
        private IDirect3DDevice? _winrtDevice;
        private Direct3D11CaptureFramePool? _framePool;
        private GraphicsCaptureSession? _session;
        private GraphicsCaptureItem? _captureItem;

        private ID3D11Texture2D? _stagingTexture;
        private byte[]? _pixelBuffer;
        private BitmapSource? _currentBitmap;

        private Direct3D11CaptureFrame? _latestFrame;
        private readonly object _frameLock = new();
        private long _globalFrameId = 0;


        public WgcCaptureEngine(string targetDeviceName)
        {
            TargetDeviceName = (targetDeviceName ?? string.Empty).Trim('\0', ' ');
            InitializeWgc(TargetDeviceName);
        }

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

        private static IDXGIAdapter1? FindAdapterForMonitor(IntPtr hMonitor)
        {
            try
            {
                if (DXGI.CreateDXGIFactory1(out IDXGIFactory1? factory).Success && factory != null)
                {
                    using (factory)
                    {
                        uint i = 0;
                        while (factory.EnumAdapters1(i, out IDXGIAdapter1? adapter).Success && adapter != null)
                        {
                            uint j = 0;
                            while (adapter.EnumOutputs(j, out IDXGIOutput? output).Success && output != null)
                            {
                                if (output.Description.Monitor == hMonitor)
                                {
                                    output.Dispose();
                                    Logger.Log($"[WGC_ADAPTER_MATCH] Found DXGI Adapter '{adapter.Description.Description}' for HMONITOR 0x{hMonitor.ToString("X")}");
                                    return adapter;
                                }
                                output.Dispose();
                                j++;
                            }
                            adapter.Dispose();
                            i++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[WGC_ADAPTER_MATCH] FindAdapterForMonitor warning: {ex.Message}");
            }
            return null;
        }

        private void InitializeWgc(string targetDeviceName)
        {
            try
            {
                if (!GraphicsCaptureSession.IsSupported())
                {
                    InitError = "Windows.Graphics.Capture is not supported on this OS version.";
                    Logger.Log($"[WgcCaptureEngine_INIT_FAIL] {InitError}");
                    return;
                }

                IntPtr hMonitor = GetMonitorHandleForDevice(targetDeviceName);
                if (hMonitor == IntPtr.Zero)
                {
                    InitError = $"HMONITOR handle for device '{targetDeviceName}' not found.";
                    Logger.Log($"[WgcCaptureEngine_INIT_FAIL] {InitError}");
                    return;
                }

                using var adapter = FindAdapterForMonitor(hMonitor);
                DriverType driverType = adapter != null ? DriverType.Unknown : DriverType.Hardware;

                var hrDev = D3D11.D3D11CreateDevice(
                    adapter,
                    driverType,
                    DeviceCreationFlags.BgraSupport,
                    new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 },
                    out _d3dDevice,
                    out _d3dContext);

                if (hrDev.Failure || _d3dDevice == null || _d3dContext == null)
                {
                    InitError = $"D3D11CreateDevice failed: HRESULT 0x{hrDev.Code:X8}";
                    Logger.Log($"[WgcCaptureEngine_INIT_FAIL] {InitError}");
                    return;
                }

                try
                {
                    Guid iidMt = new Guid("9B7E4E00-342C-4106-A19F-4F2704F689F0");
                    int hrMt = Marshal.QueryInterface(_d3dContext.NativePointer, ref iidMt, out IntPtr pMt);
                    if (hrMt == 0 && pMt != IntPtr.Zero)
                    {
                        try
                        {
                            IntPtr vt = Marshal.ReadIntPtr(pMt);
                            IntPtr pSlot5 = Marshal.ReadIntPtr(vt, 5 * IntPtr.Size);
                            var setMt = Marshal.GetDelegateForFunctionPointer<SetMultithreadProtectedDelegate>(pSlot5);
                            setMt(pMt, true);
                            Logger.Log("[WgcCaptureEngine] ID3D10Multithread protection enabled.");
                        }
                        finally
                        {
                            Marshal.Release(pMt);
                        }
                    }
                }
                catch (Exception exMt)
                {
                    Logger.Log($"[WgcCaptureEngine] SetMultithreadProtected warning: {exMt.Message}");
                }

                using var dxgiDevice = _d3dDevice.QueryInterface<IDXGIDevice>();
                int hrInterop = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out IntPtr pUnknown);
                if (hrInterop != 0 || pUnknown == IntPtr.Zero)
                {
                    InitError = $"CreateDirect3D11DeviceFromDXGIDevice failed: HRESULT 0x{hrInterop:X8}";
                    Logger.Log($"[WgcCaptureEngine_INIT_FAIL] {InitError}");
                    return;
                }

                _winrtDevice = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(pUnknown);
                Marshal.Release(pUnknown);

                _captureItem = CreateCaptureItemForMonitor(hMonitor);
                if (_captureItem == null)
                {
                    InitError = $"GraphicsCaptureItem creation failed for monitor 0x{hMonitor.ToString("X")}";
                    Logger.Log($"[WgcCaptureEngine_INIT_FAIL] {InitError}");
                    return;
                }

                Width = _captureItem.Size.Width;
                Height = _captureItem.Size.Height;
                _pixelBuffer = new byte[Width * Height * 4];

                _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                    _winrtDevice,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    2,
                    _captureItem.Size);

                _framePool.FrameArrived += OnFrameArrived;

                _session = _framePool.CreateCaptureSession(_captureItem);
                _session.IsCursorCaptureEnabled = false;
                _session.StartCapture();

                IsInitialized = true;

                var mi = new MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
                GetMonitorInfo(hMonitor, ref mi);
                int monW = mi.rcMonitor.Right - mi.rcMonitor.Left;
                int monH = mi.rcMonitor.Bottom - mi.rcMonitor.Top;

                Logger.Log($"[WGC_MONITOR_MATCH] TargetDeviceName='{targetDeviceName}' | Resolved HMONITOR=0x{hMonitor.ToString("X")} | Bounds=({mi.rcMonitor.Left},{mi.rcMonitor.Top},{monW}x{monH}) | WgcDisplayName='{_captureItem.DisplayName}' | WgcSize={Width}x{Height}");
                Logger.Log($"[WgcCaptureEngine_INIT_SUCCESS] WGC Capture initialized for '{_captureItem.DisplayName}' ({Width}x{Height})");
            }
            catch (Exception ex)
            {
                InitError = $"Exception during WGC initialization: {ex.GetType().Name} - {ex.Message}";
                Logger.LogError("[WgcCaptureEngine_INIT_FAIL]", ex);
                Dispose();
            }
        }

        private static GraphicsCaptureItem? CreateCaptureItemForMonitor(IntPtr hMonitor)
        {
            try { RoInitialize(1); } catch { }

            string runtimeClass = "Windows.Graphics.Capture.GraphicsCaptureItem";
            int hrString = WindowsCreateString(runtimeClass, runtimeClass.Length, out IntPtr hString);
            if (hrString != 0 || hString == IntPtr.Zero) return null;

            try
            {
                Guid actGuid = new Guid("00000035-0000-0000-C000-000000000046");
                int hrFactory = RoGetActivationFactory(hString, ref actGuid, out IntPtr pFactory);
                if (hrFactory != 0 || pFactory == IntPtr.Zero) return null;

                try
                {
                    Guid interopGuid = new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
                    int hrQuery = Marshal.QueryInterface(pFactory, ref interopGuid, out IntPtr pInterop);
                    if (hrQuery != 0 || pInterop == IntPtr.Zero) return null;

                    try
                    {
                        IntPtr vtable = Marshal.ReadIntPtr(pInterop);
                        IntPtr pCreateForMonitor = Marshal.ReadIntPtr(vtable, 4 * IntPtr.Size);
                        var createForMonitorFunc = Marshal.GetDelegateForFunctionPointer<CreateForMonitorDelegate>(pCreateForMonitor);

                        Guid iunkGuid = new Guid("00000000-0000-0000-C000-000000000046");
                        int hrCreate = createForMonitorFunc(pInterop, hMonitor, ref iunkGuid, out IntPtr pItem);
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

        private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            try
            {
                var frame = sender.TryGetNextFrame();
                if (frame != null)
                {
                    long currentId = Interlocked.Increment(ref _globalFrameId);
                    if (currentId <= 5)
                    {
                        Logger.Log($"[FRAME_TRACE] Stage 1 (WGC OnFrameArrived): FrameId={currentId} | ThreadId={Environment.CurrentManagedThreadId} | Timestamp={DateTime.Now:HH:mm:ss.fff}");
                        Logger.Log($"[FRAME_TRACE] Stage 2 (TryGetNextFrame Succeeded): FrameId={currentId} | ContentSize={frame.ContentSize.Width}x{frame.ContentSize.Height} | SystemRelativeTime={frame.SystemRelativeTime.Ticks}");
                    }

                    Direct3D11CaptureFrame? oldFrame;
                    lock (_frameLock)
                    {
                        oldFrame = _latestFrame;
                        _latestFrame = frame;
                    }
                    oldFrame?.Dispose();
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[WgcCaptureEngine] OnFrameArrived exception: {ex.Message}");
            }
        }

        public struct CaptureFrameResult
        {
            public BitmapSource? Bitmap;
            public bool FrameChanged;
            public double AcquisitionMs;
            public double ReadbackMs;
        }

        public async Task<CaptureFrameResult> CaptureFrameAsync()
        {
            var result = new CaptureFrameResult { Bitmap = _currentBitmap };

            if (!IsInitialized) return result;

            Direct3D11CaptureFrame? frame = null;
            lock (_frameLock)
            {
                frame = _latestFrame;
                _latestFrame = null;
            }

            if (frame == null) return result;

            result.FrameChanged = true;
            long frameId = _globalFrameId;
            var acqSw = Stopwatch.StartNew();

            try
            {
                using var surface = frame.Surface;
                if (surface == null) return result;

                if (frameId <= 5)
                {
                    Logger.Log($"[FRAME_TRACE] Stage 3 (WinRT IDirect3DSurface Obtained): FrameId={frameId}");
                }

                using var sb = await SoftwareBitmap.CreateCopyFromSurfaceAsync(surface);

                if (sb == null)
                {
                    if (frameId <= 5) Logger.Log($"[FRAME_TRACE] Stage 4 FAILED: SoftwareBitmap.CreateCopyFromSurfaceAsync returned null");
                    return result;
                }

                if (frameId <= 5)
                {
                    Logger.Log($"[FRAME_TRACE] Stage 4 (SoftwareBitmap Created): FrameId={frameId} | Size={sb.PixelWidth}x{sb.PixelHeight} | Format={sb.BitmapPixelFormat}");
                }

                acqSw.Stop();
                result.AcquisitionMs = Math.Round(acqSw.Elapsed.TotalMilliseconds, 2);

                var readSw = Stopwatch.StartNew();
                int width = sb.PixelWidth;
                int height = sb.PixelHeight;
                int rowBytes = width * 4;
                int totalBytes = rowBytes * height;

                if (_pixelBuffer == null || _pixelBuffer.Length < totalBytes)
                {
                    _pixelBuffer = new byte[totalBytes];
                }

                sb.CopyToBuffer(_pixelBuffer.AsBuffer());
                readSw.Stop();
                result.ReadbackMs = Math.Round(readSw.Elapsed.TotalMilliseconds, 2);

                if (frameId <= 5 && _pixelBuffer != null)
                {
                    int b0 = _pixelBuffer[0], g0 = _pixelBuffer[1], r0 = _pixelBuffer[2], a0 = _pixelBuffer[3];
                    int cIdx = (height / 2 * width + width / 2) * 4;
                    int bc = _pixelBuffer[cIdx], gc = _pixelBuffer[cIdx + 1], rc = _pixelBuffer[cIdx + 2], ac = _pixelBuffer[cIdx + 3];
                    int trIdx = (height / 4 * width + width * 3 / 4) * 4;
                    int btr = _pixelBuffer[trIdx], gtr = _pixelBuffer[trIdx + 1], rtr = _pixelBuffer[trIdx + 2], atr = _pixelBuffer[trIdx + 3];
                    int blIdx = (height * 3 / 4 * width + width / 4) * 4;
                    int bbl = _pixelBuffer[blIdx], gbl = _pixelBuffer[blIdx + 1], rbl = _pixelBuffer[blIdx + 2], abl = _pixelBuffer[blIdx + 3];

                    long nonZeroCount = 0;
                    for (int i = 0; i < _pixelBuffer.Length; i += 16)
                    {
                        if (_pixelBuffer[i] != 0 || _pixelBuffer[i + 1] != 0 || _pixelBuffer[i + 2] != 0)
                            nonZeroCount++;
                    }

                    Logger.Log($"[PIXEL_SAMPLE] FrameId={frameId} | Size={width}x{height} | TopLeft=(B:{b0},G:{g0},R:{r0},A:{a0}) | Center=(B:{bc},G:{gc},R:{rc},A:{ac}) | TopRight=(B:{btr},G:{gtr},R:{rtr},A:{atr}) | BottomLeft=(B:{bbl},G:{gbl},R:{rbl},A:{abl}) | SampledNonZeroRGBPixels={nonZeroCount}");
                }

                var bmp = BitmapSource.Create(
                    width, height,
                    96, 96,
                    PixelFormats.Bgra32,
                    null,
                    _pixelBuffer,
                    rowBytes);
                bmp.Freeze();
                _currentBitmap = bmp;
                result.Bitmap = bmp;

                if (frameId <= 5)
                {
                    Logger.Log($"[FRAME_TRACE] Stage 6 (Texture -> Bitmap Conversion Completed): FrameId={frameId} | Size={width}x{height} | ReadbackMs={result.ReadbackMs}ms | ThreadId={Environment.CurrentManagedThreadId}");
                }

                return result;
            }
            catch (Exception ex)
            {
                InitError = $"CaptureFrame exception: {ex.Message}";
                Logger.LogError("[WgcCaptureEngine] CaptureFrame exception, shutting down WGC engine and falling back", ex);
                Dispose();
                return result;
            }
            finally
            {
                frame?.Dispose();
            }
        }

        public void Dispose()
        {
            IsInitialized = false;
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
            _stagingTexture?.Dispose();
            _stagingTexture = null;
            _d3dContext?.Dispose();
            _d3dContext = null;
            _d3dDevice?.Dispose();
            _d3dDevice = null;
            _captureItem = null;

            lock (_frameLock)
            {
                _latestFrame?.Dispose();
                _latestFrame = null;
            }

            Logger.Log("[WgcCaptureEngine] Disposed cleanly.");
        }
    }
}
