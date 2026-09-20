using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
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

        private static readonly Guid IID_IDirect3DDxgiInterfaceAccess = new Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");

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

                var hrDev = D3D11.D3D11CreateDevice(
                    null,
                    DriverType.Hardware,
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

        public BitmapSource? CaptureFrame(out bool frameChanged, out double acquisitionMs, out double readbackMs)
        {
            frameChanged = false;
            acquisitionMs = 0;
            readbackMs = 0;

            if (!IsInitialized || _d3dDevice == null || _d3dContext == null) return _currentBitmap;

            Direct3D11CaptureFrame? frame = null;
            lock (_frameLock)
            {
                frame = _latestFrame;
                _latestFrame = null;
            }

            if (frame == null)
            {
                return _currentBitmap;
            }

            frameChanged = true;
            long frameId = _globalFrameId;
            var acqSw = Stopwatch.StartNew();

            try
            {
                using var surface = frame.Surface;
                if (surface == null) return _currentBitmap;

                IntPtr pSurfaceUnknown = IntPtr.Zero;
                try { pSurfaceUnknown = Marshal.GetIUnknownForObject(surface); } catch (Exception exUnknown) { Logger.Log($"[WgcCaptureEngine] Marshal.GetIUnknownForObject failed: {exUnknown.Message}"); }
                if (pSurfaceUnknown == IntPtr.Zero) return _currentBitmap;

                IntPtr pTexture2D = IntPtr.Zero;
                try
                {
                    Guid iidDxgiAccess = IID_IDirect3DDxgiInterfaceAccess;
                    int hrQuery = Marshal.QueryInterface(pSurfaceUnknown, ref iidDxgiAccess, out IntPtr pDxgiAccess);
                    if (hrQuery == 0 && pDxgiAccess != IntPtr.Zero)
                    {
                        try
                        {
                            IntPtr vtable = Marshal.ReadIntPtr(pDxgiAccess);
                            IntPtr pGetInterface = Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size);
                            var getInterfaceFunc = Marshal.GetDelegateForFunctionPointer<GetInterfaceDelegate>(pGetInterface);

                            Guid iidTexture2D = IID_ID3D11Texture2D;
                            int hrGet = getInterfaceFunc(pDxgiAccess, ref iidTexture2D, out pTexture2D);
                            if (hrGet != 0)
                            {
                                Logger.Log($"[WgcCaptureEngine] IDirect3DDxgiInterfaceAccess.GetInterface failed: HRESULT 0x{hrGet:X8}");
                            }
                        }
                        finally
                        {
                            Marshal.Release(pDxgiAccess);
                        }
                    }
                    else
                    {
                        Logger.Log($"[WgcCaptureEngine] QueryInterface(IDirect3DDxgiInterfaceAccess) failed: HRESULT 0x{hrQuery:X8}");
                    }
                }
                finally
                {
                    Marshal.Release(pSurfaceUnknown);
                }

                if (pTexture2D == IntPtr.Zero) return _currentBitmap;

                using var srcTexture = new ID3D11Texture2D(pTexture2D);
                Marshal.Release(pTexture2D);

                int width = (int)srcTexture.Description.Width;
                int height = (int)srcTexture.Description.Height;
                Format format = srcTexture.Description.Format;

                if (_stagingTexture == null || Width != width || Height != height)
                {
                    _stagingTexture?.Dispose();
                    Width = width;
                    Height = height;

                    var desc = new Texture2DDescription
                    {
                        Width = (uint)width,
                        Height = (uint)height,
                        MipLevels = 1,
                        ArraySize = 1,
                        Format = format,
                        SampleDescription = new SampleDescription(1, 0),
                        Usage = ResourceUsage.Staging,
                        BindFlags = BindFlags.None,
                        CPUAccessFlags = CpuAccessFlags.Read,
                        MiscFlags = ResourceOptionFlags.None
                    };

                    _stagingTexture = _d3dDevice.CreateTexture2D(desc);
                }

                _d3dContext.CopyResource(srcTexture, _stagingTexture);
                acqSw.Stop();
                acquisitionMs = Math.Round(acqSw.Elapsed.TotalMilliseconds, 2);

                var readSw = Stopwatch.StartNew();
                var mapped = _d3dContext.Map(_stagingTexture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);

                int rowBytes = width * 4;
                int totalBytes = rowBytes * height;

                if (_pixelBuffer == null || _pixelBuffer.Length < totalBytes)
                {
                    _pixelBuffer = new byte[totalBytes];
                }

                unsafe
                {
                    fixed (byte* pDest = _pixelBuffer)
                    {
                        IntPtr srcPtr = mapped.DataPointer;
                        int srcPitch = (int)mapped.RowPitch;

                        if (srcPitch == rowBytes)
                        {
                            NativeMethods.CopyMemory((IntPtr)pDest, srcPtr, (uint)totalBytes);
                        }
                        else
                        {
                            for (int y = 0; y < height; y++)
                            {
                                IntPtr rowSrc = IntPtr.Add(srcPtr, y * srcPitch);
                                IntPtr rowDest = IntPtr.Add((IntPtr)pDest, y * rowBytes);
                                NativeMethods.CopyMemory(rowDest, rowSrc, (uint)rowBytes);
                            }
                        }
                    }
                }

                _d3dContext.Unmap(_stagingTexture, 0);
                readSw.Stop();
                readbackMs = Math.Round(readSw.Elapsed.TotalMilliseconds, 2);

                var bmp = BitmapSource.Create(
                    width, height,
                    96, 96,
                    PixelFormats.Bgra32,
                    null,
                    _pixelBuffer,
                    rowBytes);
                bmp.Freeze();
                _currentBitmap = bmp;

                if (frameId <= 5)
                {
                    Logger.Log($"[FRAME_TRACE] Stage 3 (Bitmap Conversion Completed): FrameId={frameId} | Size={width}x{height} | ReadbackMs={readbackMs}ms | ThreadId={Environment.CurrentManagedThreadId}");
                }

                return _currentBitmap;
            }
            catch (Exception ex)
            {
                InitError = $"CaptureFrame exception: {ex.Message}";
                Logger.LogError("[WgcCaptureEngine] CaptureFrame exception, shutting down WGC engine and falling back", ex);
                Dispose();
                return _currentBitmap;
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
