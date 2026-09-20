namespace RejiDisplay.Models
{
    public class OutputCardState
    {
        public string CardId { get; set; } = string.Empty; // "LEFT" or "RIGHT"
        public string Title { get; set; } = string.Empty;  // "LEFT LED", "RIGHT LED"
        public string? AssignedDeviceName { get; set; }
        public string? AssignedDeviceId { get; set; }
        public ScaleMode ScaleMode { get; set; } = ScaleMode.Fit;
        public string? CurrentMediaPath { get; set; }
        public bool IsBlackout { get; set; }
        public bool IsActive { get; set; }
        public string StatusText { get; set; } = "INACTIVE";
        public string ErrorText { get; set; } = string.Empty;
    }
}
