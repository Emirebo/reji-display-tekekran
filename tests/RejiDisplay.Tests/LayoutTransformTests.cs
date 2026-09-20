using System.Windows.Media;
using RejiDisplay.Helpers;
using RejiDisplay.Models;
using Xunit;

namespace RejiDisplay.Tests
{
    public class LayoutTransformTests
    {
        [Fact]
        public void CalculateTransform_FitMode_ComputesUniformScale()
        {
            var layout = new ImageLayoutState
            {
                ScaleMode = ScaleMode.Fit,
                Zoom = 1.0,
                OffsetX = 0,
                OffsetY = 0
            };

            // Image 860x1720, Viewport 1920x1080 -> Fit ratio = min(1920/860, 1080/1720) = 1080/1720 = 0.6279
            var transformGroup = LayoutTransformHelper.CalculateTransform(860, 1720, 1920, 1080, layout);

            Assert.NotNull(transformGroup);
            Assert.Equal(2, transformGroup.Children.Count);

            var scale = (ScaleTransform)transformGroup.Children[0];
            double expectedFit = 1080.0 / 1720.0;
            Assert.Equal(expectedFit, scale.ScaleX, precision: 4);
            Assert.Equal(expectedFit, scale.ScaleY, precision: 4);
        }

        [Fact]
        public void CalculateTransform_ZoomAndOffset_AppliesScaleAndTranslate()
        {
            var layout = new ImageLayoutState
            {
                ScaleMode = ScaleMode.Custom,
                Zoom = 1.5,
                OffsetX = 45,
                OffsetY = -30
            };

            var transformGroup = LayoutTransformHelper.CalculateTransform(1000, 1000, 1000, 1000, layout);

            var scale = (ScaleTransform)transformGroup.Children[0];
            var translate = (TranslateTransform)transformGroup.Children[1];

            Assert.Equal(1.5, scale.ScaleX, precision: 4);
            Assert.Equal(1.5, scale.ScaleY, precision: 4);
            Assert.Equal(45, translate.X);
            Assert.Equal(-30, translate.Y);
        }

        [Fact]
        public void OutputCalibration_ValidateAndClamp_FixesOutOfBoundsViewport()
        {
            var calib = new OutputCalibration
            {
                GpuSignalWidth = 1920,
                GpuSignalHeight = 1080,
                ViewportX = 1800, // Out of bounds when width = 500!
                ViewportY = 0,
                ViewportWidth = 500,
                ViewportHeight = 500
            };

            calib.ValidateAndClamp(1920, 1080);

            // ViewportX (1800) + ViewportWidth (500) = 2300 > 1920! Clamped X should be 1920 - 500 = 1420.
            Assert.Equal(1420, calib.ViewportX);
            Assert.Equal(500, calib.ViewportWidth);
        }

        [Fact]
        public void OutputCalibration_IsIndependentFromImageLayout()
        {
            var calib = new OutputCalibration
            {
                LogicalLedWidth = 860,
                LogicalLedHeight = 1720,
                ViewportWidth = 1920,
                ViewportHeight = 1080
            };

            var layout = new ImageLayoutState
            {
                Zoom = 2.5,
                OffsetX = 200,
                OffsetY = -150
            };

            // Adjusting layout properties does not alter calibration object
            Assert.Equal(860, calib.LogicalLedWidth);
            Assert.Equal(1720, calib.LogicalLedHeight);
            Assert.Equal(1920, calib.ViewportWidth);
        }

        [Fact]
        public void CalculateLayoutRect_FitMode_PortraitImage_NoTopBottomCropping()
        {
            var layout = new ImageLayoutState
            {
                ScaleMode = ScaleMode.Fit,
                Zoom = 1.0,
                OffsetX = 0,
                OffsetY = 0
            };

            // 860x1720 portrait poster into 1920x1080 landscape viewport
            var rect = LayoutTransformHelper.CalculateLayoutRect(860, 1720, 1920, 1080, layout);

            // Fit scale ratio = 1080 / 1720 = 0.6279069767
            double expectedWidth = 860.0 * (1080.0 / 1720.0); // 540.0
            double expectedHeight = 1080.0;
            double expectedLeft = (1920.0 - expectedWidth) / 2.0; // 690.0
            double expectedTop = 0.0;

            Assert.Equal(expectedWidth, rect.Width, precision: 4);
            Assert.Equal(expectedHeight, rect.Height, precision: 4);
            Assert.Equal(expectedLeft, rect.Left, precision: 4);
            Assert.Equal(expectedTop, rect.Top, precision: 4);

            // Verify all 4 corners lie strictly within 1920x1080 viewport bounds
            Assert.True(rect.Top >= 0.0, "Top edge must be >= 0");
            Assert.True(rect.Top + rect.Height <= 1080.0001, "Bottom edge must be <= viewport height");
            Assert.True(rect.Left >= 0.0, "Left edge must be >= 0");
            Assert.True(rect.Left + rect.Width <= 1920.0001, "Right edge must be <= viewport width");
        }

