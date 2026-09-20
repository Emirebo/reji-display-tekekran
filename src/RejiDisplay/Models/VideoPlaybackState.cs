using System;

namespace RejiDisplay.Models
{
    public enum PlaybackStatus
    {
        Stopped,
        Playing,
        Paused
    }

    public class VideoPlaybackState
    {
        public PlaybackStatus Status { get; set; } = PlaybackStatus.Stopped;
        public bool IsLooping { get; set; } = true;
        public bool IsMuted { get; set; } = true; // Audio is muted by default for LED output safety
        public double Volume { get; set; } = 1.0; // 0.0 to 1.0 range
        public TimeSpan Position { get; set; } = TimeSpan.Zero;
        public TimeSpan Duration { get; set; } = TimeSpan.Zero;

        public VideoPlaybackState Clone()
        {
            return new VideoPlaybackState
            {
                Status = this.Status,
                IsLooping = this.IsLooping,
                IsMuted = this.IsMuted,
                Volume = this.Volume,
                Position = this.Position,
                Duration = this.Duration
            };
        }
    }
}
