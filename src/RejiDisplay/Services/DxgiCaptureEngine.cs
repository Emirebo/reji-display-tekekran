using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RejiDisplay.Helpers;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace RejiDisplay.Services
{
    public class DxgiCaptureEngine : IDisposable
    {
        public bool IsInitialized { get; private set; }
        public string DeviceName { get; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public string InitError { get; private set; } = string.Empty;

        private ID3D11Device? _d3dDevice;
        private ID3D11DeviceContext? _d3dContext;
        private IDXGIOutputDuplication? _duplication;
        private ID3D11Texture2D? _stagingTexture;
        private WriteableBitmap? _persistentWriteableBmp;
        private ulong _lastFrameHash = 0;

        public DxgiCaptureEngine(string deviceName)
        {
            DeviceName = deviceName;
            InitializeDxgi(deviceName);
        }

        private void InitializeDxgi(string targetDeviceName)
        {
            string cleanTargetName = (targetDeviceName ?? string.Empty).Trim('\0', ' ');
            Logger.Log($"[DXGI_INIT_START] Attempting DXGI GPU Desktop Duplication for display '{cleanTargetName}'");

            try
            {
                IDXGIOutputDuplication? duplication = null;
                ID3D11Device? targetDevice = null;
                ID3D11DeviceContext? targetContext = null;

                // 1. Create D3D11 Device on Primary Hardware Adapter first to establish canonical DXGI Factory context
                var hrPrim = D3D11.D3D11CreateDevice(
                    null,
                    DriverType.Hardware,
                    DeviceCreationFlags.None,
                    Array.Empty<FeatureLevel>(),
                    out ID3D11Device? primDev,
                    out ID3D11DeviceContext? primCtx);

                if (hrPrim.Success && primDev != null && primCtx != null)
                {
                    using var dxgiDev = primDev.QueryInterfaceOrNull<IDXGIDevice>();
                    using var dxgiAdap = dxgiDev?.GetAdapter();
                    using var dxgiFactory = dxgiAdap?.GetParent<IDXGIFactory1>();

                    if (dxgiAdap != null && dxgiFactory != null)
                    {
                        for (uint o = 0; dxgiAdap.EnumOutputs(o, out IDXGIOutput? output).Success; o++)
                        {
                            if (output == null) continue;
                            string cleanName = (output.Description.DeviceName ?? string.Empty).Trim('\0', ' ');
                            int outputWidth = output.Description.DesktopCoordinates.Right - output.Description.DesktopCoordinates.Left;
                            int outputHeight = output.Description.DesktopCoordinates.Bottom - output.Description.DesktopCoordinates.Top;
                            Logger.Log($"[DxgiCaptureEngine] Primary Adapter output {o}: CleanDeviceName='{cleanName}', Bounds={outputWidth}x{outputHeight}");

                            if (string.Equals(cleanName, cleanTargetName, StringComparison.OrdinalIgnoreCase))
                            {
                                using var output1 = output.QueryInterfaceOrNull<IDXGIOutput1>();
                                if (output1 != null)
                                {
                                    try
                                    {
                                        duplication = output1.DuplicateOutput(primDev);
                                        if (duplication != null)
                                        {
                                            targetDevice = primDev;
                                            targetContext = primCtx;
                                            Width = outputWidth;
                                            Height = outputHeight;
                                            output.Dispose();
                                            Logger.Log($"[DXGI_INIT_SUCCESS] Canonical Primary Adapter DuplicateOutput succeeded for {cleanTargetName} ({Width}x{Height})");
                                            break;
                                        }
                                    }
                                    catch (Exception primEx)
                                    {
                                        Logger.Log($"[DxgiCaptureEngine] Primary adapter DuplicateOutput for {cleanTargetName} failed: {primEx.Message}");
                                    }
                                }
                            }
                            output.Dispose();
                        }
                    }

                    if (duplication == null)
                    {
                        primCtx.Dispose();
                        primDev.Dispose();
                    }
                }

                // 2. Per-adapter fallback if target display is attached to secondary GPU adapter
                if (duplication == null)
                {
                    var resultFactory = DXGI.CreateDXGIFactory1(out IDXGIFactory1? factory);
                    if (resultFactory.Success && factory != null)
                    {
                        for (uint a = 0; factory.EnumAdapters1(a, out IDXGIAdapter1? adapter).Success; a++)
                        {
                            if (adapter == null) continue;
                            string adapterDesc = adapter.Description.Description ?? $"Adapter {a}";

                            var hrDev = D3D11.D3D11CreateDevice(
                                adapter,
                                DriverType.Unknown,
                                DeviceCreationFlags.BgraSupport,
                                Array.Empty<FeatureLevel>(),
                                out ID3D11Device? dev,
                                out ID3D11DeviceContext? ctx);

                            if (hrDev.Success && dev != null && ctx != null)
                            {
                                using var dxgiDev = dev.QueryInterfaceOrNull<IDXGIDevice>();
                                using var dxgiAdapter = dxgiDev?.GetAdapter();
                                if (dxgiAdapter != null)
                                {
                                    for (uint o = 0; dxgiAdapter.EnumOutputs(o, out IDXGIOutput? output).Success; o++)
                                    {
                                        if (output == null) continue;
                                        string cleanName = (output.Description.DeviceName ?? string.Empty).Trim('\0', ' ');
                                        int outputWidth = output.Description.DesktopCoordinates.Right - output.Description.DesktopCoordinates.Left;
                                        int outputHeight = output.Description.DesktopCoordinates.Bottom - output.Description.DesktopCoordinates.Top;

                                        if (string.Equals(cleanName, cleanTargetName, StringComparison.OrdinalIgnoreCase))
                                        {
                                            using var output1 = output.QueryInterfaceOrNull<IDXGIOutput1>();
                                            if (output1 != null)
                                            {
                                                try
                                                {
                                                    duplication = output1.DuplicateOutput(dev);
                                                    if (duplication != null)
                                                    {
                                                        targetDevice = dev;
                                                        targetContext = ctx;
                                                        Width = outputWidth;
                                                        Height = outputHeight;
                                                        output.Dispose();
                                                        Logger.Log($"[DXGI_INIT_SUCCESS] Per-adapter DuplicateOutput succeeded for {cleanTargetName} on Adapter {a} ('{adapterDesc}')");
                                                        break;
                                                    }
                                                }
                                                catch (Exception ex)
                                                {
                                                    InitError = $"DuplicateOutput Exception on Adapter {a} ('{adapterDesc}'): {ex.GetType().Name} - {ex.Message}";
                                                    Logger.Log($"[DXGI_INIT_FAILURE] DuplicateOutput for {cleanTargetName} on Adapter {a} failed: {ex.Message}");
                                                }
                                            }
                                        }
                                        output.Dispose();
                                    }
                                }

                                if (duplication == null)
                                {
                                    ctx.Dispose();
                                    dev.Dispose();
                                }
                            }

                            adapter.Dispose();
                            if (duplication != null) break;
                        }
                        factory.Dispose();
                    }
                }

                if (duplication == null || targetDevice == null || targetContext == null)
                {
                    if (string.IsNullOrEmpty(InitError))
                    {
                        InitError = $"Target display '{cleanTargetName}' DXGI duplication could not be initialized.";
                    }
                    Logger.Log($"[DXGI_INIT_FAILURE] {InitError}");
                    return;
                }

                _d3dDevice = targetDevice;
                _d3dContext = targetContext;
                _duplication = duplication;

                IsInitialized = true;
                Logger.Log($"[DXGI_INIT_SUCCESS] DXGI GPU Desktop Duplication active for {cleanTargetName} ({Width}x{Height})");
            }
            catch (Exception ex)
            {
                InitError = $"{ex.GetType().Name} - {ex.Message}";
                Logger.LogError($"[DXGI_INIT_FAILURE] Exception initializing DXGI for {cleanTargetName}", ex);
                Dispose();
            }
        }

        public BitmapSource? CaptureFrame(out bool frameChanged, out double acquisitionMs, out double readbackMs)
        {
            frameChanged = false;
            acquisitionMs = 0;
            readbackMs = 0;

            if (!IsInitialized || _duplication == null || _d3dDevice == null || _d3dContext == null)
            {
                return null;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();

            var result = _duplication.AcquireNextFrame(10, out OutduplFrameInfo frameInfo, out IDXGIResource? desktopResource);
            acquisitionMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);

            if (result.Failure)
            {
                if (result.Code == Vortice.DXGI.ResultCode.WaitTimeout.Code)
                {
                    frameChanged = false;
                    return null;
                }
                if (result.Code == Vortice.DXGI.ResultCode.AccessLost.Code)
                {
                    Logger.Log($"[DxgiCaptureEngine] DXGI Access lost (0x{result.Code:X8}). Device needs re-init.");
                    IsInitialized = false;
                    return null;
                }
                return null;
            }

            try
            {
                if (frameInfo.AccumulatedFrames == 0 || desktopResource == null)
                {
                    frameChanged = false;
                    return null;
                }

                sw.Restart();

                using var desktopTexture = desktopResource.QueryInterfaceOrNull<ID3D11Texture2D>();
                if (desktopTexture == null) return null;

                var texDesc = desktopTexture.Description;
                Width = (int)texDesc.Width;
                Height = (int)texDesc.Height;

                // Create persistent staging texture if needed
                if (_stagingTexture == null)
                {
                    var stagingDesc = new Texture2DDescription
                    {
                        Width = texDesc.Width,
                        Height = texDesc.Height,
                        MipLevels = 1,
                        ArraySize = 1,
                        Format = texDesc.Format,
                        SampleDescription = new SampleDescription(1, 0),
                        Usage = ResourceUsage.Staging,
                        BindFlags = BindFlags.None,
                        CPUAccessFlags = CpuAccessFlags.Read,
                        MiscFlags = ResourceOptionFlags.None
                    };

                    _stagingTexture = _d3dDevice.CreateTexture2D(stagingDesc);
                }

                _d3dContext.CopyResource(_stagingTexture, desktopTexture);

                var mapped = _d3dContext.Map(_stagingTexture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                if (mapped.DataPointer != IntPtr.Zero)
                {
                    int requiredBytes = Width * Height * 4;

                    if (_persistentWriteableBmp == null ||
                        _persistentWriteableBmp.PixelWidth != Width ||
                        _persistentWriteableBmp.PixelHeight != Height)
                    {
                        _persistentWriteableBmp = new WriteableBitmap(Width, Height, 96, 96, PixelFormats.Bgra32, null);
                    }

                    ulong currentHash = ComputeSampleHash(mapped.DataPointer, (uint)mapped.RowPitch, Width, Height);
                    frameChanged = (currentHash != _lastFrameHash);
                    _lastFrameHash = currentHash;

                    _persistentWriteableBmp.Lock();

                    if (mapped.RowPitch == Width * 4)
                    {
                        NativeMethods.CopyMemory(_persistentWriteableBmp.BackBuffer, mapped.DataPointer, (uint)requiredBytes);
                    }
                    else
                    {
                        for (int row = 0; row < Height; row++)
                        {
                            IntPtr srcRow = mapped.DataPointer + (nint)row * (nint)mapped.RowPitch;
                            IntPtr destRow = _persistentWriteableBmp.BackBuffer + row * Width * 4;
                            NativeMethods.CopyMemory(destRow, srcRow, (uint)(Width * 4));
                        }
                    }

                    _persistentWriteableBmp.AddDirtyRect(new Int32Rect(0, 0, Width, Height));
                    _persistentWriteableBmp.Unlock();

                    _d3dContext.Unmap(_stagingTexture, 0);

                    readbackMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                    return _persistentWriteableBmp;
                }
            }
            catch (Exception ex)
            {
                InitError = $"Frame process exception: {ex.Message}";
                Logger.LogError("[DxgiCaptureEngine] Frame process exception, shutting down DXGI engine and falling back", ex);
                Dispose();
            }
            finally
            {
                try { _duplication?.ReleaseFrame(); } catch { }
            }

            return null;
        }

        private static unsafe ulong ComputeSampleHash(IntPtr pData, uint rowPitch, int width, int height)
        {
            byte* ptr = (byte*)pData;
            ulong hash = 17;
            int stepX = Math.Max(1, width / 4);
            int stepY = Math.Max(1, height / 4);

            for (int y = 0; y < height; y += stepY)
            {
                byte* row = ptr + (y * rowPitch);
                for (int x = 0; x < width; x += stepX)
                {
                    uint pixel = *(uint*)(row + x * 4);
                    hash = hash * 31 + pixel;
                }
            }
            return hash;
        }

        public void Dispose()
        {
            IsInitialized = false;
            _persistentWriteableBmp = null;
            _stagingTexture?.Dispose();
            _stagingTexture = null;
            _duplication?.Dispose();
            _duplication = null;
            _d3dContext?.Dispose();
            _d3dContext = null;
            _d3dDevice?.Dispose();
            _d3dDevice = null;
        }
    }
}
