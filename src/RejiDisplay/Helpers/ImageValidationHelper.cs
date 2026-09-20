using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace RejiDisplay.Helpers
{
    public static class ImageValidationHelper
    {
        public static bool ValidateImageFile(string filePath, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(filePath))
            {
                errorMessage = "Dosya yolu bulunamadı veya boş.";
                return false;
            }

            if (!File.Exists(filePath))
            {
                errorMessage = $"Dosya mevcut değil: {filePath}";
                return false;
            }

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext != ".png" && ext != ".jpg" && ext != ".jpeg" && ext != ".webp" && ext != ".bmp")
            {
                errorMessage = $"Desteklenmeyen dosya uzantısı '{ext}'. (PNG, JPG, JPEG, WEBP, BMP desteklenir)";
                return false;
            }

            try
            {
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    if (decoder.Frames.Count == 0)
                    {
                        errorMessage = "Görsel dosyasında resim karesi okunamadı.";
                        return false;
                    }

                    var frame = decoder.Frames[0];
                    if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0)
                    {
                        errorMessage = "Görsel genişlik veya yüksekliği geçersiz.";
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"Görsel dosyası bozuk veya okunamıyor: {ex.Message}";
                return false;
            }
        }
    }
}
