using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RejiDisplay.Helpers
{
    public static class TestPatternGenerator
    {
        public static BitmapSource GenerateTestPattern(string cardTitle, int width, int height)
        {
            if (width <= 0) width = 860;
            if (height <= 0) height = 1720;

            var drawingVisual = new DrawingVisual();
            using (var dc = drawingVisual.RenderOpen())
            {
                // Dark Slate Background Grid
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(15, 23, 42)), null, new Rect(0, 0, width, height));

                // Numbered Grid Lines (every 100px)
                var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(51, 65, 85)), 1);
                var gridMajorPen = new Pen(new SolidColorBrush(Color.FromRgb(71, 85, 105)), 1.5);

                for (int x = 100; x < width; x += 100)
                {
                    bool isMajor = (x % 500 == 0);
                    dc.DrawLine(isMajor ? gridMajorPen : gridPen, new Point(x, 0), new Point(x, height));
                }

                for (int y = 100; y < height; y += 100)
                {
                    bool isMajor = (y % 500 == 0);
                    dc.DrawLine(isMajor ? gridMajorPen : gridPen, new Point(0, y), new Point(width, y));
                }

                // Outer Perimeter Border (High Contrast Cyan)
                var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(56, 189, 248)), 6);
                dc.DrawRectangle(null, borderPen, new Rect(3, 3, width - 6, height - 6));

                // Four Corner Markers (Red Accent)
                double cornerSize = Math.Min(60, Math.Min(width, height) / 8.0);
                var cornerBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68));

                dc.DrawRectangle(cornerBrush, null, new Rect(6, 6, cornerSize, cornerSize));
                dc.DrawRectangle(cornerBrush, null, new Rect(width - 6 - cornerSize, 6, cornerSize, cornerSize));
                dc.DrawRectangle(cornerBrush, null, new Rect(6, height - 6 - cornerSize, cornerSize, cornerSize));
                dc.DrawRectangle(cornerBrush, null, new Rect(width - 6 - cornerSize, height - 6 - cornerSize, cornerSize, cornerSize));

                // Center Crosshair (Amber)
                double centerX = width / 2.0;
                double centerY = height / 2.0;
                var crossPen = new Pen(new SolidColorBrush(Color.FromRgb(245, 158, 11)), 3);

                dc.DrawLine(crossPen, new Point(centerX - 40, centerY), new Point(centerX + 40, centerY));
                dc.DrawLine(crossPen, new Point(centerX, centerY - 40), new Point(centerX, centerY + 40));
                dc.DrawEllipse(null, crossPen, new Point(centerX, centerY), 20, 20);

                // Labels
                Typeface typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
                Brush textBrush = Brushes.White;

                // Center Identity & Dimension Text
                double fontSize = Math.Max(20, Math.Min(width, height) / 22.0);
                FormattedText titleText = new FormattedText(
                    $"{cardTitle}\n{width} × {height} px",
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fontSize,
                    textBrush,
                    1.0)
                {
                    TextAlignment = TextAlignment.Center
                };

                dc.DrawText(titleText, new Point(centerX, centerY - titleText.Height - 30));

                // Edge Labels (TOP, BOTTOM, LEFT, RIGHT)
                DrawEdgeText(dc, "TOP", new Point(centerX, 15), TextAlignment.Center, typeface, textBrush);
                DrawEdgeText(dc, "BOTTOM", new Point(centerX, height - 40), TextAlignment.Center, typeface, textBrush);
                DrawEdgeText(dc, "LEFT", new Point(15, centerY - 15), TextAlignment.Left, typeface, textBrush);
                DrawEdgeText(dc, "RIGHT", new Point(width - 75, centerY - 15), TextAlignment.Left, typeface, textBrush);

                // Corner Labels (TL, TR, BL, BR)
                DrawEdgeText(dc, "TL", new Point(10, 10), TextAlignment.Left, typeface, Brushes.White);
                DrawEdgeText(dc, "TR", new Point(width - 35, 10), TextAlignment.Left, typeface, Brushes.White);
                DrawEdgeText(dc, "BL", new Point(10, height - 35), TextAlignment.Left, typeface, Brushes.White);
                DrawEdgeText(dc, "BR", new Point(width - 35, height - 35), TextAlignment.Left, typeface, Brushes.White);
            }

            var renderBitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            renderBitmap.Render(drawingVisual);
            renderBitmap.Freeze();
            return renderBitmap;
        }

        private static void DrawEdgeText(DrawingContext dc, string text, Point pos, TextAlignment align, Typeface typeface, Brush brush)
        {
            FormattedText ft = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                18,
                brush,
                1.0)
            {
                TextAlignment = align
            };
            dc.DrawText(ft, pos);
        }

        public static string GenerateGridPattern(int width, int height, string label)
        {
            return SaveTestPatternToTempFile(label, width, height);
        }

        public static string SaveTestPatternToTempFile(string cardTitle, int width, int height)
        {
            var bitmap = GenerateTestPattern(cardTitle, width, height);
            string tempDir = Path.Combine(Path.GetTempPath(), "RejiDisplay");
            Directory.CreateDirectory(tempDir);
            string uniqueId = Guid.NewGuid().ToString("N").Substring(0, 8);
            string tempPath = Path.Combine(tempDir, $"TestPattern_{cardTitle}_{width}x{height}_{uniqueId}.png");

            using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(fileStream);
            }

            return tempPath;
        }
    }
}
