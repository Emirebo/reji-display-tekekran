using System;

namespace RejiDisplay.Models
{
    public enum DisplayRole
    {
        ControlMonitor,      // Display 1 — Operator UI
        PresentationSource,  // Display 2 — Captured Extended Desktop (PowerPoint / PDF / Web)
        MasterOutput,        // Display 3 — Unified LED Output (NovaStar VX2000 Pro)
        Unassigned
    }

    public class RegionGeometry
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        public RegionGeometry(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
    }

    public static class MasterCanvasGeometry
    {
        public const int CanvasWidth = 4301;
        public const int CanvasHeight = 1720;

        public const int LeftWidth = 860;
        public const int LeftHeight = 1720;

        public const int MiddleWidth = 2581;
        public const int MiddleHeight = 1376;
        public const int DefaultMiddleYOffset = 172; // Vertically centered: (1720 - 1376) / 2

        public const int RightWidth = 860;
        public const int RightHeight = 1720;

        public static RegionGeometry GetLeftRegion()
        {
            return new RegionGeometry(0, 0, LeftWidth, LeftHeight);
        }

        public static RegionGeometry GetMiddleRegion(int yOffset = DefaultMiddleYOffset)
        {
            return new RegionGeometry(LeftWidth, yOffset, MiddleWidth, MiddleHeight);
        }

        public static RegionGeometry GetRightRegion()
        {
            return new RegionGeometry(LeftWidth + MiddleWidth, 0, RightWidth, RightHeight);
        }
    }

    public class RegionContentState
    {
        public string? MediaPath { get; set; }
        public string? WebUrl { get; set; }
        public string? TestPatternName { get; set; }
        public MediaSourceType MediaType { get; set; } = MediaSourceType.Image;
        public ScaleMode ScaleMode { get; set; } = ScaleMode.Fit;
        public ImageLayoutState Layout { get; set; } = new();
        public OutputCalibration Calibration { get; set; } = new();
        public bool IsBlackout { get; set; }

        // Video playback properties
        public bool IsLooping { get; set; } = true;
        public bool IsMuted { get; set; }
        public double Volume { get; set; } = 1.0;
        public double PositionSeconds { get; set; }
        public bool IsPlaying { get; set; } = true;

        public RegionContentState Clone()
        {
            return new RegionContentState
            {
                MediaPath = this.MediaPath,
                WebUrl = this.WebUrl,
                TestPatternName = this.TestPatternName,
                MediaType = this.MediaType,
                ScaleMode = this.ScaleMode,
                Layout = this.Layout.Clone(),
                Calibration = this.Calibration.Clone(),
                IsBlackout = this.IsBlackout,
                IsLooping = this.IsLooping,
                IsMuted = this.IsMuted,
                Volume = this.Volume,
                PositionSeconds = this.PositionSeconds,
                IsPlaying = this.IsPlaying
            };
        }
    }

    public class MasterCanvasState
    {
        public RegionContentState Left { get; set; } = new();
        public RegionContentState Right { get; set; } = new();
        public int MiddleYOffset { get; set; } = MasterCanvasGeometry.DefaultMiddleYOffset;
        public bool IsMasterBlackout { get; set; }

        public MasterCanvasState Clone()
        {
            return new MasterCanvasState
            {
                Left = this.Left.Clone(),
                Right = this.Right.Clone(),
                MiddleYOffset = this.MiddleYOffset,
                IsMasterBlackout = this.IsMasterBlackout
            };
        }
    }
}