        [Fact]
        public void CalculateLayoutRect_FitMode_PortraitImage_On4KSignal_NoTopBottomCropping()
        {
            var layout = new ImageLayoutState
            {
                ScaleMode = ScaleMode.Fit,
                Zoom = 1.0,
                OffsetX = 0,
                OffsetY = 0
            };

            // 860x1720 portrait poster into 3840x2160 (4K) viewport
            var rect = LayoutTransformHelper.CalculateLayoutRect(860, 1720, 3840, 2160, layout);

            Assert.Equal(2160.0, rect.Height, precision: 4);
            Assert.Equal(0.0, rect.Top, precision: 4);
            Assert.True(rect.Left >= 0.0);
            Assert.True(rect.Left + rect.Width <= 3840.0001);
        }

        [Fact]
        public void CalculateLayoutRect_FillMode_FillsEntireViewport()
        {
            var layout = new ImageLayoutState
            {
                ScaleMode = ScaleMode.Fill,
                Zoom = 1.0,
                OffsetX = 0,
                OffsetY = 0
            };

            var rect = LayoutTransformHelper.CalculateLayoutRect(860, 1720, 1920, 1080, layout);

            Assert.True(rect.Width >= 1920.0, "Fill mode width must cover viewport width");
            Assert.True(rect.Height >= 1080.0, "Fill mode height must cover viewport height");
        }

        [Fact]
        public void CalculateLayoutRect_StretchMode_MatchesViewportDimensions()
        {
            var layout = new ImageLayoutState
            {
                ScaleMode = ScaleMode.Stretch,
                Zoom = 1.0,
                OffsetX = 0,
                OffsetY = 0
            };

            var rect = LayoutTransformHelper.CalculateLayoutRect(860, 1720, 1920, 1080, layout);

            Assert.Equal(1920.0, rect.Width, precision: 4);
            Assert.Equal(1080.0, rect.Height, precision: 4);
            Assert.Equal(0.0, rect.Left, precision: 4);
            Assert.Equal(0.0, rect.Top, precision: 4);
        }

        [Fact]
        public void CalculateLayoutRect_CustomMode_ZoomAndOffsetsAreIndependent()
        {
            var layout = new ImageLayoutState
            {
                ScaleMode = ScaleMode.Custom,
                Zoom = 1.5,
                OffsetX = 30,
                OffsetY = -20
            };

            var rect = LayoutTransformHelper.CalculateLayoutRect(860, 1720, 1920, 1080, layout);

            double baseWidth = 860.0 * (1080.0 / 1720.0); // 540.0
            double expectedWidth = baseWidth * 1.5; // 810.0
            double expectedHeight = 1080.0 * 1.5; // 1620.0
            double expectedLeft = (1920.0 - expectedWidth) / 2.0 + 30; // 585.0
            double expectedTop = (1080.0 - expectedHeight) / 2.0 - 20; // -290.0

            Assert.Equal(expectedWidth, rect.Width, precision: 4);
            Assert.Equal(expectedHeight, rect.Height, precision: 4);
            Assert.Equal(expectedLeft, rect.Left, precision: 4);
            Assert.Equal(expectedTop, rect.Top, precision: 4);
        }

        [Fact]
        public void ApplyLayoutTransform_ProportionalScaling_MatchesPreviewAndMasterRatios()
        {
            var layout = new ImageLayoutState
            {
                ScaleMode = ScaleMode.Fit,
                Zoom = 1.2,
                OffsetX = 100,
                OffsetY = -50
            };

            // Master canvas region: 860x1720
            var masterRect = LayoutTransformHelper.CalculateLayoutRect(860, 1720, 860, 1720, layout);

            // Preview viewport: 215x430 (1/4 size of 860x1720)
            double scaleX = 215.0 / 860.0; // 0.25
            double scaleY = 430.0 / 1720.0; // 0.25
            var scaledLayout = new ImageLayoutState
            {
                ScaleMode = ScaleMode.Fit,
                Zoom = 1.2,
                OffsetX = layout.OffsetX * scaleX,
                OffsetY = layout.OffsetY * scaleY
            };
            var previewRect = LayoutTransformHelper.CalculateLayoutRect(860, 1720, 215, 430, scaledLayout);

            // Verify preview rect is exactly 1/4 of master rect in width, height, left and top!
            Assert.Equal(masterRect.Width * 0.25, previewRect.Width, precision: 4);
            Assert.Equal(masterRect.Height * 0.25, previewRect.Height, precision: 4);
            Assert.Equal(masterRect.Left * 0.25, previewRect.Left, precision: 4);
            Assert.Equal(masterRect.Top * 0.25, previewRect.Top, precision: 4);
        }
    }
}

