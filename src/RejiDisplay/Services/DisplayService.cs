using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using RejiDisplay.Helpers;
using RejiDisplay.Models;

namespace RejiDisplay.Services
{
    public class DisplayService
    {
        public event EventHandler? DisplayTopologyChanged;

        public DisplayService()
        {
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            DisplayTopologyChanged?.Invoke(this, EventArgs.Empty);
        }

        public List<DisplayInfo> GetDisplays()
        {
            var displays = new List<DisplayInfo>();
            int index = 1;

            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref NativeMethods.RECT lprcMonitor, IntPtr dwData) =>
            {
                var mi = new NativeMethods.MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFOEX));

                if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
                {
                    bool isPrimary = (mi.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0;
                    string deviceName = mi.szDevice;
                    string deviceId = GetDeviceId(deviceName);

                    uint dpiX = 96, dpiY = 96;
                    try
                    {
                        NativeMethods.GetDpiForMonitor(hMonitor, NativeMethods.MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out dpiX, out dpiY);
                    }
                    catch
                    {
                        // Default to 96 if API unavailable
                    }

                    int width = mi.rcMonitor.Width;
                    int height = mi.rcMonitor.Height;

                    string friendlyName = $"Ekran {index} ({width}x{height})";

                    displays.Add(new DisplayInfo
                    {
                        DisplayIndex = index,
                        DeviceName = deviceName,
                        DeviceId = deviceId,
                        FriendlyName = friendlyName,
                        Left = mi.rcMonitor.Left,
                        Top = mi.rcMonitor.Top,
                        Width = width,
                        Height = height,
                        IsPrimary = isPrimary,
                        DpiX = dpiX,
                        DpiY = dpiY
                    });

                    index++;
                }

                return true;
            }, IntPtr.Zero);

            return displays;
        }

        private string GetDeviceId(string deviceName)
        {
            try
            {
                var dd = new NativeMethods.DISPLAY_DEVICE();
                dd.cbSize = Marshal.SizeOf(typeof(NativeMethods.DISPLAY_DEVICE));

                if (NativeMethods.EnumDisplayDevices(deviceName, 0, ref dd, 0))
                {
                    if (!string.IsNullOrWhiteSpace(dd.DeviceID))
                    {
                        return dd.DeviceID;
                    }
                }
            }
            catch
            {
                // Return empty if PnP query fails
            }

            return string.Empty;
        }

        public DisplayInfo? GetPrimaryDisplay(IEnumerable<DisplayInfo> displays)
        {
            return displays.FirstOrDefault(d => d.IsPrimary);
        }

        public DisplayInfo? FindMatchingDisplay(IEnumerable<DisplayInfo> displays, string? deviceName, string? deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceName) && string.IsNullOrWhiteSpace(deviceId))
                return null;

            var list = displays.ToList();

            // Match by both DeviceName and DeviceId
            if (!string.IsNullOrWhiteSpace(deviceName) && !string.IsNullOrWhiteSpace(deviceId))
            {
                var match = list.FirstOrDefault(d => string.Equals(d.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase)
                                                  && string.Equals(d.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }

            // Match by DeviceName
            if (!string.IsNullOrWhiteSpace(deviceName))
            {
                var match = list.FirstOrDefault(d => string.Equals(d.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }

            // Match by DeviceId
            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                var match = list.FirstOrDefault(d => string.Equals(d.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }

            return null;
        }

        /// <summary>
        /// Filters available displays for assignment choices.
        /// Rule 1: Exclude Primary display completely from assignment options.
        /// Rule 2: Exclude Reserved CENTER display.
        /// Rule 3: Exclude display already claimed by the other card.
        /// </summary>
        public List<DisplayInfo> GetAssignableDisplays(
            IEnumerable<DisplayInfo> allDisplays,
            DisplayInfo? reservedCenter,
            DisplayInfo? claimedByOtherCard)
        {
            return allDisplays.Where(d =>
                !d.IsPrimary && // Rule 1: NEVER assign primary operator screen
                (reservedCenter == null || !IsSameDisplay(d, reservedCenter)) && // Rule 2: Exclude Reserved CENTER
                (claimedByOtherCard == null || !IsSameDisplay(d, claimedByOtherCard)) // Rule 3: Exclude display assigned to other card
            ).ToList();
        }

        /// <summary>
        /// Filters assignable presentation capture source displays (Display 2 role).
        /// Rule 1: Exclude Master LED Output display (prevents recursive capture loop).
        /// </summary>
        public List<DisplayInfo> GetAssignablePresentationSources(
            IEnumerable<DisplayInfo> allDisplays,
            DisplayInfo? currentMasterOutput)
        {
            return allDisplays.Where(d =>
                !d.IsPrimary &&
                (currentMasterOutput == null || !IsSameDisplay(d, currentMasterOutput))
            ).ToList();
        }

        /// <summary>
        /// Filters assignable master LED output displays (Display 3 role).
        /// Rule 1: Exclude Primary operator control display (Display 1 role).
        /// Rule 2: Exclude Presentation capture source display (Display 2 role).
        /// </summary>
        public List<DisplayInfo> GetAssignableMasterOutputs(
            IEnumerable<DisplayInfo> allDisplays,
            DisplayInfo? currentPresentationSource)
        {
            return allDisplays.Where(d =>
                !d.IsPrimary &&
                (currentPresentationSource == null || !IsSameDisplay(d, currentPresentationSource))
            ).ToList();
        }

        /// <summary>
        /// Generates simulated 3-monitor display topology for single/dual monitor dev testing.
        /// </summary>
        public List<DisplayInfo> GetSimulatedDisplays()
        {
            return new List<DisplayInfo>
            {
                new DisplayInfo
                {
                    DisplayIndex = 1,
                    DeviceName = @"\\.\DISPLAY1",
                    DeviceId = "SIMULATED_PRIMARY_CONTROL",
                    FriendlyName = "Simulated Control Monitor (1920x1080) [Primary]",
                    Left = 0, Top = 0, Width = 1920, Height = 1080, IsPrimary = true
                },
                new DisplayInfo
                {
                    DisplayIndex = 2,
                    DeviceName = @"\\.\DISPLAY2",
                    DeviceId = "SIMULATED_PRESENTATION_SRC",
                    FriendlyName = "Simulated Presentation Display (1920x1080) [Display 2]",
                    Left = 1920, Top = 0, Width = 1920, Height = 1080, IsPrimary = false
                },
                new DisplayInfo
                {
                    DisplayIndex = 3,
                    DeviceName = @"\\.\DISPLAY3",
                    DeviceId = "SIMULATED_MASTER_OUTPUT",
                    FriendlyName = "Simulated Master LED Output (4301x1720) [Display 3]",
                    Left = 3840, Top = 0, Width = 4301, Height = 1720, IsPrimary = false
                }
            };
        }

        public bool IsSameDisplay(DisplayInfo a, DisplayInfo b)
        {
            if (a == null || b == null) return false;

            if (!string.IsNullOrWhiteSpace(a.DeviceName) && !string.IsNullOrWhiteSpace(b.DeviceName))
            {
                if (string.Equals(a.DeviceName, b.DeviceName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            if (!string.IsNullOrWhiteSpace(a.DeviceId) && !string.IsNullOrWhiteSpace(b.DeviceId))
            {
                if (string.Equals(a.DeviceId, b.DeviceId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            if (a.Width > 0 && a.Height > 0 && b.Width > 0 && b.Height > 0)
            {
                return a.Left == b.Left && a.Top == b.Top && a.Width == b.Width && a.Height == b.Height;
            }

            return false;
        }
    }
}
