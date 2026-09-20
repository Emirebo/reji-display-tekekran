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

        private List<DisplayInfo> _allDisplays = new();
        private DisplayInfo? _reservedCenterDisplay;

        private OutputCardState _leftState = new() { CardId = "LEFT", Title = "LEFT LED" };
        private OutputCardState _rightState = new() { CardId = "RIGHT", Title = "RIGHT LED" };

        private AppSettings _appSettings = new();

        public MainWindow()
        {
            _displayService = new DisplayService();
            _settingsService = new SettingsService();
            _outputManager = new OutputManager();

            InitializeComponent();

            _displayService.DisplayTopologyChanged += OnDisplayTopologyChanged;

            Loaded += MainWindow_Loaded;
            Unloaded += MainWindow_Unloaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _appSettings = _settingsService.LoadSettings();
            RefreshDisplaysAndUI();
            RestoreSavedSettings();
        }

        private void MainWindow_Unloaded(object sender, RoutedEventArgs e)
        {
            _outputManager.StopAllOutputs();
        }

        private void OnDisplayTopologyChanged(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                TxtGlobalStatus.Text = "Ekran yapısı değişti. Ekranlar yeniden taranıyor...";
                RefreshDisplaysAndUI();
                VerifyActiveOutputsAfterTopologyChange();
            });
        }

        private void RefreshDisplaysAndUI()
        {
            _allDisplays = _displayService.GetDisplays();

            // Populate Reserved Center Combo (Can be Primary or Extended display designated as Center)
            CmbReservedCenter.SelectionChanged -= CmbReservedCenter_SelectionChanged;
            CmbReservedCenter.Items.Clear();

            CmbReservedCenter.Items.Add(new ComboBoxItem { Content = "-- Seçilmedi / Varsayılan --", Tag = null });

            foreach (var display in _allDisplays)
            {
                CmbReservedCenter.Items.Add(new ComboBoxItem
                {
                    Content = display.DisplayLabel,
                    Tag = display
                });
            }

            // Restore center display selection from settings or default
            if (!string.IsNullOrEmpty(_appSettings.ReservedCenterDeviceName) || !string.IsNullOrEmpty(_appSettings.ReservedCenterDeviceId))
            {
                _reservedCenterDisplay = _displayService.FindMatchingDisplay(_allDisplays, _appSettings.ReservedCenterDeviceName, _appSettings.ReservedCenterDeviceId);
            }

            if (_reservedCenterDisplay != null)
            {
                foreach (ComboBoxItem item in CmbReservedCenter.Items)
                {
                    if (item.Tag is DisplayInfo d && _displayService.IsSameDisplay(d, _reservedCenterDisplay))
                    {
                        CmbReservedCenter.SelectedItem = item;
                        break;
                    }
                }
            }
            else
            {
                CmbReservedCenter.SelectedIndex = 0;
            }

            CmbReservedCenter.SelectionChanged += CmbReservedCenter_SelectionChanged;

            UpdateCardComboBoxes();
        }

        private void UpdateCardComboBoxes()
        {
            // Populate LEFT and RIGHT dropdowns using strict protection rules:
            // Rule 1: NO Primary Operator display allowed
            // Rule 2: NO Reserved CENTER display allowed
            // Rule 3: NO display already claimed by the other card allowed

            DisplayInfo? leftSelected = GetSelectedDisplayFromCombo(CmbLeftDisplay);
            DisplayInfo? rightSelected = GetSelectedDisplayFromCombo(CmbRightDisplay);

            PopulateCombo(CmbLeftDisplay, leftSelected, _reservedCenterDisplay, rightSelected, CmbLeftDisplay_SelectionChanged);
            PopulateCombo(CmbRightDisplay, rightSelected, _reservedCenterDisplay, leftSelected, CmbRightDisplay_SelectionChanged);
        }

        private void PopulateCombo(
            ComboBox combo,
            DisplayInfo? currentlySelected,
            DisplayInfo? reservedCenter,
            DisplayInfo? claimedByOther,
            SelectionChangedEventHandler handler)
        {
            combo.SelectionChanged -= handler;
            combo.Items.Clear();

            combo.Items.Add(new ComboBoxItem { Content = "-- Ekran Seçin --", Tag = null });

            var assignable = _displayService.GetAssignableDisplays(_allDisplays, reservedCenter, claimedByOther);

            ComboBoxItem? itemToSelect = null;

            foreach (var display in assignable)
            {
                var item = new ComboBoxItem
                {
                    Content = display.FriendlyName,
                    Tag = display
                };

                combo.Items.Add(item);

                if (currentlySelected != null && _displayService.IsSameDisplay(display, currentlySelected))
                {
                    itemToSelect = item;
                }
            }

            if (itemToSelect != null)
            {
                combo.SelectedItem = itemToSelect;
            }
            else
            {
                combo.SelectedIndex = 0;
            }

            combo.SelectionChanged += handler;
        }

        private DisplayInfo? GetSelectedDisplayFromCombo(ComboBox combo)
        {
            if (combo.SelectedItem is ComboBoxItem item && item.Tag is DisplayInfo display)
            {
                return display;
            }
            return null;
        }

        private void RestoreSavedSettings()
        {
            // Restore LEFT Card
            if (_appSettings.LeftOutput != null)
            {
                var match = _displayService.FindMatchingDisplay(_allDisplays, _appSettings.LeftOutput.DeviceName, _appSettings.LeftOutput.DeviceId);
                if (match != null && !match.IsPrimary && (_reservedCenterDisplay == null || !_displayService.IsSameDisplay(match, _reservedCenterDisplay)))
                {
                    SelectDisplayInCombo(CmbLeftDisplay, match);
                    _leftState.AssignedDeviceName = match.DeviceName;
                    _leftState.AssignedDeviceId = match.DeviceId;
                }
                else if (!string.IsNullOrEmpty(_appSettings.LeftOutput.DeviceName))
                {
                    SetCardStatus(_leftState, BadgeLeftStatus, TxtLeftStatus, "DISCONNECTED", Colors.OrangeRed);
                    TxtLeftError.Text = "Kayıtlı ekran bulunamadı veya kullanılamıyor. Atama yapılması gerekiyor.";
                    TxtLeftError.Visibility = Visibility.Visible;
                }

                SetScaleModeUI("LEFT", _appSettings.LeftOutput.ScaleMode);

                if (!string.IsNullOrEmpty(_appSettings.LeftOutput.LastMediaPath))
                {
                    LoadImageForCard(_leftState, _appSettings.LeftOutput.LastMediaPath, ImgLeftPreview, PanelLeftDropPrompt, TxtLeftMediaPath, TxtLeftError);
                }
            }

            // Restore RIGHT Card
            if (_appSettings.RightOutput != null)
            {
                var match = _displayService.FindMatchingDisplay(_allDisplays, _appSettings.RightOutput.DeviceName, _appSettings.RightOutput.DeviceId);
                if (match != null && !match.IsPrimary && (_reservedCenterDisplay == null || !_displayService.IsSameDisplay(match, _reservedCenterDisplay)))
                {
                    SelectDisplayInCombo(CmbRightDisplay, match);
                    _rightState.AssignedDeviceName = match.DeviceName;
                    _rightState.AssignedDeviceId = match.DeviceId;
                }
                else if (!string.IsNullOrEmpty(_appSettings.RightOutput.DeviceName))
                {
                    SetCardStatus(_rightState, BadgeRightStatus, TxtRightStatus, "DISCONNECTED", Colors.OrangeRed);
                    TxtRightError.Text = "Kayıtlı ekran bulunamadı veya kullanılamıyor. Atama yapılması gerekiyor.";
                    TxtRightError.Visibility = Visibility.Visible;
                }

                SetScaleModeUI("RIGHT", _appSettings.RightOutput.ScaleMode);

                if (!string.IsNullOrEmpty(_appSettings.RightOutput.LastMediaPath))
                {
                    LoadImageForCard(_rightState, _appSettings.RightOutput.LastMediaPath, ImgRightPreview, PanelRightDropPrompt, TxtRightMediaPath, TxtRightError);
                }
            }
        }

        private void SelectDisplayInCombo(ComboBox combo, DisplayInfo target)
        {
            foreach (ComboBoxItem item in combo.Items)
            {
                if (item.Tag is DisplayInfo d && _displayService.IsSameDisplay(d, target))
                {
                    combo.SelectedItem = item;
                    break;
                }
            }
        }

        private void VerifyActiveOutputsAfterTopologyChange()
        {
            // Verify LEFT output
            if (_outputManager.IsOutputActive("LEFT"))
            {
                var activeDisplay = _outputManager.GetActiveDisplay("LEFT");
                if (activeDisplay != null)
                {
                    var current = _displayService.FindMatchingDisplay(_allDisplays, activeDisplay.DeviceName, activeDisplay.DeviceId);
                    if (current == null)
                    {
                        // Display disconnected! Stop output and report unavailable
                        _outputManager.StopOutput("LEFT");
                        SetCardStatus(_leftState, BadgeLeftStatus, TxtLeftStatus, "DISCONNECTED", Colors.Red);
                        TxtLeftError.Text = "Atanan fiziksel ekran bağlantısı kesildi! Çıkış durduruldu.";
                        TxtLeftError.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        // Reposition if bounds shifted
                        _leftState.AssignedDeviceName = current.DeviceName;
                        _leftState.AssignedDeviceId = current.DeviceId;
                    }
                }
            }

            // Verify RIGHT output
            if (_outputManager.IsOutputActive("RIGHT"))
            {
                var activeDisplay = _outputManager.GetActiveDisplay("RIGHT");
                if (activeDisplay != null)
                {
                    var current = _displayService.FindMatchingDisplay(_allDisplays, activeDisplay.DeviceName, activeDisplay.DeviceId);
                    if (current == null)
                    {
                        _outputManager.StopOutput("RIGHT");
                        SetCardStatus(_rightState, BadgeRightStatus, TxtRightStatus, "DISCONNECTED", Colors.Red);
                        TxtRightError.Text = "Atanan fiziksel ekran bağlantısı kesildi! Çıkış durduruldu.";
                        TxtRightError.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        _rightState.AssignedDeviceName = current.DeviceName;
                        _rightState.AssignedDeviceId = current.DeviceId;
                    }
                }
            }

            UpdateCardComboBoxes();
        }

        // --- Event Handlers ---

        private void CmbReservedCenter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_settingsService == null || _appSettings == null) return;

            if (CmbReservedCenter.SelectedItem is ComboBoxItem item && item.Tag is DisplayInfo display)
            {
                _reservedCenterDisplay = display;
                _appSettings.ReservedCenterDeviceName = display.DeviceName;
                _appSettings.ReservedCenterDeviceId = display.DeviceId;
            }
            else
            {
                _reservedCenterDisplay = null;
                _appSettings.ReservedCenterDeviceName = null;
                _appSettings.ReservedCenterDeviceId = null;
            }

            _settingsService.SaveSettings(_appSettings);
            UpdateCardComboBoxes();
        }

        private void CmbLeftDisplay_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_settingsService == null || _appSettings?.LeftOutput == null || _leftState == null) return;

            var selected = GetSelectedDisplayFromCombo(CmbLeftDisplay);
            if (selected != null)
            {
                _leftState.AssignedDeviceName = selected.DeviceName;
                _leftState.AssignedDeviceId = selected.DeviceId;
                _appSettings.LeftOutput.DeviceName = selected.DeviceName;
                _appSettings.LeftOutput.DeviceId = selected.DeviceId;
                TxtLeftError.Visibility = Visibility.Collapsed;
            }
            else
            {
                _leftState.AssignedDeviceName = null;
                _leftState.AssignedDeviceId = null;
                _appSettings.LeftOutput.DeviceName = null;
                _appSettings.LeftOutput.DeviceId = null;
            }

            _settingsService.SaveSettings(_appSettings);
            UpdateCardComboBoxes();
        }

        private void CmbRightDisplay_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_settingsService == null || _appSettings?.RightOutput == null || _rightState == null) return;

            var selected = GetSelectedDisplayFromCombo(CmbRightDisplay);
            if (selected != null)
            {
                _rightState.AssignedDeviceName = selected.DeviceName;
                _rightState.AssignedDeviceId = selected.DeviceId;
                _appSettings.RightOutput.DeviceName = selected.DeviceName;
                _appSettings.RightOutput.DeviceId = selected.DeviceId;
                TxtRightError.Visibility = Visibility.Collapsed;
            }
            else
            {
                _rightState.AssignedDeviceName = null;
                _rightState.AssignedDeviceId = null;
                _appSettings.RightOutput.DeviceName = null;
                _appSettings.RightOutput.DeviceId = null;
            }

            _settingsService.SaveSettings(_appSettings);
            UpdateCardComboBoxes();
        }

        private void DropZone_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void DropZoneLeft_Drop(object sender, DragEventArgs e)
        {
            HandleFileDrop("LEFT", _leftState, e, ImgLeftPreview, PanelLeftDropPrompt, TxtLeftMediaPath, TxtLeftError);
        }

        private void DropZoneRight_Drop(object sender, DragEventArgs e)
        {
            HandleFileDrop("RIGHT", _rightState, e, ImgRightPreview, PanelRightDropPrompt, TxtRightMediaPath, TxtRightError);
        }

        private void HandleFileDrop(
            string cardId,
            OutputCardState cardState,
            DragEventArgs e,
            Image previewImg,
            StackPanel promptPanel,
            TextBlock mediaPathTxt,
            TextBlock errorTxt)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    string filePath = files[0];
                    if (LoadImageForCard(cardState, filePath, previewImg, promptPanel, mediaPathTxt, errorTxt))
                    {
                        if (cardId == "LEFT")
                        {
                            _appSettings.LeftOutput.LastMediaPath = filePath;
                        }
                        else
                        {
                            _appSettings.RightOutput.LastMediaPath = filePath;
                        }
                        _settingsService.SaveSettings(_appSettings);

                        // If output currently active, update media live safely
                        if (_outputManager.IsOutputActive(cardId))
                        {
                            var bmp = CreateBitmap(filePath);
                            if (bmp != null)
                            {
                                _outputManager.UpdateMedia(cardId, bmp, cardState.ScaleMode);
                            }
                        }
                    }
                }
            }
        }

        private bool LoadImageForCard(
            OutputCardState cardState,
            string filePath,
            Image previewImg,
            StackPanel promptPanel,
            TextBlock mediaPathTxt,
            TextBlock errorTxt)
        {
            // Requirement 9: Validate image files BEFORE replacing currently visible content
            if (!ImageValidationHelper.ValidateImageFile(filePath, out string err))
            {
                errorTxt.Text = $"HATA: {err}";
                errorTxt.Visibility = Visibility.Visible;
                return false;
            }

            try
            {
                var bitmap = CreateBitmap(filePath);
                if (bitmap != null)
                {
                    cardState.CurrentMediaPath = filePath;
                    previewImg.Source = bitmap;
                    previewImg.Visibility = Visibility.Visible;
                    promptPanel.Visibility = Visibility.Collapsed;
                    mediaPathTxt.Text = Path.GetFileName(filePath);
                    errorTxt.Visibility = Visibility.Collapsed;
                    return true;
                }
            }
            catch (Exception ex)
            {
                errorTxt.Text = $"Yükleme Hatası: {ex.Message}";
                errorTxt.Visibility = Visibility.Visible;
            }

            return false;
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
                bitmap.Freeze(); // Freeze for cross-thread performance
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        private void RadioLeftScale_Checked(object sender, RoutedEventArgs e)
        {
            if (RadioLeftFit == null || _settingsService == null || _appSettings?.LeftOutput == null || _leftState == null) return;

            if (RadioLeftFit.IsChecked == true) _leftState.ScaleMode = ScaleMode.Fit;
            else if (RadioLeftFill.IsChecked == true) _leftState.ScaleMode = ScaleMode.Fill;
            else if (RadioLeftStretch.IsChecked == true) _leftState.ScaleMode = ScaleMode.Stretch;

            _appSettings.LeftOutput.ScaleMode = _leftState.ScaleMode;
            _settingsService.SaveSettings(_appSettings);

            if (_outputManager != null && _outputManager.IsOutputActive("LEFT"))
            {
                _outputManager.UpdateScaleMode("LEFT", _leftState.ScaleMode);
            }
        }

        private void RadioRightScale_Checked(object sender, RoutedEventArgs e)
        {
            if (RadioRightFit == null || _settingsService == null || _appSettings?.RightOutput == null || _rightState == null) return;

            if (RadioRightFit.IsChecked == true) _rightState.ScaleMode = ScaleMode.Fit;
            else if (RadioRightFill.IsChecked == true) _rightState.ScaleMode = ScaleMode.Fill;
            else if (RadioRightStretch.IsChecked == true) _rightState.ScaleMode = ScaleMode.Stretch;

            _appSettings.RightOutput.ScaleMode = _rightState.ScaleMode;
            _settingsService.SaveSettings(_appSettings);

            if (_outputManager != null && _outputManager.IsOutputActive("RIGHT"))
            {
                _outputManager.UpdateScaleMode("RIGHT", _rightState.ScaleMode);
            }
        }

        private void SetScaleModeUI(string cardId, ScaleMode mode)
        {
            if (cardId == "LEFT")
            {
                _leftState.ScaleMode = mode;
                RadioLeftFit.IsChecked = (mode == ScaleMode.Fit);
                RadioLeftFill.IsChecked = (mode == ScaleMode.Fill);
                RadioLeftStretch.IsChecked = (mode == ScaleMode.Stretch);
            }
            else
            {
                _rightState.ScaleMode = mode;
                RadioRightFit.IsChecked = (mode == ScaleMode.Fit);
                RadioRightFill.IsChecked = (mode == ScaleMode.Fill);
                RadioRightStretch.IsChecked = (mode == ScaleMode.Stretch);
            }
        }

        // --- Action Buttons ---

        private void BtnLeftShow_Click(object sender, RoutedEventArgs e)
        {
            StartCardOutput("LEFT", _leftState, CmbLeftDisplay, BadgeLeftStatus, TxtLeftStatus, TxtLeftError);
        }

        private void BtnRightShow_Click(object sender, RoutedEventArgs e)
        {
            StartCardOutput("RIGHT", _rightState, CmbRightDisplay, BadgeRightStatus, TxtRightStatus, TxtRightError);
        }

        private void StartCardOutput(
            string cardId,
            OutputCardState state,
            ComboBox combo,
            Border badge,
            TextBlock badgeTxt,
            TextBlock errorTxt)
        {
            var display = GetSelectedDisplayFromCombo(combo);
            if (display == null)
            {
                errorTxt.Text = "Lütfen yayın için geçerli bir fiziksel ekran seçin.";
                errorTxt.Visibility = Visibility.Visible;
                return;
            }

            if (display.IsPrimary)
            {
                errorTxt.Text = "GÜVENLİK ENGELİ: Operatör birincil ekranına yayın yapılamaz!";
                errorTxt.Visibility = Visibility.Visible;
                return;
            }

            BitmapImage? bitmap = null;
            if (!string.IsNullOrEmpty(state.CurrentMediaPath))
            {
                bitmap = CreateBitmap(state.CurrentMediaPath);
            }

            try
            {
                _outputManager.StartOutput(cardId, display, state.ScaleMode, bitmap, state.IsBlackout);
                state.IsActive = true;
                SetCardStatus(state, badge, badgeTxt, state.IsBlackout ? "BLACKOUT" : "ACTIVE", state.IsBlackout ? Colors.DarkOrange : Colors.LimeGreen);
                errorTxt.Visibility = Visibility.Collapsed;
                TxtGlobalStatus.Text = $"{cardId} LED çıktısı {display.FriendlyName} üzerinde yayında.";
            }
            catch (Exception ex)
            {
                errorTxt.Text = $"Çıkış penceresi oluşturulamadı: {ex.Message}";
                errorTxt.Visibility = Visibility.Visible;
            }
        }

        private void BtnLeftStop_Click(object sender, RoutedEventArgs e)
        {
            StopCardOutput("LEFT", _leftState, BadgeLeftStatus, TxtLeftStatus);
        }

        private void BtnRightStop_Click(object sender, RoutedEventArgs e)
        {
            StopCardOutput("RIGHT", _rightState, BadgeRightStatus, TxtRightStatus);
        }

        private void StopCardOutput(string cardId, OutputCardState state, Border badge, TextBlock badgeTxt)
        {
            _outputManager.StopOutput(cardId);
            state.IsActive = false;
            SetCardStatus(state, badge, badgeTxt, "INACTIVE", Colors.Gray);
            TxtGlobalStatus.Text = $"{cardId} LED çıktısı durduruldu.";
        }

        private void BtnLeftBlack_Click(object sender, RoutedEventArgs e)
        {
            ToggleBlackout("LEFT", _leftState, BtnLeftBlack, BadgeLeftStatus, TxtLeftStatus);
        }

        private void BtnRightBlack_Click(object sender, RoutedEventArgs e)
        {
            ToggleBlackout("RIGHT", _rightState, BtnRightBlack, BadgeRightStatus, TxtRightStatus);
        }

        private void ToggleBlackout(string cardId, OutputCardState state, Button blackBtn, Border badge, TextBlock badgeTxt)
        {
            state.IsBlackout = !state.IsBlackout;

            if (state.IsBlackout)
            {
                blackBtn.Content = "🟡 GERİ YÜKLE (RESTORE)";
                if (state.IsActive)
                {
                    SetCardStatus(state, badge, badgeTxt, "BLACKOUT", Colors.DarkOrange);
                }
            }
            else
            {
                blackBtn.Content = "⚫ SİYAH EKRAN (BLACK)";
                if (state.IsActive)
                {
                    SetCardStatus(state, badge, badgeTxt, "ACTIVE", Colors.LimeGreen);
                }
            }

            _outputManager.UpdateBlackout(cardId, state.IsBlackout);

            if (cardId == "LEFT") _appSettings.LeftOutput.IsBlackout = state.IsBlackout;
            else _appSettings.RightOutput.IsBlackout = state.IsBlackout;

            _settingsService.SaveSettings(_appSettings);
        }

        private void BtnStopAll_Click(object sender, RoutedEventArgs e)
        {
            _outputManager.StopAllOutputs();
            _leftState.IsActive = false;
            _rightState.IsActive = false;

            SetCardStatus(_leftState, BadgeLeftStatus, TxtLeftStatus, "INACTIVE", Colors.Gray);
            SetCardStatus(_rightState, BadgeRightStatus, TxtRightStatus, "INACTIVE", Colors.Gray);

            TxtGlobalStatus.Text = "Tüm LED çıktıları durduruldu.";
        }

        private void BtnIdentify_Click(object sender, RoutedEventArgs e)
        {
            // Requirement 4: Identify overlays must be completely separate from media output windows
            var displays = _displayService.GetDisplays();

            foreach (var display in displays)
            {
                string role = "Ekran";
                if (display.IsPrimary)
                {
                    role = "OPERATÖR / BİRİNCİL EKRAN";
                }
                else if (_reservedCenterDisplay != null && _displayService.IsSameDisplay(display, _reservedCenterDisplay))
                {
                    role = "REZERVE CENTER (Windows Desktop)";
                }
                else if (_outputManager.IsOutputActive("LEFT") && _outputManager.GetActiveDisplay("LEFT") != null && _displayService.IsSameDisplay(display, _outputManager.GetActiveDisplay("LEFT")!))
                {
                    role = "LEFT LED (YAYINDA)";
                }
                else if (_outputManager.IsOutputActive("RIGHT") && _outputManager.GetActiveDisplay("RIGHT") != null && _displayService.IsSameDisplay(display, _outputManager.GetActiveDisplay("RIGHT")!))
                {
                    role = "RIGHT LED (YAYINDA)";
                }

                var overlay = new IdentifyOverlayWindow(display, role);
                overlay.Show();
            }

            TxtGlobalStatus.Text = "Ekran numaraları tanımlama katmanı gösteriliyor (3.5s).";
        }

        private void SetCardStatus(OutputCardState state, Border badge, TextBlock badgeTxt, string statusText, Color color)
        {
            state.StatusText = statusText;
            badgeTxt.Text = statusText;
            badge.Background = new SolidColorBrush(color) { Opacity = 0.85 };
            badgeTxt.Foreground = Brushes.White;
        }
    }
}