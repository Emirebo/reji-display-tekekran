using System.Windows.Media.Imaging;

namespace RejiDisplay.Models
{
    public class OutputCardState
    {
        public string CardId { get; set; } = string.Empty; // "LEFT" or "RIGHT"
        public string Title { get; set; } = string.Empty;  // "LEFT LED", "RIGHT LED"
        public string? AssignedDeviceName { get; set; }
        public string? AssignedDeviceId { get; set; }

        public OutputCalibration DraftCalibration { get; set; } = new();
        public OutputCalibration LiveAppliedCalibration { get; set; } = new();

        // Convenience property mapping to DraftCalibration for UI controls
        public OutputCalibration Calibration
        {
            get => DraftCalibration;
            set => DraftCalibration = value ?? new OutputCalibration();
        }

        public ImageLayoutState DraftLayout { get; set; } = new();
        public ImageLayoutState LiveAppliedLayout { get; set; } = new();

        public ScaleMode ScaleMode
        {
            get => DraftLayout.ScaleMode;
            set => DraftLayout.ScaleMode = value;
        }

        public string? PreviousMediaPath { get; set; }
        public string? WebUrl { get; set; }

        public DisplayInfo? SelectedDisplay { get; set; }
        public BitmapImage? LoadedBitmap { get; set; }

        public bool IsLiveUpdateEnabled { get; set; } = false;
        public bool IsBlackout { get; set; }
        public bool IsActive { get; set; }
        public string StatusText { get; set; } = "INACTIVE";
        public string ErrorText { get; set; } = string.Empty;
    }
}
