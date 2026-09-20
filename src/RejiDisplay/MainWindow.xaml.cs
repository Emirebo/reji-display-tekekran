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
        private readonly PresentationCaptureService _captureService;

        private List<DisplayInfo> _allDisplays = new();

        private DisplayInfo? _controlDisplay;            // Display 1 — Operator UI
        private DisplayInfo? _presentationSourceDisplay; // Display 2 — PowerPoint Capture Source
        private DisplayInfo? _masterOutputDisplay;       // Display 3 — NovaStar Master Output

        private MasterCanvasState _draftState = new();
        private MasterCanvasState _liveState = new();

        private BitmapImage? _leftDraftBitmap;
        private BitmapImage? _rightDraftBitmap;
        private BitmapImage? _leftLiveBitmap;
        private BitmapImage? _rightLiveBitmap;

        private AppSettings _appSettings = new();
        private bool _isSimulationMode = false;

        public MainWindow()
        {
            _displayService = new DisplayService();
            _settingsService = new SettingsService();
            _outputManager = new OutputManager();
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
            _appSettings = _settingsService.LoadSettings();
            _isSimulationMode = _appSettings.IsSimulationMode;

            // Set default Y-Offset to 172 if invalid
            if (_appSettings.MiddleYOffset <= 0)
            {
                _appSettings.MiddleYOffset = MasterCanvasGeometry.DefaultMiddleYOffset;
            }

            SliderMiddleYOffset.Value = _appSettings.MiddleYOffset;
            _draftState.MiddleYOffset = _appSettings.MiddleYOffset;

            RefreshDisplaysAndUI();
            RestoreSavedSettings();
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
                TxtGlobalStatus.Text = "Ekran yapısı değişti. Ekranlar yeniden taranıyor...";
                RefreshDisplaysAndUI();
            });
        }

        private void OnCaptureFrameArrived(object? sender, FrameArrivedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // Update Control Window live preview
                ImgMiddlePreview.Source = e.Frame;
                TxtMiddleDraftPrompt.Visibility = Visibility.Collapsed;

                // Update FPS & Capture Mode badge
                TxtCaptureFps.Text = $"FPS: {e.Fps} | MODE: {e.CaptureMode} ({_captureService.CapturedWidth}x{_captureService.CapturedHeight})";

                // Pass captured frame to Master Output Window if live
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
                TxtGlobalStatus.Text = $"Sunum Yakalama Uyarısı: {err}";
            });
        }

        private void RefreshDisplaysAndUI()
        {
            if (_isSimulationMode)
            {
                _allDisplays = _displayService.GetSimulatedDisplays();
                TxtGlobalStatus.Text = "🧪 SİMÜLASYON MODU AKTİF — 3 Sanal Ekran Kullanılıyor.";
            }
            else
            {
                _allDisplays = _displayService.GetDisplays();
            }

            _controlDisplay = _displayService.GetPrimaryDisplay(_allDisplays) ?? _allDisplays.FirstOrDefault();
            TxtControlDisplayLabel.Text = _controlDisplay != null ? _controlDisplay.DisplayLabel : "Tespit Edilemedi";

            UpdateRoleComboBoxes();
        }

        private void UpdateRoleComboBoxes()
        {
            // Populate Display 2 (Presentation Source) Combo
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

            // Populate Display 3 (Master LED Output) Combo
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

        private void RestoreSavedSettings()
        {
            // Restore Display 2
            if (!string.IsNullOrEmpty(_appSettings.PresentationDeviceName) || !string.IsNullOrEmpty(_appSettings.PresentationDeviceId))
            {
                var match = _displayService.FindMatchingDisplay(_allDisplays, _appSettings.PresentationDeviceName, _appSettings.PresentationDeviceId);
                if (match != null) SelectComboItem(CmbPresentationSource, match);
            }

            // Restore Display 3
            if (!string.IsNullOrEmpty(_appSettings.MasterOutputDeviceName) || !string.IsNullOrEmpty(_appSettings.MasterOutputDeviceId))
            {
                var match = _displayService.FindMatchingDisplay(_allDisplays, _appSettings.MasterOutputDeviceName, _appSettings.MasterOutputDeviceId);
                if (match != null) SelectComboItem(CmbMasterOutput, match);
            }

            // Restore Left Media
            if (_appSettings.LeftOutput != null)
            {
                _draftState.Left.ScaleMode = _appSettings.LeftOutput.ScaleMode;
                SetScaleModeUI("LEFT", _appSettings.LeftOutput.ScaleMode);
                if (!string.IsNullOrEmpty(_appSettings.LeftOutput.LastMediaPath))
                {
                    LoadImageForCard("LEFT", _appSettings.LeftOutput.LastMediaPath);
                }
            }

            // Restore Right Media
            if (_appSettings.RightOutput != null)
            {
                _draftState.Right.ScaleMode = _appSettings.RightOutput.ScaleMode;
                SetScaleModeUI("RIGHT", _appSettings.RightOutput.ScaleMode);
                if (!string.IsNullOrEmpty(_appSettings.RightOutput.LastMediaPath))
                {
                    LoadImageForCard("RIGHT", _appSettings.RightOutput.LastMediaPath);
                }
            }
        }

        private void SelectComboItem(ComboBox combo, DisplayInfo target)
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

        private DisplayInfo? GetSelectedDisplay(ComboBox combo)
        {
            if (combo.SelectedItem is ComboBoxItem item && item.Tag is DisplayInfo d)
            {
                return d;
            }
            return null;
        }

        // --- Event Handlers ---

        private void CmbPresentationSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = GetSelectedDisplay(CmbPresentationSource);
            _presentationSourceDisplay = selected;

            if (selected != null)
            {
                _appSettings.PresentationDeviceName = selected.DeviceName;
                _appSettings.PresentationDeviceId = selected.DeviceId;
                _settingsService.SaveSettings(_appSettings);

                TxtGlobalStatus.Text = $"Display 2 Sunum Yakalama Kaynağı Seçildi: {selected.FriendlyName}";
                _captureService.StartCapture(selected);
            }
            else
            {
                _appSettings.PresentationDeviceName = null;
                _appSettings.PresentationDeviceId = null;
                _settingsService.SaveSettings(_appSettings);
                _captureService.StopCapture();
                ImgMiddlePreview.Source = null;
                TxtMiddleDraftPrompt.Visibility = Visibility.Visible;
            }

            UpdateRoleComboBoxes();
        }

        private void CmbMasterOutput_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = GetSelectedDisplay(CmbMasterOutput);
            _masterOutputDisplay = selected;

            if (selected != null)
            {
                _appSettings.MasterOutputDeviceName = selected.DeviceName;
                _appSettings.MasterOutputDeviceId = selected.DeviceId;
                _settingsService.SaveSettings(_appSettings);
                TxtGlobalStatus.Text = $"Display 3 Master LED Çıkışı Seçildi: {selected.FriendlyName}";
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
            if (TxtMiddleYOffset == null) return;

            int val = (int)e.NewValue;
            _draftState.MiddleYOffset = val;
            TxtMiddleYOffset.Text = $"{val} px {(val == 172 ? "(Ortalanmış)" : "")}";

            if (_appSettings != null && _settingsService != null)
            {
                _appSettings.MiddleYOffset = val;
                _settingsService.SaveSettings(_appSettings);
            }
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
                if (files != null && files.Length > 0)
                {
                    LoadImageForCard("LEFT", files[0]);
                }
            }
        }

        private void DropZoneRight_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    LoadImageForCard("RIGHT", files[0]);
                }
            }
        }

        private bool LoadImageForCard(string cardId, string filePath)
        {
            if (!ImageValidationHelper.ValidateImageFile(filePath, out string err))
            {
                TxtGlobalStatus.Text = $"HATA ({cardId}): {err}";
                return false;
            }

            try
            {
                var bitmap = CreateBitmap(filePath);
                if (bitmap != null)
                {
                    if (cardId == "LEFT")
                    {
                        _leftDraftBitmap = bitmap;
                        _draftState.Left.MediaPath = filePath;
                        _appSettings.LeftOutput.LastMediaPath = filePath;

                        ImgLeftPreview.Source = bitmap;
                        ImgLeftCardPreview.Source = bitmap;
                        ImgLeftCardPreview.Visibility = Visibility.Visible;
                        PanelLeftDropPrompt.Visibility = Visibility.Collapsed;
                        TxtLeftMediaPath.Text = $"Seçili: {Path.GetFileName(filePath)}";
                    }
                    else
                    {
                        _rightDraftBitmap = bitmap;
                        _draftState.Right.MediaPath = filePath;
                        _appSettings.RightOutput.LastMediaPath = filePath;

                        ImgRightPreview.Source = bitmap;
                        ImgRightCardPreview.Source = bitmap;
                        ImgRightCardPreview.Visibility = Visibility.Visible;
                        PanelRightDropPrompt.Visibility = Visibility.Collapsed;
                        TxtRightMediaPath.Text = $"Seçili: {Path.GetFileName(filePath)}";
                    }

                    _settingsService.SaveSettings(_appSettings);
                    TxtGlobalStatus.Text = $"{cardId} taslak görsel yüklendi: {Path.GetFileName(filePath)}. Yayına almak için TAKE butonuna basın.";
                    return true;
                }
            }
            catch (Exception ex)
            {
                TxtGlobalStatus.Text = $"Görsel Yükleme Hatası ({cardId}): {ex.Message}";
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
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        private void RadioLeftScale_Checked(object sender, RoutedEventArgs e)
        {
            if (RadioLeftFit == null) return;
            if (RadioLeftFit.IsChecked == true) _draftState.Left.ScaleMode = ScaleMode.Fit;
            else if (RadioLeftFill.IsChecked == true) _draftState.Left.ScaleMode = ScaleMode.Fill;
            else if (RadioLeftStretch.IsChecked == true) _draftState.Left.ScaleMode = ScaleMode.Stretch;

            if (_appSettings?.LeftOutput != null)
            {
                _appSettings.LeftOutput.ScaleMode = _draftState.Left.ScaleMode;
                _settingsService?.SaveSettings(_appSettings);
            }
        }

        private void RadioRightScale_Checked(object sender, RoutedEventArgs e)
        {
            if (RadioRightFit == null) return;
            if (RadioRightFit.IsChecked == true) _draftState.Right.ScaleMode = ScaleMode.Fit;
            else if (RadioRightFill.IsChecked == true) _draftState.Right.ScaleMode = ScaleMode.Fill;
            else if (RadioRightStretch.IsChecked == true) _draftState.Right.ScaleMode = ScaleMode.Stretch;

            if (_appSettings?.RightOutput != null)
            {
                _appSettings.RightOutput.ScaleMode = _draftState.Right.ScaleMode;
                _settingsService?.SaveSettings(_appSettings);
            }
        }

        private void SetScaleModeUI(string cardId, ScaleMode mode)
        {
            if (cardId == "LEFT")
            {
                RadioLeftFit.IsChecked = (mode == ScaleMode.Fit);
                RadioLeftFill.IsChecked = (mode == ScaleMode.Fill);
                RadioLeftStretch.IsChecked = (mode == ScaleMode.Stretch);
            }
            else
            {
                RadioRightFit.IsChecked = (mode == ScaleMode.Fit);
                RadioRightFill.IsChecked = (mode == ScaleMode.Fill);
                RadioRightStretch.IsChecked = (mode == ScaleMode.Stretch);
            }
        }

        private void BtnLeftBlack_Click(object sender, RoutedEventArgs e)
        {
            _draftState.Left.IsBlackout = !_draftState.Left.IsBlackout;
            BtnLeftBlack.Content = _draftState.Left.IsBlackout ? "🟡 LEFT Restore" : "⚫ LEFT Blackout";
        }

        private void BtnRightBlack_Click(object sender, RoutedEventArgs e)
        {
            _draftState.Right.IsBlackout = !_draftState.Right.IsBlackout;
            BtnRightBlack.Content = _draftState.Right.IsBlackout ? "🟡 RIGHT Restore" : "⚫ RIGHT Blackout";
        }

        // --- TAKE ACTION ---

        private void BtnTake_Click(object sender, RoutedEventArgs e)
        {
            // Atomically copy Draft State to Live State
            _liveState = _draftState.Clone();
            _leftLiveBitmap = _leftDraftBitmap;
            _rightLiveBitmap = _rightDraftBitmap;

            if (_outputManager.IsMasterOutputActive)
            {
                _outputManager.ApplyMasterState(_liveState, _leftLiveBitmap, _rightLiveBitmap);
                TxtMasterStatusBadge.Text = "[ LIVE / APPLIED ]";
                TxtMasterStatusBadge.Foreground = new SolidColorBrush(Colors.LimeGreen);
                TxtGlobalStatus.Text = "🎬 TAKE UYGULANDI — Taslak değişiklikler canlı Master LED ekranına gönderildi.";
            }
            else
            {
                TxtGlobalStatus.Text = "TAKE HAZIR — Master Output penceresini başlatmak için 'MASTER ÇIKIŞI BAŞLAT' butonuna basın.";
            }
        }

        private void BtnStartMaster_Click(object sender, RoutedEventArgs e)
        {
            if (_masterOutputDisplay == null)
            {
                MessageBox.Show("Lütfen öncelikle Display 3 (Master LED Output) ekranını seçin.", "Master Çıkış Uyarısı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _liveState = _draftState.Clone();
            _leftLiveBitmap = _leftDraftBitmap;
            _rightLiveBitmap = _rightDraftBitmap;

            try
            {
                _outputManager.StartMasterOutput(_masterOutputDisplay, _liveState, _leftLiveBitmap, _rightLiveBitmap);
                TxtMasterStatusBadge.Text = "[ LIVE / APPLIED ]";
                TxtMasterStatusBadge.Foreground = new SolidColorBrush(Colors.LimeGreen);
                TxtGlobalStatus.Text = $"Master LED Çıktısı {_masterOutputDisplay.FriendlyName} üzerinde CANLI yayında.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Master Output penceresi başlatılamadı: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnStopMaster_Click(object sender, RoutedEventArgs e)
        {
            _outputManager.StopMasterOutput();
            TxtMasterStatusBadge.Text = "[ DRAFT PREVIEW ]";
            TxtMasterStatusBadge.Foreground = new SolidColorBrush(Colors.Orange);
            TxtGlobalStatus.Text = "Master LED Çıktısı durduruldu.";
        }

        private void BtnMasterBlackout_Click(object sender, RoutedEventArgs e)
        {
            _draftState.IsMasterBlackout = !_draftState.IsMasterBlackout;
            _liveState.IsMasterBlackout = _draftState.IsMasterBlackout;

            if (_draftState.IsMasterBlackout)
            {
                BtnMasterBlackout.Content = "🟡 RESTORE MASTER";
            }
            else
            {
                BtnMasterBlackout.Content = "⚫ MASTER SİYAH EKRAN";
            }

            _outputManager.SetMasterBlackout(_draftState.IsMasterBlackout);
        }

        private void BtnToggleSimulation_Click(object sender, RoutedEventArgs e)
        {
            _isSimulationMode = !_isSimulationMode;
            _appSettings.IsSimulationMode = _isSimulationMode;
            _settingsService.SaveSettings(_appSettings);

            BtnToggleSimulation.Content = _isSimulationMode ? "🖥️ GERÇEK EKRAN MODU" : "🧪 SİMÜLASYON MODU";
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

            TxtGlobalStatus.Text = "Ekran tanımlama katmanı gösteriliyor (3.5 sn).";
        }
    }
}