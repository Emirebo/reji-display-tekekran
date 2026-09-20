using RejiDisplay.Models;
using Xunit;

namespace RejiDisplay.Tests
{
    public class DraftVsAppliedStateTests
    {
        [Fact]
        public void DraftEdits_DoNotModifyLiveAppliedState()
        {
            var card = new OutputCardState
            {
                CardId = "LEFT",
                DraftLayout = new ImageLayoutState { Zoom = 1.0, OffsetX = 0, ScaleMode = ScaleMode.Fit },
                LiveAppliedLayout = new ImageLayoutState { Zoom = 1.0, OffsetX = 0, ScaleMode = ScaleMode.Fit }
            };

            // Operator modifies draft zoom & offset in UI
            card.DraftLayout.Zoom = 1.8;
            card.DraftLayout.OffsetX = 120;
            card.DraftLayout.ScaleMode = ScaleMode.Fill;

            // LiveAppliedLayout remains untouched
            Assert.Equal(1.0, card.LiveAppliedLayout.Zoom);
            Assert.Equal(0, card.LiveAppliedLayout.OffsetX);
            Assert.Equal(ScaleMode.Fit, card.LiveAppliedLayout.ScaleMode);

            // Draft reflects operator edits
            Assert.Equal(1.8, card.DraftLayout.Zoom);
            Assert.Equal(120, card.DraftLayout.OffsetX);
            Assert.Equal(ScaleMode.Fill, card.DraftLayout.ScaleMode);
        }

        [Fact]
        public void ApplyDraftToLive_AtomicallyCopiesDraftState()
        {
            var card = new OutputCardState
            {
                CardId = "LEFT",
                DraftLayout = new ImageLayoutState { MediaPath = "logo.png", Zoom = 2.0, OffsetX = 50 },
                LiveAppliedLayout = new ImageLayoutState { MediaPath = "old.png", Zoom = 1.0, OffsetX = 0 }
            };

            // Apply draft to live
            card.LiveAppliedLayout = card.DraftLayout.Clone();

            Assert.Equal("logo.png", card.LiveAppliedLayout.MediaPath);
            Assert.Equal(2.0, card.LiveAppliedLayout.Zoom);
            Assert.Equal(50, card.LiveAppliedLayout.OffsetX);
        }

        [Fact]
        public void BlackoutToggle_DoesNotOverwritesPendingDraft()
        {
            var card = new OutputCardState
            {
                CardId = "RIGHT",
                DraftLayout = new ImageLayoutState { Zoom = 1.5, OffsetX = 80 },
                LiveAppliedLayout = new ImageLayoutState { Zoom = 1.0, OffsetX = 0 },
                IsBlackout = false
            };

            // Toggle blackout
            card.IsBlackout = true;

            // Pending draft state is preserved
            Assert.True(card.IsBlackout);
            Assert.Equal(1.5, card.DraftLayout.Zoom);
            Assert.Equal(80, card.DraftLayout.OffsetX);
        }
    }
}
