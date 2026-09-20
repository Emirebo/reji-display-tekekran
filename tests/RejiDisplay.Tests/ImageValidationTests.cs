using System.IO;
using RejiDisplay.Helpers;
using Xunit;

namespace RejiDisplay.Tests
{
    public class ImageValidationTests
    {
        [Fact]
        public void NonExistentFile_FailsValidation()
        {
            bool valid = ImageValidationHelper.ValidateImageFile(@"C:\non_existent_folder\image_12345.png", out string err);

            Assert.False(valid);
            Assert.Contains("mevcut değil", err);
        }

        [Fact]
        public void UnsupportedExtension_FailsValidation()
        {
            string tempFile = Path.GetTempFileName() + ".exe";
            File.WriteAllText(tempFile, "fake exe content");

            try
            {
                bool valid = ImageValidationHelper.ValidateImageFile(tempFile, out string err);

                Assert.False(valid);
                Assert.Contains("Desteklenmeyen dosya uzantısı", err);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }
    }
}
