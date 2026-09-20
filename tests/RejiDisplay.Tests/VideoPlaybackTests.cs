using System;
using System.IO;
using RejiDisplay.Helpers;
using RejiDisplay.Models;
using Xunit;

namespace RejiDisplay.Tests
{
    public class VideoPlaybackTests
    {
        [Theory]
        [InlineData("promo.mp4", MediaSourceType.Video)]
        [InlineData("clip.mov", MediaSourceType.Video)]
        [InlineData("movie.mkv", MediaSourceType.Video)]
        [InlineData("web.webm", MediaSourceType.Video)]
        [InlineData("legacy.avi", MediaSourceType.Video)]
        [InlineData("poster.png", MediaSourceType.Image)]
        [InlineData("photo.jpg", MediaSourceType.Image)]
        [InlineData("TestPattern_LEFT_860x1720.png", MediaSourceType.TestPattern)]
        [InlineData("", MediaSourceType.None)]
        public void MediaSource_TypeDetection_IdentifiesFileTypeCorrectly(string path, MediaSourceType expectedType)
        {
            var media = MediaSource.FromFile(path);
            Assert.Equal(expectedType, media.Type);
        }

        [Fact]
        public void MediaSource_TransitionBetweenImageAndVideo_UpdatesType()
        {
            var layout = new ImageLayoutState();

            // 1. Initial State
            Assert.Equal(MediaSourceType.None, layout.MediaSource.Type);

            // 2. Transition to Video
            layout.MediaPath = @"C:\media\intro.mp4";
            Assert.Equal(MediaSourceType.Video, layout.MediaSource.Type);
            Assert.Equal("intro.mp4", layout.MediaSource.DisplayName);

            // 3. Transition to Image
            layout.MediaPath = @"C:\media\poster.png";
            Assert.Equal(MediaSourceType.Image, layout.MediaSource.Type);
            Assert.Equal("poster.png", layout.MediaSource.DisplayName);
        }

        [Fact]
        public void VideoPlaybackState_DefaultsAreSafeForLedOutput()
        {
            var state = new VideoPlaybackState();

            Assert.True(state.IsMuted, "LED output audio must be MUTED BY DEFAULT");
            Assert.True(state.IsLooping, "Default loop mode should be enabled for continuous displays");
            Assert.Equal(PlaybackStatus.Stopped, state.Status);
            Assert.Equal(1.0, state.Volume);
        }

        [Fact]
        public void IndependentPlaybackState_LeftAndRightOutputsAreIsolated()
        {
            var leftCard = new OutputCardState { CardId = "LEFT" };
            var rightCard = new OutputCardState { CardId = "RIGHT" };

            // Start playing video on LEFT with volume 0.5
            leftCard.DraftLayout.MediaSource = MediaSource.FromFile(@"C:\media\left_loop.mp4");
            leftCard.DraftLayout.VideoState.Status = PlaybackStatus.Playing;
            leftCard.DraftLayout.VideoState.IsMuted = false;
            leftCard.DraftLayout.VideoState.Volume = 0.5;

            // RIGHT card remains stopped and muted
            Assert.Equal(PlaybackStatus.Stopped, rightCard.DraftLayout.VideoState.Status);
            Assert.True(rightCard.DraftLayout.VideoState.IsMuted);

            Assert.Equal(PlaybackStatus.Playing, leftCard.DraftLayout.VideoState.Status);
            Assert.False(leftCard.DraftLayout.VideoState.IsMuted);
            Assert.Equal(0.5, leftCard.DraftLayout.VideoState.Volume);
        }

        [Fact]
        public void DraftVsAppliedMedia_SelectionDoesNotAlterLiveOutput()
        {
            var card = new OutputCardState
            {
                CardId = "LEFT",
                DraftLayout = new ImageLayoutState { MediaPath = @"C:\media\active_live.png" },
                LiveAppliedLayout = new ImageLayoutState { MediaPath = @"C:\media\active_live.png" }
            };

            // Operator selects a new draft video
            card.DraftLayout.MediaPath = @"C:\media\new_draft_video.mp4";
            card.DraftLayout.VideoState.Status = PlaybackStatus.Playing;

            // Live applied output remains unchanged
            Assert.Equal(@"C:\media\active_live.png", card.LiveAppliedLayout.MediaPath);
            Assert.Equal(MediaSourceType.Image, card.LiveAppliedLayout.MediaSource.Type);

            // Draft reflects new video
            Assert.Equal(@"C:\media\new_draft_video.mp4", card.DraftLayout.MediaPath);
            Assert.Equal(MediaSourceType.Video, card.DraftLayout.MediaSource.Type);
        }

        [Fact]
        public void MediaValidationHelper_RejectsInvalidOrCorruptFiles()
        {
            // Non-existent file
            bool validFile = MediaValidationHelper.ValidateMediaFile(@"C:\non_existent_file.mp4", out string err1);
            Assert.False(validFile);
            Assert.Contains("Dosya bulunamadı", err1);

            // Invalid extension
            string tempFile = Path.Combine(Path.GetTempPath(), "test.exe");
            File.WriteAllText(tempFile, "dummy");
            try
            {
                bool validExt = MediaValidationHelper.ValidateMediaFile(tempFile, out string err2);
                Assert.False(validExt);
                Assert.Contains("Desteklenmeyen dosya formatı", err2);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void VideoLayout_FitMode_PortraitVideo_PreservesAspectWithoutCropping()
        {
            var layout = new ImageLayoutState
            {
                ScaleMode = ScaleMode.Fit,
                Zoom = 1.0,
                OffsetX = 0,
                OffsetY = 0
            };

            // 1080x1920 portrait video on a 1920x1080 GPU signal
            var rect = LayoutTransformHelper.CalculateLayoutRect(1080, 1920, 1920, 1080, layout);

            // Scale factor = min(1920/1080, 1080/1920) = 1080/1920 = 0.5625
            double expectedWidth = 1080.0 * 0.5625; // 607.5
            double expectedHeight = 1080.0;
            double expectedLeft = (1920.0 - expectedWidth) / 2.0; // 656.25

            Assert.Equal(expectedWidth, rect.Width, precision: 4);
            Assert.Equal(expectedHeight, rect.Height, precision: 4);
            Assert.Equal(expectedLeft, rect.Left, precision: 4);
            Assert.Equal(0.0, rect.Top, precision: 4);
        }

        [Fact]
        public void MediaValidationHelper_ValidatesRealLocalVideoFile_IfAvailable()
        {
            string realVideoPath = @"C:\Users\Emir\Videos\Kısa versiyon.mp4";
            if (File.Exists(realVideoPath))
            {
                bool valid = MediaValidationHelper.ValidateMediaFile(realVideoPath, out string error);
                Assert.True(valid, $"Validation failed for existing video: {error}");
                var mediaSource = MediaSource.FromFile(realVideoPath);
                Assert.Equal(MediaSourceType.Video, mediaSource.Type);
                Assert.Equal("Kısa versiyon.mp4", mediaSource.DisplayName);
            }
        }
    }
}
