using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RejiDisplay.Helpers;

namespace RejiDisplay.Services
{
    public class DxgiCaptureEngine : IDisposable
    {
        public bool IsInitialized { get; private set; }
        public string DeviceName { get; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public string InitError { get; private set; } = string.Empty;

        private IntPtr _d3dDevice = IntPtr.Zero;
        private IntPtr _d3dContext = IntPtr.Zero;
        private IDXGIOutputDuplication? _duplication;
        private ID3D11Texture2D? _stagingTexture;

        public DxgiCaptureEngine(string deviceName)
        {
            DeviceName = deviceName;
            InitializeDxgi(deviceName);
        }

        private void InitializeDxgi(string targetDeviceName)
        {
            try
            {
                int hr = NativeDxgi.D3D11CreateDevice(
                    IntPtr.Zero,
                    1, // D3D_DRIVER_TYPE_HARDWARE
                    IntPtr.Zero,
                    0, // D3D11_CREATE_DEVICE_FLAG
                    IntPtr.Zero,
                    0,
                    7, // D3D11_SDK_VERSION
                    out _d3dDevice,
                    out _,
                    out _d3dContext);

                if (hr != 0 || _d3dDevice == IntPtr.Zero)
                {
                    InitError = $"D3D11CreateDevice failed with HRESULT 0x{hr:X8}";
                    Logger.Log($"[DxgiCaptureEngine] {InitError}");
                    return;
                }

                Guid factoryGuid = typeof(IDXGIFactory1).GUID;
                hr = NativeDxgi.CreateDXGIFactory1(ref factoryGuid, out IntPtr factoryPtr);
                if (hr != 0 || factoryPtr == IntPtr.Zero)
                {
                    InitError = $"CreateDXGIFactory1 failed with HRESULT 0x{hr:X8}";
                    Logger.Log($"[DxgiCaptureEngine] {InitError}");
                    return;
                }

                var factory = (IDXGIFactory1)Marshal.GetObjectForIUnknown(factoryPtr);
                Marshal.Release(factoryPtr);

                IDXGIOutput1? matchedOutput = null;

                uint adapterIndex = 0;
                while (factory.EnumAdapters1(adapterIndex, out IntPtr adapterPtr) == 0 && adapterPtr != IntPtr.Zero)
                {
                    var adapter = (IDXGIAdapter1)Marshal.GetObjectForIUnknown(adapterPtr);
                    uint outputIndex = 0;

                    while (adapter.EnumOutputs(outputIndex, out IntPtr outputPtr) == 0 && outputPtr != IntPtr.Zero)
                    {
                        var output = (IDXGIOutput)Marshal.GetObjectForIUnknown(outputPtr);
                        output.GetDesc(out DXGI_OUTPUT_DESC desc);

                        if (!string.IsNullOrEmpty(desc.DeviceName) &&
                            string.Equals(desc.DeviceName, targetDeviceName, StringComparison.OrdinalIgnoreCase))
                        {
                            var output1 = (IDXGIOutput1)Marshal.GetObjectForIUnknown(outputPtr);
                            matchedOutput = output1;
                            Width = desc.DesktopCoordinates.Width;
                            Height = desc.DesktopCoordinates.Height;
                            Marshal.ReleaseComObject(output);
                            Marshal.ReleaseComObject(adapter);
                            break;
                        }
                        Marshal.ReleaseComObject(output);
                        outputIndex++;
                    }

                    if (matchedOutput != null) break;
                    Marshal.ReleaseComObject(adapter);
                    adapterIndex++;
                }

                if (matchedOutput == null)
                {
                    InitError = $"Target display '{targetDeviceName}' not found in DXGI adapter enum.";
                    Logger.Log($"[DxgiCaptureEngine] {InitError}");
                    return;
                }

                hr = matchedOutput.DuplicateOutput(_d3dDevice, out _duplication);
                Marshal.ReleaseComObject(matchedOutput);

                if (hr != 0 || _duplication == null)
                {
                    InitError = $"DuplicateOutput failed for {targetDeviceName} with HRESULT 0x{hr:X8}";
                    Logger.Log($"[DxgiCaptureEngine] {InitError}");
                    return;
                }

                IsInitialized = true;
                Logger.Log($"[DxgiCaptureEngine] DXGI GPU Capture initialized successfully for {targetDeviceName} ({Width}x{Height})");
            }
            catch (Exception ex)
            {
                InitError = ex.Message;
                Logger.LogError($"[DxgiCaptureEngine] Exception during initialization for {targetDeviceName}", ex);
                Dispose();
            }
        }

        private WriteableBitmap? _persistentWriteableBmp;
        private ulong _lastFrameHash = 0;

        public BitmapSource? CaptureFrame(out bool frameChanged, out double acquisitionMs, out double readbackMs)
        {
            frameChanged = false;
            acquisitionMs = 0;
            readbackMs = 0;

            if (!IsInitialized || _duplication == null || _d3dDevice == IntPtr.Zero || _d3dContext == IntPtr.Zero)
            {
                return null;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();

            int hr = _duplication.AcquireNextFrame(10, out DXGI_OUTDUPL_FRAME_INFO frameInfo, out IntPtr desktopResourcePtr);
            acquisitionMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);

            if (hr != 0)
            {
                // DXGI_ERROR_WAIT_TIMEOUT (0x887A0027) = No new frame presented yet
                if ((uint)hr == 0x887A0027)
                {
                    frameChanged = false;
                    return null;
                }
                if ((uint)hr == 0x887A0026) // DXGI_ERROR_ACCESS_LOST
                {
                    Logger.Log($"[DxgiCaptureEngine] DXGI Access lost (0x{hr:X8}). Device needs re-init.");
                    IsInitialized = false;
                    return null;
                }
                return null;
            }

            try
            {
                if (frameInfo.AccumulatedFrames == 0 || desktopResourcePtr == IntPtr.Zero)
                {
                    frameChanged = false;
                    return null;
                }

                sw.Restart();

                var desktopTexture = (ID3D11Texture2D)Marshal.GetObjectForIUnknown(desktopResourcePtr);
                desktopTexture.GetDesc(out D3D11_TEXTURE2D_DESC texDesc);

                Width = (int)texDesc.Width;
                Height = (int)texDesc.Height;

                // Create persistent staging texture if needed
                if (_stagingTexture == null)
                {
                    var stagingDesc = new D3D11_TEXTURE2D_DESC
                    {
                        Width = texDesc.Width,
                        Height = texDesc.Height,
                        MipLevels = 1,
                        ArraySize = 1,
                        Format = texDesc.Format,
                        SampleDesc = new DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
                        Usage = 3, // D3D11_USAGE_STAGING
                        BindFlags = 0,
                        CPUAccessFlags = 0x20000, // D3D11_CPU_ACCESS_READ
                        MiscFlags = 0
                    };

                    var d3dDeviceObj = (ID3D11Device)Marshal.GetObjectForIUnknown(_d3dDevice);
                    hr = d3dDeviceObj.CreateTexture2D(ref stagingDesc, IntPtr.Zero, out _stagingTexture);
                    Marshal.ReleaseComObject(d3dDeviceObj);

                    if (hr != 0 || _stagingTexture == null)
                    {
                        Logger.Log($"[DxgiCaptureEngine] Failed to create staging texture HRESULT 0x{hr:X8}");
                        return null;
                    }
                }

                var contextObj = (ID3D11DeviceContext)Marshal.GetObjectForIUnknown(_d3dContext);
                contextObj.CopyResource(_stagingTexture, desktopTexture);
                Marshal.ReleaseComObject(desktopTexture);

                int hrMap = contextObj.Map(_stagingTexture, 0, 1 /* D3D11_MAP_READ */, 0, out D3D11_MAPPED_SUBRESOURCE mapped);
                if (hrMap == 0 && mapped.pData != IntPtr.Zero)
                {
                    int requiredBytes = Width * Height * 4;

                    // Ensure persistent WriteableBitmap matches dimensions
                    if (_persistentWriteableBmp == null ||
                        _persistentWriteableBmp.PixelWidth != Width ||
                        _persistentWriteableBmp.PixelHeight != Height)
                    {
                        _persistentWriteableBmp = new WriteableBitmap(Width, Height, 96, 96, PixelFormats.Bgra32, null);
                    }

                    // Fast sample hash check across 16 sampling points to detect unique frame changes
                    ulong currentHash = ComputeSampleHash(mapped.pData, mapped.RowPitch, Width, Height);
                    frameChanged = (currentHash != _lastFrameHash);
                    _lastFrameHash = currentHash;

                    // Direct zero-copy mapping into persistent WriteableBitmap BackBuffer
                    _persistentWriteableBmp.Lock();

                    if (mapped.RowPitch == Width * 4)
                    {
                        NativeMethods.CopyMemory(_persistentWriteableBmp.BackBuffer, mapped.pData, (uint)requiredBytes);
                    }
                    else
                    {
                        for (int row = 0; row < Height; row++)
                        {
                            IntPtr srcRow = mapped.pData + row * (int)mapped.RowPitch;
                            IntPtr destRow = _persistentWriteableBmp.BackBuffer + row * Width * 4;
                            NativeMethods.CopyMemory(destRow, srcRow, (uint)(Width * 4));
                        }
                    }

                    _persistentWriteableBmp.AddDirtyRect(new Int32Rect(0, 0, Width, Height));
                    _persistentWriteableBmp.Unlock();

                    contextObj.Unmap(_stagingTexture, 0);
                    Marshal.ReleaseComObject(contextObj);

                    readbackMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                    return _persistentWriteableBmp;
                }
                else
                {
                    Marshal.ReleaseComObject(contextObj);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("[DxgiCaptureEngine] Frame process error", ex);
            }
            finally
            {
                _duplication.ReleaseFrame();
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
            if (_stagingTexture != null)
            {
                Marshal.ReleaseComObject(_stagingTexture);
                _stagingTexture = null;
            }
            if (_duplication != null)
            {
                Marshal.ReleaseComObject(_duplication);
                _duplication = null;
            }
            if (_d3dContext != IntPtr.Zero)
            {
                Marshal.Release(_d3dContext);
                _d3dContext = IntPtr.Zero;
            }
            if (_d3dDevice != IntPtr.Zero)
            {
                Marshal.Release(_d3dDevice);
                _d3dDevice = IntPtr.Zero;
            }
        }
    }

    internal static class NativeDxgi
    {
        [DllImport("d3d11.dll", EntryPoint = "D3D11CreateDevice", SetLastError = true, CallingConvention = CallingConvention.StdCall)]
        public static extern int D3D11CreateDevice(
            IntPtr pAdapter,
            int driverType,
            IntPtr Software,
            uint flags,
            IntPtr pFeatureLevels,
            uint FeatureLevels,
            uint SDKVersion,
            out IntPtr ppDevice,
            out int pFeatureLevel,
            out IntPtr ppImmediateContext);

        [DllImport("dxgi.dll", EntryPoint = "CreateDXGIFactory1", SetLastError = true)]
        public static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr ppFactory);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DXGI_OUTPUT_DESC
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        public NativeMethods.RECT DesktopCoordinates;
        public bool AttachedToDesktop;
        public int Rotation;
        public IntPtr Monitor;
    }

    [ComImport]
    [Guid("770aae78-f26f-4dba-a829-253c83d1b387")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDXGIFactory1
    {
        [PreserveSig] int SetPrivateData(ref Guid Name, uint DataSize, IntPtr pData);
        [PreserveSig] int SetPrivateDataInterface(ref Guid Name, IntPtr pUnknown);
        [PreserveSig] int GetPrivateData(ref Guid Name, ref uint pDataSize, IntPtr pData);
        [PreserveSig] int GetParent(ref Guid riid, out IntPtr ppParent);
        [PreserveSig] int EnumAdapters(uint Adapter, out IntPtr ppAdapter);
        [PreserveSig] int MakeWindowAssociation(IntPtr WindowHandle, uint Flags);
        [PreserveSig] int GetWindowAssociation(out IntPtr pWindowHandle);
        [PreserveSig] int CreateSwapChain(IntPtr pDevice, IntPtr pDesc, out IntPtr ppSwapChain);
        [PreserveSig] int CreateSoftwareAdapter(IntPtr Module, out IntPtr ppAdapter);
        [PreserveSig] int EnumAdapters1(uint Adapter, out IntPtr ppAdapter);
        [PreserveSig] bool IsCurrent();
    }

    [ComImport]
    [Guid("0a862446-a090-46c3-8074-f98b181d059f")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDXGIAdapter1
    {
        [PreserveSig] int SetPrivateData(ref Guid Name, uint DataSize, IntPtr pData);
        [PreserveSig] int SetPrivateDataInterface(ref Guid Name, IntPtr pUnknown);
        [PreserveSig] int GetPrivateData(ref Guid Name, ref uint pDataSize, IntPtr pData);
        [PreserveSig] int GetParent(ref Guid riid, out IntPtr ppParent);
        [PreserveSig] int EnumOutputs(uint Output, out IntPtr ppOutput);
        [PreserveSig] int GetDesc(IntPtr pDesc);
        [PreserveSig] int CheckInterfaceSupport(ref Guid InterfaceName, out long pUMDVersion);
        [PreserveSig] int GetDesc1(IntPtr pDesc);
    }

    [ComImport]
    [Guid("ae0ebe64-708f-4ed0-b44f-5ce353580556")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDXGIOutput
    {
        [PreserveSig] int SetPrivateData(ref Guid Name, uint DataSize, IntPtr pData);
        [PreserveSig] int SetPrivateDataInterface(ref Guid Name, IntPtr pUnknown);
        [PreserveSig] int GetPrivateData(ref Guid Name, ref uint pDataSize, IntPtr pData);
        [PreserveSig] int GetParent(ref Guid riid, out IntPtr ppParent);
        [PreserveSig] int GetDesc(out DXGI_OUTPUT_DESC pDesc);
    }

    [ComImport]
    [Guid("00cd6892-0b42-4b86-8b53-000267493a7f")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDXGIOutput1
    {
        [PreserveSig] int SetPrivateData(ref Guid Name, uint DataSize, IntPtr pData);
        [PreserveSig] int SetPrivateDataInterface(ref Guid Name, IntPtr pUnknown);
        [PreserveSig] int GetPrivateData(ref Guid Name, ref uint pDataSize, IntPtr pData);
        [PreserveSig] int GetParent(ref Guid riid, out IntPtr ppParent);
        [PreserveSig] int GetDevice(ref Guid riid, out IntPtr ppDevice);
        [PreserveSig] int GetDisplayModeList(uint EnumFormat, uint Flags, ref uint pNumModes, IntPtr pDesc);
        [PreserveSig] int FindClosestMatchingMode(IntPtr pModeToMatch, out IntPtr pClosestMatch, IntPtr pConcernToFind);
        [PreserveSig] int WaitForVBlank();
        [PreserveSig] int TakeOwnership(IntPtr pDevice, bool bExclusive);
        [PreserveSig] void ReleaseOwnership();
        [PreserveSig] int GetGammaControlCapabilities(IntPtr pGammaCaps);
        [PreserveSig] int SetGammaControl(IntPtr pArray);
        [PreserveSig] int GetGammaControl(IntPtr pArray);
        [PreserveSig] int SetDisplaySurface(IntPtr pScanoutSurface);
        [PreserveSig] int GetDisplaySurfaceData(IntPtr pDestination);
        [PreserveSig] int GetFrameStatistics(IntPtr pStats);
        [PreserveSig] int GetDisplayModeList1(uint EnumFormat, uint Flags, ref uint pNumModes, IntPtr pDesc);
        [PreserveSig] int FindClosestMatchingMode1(IntPtr pModeToMatch, out IntPtr pClosestMatch, IntPtr pConcernToFind);
        [PreserveSig] int GetDisplaySurfaceData1(IntPtr pDestination);
        [PreserveSig] int DuplicateOutput(IntPtr pDevice, out IDXGIOutputDuplication ppOutputDuplication);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_SAMPLE_DESC
    {
        public uint Count;
        public uint Quality;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_TEXTURE2D_DESC
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public int Format;
        public DXGI_SAMPLE_DESC SampleDesc;
        public int Usage;
        public uint BindFlags;
        public uint CPUAccessFlags;
        public uint MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_MAPPED_SUBRESOURCE
    {
        public IntPtr pData;
        public uint RowPitch;
        public uint DepthPitch;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_MODE_DESC
    {
        public uint Width;
        public uint Height;
        public DXGI_RATIONAL RefreshRate;
        public int Format;
        public int ScanlineOrdering;
        public int Scaling;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_OUTDUPL_DESC
    {
        public DXGI_MODE_DESC ModeDesc;
        public int Rotation;
        public bool DesktopImageInSystemMemory;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_OUTDUPL_POINTER_POSITION
    {
        public NativeMethods.POINT Position;
        public bool Visible;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_OUTDUPL_FRAME_INFO
    {
        public long LastPresentTime;
        public long LastMouseUpdateTime;
        public uint AccumulatedFrames;
        public bool RectsCoalesced;
        public bool ProtectedContentMasked;
        public DXGI_OUTDUPL_POINTER_POSITION PointerPosition;
        public uint TotalMetadataBufferSize;
        public uint PointerShapeBufferSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_MAPPED_RECT
    {
        public int Pitch;
        public IntPtr pBits;
    }

    [ComImport]
    [Guid("191cf12c-02e5-4706-96e0-2e8f17e089d7")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDXGIOutputDuplication
    {
        [PreserveSig] int SetPrivateData(ref Guid Name, uint DataSize, IntPtr pData);
        [PreserveSig] int SetPrivateDataInterface(ref Guid Name, IntPtr pUnknown);
        [PreserveSig] int GetPrivateData(ref Guid Name, ref uint pDataSize, IntPtr pData);
        [PreserveSig] int GetParent(ref Guid riid, out IntPtr ppParent);
        [PreserveSig] void GetDesc(out DXGI_OUTDUPL_DESC pDesc);
        [PreserveSig] int AcquireNextFrame(uint TimeoutInMilliseconds, out DXGI_OUTDUPL_FRAME_INFO pFrameInfo, out IntPtr ppDesktopResource);
        [PreserveSig] int GetFrameDirtyRects(uint BufferSizeInBytes, IntPtr pDirtyRectsBuffer, out uint pDirtyRectsBufferSizeRequired);
        [PreserveSig] int GetFrameMoveRects(uint BufferSizeInBytes, IntPtr pMoveRectsBuffer, out uint pMoveRectsBufferSizeRequired);
        [PreserveSig] int PointerPosition(uint BufferSizeInBytes, IntPtr pPointerPositionBuffer, out uint pPointerPositionBufferSizeRequired);
        [PreserveSig] int MapDesktopSurface(out DXGI_MAPPED_RECT pLockedRect);
        [PreserveSig] int UnmapDesktopSurface();
        [PreserveSig] int ReleaseFrame();
    }

    [ComImport]
    [Guid("db6f6ddb-ac77-4e88-8253-819df96f140c")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ID3D11Device
    {
        [PreserveSig] int CreateBuffer(IntPtr pDesc, IntPtr pInitialData, out IntPtr ppBuffer);
        [PreserveSig] int CreateTexture1D(IntPtr pDesc, IntPtr pInitialData, out IntPtr ppTexture1D);
        [PreserveSig] int CreateTexture2D(ref D3D11_TEXTURE2D_DESC pDesc, IntPtr pInitialData, out ID3D11Texture2D ppTexture2D);
    }

    [ComImport]
    [Guid("6f158970-d25c-4a39-a2d9-9529ce31090d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ID3D11Texture2D
    {
        [PreserveSig] void GetDevice(out ID3D11Device ppDevice);
        [PreserveSig] int GetPrivateData(ref Guid guid, ref uint pDataSize, IntPtr pData);
        [PreserveSig] int SetPrivateData(ref Guid guid, uint DataSize, IntPtr pData);
        [PreserveSig] int SetPrivateDataInterface(ref Guid guid, IntPtr pData);
        [PreserveSig] void GetType(out int pResourceDimension);
        [PreserveSig] void SetEvictionPriority(uint EvictionPriority);
        [PreserveSig] uint GetEvictionPriority();
        [PreserveSig] void GetDesc(out D3D11_TEXTURE2D_DESC pDesc);
    }

    [ComImport]
    [Guid("c086eddc-451e-4703-8a99-9e2304f36baf")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ID3D11DeviceContext
    {
        [PreserveSig] void VSSetConstantBuffers(uint StartSlot, uint NumBuffers, IntPtr ppConstantBuffers);
        [PreserveSig] void PSSetShaderResources(uint StartSlot, uint NumViews, IntPtr ppShaderResourceViews);
        [PreserveSig] void PSSetShader(IntPtr pPixelShader, IntPtr ppClassInstances, uint NumClassInstances);
        [PreserveSig] void PSSetSamplers(uint StartSlot, uint NumSamplers, IntPtr ppSamplers);
        [PreserveSig] void VSSetShader(IntPtr pVertexShader, IntPtr ppClassInstances, uint NumClassInstances);
        [PreserveSig] void DrawIndexed(uint IndexCount, uint StartIndexLocation, int BaseVertexLocation);
        [PreserveSig] void Draw(uint VertexCount, uint StartVertexLocation);
        [PreserveSig] int Map(ID3D11Texture2D pResource, uint Subresource, uint MapType, uint MapFlags, out D3D11_MAPPED_SUBRESOURCE pMappedResource);
        [PreserveSig] void Unmap(ID3D11Texture2D pResource, uint Subresource);
        [PreserveSig] void PSSetConstantBuffers(uint StartSlot, uint NumBuffers, IntPtr ppConstantBuffers);
        [PreserveSig] void IASetInputLayout(IntPtr pInputLayout);
        [PreserveSig] void IASetVertexBuffers(uint StartSlot, uint NumBuffers, IntPtr ppVertexBuffers, IntPtr pStrides, IntPtr pOffsets);
        [PreserveSig] void IASetIndexBuffer(IntPtr pIndexBuffer, uint Format, uint Offset);
        [PreserveSig] void DrawIndexedInstanced(uint IndexCountPerInstance, uint InstanceCount, uint StartIndexLocation, int BaseVertexLocation, uint StartInstanceLocation);
        [PreserveSig] void DrawInstanced(uint VertexCountPerInstance, uint InstanceCount, uint StartVertexLocation, uint StartInstanceLocation);
        [PreserveSig] void GSSetConstantBuffers(uint StartSlot, uint NumBuffers, IntPtr ppConstantBuffers);
        [PreserveSig] void GSSetShader(IntPtr pShader, IntPtr ppClassInstances, uint NumClassInstances);
        [PreserveSig] void IASetPrimitiveTopology(uint Topology);
        [PreserveSig] void VSSetShaderResources(uint StartSlot, uint NumViews, IntPtr ppShaderResourceViews);
        [PreserveSig] void VSSetSamplers(uint StartSlot, uint NumSamplers, IntPtr ppSamplers);
        [PreserveSig] void Begin(IntPtr pAsync);
        [PreserveSig] void End(IntPtr pAsync);
        [PreserveSig] int GetData(IntPtr pAsync, IntPtr pData, uint DataSize, uint GetDataFlags);
        [PreserveSig] void SetPredication(IntPtr pPredicate, bool PredicateValue);
        [PreserveSig] void GSSetShaderResources(uint StartSlot, uint NumViews, IntPtr ppShaderResourceViews);
        [PreserveSig] void GSSetSamplers(uint StartSlot, uint NumSamplers, IntPtr ppSamplers);
        [PreserveSig] void OMSetRenderTargets(uint NumViews, IntPtr ppRenderTargetViews, IntPtr pDepthStencilView);
        [PreserveSig] void OMSetRenderTargetsAndUnorderedAccessViews(uint NumRTVs, IntPtr ppRenderTargetViews, IntPtr pDepthStencilView, uint UAVStartSlot, uint NumUAVs, IntPtr ppUnorderedAccessViews, IntPtr pUAVInitialCounts);
        [PreserveSig] void OMSetBlendState(IntPtr pBlendState, float[] BlendFactor, uint SampleMask);
        [PreserveSig] void OMSetDepthStencilState(IntPtr pDepthStencilState, uint StencilRef);
        [PreserveSig] void RSSetState(IntPtr pRasterizerState);
        [PreserveSig] void RSSetViewports(uint NumViewports, IntPtr pViewports);
        [PreserveSig] void RSSetScissorRects(uint NumRects, IntPtr pRects);
        [PreserveSig] void CopyResource(ID3D11Texture2D pDstResource, ID3D11Texture2D pSrcResource);
    }
}
