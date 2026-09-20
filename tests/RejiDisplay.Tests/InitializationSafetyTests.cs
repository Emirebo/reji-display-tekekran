using RejiDisplay.Models;
using Xunit;

namespace RejiDisplay.Tests
{
    public class InitializationSafetyTests
    {
        [Fact]
        public void ModelInitialization_HasNonNullDefaults()
        {
            var config = new OutputConfig();
            config.PerformMigrationIfNeeded();

            Assert.NotNull(config.Calibration);
            Assert.NotNull(config.DraftLayout);
            Assert.NotNull(config.LiveAppliedLayout);
            Assert.False(config.IsLiveUpdateEnabled);

            Assert.Equal(860, config.Calibration.LogicalLedWidth);
            Assert.Equal(1720, config.Calibration.LogicalLedHeight);
            Assert.Equal(1.0, config.DraftLayout.Zoom);
        }

        [Fact]
        public void OutputCardState_DefaultsToDisabledLiveSync()
        {
            var card = new OutputCardState();

            Assert.False(card.IsLiveUpdateEnabled);
            Assert.False(card.IsActive);
            Assert.NotNull(card.Calibration);
            Assert.NotNull(card.DraftLayout);
            Assert.NotNull(card.LiveAppliedLayout);
        }
    }
}
