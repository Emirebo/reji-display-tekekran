namespace RejiDisplay.Models
{
    public class DisplayInfo
    {
        public string DeviceName { get; set; } = string.Empty; // e.g. \\.\DISPLAY1
        public string DeviceId { get; set; } = string.Empty;   // PnP / Win32 monitor ID
        public string FriendlyName { get; set; } = string.Empty; // e.g. Display 1 (1920x1080) [Primary]
        public int Left { get; set; }
        public int Top { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsPrimary { get; set; }
        public uint DpiX { get; set; } = 96;
        public uint DpiY { get; set; } = 96;
        public int DisplayIndex { get; set; }
        public int RefreshRate { get; set; } = 60;

        public string DisplayLabel => $"{FriendlyName}{(RefreshRate > 0 ? $" @ {RefreshRate}Hz" : "")}{(IsPrimary ? " [OPERATOR / PRIMARY]" : "")}";

        public override string ToString() => DisplayLabel;
    }
}
