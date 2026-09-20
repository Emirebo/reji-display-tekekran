using System;
using System.Collections.Generic;
using System.IO;
using RejiDisplay.Models;
using RejiDisplay.Services;
using Xunit;

namespace RejiDisplay.Tests
{
    public class MasterCanvasTests
    {
        [Fact]
        public void MasterCanvasGeometry_DimensionsAndPositions_AreExact()
        {
            Assert.Equal(4301, MasterCanvasGeometry.CanvasWidth);
            Assert.Equal(1720, MasterCanvasGeometry.CanvasHeight);

            // LEFT Region: 860x1720 at (0, 0)
            var left = MasterCanvasGeometry.GetLeftRegion();
            Assert.Equal(0, left.X);
            Assert.Equal(0, left.Y);
            Assert.Equal(860, left.Width);
            Assert.Equal(1720, left.Height);

            // MIDDLE Region: 2581x1376 at (860, 172 default)
            var middleDefault = MasterCanvasGeometry.GetMiddleRegion();
            Assert.Equal(860, middleDefault.X);
            Assert.Equal(172, middleDefault.Y);
            Assert.Equal(2581, middleDefault.Width);
            Assert.Equal(1376, middleDefault.Height);

            // MIDDLE Region: Custom Y-Offset calibration
            var middleCustom = MasterCanvasGeometry.GetMiddleRegion(yOffset: 200);
            Assert.Equal(860, middleCustom.X);
            Assert.Equal(200, middleCustom.Y);
            Assert.Equal(2581, middleCustom.Width);
            Assert.Equal(1376, middleCustom.Height);

            // RIGHT Region: 860x1720 at (3441, 0)
            var right = MasterCanvasGeometry.GetRightRegion();
            Assert.Equal(3441, right.X); // 860 + 2581 = 3441
            Assert.Equal(0, right.Y);
            Assert.Equal(860, right.Width);
            Assert.Equal(1720, right.Height);
        }

        [Fact]
        public void DraftAndLiveState_Isolation_IsPreservedUntilTake()
        {
            var draftState = new MasterCanvasState
            {
                Left = new RegionContentState { MediaPath = "left_draft.png", ScaleMode = ScaleMode.Fit },
                Right = new RegionContentState { MediaPath = "right_draft.png", ScaleMode = ScaleMode.Fill },
                MiddleYOffset = 172
            };

            // Before Take: Live state is independent/unaffected
            var liveState = draftState.Clone();

            // Mutate Draft State
            draftState.Left.MediaPath = "left_draft_updated.png";
            draftState.Left.ScaleMode = ScaleMode.Stretch;
            draftState.MiddleYOffset = 210;

            // Assert Live State retains old values prior to Take
            Assert.Equal("left_draft.png", liveState.Left.MediaPath);
            Assert.Equal(ScaleMode.Fit, liveState.Left.ScaleMode);
            Assert.Equal(172, liveState.MiddleYOffset);

            // Perform Take (clone draft into live)
            liveState = draftState.Clone();

            Assert.Equal("left_draft_updated.png", liveState.Left.MediaPath);
            Assert.Equal(ScaleMode.Stretch, liveState.Left.ScaleMode);
            Assert.Equal(210, liveState.MiddleYOffset);
        }

        [Fact]
        public void DisplayRoleRestrictions_PreventSimultaneousRoleAssignment()
        {
            var displayService = new DisplayService();

            var controlMonitor = new DisplayInfo { DeviceName = @"\\.\DISPLAY1", IsPrimary = true };
            var presentationSource = new DisplayInfo { DeviceName = @"\\.\DISPLAY2", IsPrimary = false };
            var masterOutput = new DisplayInfo { DeviceName = @"\\.\DISPLAY3", IsPrimary = false };

            var allDisplays = new List<DisplayInfo> { controlMonitor, presentationSource, masterOutput };

            // 1. Presentation source options MUST exclude Primary Control Monitor and selected Master Output
            var presentationSources = displayService.GetAssignablePresentationSources(allDisplays, currentMasterOutput: masterOutput);
            Assert.Single(presentationSources);
            Assert.Contains(presentationSource, presentationSources);
            Assert.DoesNotContain(controlMonitor, presentationSources);
            Assert.DoesNotContain(masterOutput, presentationSources);

            // 2. Master output options MUST exclude Primary Control Monitor and selected Presentation Source
            var masterOutputs = displayService.GetAssignableMasterOutputs(allDisplays, currentPresentationSource: presentationSource);
            Assert.Single(masterOutputs);
            Assert.Contains(masterOutput, masterOutputs);
            Assert.DoesNotContain(controlMonitor, masterOutputs);
            Assert.DoesNotContain(presentationSource, masterOutputs);
        }

        [Fact]
        public void SettingsIsolation_DefaultDirectory_IsRejiDisplayTekEkran()
        {
            var service = new SettingsService();
            string expectedDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RejiDisplay-TekEkran");
            string expectedFilePath = Path.Combine(expectedDir, "settings.json");

            Assert.Equal(expectedFilePath, service.SettingsFilePath);
        }
    }
}
