namespace RejiDisplay.Models
{
    public class OutputConfig
    {
        public string? DeviceName { get; set; }
        public string? DeviceId { get; set; }
        public ScaleMode ScaleMode { get; set; } = ScaleMode.Fit;
        public string? LastMediaPath { get; set; }
        public bool IsBlackout { get; set; }
    }

    public class AppSettings
    {
        public OutputConfig LeftOutput { get; set; } = new();
        public OutputConfig RightOutput { get; set; } = new();
        public string? ReservedCenterDeviceName { get; set; }
        public string? ReservedCenterDeviceId { get; set; }
    }
}
