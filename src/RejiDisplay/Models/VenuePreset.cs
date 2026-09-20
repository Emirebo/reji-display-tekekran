namespace RejiDisplay.Models
{
    public class VenuePreset
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int DefaultLeftLogicalWidth { get; set; } = 860;
        public int DefaultLeftLogicalHeight { get; set; } = 1720;
        public int DefaultRightLogicalWidth { get; set; } = 860;
        public int DefaultRightLogicalHeight { get; set; } = 1720;

        public static VenuePreset NovaStarVX2000ProStandard => new VenuePreset
        {
            Name = "NovaStar VX2000 Pro Standard",
            Description = "LEFT: 860x1720 | RIGHT: 860x1720 | CENTER: 2581x1376 (Reserved)",
            DefaultLeftLogicalWidth = 860,
            DefaultLeftLogicalHeight = 1720,
            DefaultRightLogicalWidth = 860,
            DefaultRightLogicalHeight = 1720,
            LeftCalibration = new OutputCalibration { LogicalLedWidth = 860, LogicalLedHeight = 1720 },
            RightCalibration = new OutputCalibration { LogicalLedWidth = 860, LogicalLedHeight = 1720 }
        };

        public OutputCalibration LeftCalibration { get; set; } = new();
        public OutputCalibration RightCalibration { get; set; } = new();

        public static VenuePreset FitToSignal => new VenuePreset
        {
            Name = "Fit to Full GPU Signal",
            Description = "Maps physical LED dimensions to full output signal bounds",
            DefaultLeftLogicalWidth = 1920,
            DefaultLeftLogicalHeight = 1080,
            DefaultRightLogicalWidth = 1920,
            DefaultRightLogicalHeight = 1080
        };

        public override string ToString() => Name;
    }
}
