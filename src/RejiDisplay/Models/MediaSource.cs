using System;
using System.IO;

namespace RejiDisplay.Models
{
    public class MediaSource
    {
        public MediaSourceType Type { get; set; } = MediaSourceType.None;
        public string? FilePath { get; set; }
        public string DisplayName { get; set; } = string.Empty;

        public static MediaSource FromFile(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return new MediaSource { Type = MediaSourceType.None };
            }

            string fileName = Path.GetFileName(path);
            if (fileName.StartsWith("TestPattern_", StringComparison.OrdinalIgnoreCase))
            {
                return new MediaSource
                {
                    Type = MediaSourceType.TestPattern,
                    FilePath = path,
                    DisplayName = fileName
                };
            }

            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".mp4" || ext == ".mov" || ext == ".mkv" || ext == ".webm" || ext == ".avi" || ext == ".wmv")
            {
                return new MediaSource
                {
                    Type = MediaSourceType.Video,
                    FilePath = path,
                    DisplayName = fileName
                };
            }

            return new MediaSource
            {
                Type = MediaSourceType.Image,
                FilePath = path,
                DisplayName = fileName
            };
        }

        public MediaSource Clone()
        {
            return new MediaSource
            {
                Type = this.Type,
                FilePath = this.FilePath,
                DisplayName = this.DisplayName
            };
        }
    }
}
