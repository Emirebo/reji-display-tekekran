using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
using RejiDisplay.Models;

namespace RejiDisplay.Services
{
    public class OutputManager
    {
        private MasterOutputWindow? _masterWindow;
        private readonly Dictionary<string, OutputWindow> _legacyWindows = new(StringComparer.OrdinalIgnoreCase);

        public bool IsMasterOutputActive => _masterWindow != null && _masterWindow.IsLoaded;
        public DisplayInfo? ActiveMasterDisplay => _masterWindow?.TargetDisplay;

        public void StartMasterOutput(DisplayInfo display, MasterCanvasState state, BitmapImage? leftBmp, BitmapImage? rightBmp)
        {
            StopMasterOutput();

            _masterWindow = new MasterOutputWindow(display);
            _masterWindow.Closed += (s, e) => { _masterWindow = null; };

            _masterWindow.Show();
            _masterWindow.ApplyState(state, leftBmp, rightBmp);
        }

        public void StopMasterOutput()
        {
            if (_masterWindow != null)
            {
                var win = _masterWindow;
                _masterWindow = null;
                try
                {
                    win.Close();
                }
                catch
                {
                    // Ignore window close exceptions
                }
            }
        }

        public void ApplyMasterState(MasterCanvasState state, BitmapImage? leftBmp, BitmapImage? rightBmp)
        {
            if (_masterWindow != null && _masterWindow.IsLoaded)
            {
                _masterWindow.ApplyState(state, leftBmp, rightBmp);
            }
        }

        public void UpdateMiddleCaptureFrame(BitmapSource? frameBitmap)
        {
            if (_masterWindow != null && _masterWindow.IsLoaded)
            {
                _masterWindow.UpdateMiddleCaptureFrame(frameBitmap);
            }
        }

        public void SetMasterBlackout(bool isBlackout)
        {
            if (_masterWindow != null && _masterWindow.IsLoaded)
            {
                _masterWindow.SetMasterBlackout(isBlackout);
            }
        }

        // --- Legacy v0.2 Support ---

        public bool IsOutputActive(string cardId)
        {
            return _legacyWindows.ContainsKey(cardId) && _legacyWindows[cardId].IsLoaded;
        }

        public DisplayInfo? GetActiveDisplay(string cardId)
        {
            if (_legacyWindows.TryGetValue(cardId, out var win))
            {
                return win.TargetDisplay;
            }
            return null;
        }

        public void StartOutput(string cardId, DisplayInfo display, ScaleMode scaleMode, BitmapImage? imageBitmap, bool isBlackout)
        {
            StopOutput(cardId);
            var window = new OutputWindow(display);
            _legacyWindows[cardId] = window;
            window.Closed += (s, e) => { _legacyWindows.Remove(cardId); };
            window.Show();
            if (imageBitmap != null) window.SetImage(imageBitmap, scaleMode);
            else window.SetScaleMode(scaleMode);
            window.SetBlackout(isBlackout);
        }

        public void StopOutput(string cardId)
        {
            if (_legacyWindows.TryGetValue(cardId, out var window))
            {
                _legacyWindows.Remove(cardId);
                try { window.Close(); } catch { }
            }
        }

        public void StopAllOutputs()
        {
            StopMasterOutput();
            var cardIds = new List<string>(_legacyWindows.Keys);
            foreach (var id in cardIds)
            {
                StopOutput(id);
            }
        }

        public void UpdateScaleMode(string cardId, ScaleMode scaleMode)
        {
            if (_legacyWindows.TryGetValue(cardId, out var window))
            {
                window.SetScaleMode(scaleMode);
            }
        }

        public void UpdateBlackout(string cardId, bool isBlackout)
        {
            if (_legacyWindows.TryGetValue(cardId, out var window))
            {
                window.SetBlackout(isBlackout);
            }
        }

        public void UpdateMedia(string cardId, BitmapImage bitmap, ScaleMode scaleMode)
        {
            if (_legacyWindows.TryGetValue(cardId, out var window))
            {
                window.SetImage(bitmap, scaleMode);
            }
        }
    }
}
