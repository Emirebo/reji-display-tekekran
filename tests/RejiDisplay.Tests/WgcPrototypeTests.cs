using System;
using System.Diagnostics;
using System.Threading;
using RejiDisplay.Helpers;
using RejiDisplay.Services;
using Xunit;
using Xunit.Abstractions;

namespace RejiDisplay.Tests
{
    public class WgcPrototypeTests
    {
        private readonly ITestOutputHelper _output;

        public WgcPrototypeTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void Task2_VerifyProcessSessionAndDesktopContext()
        {
            int sessionId = Process.GetCurrentProcess().SessionId;
            _output.WriteLine($"Current Process Session ID: {sessionId}");
            Logger.Log($"[TASK2_DIAGNOSTICS] Current Process Session ID: {sessionId}");

            Assert.True(sessionId >= 0, "Session ID should be non-negative.");
        }

        [Fact]
        public void Task3_RunIsolatedWgcFeasibilityTestOnPresentationDisplay()
        {
            string targetDisplay = @"\\.\DISPLAY3";
            IntPtr hMonitor = WgcCapturePrototype.GetMonitorHandleForDevice(targetDisplay);
            if (hMonitor == IntPtr.Zero)
            {
                targetDisplay = @"\\.\DISPLAY1";
                hMonitor = WgcCapturePrototype.GetMonitorHandleForDevice(targetDisplay);
            }

            _output.WriteLine($"Target Display: '{targetDisplay}', HMONITOR: 0x{hMonitor.ToString("X")}");
            Logger.Log($"[TASK3_WGC_TEST] Target Display: '{targetDisplay}', HMONITOR: 0x{hMonitor.ToString("X")}");

            if (hMonitor == IntPtr.Zero)
            {
                _output.WriteLine("No physical display monitor handle resolved. Skipping WGC test on headless environment.");
                return;
            }

            using var prototype = new WgcCapturePrototype();
            bool started = false;

            var staThread = new Thread(() =>
            {
                started = prototype.StartTest(targetDisplay);
                if (started)
                {
                    var swPoll = System.Diagnostics.Stopwatch.StartNew();
                    while (swPoll.ElapsedMilliseconds < 3000 && prototype.IsRunning)
                    {
                        prototype.TryPollFrame(out _);
                        Thread.Sleep(16);
                    }
                }
            });
            staThread.SetApartmentState(ApartmentState.STA);
            staThread.Start();
            staThread.Join();

            prototype.Stop();

            _output.WriteLine($"WGC Test Completed for '{prototype.TargetDisplayDevice}':");
            _output.WriteLine($"Captured Frames: {prototype.CapturedFrameCount}");
            _output.WriteLine($"Measured FPS: {prototype.MeasuredFps}");
            _output.WriteLine($"Average Frame Latency: {prototype.AverageLatencyMs} ms");

            Logger.Log($"[TASK3_WGC_RESULTS] Captured Frames: {prototype.CapturedFrameCount}, Measured FPS: {prototype.MeasuredFps}, Avg Latency: {prototype.AverageLatencyMs} ms");

            Assert.True(prototype.CapturedFrameCount > 0, "WGC Prototype should deliver captured frames for physical display.");
        }
    }
}
