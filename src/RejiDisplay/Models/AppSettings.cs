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
        // Legacy v0.2 Settings Preserved
        public OutputConfig LeftOutput { get; set; } = new();
        public OutputConfig RightOutput { get; set; } = new();
        public string? ReservedCenterDeviceName { get; set; }
        public string? ReservedCenterDeviceId { get; set; }

        // v0.3 Master Output & Presentation Composition Settings
        public string? MasterOutputDeviceName { get; set; }
        public string? MasterOutputDeviceId { get; set; }

        public string? PresentationDeviceName { get; set; }
        public string? PresentationDeviceId { get; set; }

        public string? ControlDeviceName { get; set; }
        public string? ControlDeviceId { get; set; }

        public int MiddleYOffset { get; set; } = MasterCanvasGeometry.DefaultMiddleYOffset; // Default 172px
        public bool IsMasterBlackout { get; set; }
        public bool IsSimulationMode { get; set; }
    }
}
