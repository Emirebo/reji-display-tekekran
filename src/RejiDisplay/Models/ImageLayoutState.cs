namespace RejiDisplay.Models
{
    public class ImageLayoutState
    {
        private MediaSource _mediaSource = new();

        public MediaSource MediaSource
        {
            get => _mediaSource;
            set => _mediaSource = value ?? new MediaSource();
        }

        public string? MediaPath
        {
            get => _mediaSource.FilePath;
            set
            {
                if (_mediaSource.FilePath != value)
                {
                    _mediaSource = MediaSource.FromFile(value);
                }
            }
        }

        public VideoPlaybackState VideoState { get; set; } = new();

        public ScaleMode ScaleMode { get; set; } = ScaleMode.Fit;
        public double Zoom { get; set; } = 1.0; // 1.0 = 100% (range 0.1 to 4.0)
        public double OffsetX { get; set; } = 0; // in pixels
        public double OffsetY { get; set; } = 0; // in pixels

        public MediaSourceType MediaType
        {
            get => _mediaSource.Type;
            set => _mediaSource.Type = value;
        }

        public int ZoomPercent
        {
            get => (int)Math.Round(Zoom * 100.0);
            set => Zoom = Math.Max(10, Math.Min(400, value)) / 100.0;
        }

        public void ResetTransforms()
        {
            Zoom = 1.0;
            OffsetX = 0;
            OffsetY = 0;
            ScaleMode = ScaleMode.Fit;
        }

        public ImageLayoutState Clone()
        {
            return new ImageLayoutState
            {
                MediaSource = this.MediaSource.Clone(),
                VideoState = this.VideoState.Clone(),
                ScaleMode = this.ScaleMode,
                Zoom = this.Zoom,
                OffsetX = this.OffsetX,
                OffsetY = this.OffsetY
            };
        }
    }
}
