using System;
using System.Collections.Generic;
using System.IO;
using RejiDisplay.Models;
using RejiDisplay.Services;
using Vortice.DXGI;
using Xunit;

namespace RejiDisplay.Tests
{
    public class CaptureEngineTests
    {
        [Fact]
        public void PresentationCaptureService_InitialState_IsStoppedAndIdle()
        {
            using var service = new PresentationCaptureService();
            Assert.False(service.IsCapturing);
            Assert.Equal("IDLE", service.CurrentCaptureMode);
            Assert.Equal(0, service.CurrentFps);
            Assert.Equal(0, service.MasterDeliveryFps);
            Assert.Equal(0, service.PreviewDeliveryFps);
            Assert.Equal(0, service.DroppedFrames);
        }

        [Fact]
        public void PresentationCaptureService_StartAndStop_UpdatesStateCleanly()
        {
            using var service = new PresentationCaptureService();
            var display = new DisplayInfo
            {
                DeviceName = @"\\.\DISPLAY99",
                Width = 1920,
                Height = 1080,
                RefreshRate = 60
            };

            service.StartCapture(display);
            Assert.True(service.IsCapturing);

            System.Threading.Thread.Sleep(150); // Give background task time to set mode
            Assert.NotEqual("IDLE", service.CurrentCaptureMode);

            service.StopCapture();
            Assert.False(service.IsCapturing);
            Assert.Equal("STOPPED", service.CurrentCaptureMode);
        }

        [Fact]
        public void CaptureRestart_CleansUpAndStartsNewCapture()
        {
            using var service = new PresentationCaptureService();
            var display1 = new DisplayInfo { DeviceName = @"\\.\DISPLAY1", Width = 1920, Height = 1080, RefreshRate = 60 };
            var display2 = new DisplayInfo { DeviceName = @"\\.\DISPLAY2", Width = 3840, Height = 2160, RefreshRate = 60 };

            service.StartCapture(display1);
            Assert.True(service.IsCapturing);

            // Restart on display2
            service.StartCapture(display2);
            Assert.True(service.IsCapturing);

            service.StopCapture();
            Assert.False(service.IsCapturing);
        }

        [Fact]
        public void DxgiCaptureEngine_InvalidDeviceName_FailsGracefullyWithoutCrashing()
        {
            using var dxgi = new DxgiCaptureEngine(@"\\.\NON_EXISTENT_DISPLAY");
            Assert.False(dxgi.IsInitialized);
            Assert.False(string.IsNullOrEmpty(dxgi.InitError));

            var frame = dxgi.CaptureFrame(out bool frameChanged, out double acqMs, out double readMs);
            Assert.False(frameChanged);
            Assert.Null(frame);
        }

        [Fact]
        public void MiddlePresentationGeometry_MaintainsAspectAndOffset()
        {
            var region = MasterCanvasGeometry.GetMiddleRegion(yOffset: 172);
            Assert.Equal(860, region.X);
            Assert.Equal(172, region.Y);
            Assert.Equal(2581, region.Width);
            Assert.Equal(1376, region.Height);
            Assert.Equal(4301, MasterCanvasGeometry.CanvasWidth);
            Assert.Equal(1720, MasterCanvasGeometry.CanvasHeight);
        }

        [Fact]
        public void DxgiCaptureEngine_PhysicalHardwareDisplay_HandlesInitializationSafely()
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            if (factory == null) return;

            var displays = new List<string>();
            for (uint a = 0; factory.EnumAdapters1(a, out IDXGIAdapter1? adapter).Success; a++)
            {
                if (adapter == null) continue;
                for (uint o = 0; adapter.EnumOutputs(o, out IDXGIOutput? output).Success; o++)
                {
                    if (output != null)
                    {
                        displays.Add(output.Description.DeviceName);
                        output.Dispose();
                    }
                }
                adapter.Dispose();
            }

            Assert.NotEmpty(displays);

            foreach (var display in displays)
            {
                using var dxgi = new DxgiCaptureEngine(display);
                if (dxgi.IsInitialized)
                {
                    Assert.Empty(dxgi.InitError);
                    var frame = dxgi.CaptureFrame(out bool frameChanged, out double acqMs, out double readMs);
                    Assert.True(dxgi.IsInitialized, $"DXGI lost initialization after frame capture call on '{display}'");
                }
                else
                {
                    Assert.False(string.IsNullOrEmpty(dxgi.InitError), $"InitError should explain failure for '{display}'");
                }
            }
        }

        [Fact]
        public void DiagnosticScenarios_Configuration_UpdatesActiveScenario()
        {
            using var service = new PresentationCaptureService();
            Assert.Equal(DiagnosticScenario.ScenarioD_FullApp, service.ActiveScenario);

            service.ActiveScenario = DiagnosticScenario.ScenarioA_CaptureOnly;
            Assert.Equal(DiagnosticScenario.ScenarioA_CaptureOnly, service.ActiveScenario);
        }
    }
}
