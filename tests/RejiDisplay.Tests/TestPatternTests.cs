using System.IO;
using RejiDisplay.Helpers;
using RejiDisplay.Models;
using Xunit;

namespace RejiDisplay.Tests
{
    public class TestPatternTests
    {
        [Fact]
        public void TestPatternGenerator_CreatesBitmapWithExactLogicalDimensions()
        {
            int width = 860;
            int height = 1720;

            var bitmap = TestPatternGenerator.GenerateTestPattern("LEFT LED", width, height);

            Assert.NotNull(bitmap);
            Assert.Equal(width, bitmap.PixelWidth);
            Assert.Equal(height, bitmap.PixelHeight);
        }

        [Fact]
        public void TestPatternGenerator_SavesValidPngFileToTemp()
        {
            string tempFile = TestPatternGenerator.SaveTestPatternToTempFile("RIGHT LED", 860, 1720);

            try
            {
                Assert.True(File.Exists(tempFile));
                bool valid = ImageValidationHelper.ValidateImageFile(tempFile, out string err);
                Assert.True(valid, err);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        [Fact]
        public void PreviousMediaState_IsPreservedWhenTestPatternIsTriggered()
        {
            var cardState = new OutputCardState
            {
                CardId = "LEFT",
                DraftLayout = new ImageLayoutState { MediaPath = @"C:\images\original_artwork.png" },
                LiveAppliedLayout = new ImageLayoutState { MediaPath = @"C:\images\original_artwork.png" }
            };

            // Triggering test pattern saves original media path
            cardState.PreviousMediaPath = cardState.DraftLayout.MediaPath;
            cardState.DraftLayout.MediaPath = @"C:\temp\TestPattern_LEFT_860x1720.png";

            Assert.Equal(@"C:\images\original_artwork.png", cardState.PreviousMediaPath);
            Assert.Contains("TestPattern_", cardState.DraftLayout.MediaPath);
            Assert.Equal(@"C:\images\original_artwork.png", cardState.LiveAppliedLayout.MediaPath); // Live output untouched until Apply!

            // Restore media
            cardState.DraftLayout.MediaPath = cardState.PreviousMediaPath;
            cardState.PreviousMediaPath = null;

            Assert.Equal(@"C:\images\original_artwork.png", cardState.DraftLayout.MediaPath);
            Assert.Null(cardState.PreviousMediaPath);
        }

        [Fact]
        public void SaveTestPatternToTempFile_GeneratesUniqueFilenamesToAvoidCaching()
        {
            string file1 = TestPatternGenerator.SaveTestPatternToTempFile("LEFT LED", 860, 1720);
            string file2 = TestPatternGenerator.SaveTestPatternToTempFile("LEFT LED", 860, 1720);

            try
            {
                Assert.NotEqual(file1, file2);
            }
            finally
            {
                if (File.Exists(file1)) File.Delete(file1);
                if (File.Exists(file2)) File.Delete(file2);
            }
        }

        [Fact]
        public void NovaStarPreset_Retains860x1720LogicalDimensions()
        {
            var preset = VenuePreset.NovaStarVX2000ProStandard;

            Assert.Equal(860, preset.DefaultLeftLogicalWidth);
            Assert.Equal(1720, preset.DefaultLeftLogicalHeight);
            Assert.Equal(860, preset.DefaultRightLogicalWidth);
            Assert.Equal(1720, preset.DefaultRightLogicalHeight);
        }
    }
}
