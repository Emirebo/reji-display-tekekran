using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace RejiDisplay.Helpers
{
    public static class MediaValidationHelper
    {
        public static bool ValidateMediaFile(string filePath, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(filePath))
            {
                errorMessage = "Dosya yolu bulunamadı veya boş.";
                return false;
            }

            if (!File.Exists(filePath))
            {
                errorMessage = $"Dosya bulunamadı: {filePath}";
                return false;
            }

            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length == 0)
            {
                errorMessage = "Dosya boyutu 0 bayt (boş dosya).";
                return false;
            }

            string ext = Path.GetExtension(filePath).ToLowerInvariant();

            // Check if Video
            if (ext == ".mp4" || ext == ".mov" || ext == ".mkv" || ext == ".webm" || ext == ".avi" || ext == ".wmv")
            {
                // Basic read test for video files
                try
                {
                    using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        if (stream.Length < 16)
                        {
                            errorMessage = "Video dosyası çok küçük veya bozuk.";
                            return false;
                        }
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    errorMessage = $"Video dosyasına erişilemiyor: {ex.Message}";
                    return false;
                }
            }

            // Check if Image
            if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".webp" || ext == ".bmp")
            {
                return ImageValidationHelper.ValidateImageFile(filePath, out errorMessage);
            }

            errorMessage = $"Desteklenmeyen dosya formatı '{ext}'. (Desteklenenler: PNG, JPG, WEBP, BMP, MP4, MOV, MKV, WEBM, AVI, WMV)";
            return false;
        }
    }
}
