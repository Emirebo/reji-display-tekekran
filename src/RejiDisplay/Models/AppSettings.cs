namespace RejiDisplay.Models
{
    public class OutputConfig
    {
        public string? DeviceName { get; set; }
        public string? DeviceId { get; set; }

        // Legacy v0.1 properties for backward compatibility
        public ScaleMode ScaleMode { get; set; } = ScaleMode.Fit;
        public string? LastMediaPath { get; set; }

        // v0.2 Enhanced Calibration & State Isolation
        public OutputCalibration Calibration { get; set; } = new();
        public ImageLayoutState DraftLayout { get; set; } = new();
        public ImageLayoutState LiveAppliedLayout { get; set; } = new();
        public bool IsLiveUpdateEnabled { get; set; } = false;
        public bool IsBlackout { get; set; }

        public void PerformMigrationIfNeeded()
        {
            if (DraftLayout == null) DraftLayout = new ImageLayoutState();
            if (LiveAppliedLayout == null) LiveAppliedLayout = new ImageLayoutState();
            if (Calibration == null) Calibration = new OutputCalibration();

            if (!string.IsNullOrEmpty(LastMediaPath))
            {
                if (string.IsNullOrEmpty(DraftLayout.MediaPath))
                {
                    DraftLayout.MediaPath = LastMediaPath;
                    DraftLayout.ScaleMode = ScaleMode;
                }

                if (string.IsNullOrEmpty(LiveAppliedLayout.MediaPath))
                {
                    LiveAppliedLayout.MediaPath = LastMediaPath;
                    LiveAppliedLayout.ScaleMode = ScaleMode;
                }
            }
        }
    }

    public class AppSettings
    {
        public int SchemaVersion { get; set; } = 2;
        public OutputConfig LeftOutput { get; set; } = new();
        public OutputConfig RightOutput { get; set; } = new();
        public string? ReservedCenterDeviceName { get; set; }
        public string? ReservedCenterDeviceId { get; set; }
        public string SelectedVenuePresetName { get; set; } = "NovaStar VX2000 Pro Standard";

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

        public void Migrate()
        {
            LeftOutput?.PerformMigrationIfNeeded();
            RightOutput?.PerformMigrationIfNeeded();
        }
    }
}
