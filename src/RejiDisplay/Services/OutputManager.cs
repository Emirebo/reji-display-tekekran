using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
using RejiDisplay.Models;

namespace RejiDisplay.Services
{
    public class OutputManager
    {
        private readonly Dictionary<string, OutputWindow> _activeWindows = new(StringComparer.OrdinalIgnoreCase);

        public bool IsOutputActive(string cardId)
        {
            return _activeWindows.ContainsKey(cardId) && _activeWindows[cardId].IsLoaded;
        }

        public DisplayInfo? GetActiveDisplay(string cardId)
        {
            if (_activeWindows.TryGetValue(cardId, out var win))
            {
                return win.TargetDisplay;
            }
            return null;
        }

        public void StartOutput(string cardId, DisplayInfo display, ScaleMode scaleMode, BitmapImage? imageBitmap, bool isBlackout)
        {
            // Close existing window for this card if any
            StopOutput(cardId);

            var window = new OutputWindow(display);
            _activeWindows[cardId] = window;

            window.Closed += (s, e) =>
            {
                if (_activeWindows.TryGetValue(cardId, out var existing) && existing == window)
                {
                    _activeWindows.Remove(cardId);
                }
            };

            window.Show();
            if (imageBitmap != null)
            {
                window.SetImage(imageBitmap, scaleMode);
            }
            else
            {
                window.SetScaleMode(scaleMode);
            }

            window.SetBlackout(isBlackout);
        }

        public void StopOutput(string cardId)
        {
            if (_activeWindows.TryGetValue(cardId, out var window))
            {
                _activeWindows.Remove(cardId);
                try
                {
                    window.Close();
                }
                catch
                {
                    // Ignore window close exceptions
                }
            }
        }

        public void StopAllOutputs()
        {
            var cardIds = new List<string>(_activeWindows.Keys);
            foreach (var id in cardIds)
            {
                StopOutput(id);
            }
        }

        public void UpdateScaleMode(string cardId, ScaleMode scaleMode)
        {
            if (_activeWindows.TryGetValue(cardId, out var window))
            {
                window.SetScaleMode(scaleMode);
            }
        }

        public void UpdateBlackout(string cardId, bool isBlackout)
        {
            if (_activeWindows.TryGetValue(cardId, out var window))
            {
                window.SetBlackout(isBlackout);
            }
        }

        public void UpdateMedia(string cardId, BitmapImage bitmap, ScaleMode scaleMode)
        {
            if (_activeWindows.TryGetValue(cardId, out var window))
            {
                window.SetImage(bitmap, scaleMode);
            }
        }
    }
}
