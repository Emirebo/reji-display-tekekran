using System.IO;
using System.Text.Json;
using RejiDisplay.Models;
using RejiDisplay.Services;
using Xunit;

namespace RejiDisplay.Tests
{
    public class JsonMigrationTests
    {
        [Fact]
        public void Migrate_V01Settings_PopulatesV02DraftAndLiveLayouts()
        {
            // Simulate legacy v0.1 JSON format
            string legacyJson = @"{
                ""LeftOutput"": {
                    ""DeviceName"": ""\\\\.\\DISPLAY2"",
                    ""DeviceId"": ""MON222"",
                    ""ScaleMode"": 1,
                    ""LastMediaPath"": ""C:\\images\\left_v01.png"",
                    ""IsBlackout"": false
                },
                ""RightOutput"": {
                    ""DeviceName"": ""\\\\.\\DISPLAY4"",
                    ""DeviceId"": ""MON444"",
                    ""ScaleMode"": 2,
                    ""LastMediaPath"": ""C:\\images\\right_v01.png"",
                    ""IsBlackout"": true
                },
                ""ReservedCenterDeviceName"": ""\\\\.\\DISPLAY3""
            }";

            var appSettings = JsonSerializer.Deserialize<AppSettings>(legacyJson);
            Assert.NotNull(appSettings);

            // Execute migration
            appSettings.Migrate();

            Assert.Equal(@"C:\images\left_v01.png", appSettings.LeftOutput.DraftLayout.MediaPath);
            Assert.Equal(@"C:\images\left_v01.png", appSettings.LeftOutput.LiveAppliedLayout.MediaPath);
            Assert.Equal(ScaleMode.Fill, appSettings.LeftOutput.DraftLayout.ScaleMode);

            Assert.Equal(@"C:\images\right_v01.png", appSettings.RightOutput.DraftLayout.MediaPath);
            Assert.Equal(@"C:\images\right_v01.png", appSettings.RightOutput.LiveAppliedLayout.MediaPath);
            Assert.Equal(ScaleMode.Stretch, appSettings.RightOutput.DraftLayout.ScaleMode);
            Assert.True(appSettings.RightOutput.IsBlackout);
        }
    }
}
