using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RejiDisplay.Helpers;
using RejiDisplay.Models;
using RejiDisplay.Services;

namespace RejiDisplay
{
    public partial class MainWindow : Window
    {
        private readonly DisplayService _displayService;
        private readonly SettingsService _settingsService;
        private readonly OutputManager _outputManager;
        private readonly VenuePresetService _venuePresetService;
        private readonly PresentationCaptureService _captureService;

        private List<DisplayInfo> _allDisplays = new();
        private DisplayInfo? _controlDisplay;            // Display 1 — Operator UI
        private DisplayInfo? _presentationSourceDisplay; // Display 2 — PowerPoint Capture Source
        private DisplayInfo? _masterOutputDisplay;       // Display 3 — NovaStar Master Output
        private DisplayInfo? _reservedCenterDisplay;     // Legacy Reserved Center Display

        private OutputCardState _leftState = new() { CardId = "LEFT", Title = "LEFT LED" };
        private OutputCardState _rightState = new() { CardId = "RIGHT", Title = "RIGHT LED" };

        private MasterCanvasState _masterDraftState = new();
        private MasterCanvasState _masterLiveState = new();

        private AppSettings _appSettings = new();
        private bool _isInitializing = true;
        private bool _isUpdatingUI = false;
        private bool _isSimulationMode = false;

        public MainWindow()
        {
            _isInitializing = true;
            _displayService = new DisplayService();
            _settingsService = new SettingsService();
            _outputManager = new OutputManager();
            _venuePresetService = new VenuePresetService();
            _captureService = new PresentationCaptureService();

            InitializeComponent();

            _displayService.DisplayTopologyChanged += OnDisplayTopologyChanged;
            _captureService.FrameArrived += OnCaptureFrameArrived;
            _captureService.CaptureError += OnCaptureError;

            Loaded += MainWindow_Loaded;
            Unloaded += MainWindow_Unloaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _isUpdatingUI = true;
            try
            {
                _appSettings = _settingsService.LoadSettings();
                _isSimulationMode = _appSettings.IsSimulationMode;

                if (_appSettings.MiddleYOffset <= 0)
                {
                    _appSettings.MiddleYOffset = MasterCanvasGeometry.DefaultMiddleYOffset;
                }

                if (SliderMiddleYOffset != null) SliderMiddleYOffset.Value = _appSettings.MiddleYOffset;
                _masterDraftState.MiddleYOffset = _appSettings.MiddleYOffset;

                PopulateVenuePresets();
                RefreshDisplaysAndUI();
                RestoreSavedSettings();

                // Startup Logging (Priority 2)
                try
                {
                    string execPath = BuildInfo.GetExecutablePath();
                    string ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.3.0";
                    var dispList = _allDisplays.Select(d => $"{d.FriendlyName} [{d.DeviceName}] ({d.Width}x{d.Height}@{d.RefreshRate}Hz)").ToList();
                    var gpuList = new List<string>();
                    try
                    {
                        if (Vortice.DXGI.DXGI.CreateDXGIFactory1(out Vortice.DXGI.IDXGIFactory1? factory).Success && factory != null)
                        {
                            using (factory)
                            {
                                for (uint a = 0; factory.EnumAdapters1(a, out Vortice.DXGI.IDXGIAdapter1? adapter).Success; a++)
                                {
                                    if (adapter != null)
                                    {
                                        gpuList.Add($"Adapter {a}: {adapter.Description.Description}");
                                        adapter.Dispose();
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                        gpuList.Add("DXGI Factory Enum Unavailable");
                    }

                    Logger.Log($"[BUILD_IDENTITY] {BuildInfo.BuildBanner} | Built: {BuildInfo.BuildTimestamp}");

                    Logger.LogStartup(
                        execPath,
                        ver,
                        BuildInfo.GitCommitHash,
                        dispList,
                        _presentationSourceDisplay?.DeviceName,
                        gpuList
                    );

                    if (TxtGlobalStatus != null)
                    {
                        TxtGlobalStatus.Text = $"🚀 RejiDisplay v0.3 {BuildInfo.BuildBanner}";
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError("Startup Logging Error", ex);
                }
            }
            finally
            {
                _isUpdatingUI = false;
                _isInitializing = false;
            }

            if (_presentationSourceDisplay != null)
            {
                _captureService.StartCapture(_presentationSourceDisplay);
            }

            RenderDraftPreview("LEFT");
            RenderDraftPreview("RIGHT");
            UpdateMasterUIState();
        }

        private void BtnOpenLogFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string logFile = Logger.LogFilePath;
                if (System.IO.File.Exists(logFile))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{logFile}\"") { UseShellExecute = true });
                    if (TxtGlobalStatus != null) TxtGlobalStatus.Text = $"📋 Log dosyası açıldı: {logFile}";
                }
                else
                {
                    string dir = System.IO.Path.GetDirectoryName(logFile) ?? string.Empty;
                    if (System.IO.Directory.Exists(dir))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
                        if (TxtGlobalStatus != null) TxtGlobalStatus.Text = $"📋 Log klasörü açıldı: {dir}";
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Open Log File Error", ex);
            }
        }

        private void MainWindow_Unloaded(object sender, RoutedEventArgs e)
        {
            _captureService.Dispose();
            _outputManager.StopAllOutputs();
        }

        private void OnDisplayTopologyChanged(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (TxtGlobalStatus != null) TxtGlobalStatus.Text = "Ekran yapısı değişti. Ekranlar taranıyor...";
                RefreshDisplaysAndUI();
            });
        }

