using System.Collections.Generic;
using RejiDisplay.Models;
using RejiDisplay.Services;
using Xunit;

namespace RejiDisplay.Tests
{
    public class DisplayAssignmentTests
    {
        private readonly DisplayService _displayService;

        public DisplayAssignmentTests()
        {
            _displayService = new DisplayService();
        }

        [Fact]
        public void PrimaryDisplay_IsExcludedFromAssignableDisplays()
        {
            var primary = new DisplayInfo { DeviceName = @"\\.\DISPLAY1", IsPrimary = true, Width = 1920, Height = 1080 };
            var secondary = new DisplayInfo { DeviceName = @"\\.\DISPLAY2", IsPrimary = false, Width = 1920, Height = 1080 };

            var allDisplays = new List<DisplayInfo> { primary, secondary };

            var assignable = _displayService.GetAssignableDisplays(allDisplays, reservedCenter: null, claimedByOtherCard: null);

            Assert.Single(assignable);
            Assert.DoesNotContain(primary, assignable);
            Assert.Contains(secondary, assignable);
        }

        [Fact]
        public void ReservedCenterDisplay_IsExcludedFromAssignableDisplays()
        {
            var primary = new DisplayInfo { DeviceName = @"\\.\DISPLAY1", IsPrimary = true };
            var center = new DisplayInfo { DeviceName = @"\\.\DISPLAY3", IsPrimary = false };
            var leftOutput = new DisplayInfo { DeviceName = @"\\.\DISPLAY2", IsPrimary = false };

            var allDisplays = new List<DisplayInfo> { primary, leftOutput, center };

            var assignable = _displayService.GetAssignableDisplays(allDisplays, reservedCenter: center, claimedByOtherCard: null);

            Assert.Single(assignable);
            Assert.Contains(leftOutput, assignable);
            Assert.DoesNotContain(center, assignable);
            Assert.DoesNotContain(primary, assignable);
        }

        [Fact]
        public void ClaimedByOtherCard_IsExcludedFromAssignableDisplays()
        {
            var primary = new DisplayInfo { DeviceName = @"\\.\DISPLAY1", IsPrimary = true };
            var displayLeft = new DisplayInfo { DeviceName = @"\\.\DISPLAY2", IsPrimary = false };
            var displayRight = new DisplayInfo { DeviceName = @"\\.\DISPLAY4", IsPrimary = false };

            var allDisplays = new List<DisplayInfo> { primary, displayLeft, displayRight };

            // LEFT card claimed displayLeft
            var assignableForRight = _displayService.GetAssignableDisplays(allDisplays, reservedCenter: null, claimedByOtherCard: displayLeft);

            Assert.Single(assignableForRight);
            Assert.Contains(displayRight, assignableForRight);
            Assert.DoesNotContain(displayLeft, assignableForRight);
        }

        [Fact]
        public void DisplayMatching_FindsExactDeviceNameAndId()
        {
            var displays = new List<DisplayInfo>
            {
                new DisplayInfo { DeviceName = @"\\.\DISPLAY1", DeviceId = "MON111" },
                new DisplayInfo { DeviceName = @"\\.\DISPLAY2", DeviceId = "MON222" }
            };

            var match = _displayService.FindMatchingDisplay(displays, @"\\.\DISPLAY2", "MON222");

            Assert.NotNull(match);
            Assert.Equal(@"\\.\DISPLAY2", match.DeviceName);
        }
    }
}
