using System;
using System.Windows;
using System.Windows.Media;
using RejiDisplay.Models;

namespace RejiDisplay.Helpers
{
    public struct LayoutRect
    {
        public double Width { get; set; }
        public double Height { get; set; }
        public double Left { get; set; }
        public double Top { get; set; }
    }

    public static class LayoutTransformHelper
    {
        public static LayoutRect CalculateLayoutRect(
            double imgWidth,
            double imgHeight,
            double viewportWidth,
            double viewportHeight,
            ImageLayoutState layout)
        {
            if (imgWidth <= 0 || imgHeight <= 0 || viewportWidth <= 0 || viewportHeight <= 0)
            {
                return new LayoutRect { Width = 0, Height = 0, Left = 0, Top = 0 };
            }

            double baseScaleX = 1.0;
            double baseScaleY = 1.0;

            switch (layout.ScaleMode)
            {
                case ScaleMode.Fit:
                    double fitRatio = Math.Min(viewportWidth / imgWidth, viewportHeight / imgHeight);
                    baseScaleX = fitRatio;
                    baseScaleY = fitRatio;
                    break;

                case ScaleMode.Fill:
                    double fillRatio = Math.Max(viewportWidth / imgWidth, viewportHeight / imgHeight);
                    baseScaleX = fillRatio;
                    baseScaleY = fillRatio;
                    break;

                case ScaleMode.Stretch:
                    baseScaleX = viewportWidth / imgWidth;
                    baseScaleY = viewportHeight / imgHeight;
                    break;

                case ScaleMode.Custom:
                    double customFit = Math.Min(viewportWidth / imgWidth, viewportHeight / imgHeight);
                    baseScaleX = customFit;
                    baseScaleY = customFit;
                    break;
            }

            double finalZoom = Math.Max(0.1, Math.Min(4.0, layout.Zoom));
            double renderedWidth = imgWidth * baseScaleX * finalZoom;
            double renderedHeight = imgHeight * baseScaleY * finalZoom;

            double left = (viewportWidth - renderedWidth) / 2.0 + layout.OffsetX;
            double top = (viewportHeight - renderedHeight) / 2.0 + layout.OffsetY;

            return new LayoutRect
            {
                Width = renderedWidth,
                Height = renderedHeight,
                Left = left,
                Top = top
            };
        }

        public static TransformGroup CalculateTransform(
            double imgWidth,
            double imgHeight,
            double viewportWidth,
            double viewportHeight,
            ImageLayoutState layout)
        {
            var transformGroup = new TransformGroup();

            if (imgWidth <= 0 || imgHeight <= 0 || viewportWidth <= 0 || viewportHeight <= 0)
            {
                return transformGroup;
            }

            double baseScaleX = 1.0;
            double baseScaleY = 1.0;

            switch (layout.ScaleMode)
            {
                case ScaleMode.Fit:
                    double fitRatio = Math.Min(viewportWidth / imgWidth, viewportHeight / imgHeight);
                    baseScaleX = fitRatio;
                    baseScaleY = fitRatio;
                    break;

                case ScaleMode.Fill:
                    double fillRatio = Math.Max(viewportWidth / imgWidth, viewportHeight / imgHeight);
                    baseScaleX = fillRatio;
                    baseScaleY = fillRatio;
                    break;

                case ScaleMode.Stretch:
                    baseScaleX = viewportWidth / imgWidth;
                    baseScaleY = viewportHeight / imgHeight;
                    break;

                case ScaleMode.Custom:
                    double customFit = Math.Min(viewportWidth / imgWidth, viewportHeight / imgHeight);
                    baseScaleX = customFit;
                    baseScaleY = customFit;
                    break;
            }

            double finalZoom = Math.Max(0.1, Math.Min(4.0, layout.Zoom));
            double scaleX = baseScaleX * finalZoom;
            double scaleY = baseScaleY * finalZoom;

            var scaleTransform = new ScaleTransform(scaleX, scaleY);
            var translateTransform = new TranslateTransform(layout.OffsetX, layout.OffsetY);

            transformGroup.Children.Add(scaleTransform);
            transformGroup.Children.Add(translateTransform);

            return transformGroup;
        }

        public static void ApplyLayoutTransform(
            FrameworkElement element,
            FrameworkElement? container,
            ScaleMode scaleMode,
            double zoomPercent,
            double offsetX,
            double offsetY,
            double rotationAngle,
            double imgWidth,
            double imgHeight,
            double targetLogicalWidth = 860.0,
            double targetLogicalHeight = 1720.0)
        {
            if (element == null) return;

            double containerW = container != null && container.ActualWidth > 0 ? container.ActualWidth : targetLogicalWidth;
            double containerH = container != null && container.ActualHeight > 0 ? container.ActualHeight : targetLogicalHeight;

            // Scale offsets proportionally if container differs from target logical bounds (e.g. preview viewport)
            double scaledOffsetX = offsetX * (containerW / targetLogicalWidth);
            double scaledOffsetY = offsetY * (containerH / targetLogicalHeight);

            var layout = new ImageLayoutState
            {
                ScaleMode = scaleMode,
                ZoomPercent = (int)zoomPercent,
                OffsetX = scaledOffsetX,
                OffsetY = scaledOffsetY
            };

            var rect = CalculateLayoutRect(imgWidth, imgHeight, containerW, containerH, layout);

            element.Width = rect.Width;
            element.Height = rect.Height;
            System.Windows.Controls.Canvas.SetLeft(element, rect.Left);
            System.Windows.Controls.Canvas.SetTop(element, rect.Top);

            if (element is System.Windows.Controls.Image img)
            {
                img.Stretch = Stretch.Fill;
            }

            if (rotationAngle != 0)
            {
                element.RenderTransform = new RotateTransform(rotationAngle, rect.Width / 2.0, rect.Height / 2.0);
            }
            else
            {
                element.RenderTransform = Transform.Identity;
            }
        }
    }
}


