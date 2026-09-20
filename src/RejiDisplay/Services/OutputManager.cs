using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
using RejiDisplay.Helpers;
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

        public void StartOutput(
            string cardId,
            DisplayInfo display,
            OutputCalibration calibration,
            ImageLayoutState liveAppliedLayout,
            BitmapImage? imageBitmap,
            bool isBlackout)
        {
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
            window.RenderLiveAppliedState(calibration, liveAppliedLayout, imageBitmap, isBlackout);
        }

        public void UpdateLiveOutput(
            string cardId,
            OutputCalibration calibration,
            ImageLayoutState liveAppliedLayout,
            BitmapImage? imageBitmap,
            bool isBlackout)
        {
            if (_activeWindows.TryGetValue(cardId, out var window))
            {
                window.RenderLiveAppliedState(calibration, liveAppliedLayout, imageBitmap, isBlackout);
            }
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

        // --- SINGLE MASTER OUTPUT LIFECYCLE ---
        private MasterOutputWindow? _masterWindow;

        public OutputLifecycleState MasterState { get; private set; } = OutputLifecycleState.Stopped;
        public string? MasterError { get; private set; }

        public bool IsMasterOutputActive => _masterWindow != null && _masterWindow.IsLoaded && MasterState == OutputLifecycleState.Running;
        public DisplayInfo? ActiveMasterDisplay => _masterWindow?.TargetDisplay;

        public bool StartMasterOutput(DisplayInfo display, MasterCanvasState state, BitmapImage? leftBmp, BitmapImage? rightBmp, bool isDiagnosticMode = false)
        {
            StopMasterOutput();

            MasterState = OutputLifecycleState.Starting;
            MasterError = null;
            Logger.Log($"[OutputManager] Starting Master Output on display: {display.DisplayLabel} ({display.DeviceName})");

            try
            {
                _masterWindow = new MasterOutputWindow(display);
                _masterWindow.IsDiagnosticMode = isDiagnosticMode;

                _masterWindow.Closed += (s, e) =>
                {
                    _masterWindow = null;
                    MasterState = OutputLifecycleState.Stopped;
                    Logger.Log("[OutputManager] MasterOutputWindow closed.");
                };

                _masterWindow.Show();
                _masterWindow.PositionOnDisplay(display);
                _masterWindow.ApplyState(state, leftBmp, rightBmp);

                MasterState = OutputLifecycleState.Running;
                Logger.Log($"[OutputManager] Master Output successfully running on {display.DisplayLabel}");
                return true;
            }
            catch (Exception ex)
            {
                MasterState = OutputLifecycleState.Failed;
                MasterError = ex.Message;
                Logger.LogError("[OutputManager] Failed to start Master Output", ex);
                StopMasterOutput();
                return false;
            }
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
                catch (Exception ex)
                {
                    Logger.LogError("[OutputManager] Exception closing MasterOutputWindow", ex);
                }
            }
            MasterState = OutputLifecycleState.Stopped;
            Logger.Log("[OutputManager] Master Output stopped.");
        }

        public void ApplyMasterState(MasterCanvasState state, BitmapImage? leftBmp, BitmapImage? rightBmp)
        {
            if (_masterWindow != null && _masterWindow.IsLoaded)
            {
                _masterWindow.ApplyState(state, leftBmp, rightBmp);
                Logger.Log("[OutputManager] Master state updated and applied.");
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
                Logger.Log($"[OutputManager] Master Blackout set to: {isBlackout}");
            }
        }

        public void StopAllOutputs()
        {
            StopMasterOutput();
            var cardIds = new List<string>(_activeWindows.Keys);
            foreach (var id in cardIds)
            {
                StopOutput(id);
            }
        }

        public void UpdateBlackout(string cardId, bool isBlackout)
        {
            if (_activeWindows.TryGetValue(cardId, out var window))
            {
                window.SetBlackout(isBlackout);
            }
        }
    }
}
