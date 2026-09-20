using System;
using System.IO;
using RejiDisplay.Models;
using RejiDisplay.Services;
using Xunit;

namespace RejiDisplay.Tests
{
    public class SettingsServiceTests
    {
        [Fact]
        public void SettingsService_SaveAndLoad_RoundtripsCorrectly()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"reji_test_{Guid.NewGuid():N}.json");

            try
            {
                var service = new SettingsService(tempFile);

                var originalSettings = new AppSettings
                {
                    ReservedCenterDeviceName = @"\\.\DISPLAY3",
                    LeftOutput = new OutputConfig
                    {
                        DeviceName = @"\\.\DISPLAY2",
                        DeviceId = "MON222",
                        ScaleMode = ScaleMode.Fill,
                        LastMediaPath = @"C:\test\left.png",
                        IsBlackout = true
                    },
                    RightOutput = new OutputConfig
                    {
                        DeviceName = @"\\.\DISPLAY4",
                        DeviceId = "MON444",
                        ScaleMode = ScaleMode.Stretch,
                        LastMediaPath = @"C:\test\right.png",
                        IsBlackout = false
                    }
                };

                bool saved = service.SaveSettings(originalSettings);
                Assert.True(saved);
                Assert.True(File.Exists(tempFile));

                var loadedSettings = service.LoadSettings();

                Assert.Equal(@"\\.\DISPLAY3", loadedSettings.ReservedCenterDeviceName);
                Assert.Equal(@"\\.\DISPLAY2", loadedSettings.LeftOutput.DeviceName);
                Assert.Equal(ScaleMode.Fill, loadedSettings.LeftOutput.ScaleMode);
                Assert.True(loadedSettings.LeftOutput.IsBlackout);

                Assert.Equal(@"\\.\DISPLAY4", loadedSettings.RightOutput.DeviceName);
                Assert.Equal(ScaleMode.Stretch, loadedSettings.RightOutput.ScaleMode);
                Assert.False(loadedSettings.RightOutput.IsBlackout);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }
    }
}