        private void OnCaptureFrameArrived(object? sender, FrameArrivedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (ImgMiddleMasterPreview != null) ImgMiddleMasterPreview.Source = e.Frame;
                string fpsStr = $"{e.Fps:F1}";
                if (TxtCaptureFps != null) TxtCaptureFps.Text = fpsStr;

                if (TxtHeaderCapture != null && DotHeaderCapture != null)
                {
                    if (e.CaptureMode.Contains("WGC_GPU"))
                    {
                        TxtHeaderCapture.Text = "🟢 WGC GPU";
                        TxtHeaderCapture.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                        DotHeaderCapture.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                    }
                    else if (e.CaptureMode.Contains("DXGI_GPU"))
                    {
                        TxtHeaderCapture.Text = "🟢 DXGI GPU";
                        TxtHeaderCapture.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                        DotHeaderCapture.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                    }
                    else
                    {
                        TxtHeaderCapture.Text = "⚠️ WIN32_GDI";
                        TxtHeaderCapture.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                        DotHeaderCapture.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                    }
                }

                if (_outputManager.IsMasterOutputActive)
                {
                    _outputManager.UpdateMiddleCaptureFrame(e.Frame);
                }
            });
        }

        private void OnCaptureError(object? sender, string err)
        {
            Dispatcher.Invoke(() =>
            {
                if (TxtGlobalStatus != null) TxtGlobalStatus.Text = $"Sunum Yakalama Uyarısı: {err}";
                if (TxtHeaderCapture != null && DotHeaderCapture != null)
                {
                    TxtHeaderCapture.Text = "❌ HATA";
                    TxtHeaderCapture.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                    DotHeaderCapture.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                }
            });
        }

        private void PopulateVenuePresets()
        {
            if (CmbVenuePresets == null) return;
            CmbVenuePresets.SelectionChanged -= CmbVenuePresets_SelectionChanged;
            CmbVenuePresets.Items.Clear();

            var presets = _venuePresetService.GetPresets();
            foreach (var preset in presets)
            {
                CmbVenuePresets.Items.Add(new ComboBoxItem
                {
                    Content = preset.Name,
                    Tag = preset
                });
            }

            if (!string.IsNullOrEmpty(_appSettings.SelectedVenuePresetName))
            {
                foreach (ComboBoxItem item in CmbVenuePresets.Items)
                {
                    if (item.Tag is VenuePreset p && string.Equals(p.Name, _appSettings.SelectedVenuePresetName, StringComparison.OrdinalIgnoreCase))
                    {
                        CmbVenuePresets.SelectedItem = item;
                        break;
                    }
                }
            }

            CmbVenuePresets.SelectionChanged += CmbVenuePresets_SelectionChanged;
        }

        private void RefreshDisplaysAndUI()
        {
            if (_isSimulationMode)
            {
                _allDisplays = _displayService.GetSimulatedDisplays();
                if (TxtGlobalStatus != null) TxtGlobalStatus.Text = "🧪 SİMÜLASYON MODU AKTİF — 3 Sanal Ekran Kullanılıyor.";
            }
            else
            {
                _allDisplays = _displayService.GetDisplays();
            }

            _controlDisplay = _displayService.GetPrimaryDisplay(_allDisplays) ?? _allDisplays.FirstOrDefault();
            if (TxtControlDisplayLabel != null) TxtControlDisplayLabel.Text = _controlDisplay != null ? _controlDisplay.DisplayLabel : "Tespit Edilemedi";

            // Reserved Center Combo
            if (CmbReservedCenter != null)
            {
                CmbReservedCenter.SelectionChanged -= CmbReservedCenter_SelectionChanged;
                CmbReservedCenter.Items.Clear();
                CmbReservedCenter.Items.Add(new ComboBoxItem { Content = "-- Seçilmedi / Varsayılan --", Tag = null });

                foreach (var display in _allDisplays)
                {
                    CmbReservedCenter.Items.Add(new ComboBoxItem { Content = display.DisplayLabel, Tag = display });
                }

                if (!string.IsNullOrEmpty(_appSettings.ReservedCenterDeviceName) || !string.IsNullOrEmpty(_appSettings.ReservedCenterDeviceId))
                {
                    _reservedCenterDisplay = _displayService.FindMatchingDisplay(_allDisplays, _appSettings.ReservedCenterDeviceName, _appSettings.ReservedCenterDeviceId);
                }

                SelectComboItem(CmbReservedCenter, _reservedCenterDisplay);
                CmbReservedCenter.SelectionChanged += CmbReservedCenter_SelectionChanged;
            }

            UpdateRoleComboBoxes();
            UpdateLegacyCardDisplayComboBoxes();
        }

        private void UpdateRoleComboBoxes()
        {
            bool wasUpdating = _isUpdatingUI;
            _isUpdatingUI = true;
            try
            {
                // Display 2 (Presentation Source)
                if (CmbPresentationSource != null)
                {
                    CmbPresentationSource.SelectionChanged -= CmbPresentationSource_SelectionChanged;
                    CmbPresentationSource.Items.Clear();
                    CmbPresentationSource.Items.Add(new ComboBoxItem { Content = "-- Sunum Ekranı Seçilmedi --", Tag = null });

                    var presentationAssignables = _displayService.GetAssignablePresentationSources(_allDisplays, _masterOutputDisplay);
                    foreach (var d in presentationAssignables)
                    {
                        var item = new ComboBoxItem { Content = d.DisplayLabel, Tag = d };
                        CmbPresentationSource.Items.Add(item);
                        if (_presentationSourceDisplay != null && _displayService.IsSameDisplay(d, _presentationSourceDisplay))
                        {
                            CmbPresentationSource.SelectedItem = item;
                        }
                    }
                    if (CmbPresentationSource.SelectedItem == null) CmbPresentationSource.SelectedIndex = 0;
                    CmbPresentationSource.SelectionChanged += CmbPresentationSource_SelectionChanged;
                }

                // Display 3 (Master LED Output)
                if (CmbMasterOutput != null)
                {
                    CmbMasterOutput.SelectionChanged -= CmbMasterOutput_SelectionChanged;
                    CmbMasterOutput.Items.Clear();
                    CmbMasterOutput.Items.Add(new ComboBoxItem { Content = "-- Master LED Ekranı Seçilmedi --", Tag = null });

                    var masterAssignables = _displayService.GetAssignableMasterOutputs(_allDisplays, _presentationSourceDisplay);
                    foreach (var d in masterAssignables)
                    {
                        var item = new ComboBoxItem { Content = d.DisplayLabel, Tag = d };
                        CmbMasterOutput.Items.Add(item);
                        if (_masterOutputDisplay != null && _displayService.IsSameDisplay(d, _masterOutputDisplay))
                        {
                            CmbMasterOutput.SelectedItem = item;
                        }
                    }
                    if (CmbMasterOutput.SelectedItem == null) CmbMasterOutput.SelectedIndex = 0;
                    CmbMasterOutput.SelectionChanged += CmbMasterOutput_SelectionChanged;
                }
            }
            finally
            {
                _isUpdatingUI = wasUpdating;
            }
        }

        private void UpdateLegacyCardDisplayComboBoxes()
        {
            // Single Master Output mode uses master display selection in Settings modal
        }

        private void RestoreSavedSettings()
        {
            // Restore Display 2 (Presentation)
            if (!string.IsNullOrEmpty(_appSettings.PresentationDeviceName) || !string.IsNullOrEmpty(_appSettings.PresentationDeviceId))
            {
                var match = _displayService.FindMatchingDisplay(_allDisplays, _appSettings.PresentationDeviceName, _appSettings.PresentationDeviceId);
                if (match != null && CmbPresentationSource != null)
                {
                    _presentationSourceDisplay = match;
                    SelectComboItem(CmbPresentationSource, match);
                }
            }

            // Auto-select first assignable presentation source if none saved
            if (_presentationSourceDisplay == null)
            {
                var assignable = _displayService.GetAssignablePresentationSources(_allDisplays, _masterOutputDisplay);
                if (assignable.Count > 0)
                {
                    _presentationSourceDisplay = assignable[0];
                    if (CmbPresentationSource != null) SelectComboItem(CmbPresentationSource, _presentationSourceDisplay);
                }
            }

            // Restore Display 3 (Master Output)
            if (!string.IsNullOrEmpty(_appSettings.MasterOutputDeviceName) || !string.IsNullOrEmpty(_appSettings.MasterOutputDeviceId))
            {
                var match = _displayService.FindMatchingDisplay(_allDisplays, _appSettings.MasterOutputDeviceName, _appSettings.MasterOutputDeviceId);
                if (match != null && CmbMasterOutput != null)
                {
                    _masterOutputDisplay = match;
                    SelectComboItem(CmbMasterOutput, match);
                }
            }

            // Restore Left Output
            if (_appSettings.LeftOutput != null)
            {
                _leftState.ScaleMode = _appSettings.LeftOutput.ScaleMode;
                _leftState.DraftLayout = _appSettings.LeftOutput.DraftLayout.Clone();
                _leftState.LiveAppliedLayout = _appSettings.LeftOutput.LiveAppliedLayout.Clone();
                _leftState.Calibration = _appSettings.LeftOutput.Calibration.Clone();
                _leftState.IsBlackout = _appSettings.LeftOutput.IsBlackout;

                if (!string.IsNullOrEmpty(_leftState.DraftLayout.MediaPath))
                {
                    LoadMediaForCard("LEFT", _leftState.DraftLayout.MediaPath);
                }
            }

            // Restore Right Output
            if (_appSettings.RightOutput != null)
            {
                _rightState.ScaleMode = _appSettings.RightOutput.ScaleMode;
                _rightState.DraftLayout = _appSettings.RightOutput.DraftLayout.Clone();
                _rightState.LiveAppliedLayout = _appSettings.RightOutput.LiveAppliedLayout.Clone();
                _rightState.Calibration = _appSettings.RightOutput.Calibration.Clone();
                _rightState.IsBlackout = _appSettings.RightOutput.IsBlackout;

                if (!string.IsNullOrEmpty(_rightState.DraftLayout.MediaPath))
                {
                    LoadMediaForCard("RIGHT", _rightState.DraftLayout.MediaPath);
                }
            }

            // Priority 5: Default Website URL (https://uulive.ai.studio/?mode=projector)
            string defaultWebUrl = "https://uulive.ai.studio/?mode=projector";

            if (string.IsNullOrWhiteSpace(_leftState.WebUrl))
            {
                _leftState.WebUrl = defaultWebUrl;
            }
            if (TxtLeftWebUrl != null) TxtLeftWebUrl.Text = _leftState.WebUrl;

            if (string.IsNullOrWhiteSpace(_rightState.WebUrl))
            {
                _rightState.WebUrl = defaultWebUrl;
            }
            if (TxtRightWebUrl != null) TxtRightWebUrl.Text = _rightState.WebUrl;
        }

        private void SelectComboItem(ComboBox combo, DisplayInfo? target)
        {
            if (combo == null) return;

            if (target == null)
            {
                combo.SelectedIndex = 0;
                return;
            }

            foreach (ComboBoxItem item in combo.Items)
            {
                if (item.Tag is DisplayInfo d && _displayService.IsSameDisplay(d, target))
                {
                    combo.SelectedItem = item;
                    break;
                }
            }
        }

        private DisplayInfo? GetSelectedDisplay(ComboBox combo)
        {
            if (combo != null && combo.SelectedItem is ComboBoxItem item && item.Tag is DisplayInfo d)
            {
                return d;
            }
            return null;
        }

        // --- Event Handlers ---

        private void CmbPresentationSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || _isUpdatingUI) return;

            var selected = GetSelectedDisplay(CmbPresentationSource);
            _presentationSourceDisplay = selected;

            if (selected != null)
            {
                _appSettings.PresentationDeviceName = selected.DeviceName;
                _appSettings.PresentationDeviceId = selected.DeviceId;
                _settingsService.SaveSettings(_appSettings);

                if (TxtGlobalStatus != null) TxtGlobalStatus.Text = $"Display 2 Sunum Yakalama Kaynağı Seçildi: {selected.FriendlyName}";
                _captureService.StartCapture(selected);
            }
            else
            {
                _appSettings.PresentationDeviceName = null;
                _appSettings.PresentationDeviceId = null;
                _settingsService.SaveSettings(_appSettings);
                _captureService.StopCapture();
                if (ImgMiddleMasterPreview != null) ImgMiddleMasterPreview.Source = null;
            }

            UpdateRoleComboBoxes();
        }

        private void CmbMasterOutput_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || _isUpdatingUI) return;

            var selected = GetSelectedDisplay(CmbMasterOutput);
            _masterOutputDisplay = selected;

            if (selected != null)
            {
                _appSettings.MasterOutputDeviceName = selected.DeviceName;
                _appSettings.MasterOutputDeviceId = selected.DeviceId;
                _settingsService.SaveSettings(_appSettings);
                if (TxtGlobalStatus != null) TxtGlobalStatus.Text = $"Display 3 Master LED Çıkışı Seçildi: {selected.FriendlyName}";
            }
            else
            {
                _appSettings.MasterOutputDeviceName = null;
                _appSettings.MasterOutputDeviceId = null;
                _settingsService.SaveSettings(_appSettings);
            }

            UpdateRoleComboBoxes();
        }

        private void SliderMiddleYOffset_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing || _isUpdatingUI || TxtMiddleYOffset == null) return;

            int val = (int)e.NewValue;
            _masterDraftState.MiddleYOffset = val;
            TxtMiddleYOffset.Text = $"{val} px {(val == 172 ? "(Ortalanmış)" : "")}";

            if (_appSettings != null && _settingsService != null)
            {
                _appSettings.MiddleYOffset = val;
                _settingsService.SaveSettings(_appSettings);
            }
        }

        private void CmbVenuePresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || _isUpdatingUI) return;

            if (CmbVenuePresets.SelectedItem is ComboBoxItem item && item.Tag is VenuePreset preset)
            {
                _leftState.Calibration = preset.LeftCalibration.Clone();
                _rightState.Calibration = preset.RightCalibration.Clone();

                _appSettings.SelectedVenuePresetName = preset.Name;
                _settingsService.SaveSettings(_appSettings);

                RenderDraftPreview("LEFT");
                RenderDraftPreview("RIGHT");

                if (TxtGlobalStatus != null) TxtGlobalStatus.Text = $"Venue Preset Uygulandı: {preset.Name}";
            }
        }

        private void CmbReservedCenter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || _isUpdatingUI) return;

            _reservedCenterDisplay = GetSelectedDisplay(CmbReservedCenter);

            _appSettings.ReservedCenterDeviceName = _reservedCenterDisplay?.DeviceName;
            _appSettings.ReservedCenterDeviceId = _reservedCenterDisplay?.DeviceId;
            _settingsService.SaveSettings(_appSettings);

            UpdateLegacyCardDisplayComboBoxes();
        }

        private void CmbLeftDisplay_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }

        private void CmbRightDisplay_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }

        private void BtnOpenSettings_Click(object sender, RoutedEventArgs e)
        {
            if (SettingsModal != null) SettingsModal.Visibility = Visibility.Visible;
        }

        private void BtnCloseSettings_Click(object sender, RoutedEventArgs e)
        {
            if (SettingsModal != null) SettingsModal.Visibility = Visibility.Collapsed;
        }

        private void DropZone_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void DropZoneLeft_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0) LoadMediaForCard("LEFT", files[0]);
            }
        }

        private void DropZoneRight_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0) LoadMediaForCard("RIGHT", files[0]);
            }
        }

        private bool LoadMediaForCard(string cardId, string filePath)
        {
            if (!MediaValidationHelper.ValidateMediaFile(filePath, out string err))
            {
                if (TxtGlobalStatus != null) TxtGlobalStatus.Text = $"HATA ({cardId}): {err}";
                return false;
            }

            var mediaSource = MediaSource.FromFile(filePath);
            var mediaType = mediaSource.Type;

            var card = cardId == "LEFT" ? _leftState : _rightState;
            card.DraftLayout.MediaPath = filePath;
            card.DraftLayout.MediaType = mediaType;

            if (cardId == "LEFT")
            {
                _appSettings.LeftOutput.DraftLayout.MediaPath = filePath;
                if (TxtLeftMediaPath != null) TxtLeftMediaPath.Text = Path.GetFileName(filePath);
                if (ImgLeftMasterPreview != null) ImgLeftMasterPreview.Source = card.LoadedBitmap;
            }
            else
            {
                _appSettings.RightOutput.DraftLayout.MediaPath = filePath;
                if (TxtRightMediaPath != null) TxtRightMediaPath.Text = Path.GetFileName(filePath);
                if (ImgRightMasterPreview != null) ImgRightMasterPreview.Source = card.LoadedBitmap;
            }

            _settingsService.SaveSettings(_appSettings);
            RenderDraftPreview(cardId);

            if (TxtGlobalStatus != null) TxtGlobalStatus.Text = $"{cardId} Medya yüklendi ({mediaType}): {Path.GetFileName(filePath)}. Yayına almak için TAKE butonuna basın.";
            return true;
        }

        private void RenderDraftPreview(string cardId)
        {
            var card = cardId == "LEFT" ? _leftState : _rightState;
            var viewport = cardId == "LEFT" ? CanvasLeftPreviewViewport : CanvasRightPreviewViewport;
            var img = cardId == "LEFT" ? ImgLeftPreview : ImgRightPreview;
            var video = cardId == "LEFT" ? MediaLeftPreviewVideo : MediaRightPreviewVideo;
            var prompt = cardId == "LEFT" ? PanelLeftDropPrompt : PanelRightDropPrompt;

            if (viewport == null || img == null || video == null || prompt == null) return;

            if (string.IsNullOrEmpty(card.DraftLayout.MediaPath))
            {
                prompt.Visibility = Visibility.Visible;
                img.Visibility = Visibility.Collapsed;
                video.Visibility = Visibility.Collapsed;
                return;
            }

            prompt.Visibility = Visibility.Collapsed;

            if (card.DraftLayout.MediaType == MediaSourceType.Video)
            {
                img.Visibility = Visibility.Collapsed;
                video.Visibility = Visibility.Visible;
                if (video.Source == null || video.Source.LocalPath != card.DraftLayout.MediaPath)
                {
                    video.Source = new Uri(card.DraftLayout.MediaPath, UriKind.Absolute);
                }
                video.Play();
            }
            else
            {
                video.Stop();
                video.Visibility = Visibility.Collapsed;
                img.Visibility = Visibility.Visible;

                if (card.LoadedBitmap == null || card.LoadedBitmap.UriSource?.LocalPath != card.DraftLayout.MediaPath)
                {
                    card.LoadedBitmap = CreateBitmap(card.DraftLayout.MediaPath);
                }

                img.Source = card.LoadedBitmap;
                if (cardId == "LEFT" && ImgLeftMasterPreview != null) ImgLeftMasterPreview.Source = card.LoadedBitmap;
                else if (cardId == "RIGHT" && ImgRightMasterPreview != null) ImgRightMasterPreview.Source = card.LoadedBitmap;
            }

            FrameworkElement targetControl = card.DraftLayout.MediaType == MediaSourceType.Video ? video : img;
            double mediaW = card.DraftLayout.MediaType == MediaSourceType.Video
                ? (video.NaturalVideoWidth > 0 ? video.NaturalVideoWidth : 1920)
                : (card.LoadedBitmap?.PixelWidth > 0 ? card.LoadedBitmap.PixelWidth : 860);
            double mediaH = card.DraftLayout.MediaType == MediaSourceType.Video
                ? (video.NaturalVideoHeight > 0 ? video.NaturalVideoHeight : 1080)
                : (card.LoadedBitmap?.PixelHeight > 0 ? card.LoadedBitmap.PixelHeight : 1720);

            LayoutTransformHelper.ApplyLayoutTransform(
                targetControl,
                viewport,
                card.DraftLayout.ScaleMode,
                card.DraftLayout.ZoomPercent,
                card.DraftLayout.OffsetX,
                card.DraftLayout.OffsetY,
                card.Calibration.RotationAngle,
                mediaW,
                mediaH);
        }

        private BitmapImage? CreateBitmap(string filePath)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        // --- Layout Controls Handlers ---

        private void RadioLeftScale_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _isUpdatingUI || RadioLeftFit == null) return;
            if (RadioLeftFit.IsChecked == true) _leftState.DraftLayout.ScaleMode = ScaleMode.Fit;
            else if (RadioLeftFill.IsChecked == true) _leftState.DraftLayout.ScaleMode = ScaleMode.Fill;
            else if (RadioLeftStretch.IsChecked == true) _leftState.DraftLayout.ScaleMode = ScaleMode.Stretch;
            else if (RadioLeftCustom.IsChecked == true) _leftState.DraftLayout.ScaleMode = ScaleMode.Custom;

            RenderDraftPreview("LEFT");
        }

        private void RadioRightScale_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _isUpdatingUI || RadioRightFit == null) return;
            if (RadioRightFit.IsChecked == true) _rightState.DraftLayout.ScaleMode = ScaleMode.Fit;
            else if (RadioRightFill.IsChecked == true) _rightState.DraftLayout.ScaleMode = ScaleMode.Fill;
            else if (RadioRightStretch.IsChecked == true) _rightState.DraftLayout.ScaleMode = ScaleMode.Stretch;
            else if (RadioRightCustom.IsChecked == true) _rightState.DraftLayout.ScaleMode = ScaleMode.Custom;

            RenderDraftPreview("RIGHT");
        }

        private void SliderLeftLayout_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing || _isUpdatingUI || SliderLeftZoom == null || SliderLeftOffsetX == null || SliderLeftOffsetY == null || TxtLeftZoom == null || TxtLeftOffsetX == null || TxtLeftOffsetY == null) return;

            _leftState.DraftLayout.ZoomPercent = (int)SliderLeftZoom.Value;
            _leftState.DraftLayout.OffsetX = (int)SliderLeftOffsetX.Value;
            _leftState.DraftLayout.OffsetY = (int)SliderLeftOffsetY.Value;

            TxtLeftZoom.Text = $"{_leftState.DraftLayout.ZoomPercent}%";
            TxtLeftOffsetX.Text = $"{_leftState.DraftLayout.OffsetX} px";
            TxtLeftOffsetY.Text = $"{_leftState.DraftLayout.OffsetY} px";

            RenderDraftPreview("LEFT");
        }

        private void SliderRightLayout_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing || _isUpdatingUI || SliderRightZoom == null || SliderRightOffsetX == null || SliderRightOffsetY == null || TxtRightZoom == null || TxtRightOffsetX == null || TxtRightOffsetY == null) return;

            _rightState.DraftLayout.ZoomPercent = (int)SliderRightZoom.Value;
            _rightState.DraftLayout.OffsetX = (int)SliderRightOffsetX.Value;
            _rightState.DraftLayout.OffsetY = (int)SliderRightOffsetY.Value;

            TxtRightZoom.Text = $"{_rightState.DraftLayout.ZoomPercent}%";
            TxtRightOffsetX.Text = $"{_rightState.DraftLayout.OffsetX} px";
            TxtRightOffsetY.Text = $"{_rightState.DraftLayout.OffsetY} px";

            RenderDraftPreview("RIGHT");
        }

        private void BtnLeftResetLayout_Click(object sender, RoutedEventArgs e)
        {
            _leftState.DraftLayout.ResetTransforms();
            _isUpdatingUI = true;
            if (SliderLeftZoom != null) SliderLeftZoom.Value = 100;
            if (SliderLeftOffsetX != null) SliderLeftOffsetX.Value = 0;
            if (SliderLeftOffsetY != null) SliderLeftOffsetY.Value = 0;
            if (RadioLeftFit != null) RadioLeftFit.IsChecked = true;
            _isUpdatingUI = false;
            RenderDraftPreview("LEFT");
        }

        private void BtnRightResetLayout_Click(object sender, RoutedEventArgs e)
        {
            _rightState.DraftLayout.ResetTransforms();
            _isUpdatingUI = true;
            if (SliderRightZoom != null) SliderRightZoom.Value = 100;
            if (SliderRightOffsetX != null) SliderRightOffsetX.Value = 0;
            if (SliderRightOffsetY != null) SliderRightOffsetY.Value = 0;
            if (RadioRightFit != null) RadioRightFit.IsChecked = true;
            _isUpdatingUI = false;
            RenderDraftPreview("RIGHT");
        }

        // --- Video Control Handlers ---

        private void BtnLeftPlay_Click(object sender, RoutedEventArgs e) { MediaLeftPreviewVideo?.Play(); }
        private void BtnLeftPause_Click(object sender, RoutedEventArgs e) { MediaLeftPreviewVideo?.Pause(); }
        private void BtnLeftStopVideo_Click(object sender, RoutedEventArgs e) { MediaLeftPreviewVideo?.Stop(); }

        private void BtnRightPlay_Click(object sender, RoutedEventArgs e) { MediaRightPreviewVideo?.Play(); }
        private void BtnRightPause_Click(object sender, RoutedEventArgs e) { MediaRightPreviewVideo?.Pause(); }
        private void BtnRightStopVideo_Click(object sender, RoutedEventArgs e) { MediaRightPreviewVideo?.Stop(); }

        private void ChkLeftLoop_Click(object sender, RoutedEventArgs e) { }
        private void ChkLeftMute_Click(object sender, RoutedEventArgs e) { if (MediaLeftPreviewVideo != null) MediaLeftPreviewVideo.IsMuted = ChkLeftMute.IsChecked == true; }
        private void SliderLeftVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (MediaLeftPreviewVideo != null) MediaLeftPreviewVideo.Volume = e.NewValue / 100.0; }
        private void SliderLeftVideoPosition_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { }

        private void ChkRightLoop_Click(object sender, RoutedEventArgs e) { }
        private void ChkRightMute_Click(object sender, RoutedEventArgs e) { if (MediaRightPreviewVideo != null) MediaRightPreviewVideo.IsMuted = ChkRightMute.IsChecked == true; }
        private void SliderRightVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (MediaRightPreviewVideo != null) MediaRightPreviewVideo.Volume = e.NewValue / 100.0; }
        private void SliderRightVideoPosition_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { }

        private void MediaLeftPreviewVideo_MediaOpened(object sender, RoutedEventArgs e) { if (PanelLeftVideoControls != null) PanelLeftVideoControls.Visibility = Visibility.Visible; }
        private void MediaRightPreviewVideo_MediaOpened(object sender, RoutedEventArgs e) { if (PanelRightVideoControls != null) PanelRightVideoControls.Visibility = Visibility.Visible; }

        // --- Media Chooser & Test Pattern Handlers ---

        private void BtnLeftChooseMedia_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Desteklenen Medya (*.png;*.jpg;*.jpeg;*.webp;*.mp4;*.mov)|*.png;*.jpg;*.jpeg;*.webp;*.mp4;*.mov|Görseller (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp|Videolar (*.mp4;*.mov)|*.mp4;*.mov"
            };

            if (dlg.ShowDialog() == true)
            {
                LoadMediaForCard("LEFT", dlg.FileName);
            }
        }

        private void BtnRightChooseMedia_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Desteklenen Medya (*.png;*.jpg;*.jpeg;*.webp;*.mp4;*.mov)|*.png;*.jpg;*.jpeg;*.webp;*.mp4;*.mov|Görseller (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp|Videolar (*.mp4;*.mov)|*.mp4;*.mov"
            };

            if (dlg.ShowDialog() == true)
            {
                LoadMediaForCard("RIGHT", dlg.FileName);
            }
        }

        private void BtnLeftTestPattern_Click(object sender, RoutedEventArgs e)
        {
            string path = TestPatternGenerator.GenerateGridPattern(860, 1720, "LEFT TEST");
            LoadMediaForCard("LEFT", path);
        }

        private void BtnRightTestPattern_Click(object sender, RoutedEventArgs e)
        {
            string path = TestPatternGenerator.GenerateGridPattern(860, 1720, "RIGHT TEST");
            LoadMediaForCard("RIGHT", path);
        }

        private void BtnLeftRestoreMedia_Click(object sender, RoutedEventArgs e) { }
        private void BtnRightRestoreMedia_Click(object sender, RoutedEventArgs e) { }

        // --- Blackout & Legacy Output Handlers ---

        private void BtnLeftBlack_Click(object sender, RoutedEventArgs e)
        {
            _leftState.IsBlackout = !_leftState.IsBlackout;
            _outputManager.UpdateBlackout("LEFT", _leftState.IsBlackout);
        }

        private void BtnRightBlack_Click(object sender, RoutedEventArgs e)
        {
            _rightState.IsBlackout = !_rightState.IsBlackout;
            _outputManager.UpdateBlackout("RIGHT", _rightState.IsBlackout);
        }

        private void BtnLeftStartOutput_Click(object sender, RoutedEventArgs e)
        {
            if (_leftState.SelectedDisplay == null) return;
            _outputManager.StartOutput("LEFT", _leftState.SelectedDisplay, _leftState.Calibration, _leftState.DraftLayout, _leftState.LoadedBitmap, _leftState.IsBlackout);
        }

        private void BtnRightStartOutput_Click(object sender, RoutedEventArgs e)
        {
            if (_rightState.SelectedDisplay == null) return;
            _outputManager.StartOutput("RIGHT", _rightState.SelectedDisplay, _rightState.Calibration, _rightState.DraftLayout, _rightState.LoadedBitmap, _rightState.IsBlackout);
        }

        // --- v0.3 MASTER OUTPUT & TAKE ACTION ---

        private void UpdateMasterUIState()
        {
            // 1. Master Output Status UI
            bool isMasterRunning = _outputManager.MasterState == OutputLifecycleState.Running;
            if (TxtHeaderMaster != null)
            {
                TxtHeaderMaster.Text = isMasterRunning ? "CANLI" : (_outputManager.MasterState == OutputLifecycleState.Failed ? "HATA" : "PASİF");
                TxtHeaderMaster.Foreground = new SolidColorBrush(isMasterRunning ? Colors.LimeGreen : (_outputManager.MasterState == OutputLifecycleState.Failed ? Colors.Red : Color.FromRgb(148, 163, 184)));
            }
            if (DotHeaderMaster != null)
            {
                DotHeaderMaster.Fill = new SolidColorBrush(isMasterRunning ? Colors.LimeGreen : (_outputManager.MasterState == OutputLifecycleState.Failed ? Colors.Red : Color.FromRgb(100, 116, 139)));
            }

            if (TxtLiveStatusOutput != null)
            {
                TxtLiveStatusOutput.Text = isMasterRunning ? $"Master Output Canlı ({_masterOutputDisplay?.DisplayLabel ?? "Display 3"})" : "Master Output Kapalı";
                TxtLiveStatusOutput.Foreground = new SolidColorBrush(isMasterRunning ? Colors.LimeGreen : Color.FromRgb(148, 163, 184));
            }
            if (DotLiveStatusOutput != null)
            {
                DotLiveStatusOutput.Fill = new SolidColorBrush(isMasterRunning ? Colors.LimeGreen : Color.FromRgb(100, 116, 139));
            }

            // Start/Stop Master Button text & icon
            if (TxtStartMasterTitle != null) TxtStartMasterTitle.Text = isMasterRunning ? "MASTER ÇIKIŞINI DURDUR" : "MASTER ÇIKIŞINI BAŞLAT";
            if (TxtStartMasterIcon != null) TxtStartMasterIcon.Text = isMasterRunning ? "⏹" : "▶";
            if (TxtStartMasterSub != null) TxtStartMasterSub.Text = isMasterRunning ? "Çalışan Master Output'u kapat" : "Seçilen ekranda yayını aç";

            // Preview Status Badge
            if (TxtMasterStatusBadge != null)
            {
                TxtMasterStatusBadge.Text = isMasterRunning ? "[ CANLI YAYIN ]" : "[ DRAFT PREVIEW ]";
                TxtMasterStatusBadge.Foreground = new SolidColorBrush(isMasterRunning ? Colors.LimeGreen : Colors.Orange);
            }

            // 2. Presentation Capture UI Status
            bool isCapturing = _captureService.IsCapturing;
            if (TxtHeaderCapture != null)
            {
                TxtHeaderCapture.Text = isCapturing ? "CANLI" : "BEKLİYOR";
                TxtHeaderCapture.Foreground = new SolidColorBrush(isCapturing ? Colors.LimeGreen : Color.FromRgb(148, 163, 184));
            }
            if (DotHeaderCapture != null)
            {
                DotHeaderCapture.Fill = new SolidColorBrush(isCapturing ? Colors.LimeGreen : Color.FromRgb(100, 116, 139));
            }
            if (TxtLiveStatusCapture != null)
            {
                TxtLiveStatusCapture.Text = isCapturing ? $"Sunum Yakalama Aktif ({_presentationSourceDisplay?.DisplayLabel ?? "Display 2"})" : "Sunum Yakalama Bekliyor";
                TxtLiveStatusCapture.Foreground = new SolidColorBrush(isCapturing ? Colors.LimeGreen : Color.FromRgb(148, 163, 184));
            }
            if (DotLiveStatusCapture != null)
            {
                DotLiveStatusCapture.Fill = new SolidColorBrush(isCapturing ? Colors.LimeGreen : Color.FromRgb(100, 116, 139));
            }
        }

        private void BtnTake_Click(object sender, RoutedEventArgs e)
        {
            _masterDraftState.Left.MediaPath = _leftState.DraftLayout.MediaPath;
            _masterDraftState.Left.WebUrl = _leftState.WebUrl;
            _masterDraftState.Left.MediaType = _leftState.DraftLayout.MediaType;
            _masterDraftState.Left.ScaleMode = _leftState.DraftLayout.ScaleMode;
            _masterDraftState.Left.Layout = _leftState.DraftLayout.Clone();
            _masterDraftState.Left.Calibration = _leftState.Calibration.Clone();
            _masterDraftState.Left.IsBlackout = _leftState.IsBlackout;

            _masterDraftState.Right.MediaPath = _rightState.DraftLayout.MediaPath;
            _masterDraftState.Right.WebUrl = _rightState.WebUrl;
            _masterDraftState.Right.MediaType = _rightState.DraftLayout.MediaType;
            _masterDraftState.Right.ScaleMode = _rightState.DraftLayout.ScaleMode;
            _masterDraftState.Right.Layout = _rightState.DraftLayout.Clone();
            _masterDraftState.Right.Calibration = _rightState.Calibration.Clone();
            _masterDraftState.Right.IsBlackout = _rightState.IsBlackout;

            _masterLiveState = _masterDraftState.Clone();

            if (_outputManager.IsMasterOutputActive)
            {
                _outputManager.ApplyMasterState(_masterLiveState, _leftState.LoadedBitmap, _rightState.LoadedBitmap);
                if (TxtGlobalStatus != null) TxtGlobalStatus.Text = "🎬 TAKE UYGULANDI — Taslak değişiklikler canlı Master LED ekranına gönderildi.";
            }
            else
            {
                if (TxtGlobalStatus != null) TxtGlobalStatus.Text = "TAKE TASLAĞA UYGULANDI — Master Output kapalı. Yayını ekranda görmek için '▶ MASTER ÇIKIŞINI BAŞLAT' butonuna basın.";
            }

            UpdateMasterUIState();
        }

        private void BtnStartMaster_Click(object sender, RoutedEventArgs e)
        {
            if (_outputManager.MasterState == OutputLifecycleState.Running)
            {
                _outputManager.StopMasterOutput();
                if (TxtGlobalStatus != null) TxtGlobalStatus.Text = "Master LED Çıktısı durduruldu.";
                UpdateMasterUIState();
                return;
            }

            if (_masterOutputDisplay == null)
            {
                MessageBox.Show("Lütfen öncelikle Display 3 (Master LED Output) ekranını seçin.", "Master Çıkış Uyarısı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnTake_Click(sender, e);

            Logger.Log($"[BtnStartMaster_Click] Launching Master Output on {_masterOutputDisplay.DisplayLabel}...");
            bool ok = _outputManager.StartMasterOutput(_masterOutputDisplay, _masterLiveState, _leftState.LoadedBitmap, _rightState.LoadedBitmap);

            if (ok)
            {
                if (TxtGlobalStatus != null) TxtGlobalStatus.Text = $"Master LED Çıktısı {_masterOutputDisplay.DisplayLabel} üzerinde CANLI yayında.";
            }
            else
            {
                MessageBox.Show($"Master Output penceresi başlatılamadı.\nDetaylar log dosyasında: {Logger.LogFilePath}\nHata: {_outputManager.MasterError}", "Master Çıkış Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
                if (TxtGlobalStatus != null) TxtGlobalStatus.Text = $"HATA: Master Output başlatılamadı. Detaylar: {Logger.LogFilePath}";
            }

            UpdateMasterUIState();
        }

        private void BtnStopMaster_Click(object sender, RoutedEventArgs e)
        {
            _outputManager.StopMasterOutput();
            if (TxtGlobalStatus != null) TxtGlobalStatus.Text = "Master LED Çıktısı durduruldu.";
            UpdateMasterUIState();
        }

        private void BtnRgbTestPattern_Click(object sender, RoutedEventArgs e)
        {
            if (_masterOutputDisplay == null)
            {
                MessageBox.Show("Lütfen öncelikle Display 3 (Master LED Output) ekranını seçin.", "Test Deseni Uyarısı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool ok = _outputManager.StartMasterOutput(_masterOutputDisplay, _masterLiveState, _leftState.LoadedBitmap, _rightState.LoadedBitmap, isDiagnosticMode: true);
            if (ok)
            {
                if (TxtGlobalStatus != null) TxtGlobalStatus.Text = "🔴🟢🔵 RGB DIAGNOSTIC TEST PATTERN (RED / GREEN / BLUE) Master Output üzerinde gösteriliyor!";
            }
            UpdateMasterUIState();
        }

        private void BtnMasterBlackout_Click(object sender, RoutedEventArgs e)
        {
            _masterDraftState.IsMasterBlackout = !_masterDraftState.IsMasterBlackout;
            _masterLiveState.IsMasterBlackout = _masterDraftState.IsMasterBlackout;

            if (_masterDraftState.IsMasterBlackout)
            {
                if (BtnMasterBlackout != null) BtnMasterBlackout.Content = "🟡 RESTORE MASTER";
            }
            else
            {
                if (BtnMasterBlackout != null) BtnMasterBlackout.Content = "⚫ MASTER SİYAH EKRAN";
            }

            _outputManager.SetMasterBlackout(_masterDraftState.IsMasterBlackout);
        }

        private void BtnToggleSimulation_Click(object sender, RoutedEventArgs e)
        {
            _isSimulationMode = !_isSimulationMode;
            _appSettings.IsSimulationMode = _isSimulationMode;
            _settingsService.SaveSettings(_appSettings);

            if (BtnToggleSimulation != null) BtnToggleSimulation.Content = _isSimulationMode ? "🖥️ GERÇEK EKRAN MODU" : "🧪 SİMÜLASYON MODU";
            RefreshDisplaysAndUI();
        }

        private void BtnIdentify_Click(object sender, RoutedEventArgs e)
        {
            foreach (var display in _allDisplays)
            {
                string role = "Ekran";
                if (display.IsPrimary) role = "DISPLAY 1 (OPERATÖR KONTROL)";
                else if (_presentationSourceDisplay != null && _displayService.IsSameDisplay(display, _presentationSourceDisplay)) role = "DISPLAY 2 (SUNUM YAKALAMA KAYNAĞI)";
                else if (_masterOutputDisplay != null && _displayService.IsSameDisplay(display, _masterOutputDisplay)) role = "DISPLAY 3 (MASTER LED ÇIKIŞI)";

                var overlay = new IdentifyOverlayWindow(display, role);
                overlay.Show();
            }

            if (TxtGlobalStatus != null) TxtGlobalStatus.Text = "Ekran tanımlama katmanı gösteriliyor (3.5 sn).";
        }

        // --- Web Sitesi & Source Type Tab Handlers ---

        private void SourceTab_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _isUpdatingUI) return;

            // SOL LED Tabs
            if (RadLeftSourceImage != null && RadLeftSourceImage.IsChecked == true)
            {
                _leftState.DraftLayout.MediaType = MediaSourceType.Image;
                if (PanelLeftImageContent != null) PanelLeftImageContent.Visibility = Visibility.Visible;
                if (PanelLeftVideoContent != null) PanelLeftVideoContent.Visibility = Visibility.Collapsed;
                if (PanelLeftWebContent != null) PanelLeftWebContent.Visibility = Visibility.Collapsed;
                if (PanelLeftTestContent != null) PanelLeftTestContent.Visibility = Visibility.Collapsed;
            }
            else if (RadLeftSourceVideo != null && RadLeftSourceVideo.IsChecked == true)
            {
                _leftState.DraftLayout.MediaType = MediaSourceType.Video;
                if (PanelLeftImageContent != null) PanelLeftImageContent.Visibility = Visibility.Collapsed;
                if (PanelLeftVideoContent != null) PanelLeftVideoContent.Visibility = Visibility.Visible;
                if (PanelLeftWebContent != null) PanelLeftWebContent.Visibility = Visibility.Collapsed;
                if (PanelLeftTestContent != null) PanelLeftTestContent.Visibility = Visibility.Collapsed;
            }
            else if (RadLeftSourceWeb != null && RadLeftSourceWeb.IsChecked == true)
            {
                _leftState.DraftLayout.MediaType = MediaSourceType.Website;
                if (PanelLeftImageContent != null) PanelLeftImageContent.Visibility = Visibility.Collapsed;
                if (PanelLeftVideoContent != null) PanelLeftVideoContent.Visibility = Visibility.Collapsed;
                if (PanelLeftWebContent != null) PanelLeftWebContent.Visibility = Visibility.Visible;
                if (PanelLeftTestContent != null) PanelLeftTestContent.Visibility = Visibility.Collapsed;
            }
            else if (RadLeftSourceTest != null && RadLeftSourceTest.IsChecked == true)
            {
                _leftState.DraftLayout.MediaType = MediaSourceType.TestPattern;
                if (PanelLeftImageContent != null) PanelLeftImageContent.Visibility = Visibility.Collapsed;
                if (PanelLeftVideoContent != null) PanelLeftVideoContent.Visibility = Visibility.Collapsed;
                if (PanelLeftWebContent != null) PanelLeftWebContent.Visibility = Visibility.Collapsed;
                if (PanelLeftTestContent != null) PanelLeftTestContent.Visibility = Visibility.Visible;
            }

            // SAĞ LED Tabs
            if (RadRightSourceImage != null && RadRightSourceImage.IsChecked == true)
            {
                _rightState.DraftLayout.MediaType = MediaSourceType.Image;
                if (PanelRightImageContent != null) PanelRightImageContent.Visibility = Visibility.Visible;
                if (PanelRightVideoContent != null) PanelRightVideoContent.Visibility = Visibility.Collapsed;
                if (PanelRightWebContent != null) PanelRightWebContent.Visibility = Visibility.Collapsed;
                if (PanelRightTestContent != null) PanelRightTestContent.Visibility = Visibility.Collapsed;
            }
            else if (RadRightSourceVideo != null && RadRightSourceVideo.IsChecked == true)
            {
                _rightState.DraftLayout.MediaType = MediaSourceType.Video;
                if (PanelRightImageContent != null) PanelRightImageContent.Visibility = Visibility.Collapsed;
                if (PanelRightVideoContent != null) PanelRightVideoContent.Visibility = Visibility.Visible;
                if (PanelRightWebContent != null) PanelRightWebContent.Visibility = Visibility.Collapsed;
                if (PanelRightTestContent != null) PanelRightTestContent.Visibility = Visibility.Collapsed;
            }
            else if (RadRightSourceWeb != null && RadRightSourceWeb.IsChecked == true)
            {
                _rightState.DraftLayout.MediaType = MediaSourceType.Website;
                if (PanelRightImageContent != null) PanelRightImageContent.Visibility = Visibility.Collapsed;
                if (PanelRightVideoContent != null) PanelRightVideoContent.Visibility = Visibility.Collapsed;
                if (PanelRightWebContent != null) PanelRightWebContent.Visibility = Visibility.Visible;
                if (PanelRightTestContent != null) PanelRightTestContent.Visibility = Visibility.Collapsed;
            }
            else if (RadRightSourceTest != null && RadRightSourceTest.IsChecked == true)
            {
                _rightState.DraftLayout.MediaType = MediaSourceType.TestPattern;
                if (PanelRightImageContent != null) PanelRightImageContent.Visibility = Visibility.Collapsed;
                if (PanelRightVideoContent != null) PanelRightVideoContent.Visibility = Visibility.Collapsed;
                if (PanelRightWebContent != null) PanelRightWebContent.Visibility = Visibility.Collapsed;
                if (PanelRightTestContent != null) PanelRightTestContent.Visibility = Visibility.Visible;
            }

            RenderDraftPreview("LEFT");
            RenderDraftPreview("RIGHT");
        }

        private void BtnLeftWebGo_Click(object sender, RoutedEventArgs e)
        {
            if (TxtLeftWebUrl != null && !string.IsNullOrWhiteSpace(TxtLeftWebUrl.Text))
            {
                string url = TxtLeftWebUrl.Text.Trim();
                if (!url.StartsWith("http://") && !url.StartsWith("https://")) url = "https://" + url;
                _leftState.WebUrl = url;
                _leftState.DraftLayout.MediaType = MediaSourceType.Website;
                if (TxtLeftWebStatus != null) TxtLeftWebStatus.Text = $"Aktif Web Adresi: {url}";
            }
        }

        private void BtnRightWebGo_Click(object sender, RoutedEventArgs e)
        {
            if (TxtRightWebUrl != null && !string.IsNullOrWhiteSpace(TxtRightWebUrl.Text))
            {
                string url = TxtRightWebUrl.Text.Trim();
                if (!url.StartsWith("http://") && !url.StartsWith("https://")) url = "https://" + url;
                _rightState.WebUrl = url;
                _rightState.DraftLayout.MediaType = MediaSourceType.Website;
                if (TxtRightWebStatus != null) TxtRightWebStatus.Text = $"Aktif Web Adresi: {url}";
            }
        }
    }
}