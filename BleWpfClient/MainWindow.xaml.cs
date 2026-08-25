using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using TfLuna.BleClientLib;

namespace BleWpfClient;

public partial class MainWindow : Window
{
    private const int DefaultGraphWindowSeconds = 30;
    private const byte DefaultMaxGraphDistanceMetres = 10;

    private readonly TfLumaBleClientLib _client = new();
    private int _graphWindowSeconds = DefaultGraphWindowSeconds;
    private byte _maxGraphDistanceMetres = DefaultMaxGraphDistanceMetres;
    private readonly Stopwatch _elapsedStopwatch = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly Queue<(DateTime TimestampUtc, ushort DistanceMm)> _graphSamples = new();
    private bool _tfLunaFoundInScan;
    private bool _oneShotAwaitingCapture;

    public MainWindow()
    {
        InitializeComponent();
        Closed += MainWindow_Closed;
        DistanceGraphCanvas.SizeChanged += DistanceGraphCanvas_SizeChanged;

        _client.DistanceReceived += Client_DistanceReceived;

        ApplySavedSettingsToControls();
        _graphWindowSeconds = _settings.GraphWindowSeconds;
        _maxGraphDistanceMetres = _settings.GraphMaxDistanceMetres;
        GraphMaxDistanceTextBox.Text = _maxGraphDistanceMetres.ToString();
        GraphWindowTextBox.Text = FormatGraphWindow(_graphWindowSeconds);
        ViewToggleButton.Content = "View Graph";
        LogBorder.Visibility = Visibility.Visible;
        DistanceGraphCanvas.Visibility = Visibility.Collapsed;
        UpdateGraphVisibility();
        ResetToInitialState();
        Log("Ready. Run a scan to locate the TF-Luna BLE service.");
    }

    private void ApplySavedSettingsToControls()
    {
        foreach (ComboBoxItem item in ModeCombo.Items)
        {
            if (item.Tag is string tag && byte.TryParse(tag, out var modeValue) && modeValue == _settings.Mode)
            {
                ModeCombo.SelectedItem = item;
                break;
            }
        }

        ThresholdTextBox.Text = _settings.ThresholdMm.ToString();
        RangeMinTextBox.Text = _settings.RangeMinMm.ToString();
        RangeMaxTextBox.Text = _settings.RangeMaxMm.ToString();
        GraphMaxDistanceTextBox.Text = _settings.GraphMaxDistanceMetres.ToString();
        GraphWindowTextBox.Text = FormatGraphWindow(_settings.GraphWindowSeconds);
    }

    private static string FormatGraphWindow(int totalSeconds)
    {
        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;
        return $"{minutes:00}:{seconds:00}";
    }

    private static bool TryParseGraphWindow(string? text, out int totalSeconds)
    {
        totalSeconds = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();

        if (int.TryParse(trimmed, out var rawSeconds))
        {
            totalSeconds = rawSeconds;
        }
        else
        {
            var parts = trimmed.Split(':');
            if (parts.Length != 2)
            {
                return false;
            }

            if (!int.TryParse(parts[0], out var minutes) || !int.TryParse(parts[1], out var seconds))
            {
                return false;
            }

            if (minutes < 0 || seconds < 0 || seconds >= 60)
            {
                return false;
            }

            totalSeconds = (minutes * 60) + seconds;
        }

        if (totalSeconds <= 0)
        {
            return false;
        }

        totalSeconds = (totalSeconds / 10) * 10;
        return totalSeconds > 0;
    }

    private void ApplyGraphSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!byte.TryParse(GraphMaxDistanceTextBox.Text?.Trim(), out var graphMaxDistanceMetres) || graphMaxDistanceMetres < 1 || graphMaxDistanceMetres > 10)
        {
            MessageBox.Show("Graph max distance must be a number between 1 and 10 metres.", "Invalid Graph Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryParseGraphWindow(GraphWindowTextBox.Text, out var graphWindowSeconds) || graphWindowSeconds <= 0)
        {
            MessageBox.Show("Graph time must be either a seconds value or mm:ss, truncated to the nearest lower multiple of 10 seconds.", "Invalid Graph Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _maxGraphDistanceMetres = graphMaxDistanceMetres;
        _graphWindowSeconds = graphWindowSeconds;
        _settings.GraphMaxDistanceMetres = _maxGraphDistanceMetres;
        _settings.GraphWindowSeconds = _graphWindowSeconds;
        _settings.Save();

        GraphMaxDistanceTextBox.Text = _maxGraphDistanceMetres.ToString();
        GraphWindowTextBox.Text = FormatGraphWindow(_graphWindowSeconds);
        _graphSamples.Clear();
        RenderDistanceGraph();
        Log($"Graph settings updated: max={_maxGraphDistanceMetres} m, window={FormatGraphWindow(_graphWindowSeconds)}");
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true, "Scanning for TF-Luna...");
        try
        {
            var devices = await _client.ScanAsync(TimeSpan.FromSeconds(6));
            if (devices.Count == 0)
            {
                Log("Scan: no BLE advertisers detected.");
                return;
            }

            Log($"Scan: found {devices.Count} advertiser(s)");
            foreach (var d in devices.OrderByDescending(x => x.Rssi))
            {
                var name = string.IsNullOrWhiteSpace(d.Name) ? "(no local name)" : d.Name;
                var hasService = d.HasTargetService ? " [svc]" : string.Empty;
                Log($"  {TfLumaBleClientLib.FormatBluetoothAddress(d.Address)}  {name}  RSSI {d.Rssi} dBm{hasService}");
            }

            _tfLunaFoundInScan = devices.Any(d => d.HasTargetService);
            Log(_tfLunaFoundInScan
                ? "Scan: TF-Luna service found. Connecting automatically..."
                : "Scan: TF-Luna service not found. Connect remains disabled.");

            if (_tfLunaFoundInScan)
            {
                SetBusy(true, "Connecting...");
                await ConnectToDeviceAsync();
            }
        }
        catch (Exception ex)
        {
            Log($"Scan error: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_client.IsConnected)
        {
            SetBusy(true, "Disconnecting...");
            try
            {
                await _client.DisconnectAsync();
            }
            finally
            {
                ResetToInitialState();
                Log("Disconnected.");
                SetBusy(false);
            }

            return;
        }

        SetBusy(true, "Connecting...");
        try
        {
            await ConnectToDeviceAsync();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ConnectToDeviceAsync()
    {
        ConnectButton.Content = "Connecting";
        try
        {
            await _client.DisconnectAsync();

            var connected = await _client.ConnectAsync();
            if (!connected)
            {
                StatusText.Text = "Device not found";
                StatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
                ConnectButton.Content = "Connect";
                Log("Connect: TF-Luna not found.");
                return;
            }

            var modeOk = await SendModeAsync();
            var thresholdOk = await SendThresholdAsync();

            SetConnectedState(true);
            var addr = _client.ConnectedBluetoothAddress.HasValue
                ? TfLumaBleClientLib.FormatBluetoothAddress(_client.ConnectedBluetoothAddress.Value)
                : "(unknown)";
            StatusText.Text = $"Connected: {_client.ConnectedDeviceName} ({addr})";
            StatusText.Foreground = System.Windows.Media.Brushes.DarkGreen;
            Log($"Connect: success. mode={(modeOk ? "ok" : "failed")}, threshold={(thresholdOk ? "ok" : "failed")}");

            ElapsedText.Text = "--";
        }
        catch (Exception ex)
        {
            Log($"Connect error: {ex.Message}");
            await _client.DisconnectAsync();
            ResetToInitialState();
        }
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void SendThresholdButton_Click(object sender, RoutedEventArgs e)
    {
        await SendThresholdAsync();
    }

    private async void SendRangeButton_Click(object sender, RoutedEventArgs e)
    {
        await SendRangeAsync();
    }

    private void CaptureMinButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_client.TryGetLastDistanceMm(out var distanceMm))
        {
            Log("Min: no distance reading received yet.");
            return;
        }

        var thresholdMm = GetEffectiveThresholdMm();
        if (ushort.TryParse(RangeMaxTextBox.Text?.Trim(), out var currentMax) && distanceMm + thresholdMm > currentMax)
        {
            var message = $"Captured min ({distanceMm} mm) + threshold ({thresholdMm} mm) exceeds the current max ({currentMax} mm). Keeping the previous min value.";
            Log($"Min: {message}");
            MessageBox.Show(message, "Invalid Range", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        RangeMinTextBox.Text = distanceMm.ToString();
        Log($"Min: captured live distance {distanceMm} mm.");
    }

    private void CaptureMaxButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_client.TryGetLastDistanceMm(out var distanceMm))
        {
            Log("Max: no distance reading received yet.");
            return;
        }

        var thresholdMm = GetEffectiveThresholdMm();
        if (ushort.TryParse(RangeMinTextBox.Text?.Trim(), out var currentMin) && currentMin + thresholdMm > distanceMm)
        {
            var message = $"Current min ({currentMin} mm) + threshold ({thresholdMm} mm) exceeds the captured max ({distanceMm} mm). Keeping the previous max value.";
            Log($"Max: {message}");
            MessageBox.Show(message, "Invalid Range", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        RangeMaxTextBox.Text = distanceMm.ToString();
        Log($"Max: captured live distance {distanceMm} mm.");
    }

    private ushort GetEffectiveThresholdMm()
    {
        if (ushort.TryParse(ThresholdTextBox.Text?.Trim(), out var thresholdMm) && thresholdMm > 0)
        {
            return thresholdMm;
        }

        return 1;
    }

    private async void StartTimerButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_client.IsConnected)
        {
            Log("Start: device not connected.");
            return;
        }

        // Elapsed is timed on the PC clock rather than the device's onboard timestamp, which
        // can be stale in Threshold/One-Shot modes where notifications are infrequent.
        ElapsedText.Text = "00.000";
        _elapsedStopwatch.Restart();

        if (IsOneShotModeSelected())
        {
            _oneShotAwaitingCapture = true;

            var ok = await _client.TriggerOneShotRangeCaptureAsync();
            Log($"Start: one-shot capture armed: {(ok ? "sent" : "failed")}");
            return;
        }

        _graphSamples.Clear();
        Log("Start: elapsed timer started.");
    }

    private bool IsOneShotModeSelected()
    {
        return ModeCombo.SelectedItem is ComboBoxItem item && item.Tag is string modeText && modeText == "3";
    }

    private bool IsThresholdModeSelected()
    {
        return ModeCombo.SelectedItem is ComboBoxItem item && item.Tag is string modeText && modeText == "1";
    }

    private async void ModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateModeDependentControls();

        if (_client.IsConnected)
        {
            await SendModeAsync();
        }
    }

    private void UpdateModeDependentControls()
    {
        if (StartTimerButton is null || SendThresholdButton is null || SendRangeButton is null || ThresholdTextBox is null || RangeMinTextBox is null || RangeMaxTextBox is null
            || CaptureMinButton is null || CaptureMaxButton is null || RangeSettingsBorder is null || ThresholdSettingsBorder is null)
        {
            // Fires from InitializeComponent before later-declared controls exist yet.
            return;
        }

        var connected = _client.IsConnected;
        var oneShot = IsOneShotModeSelected();
        var thresholdMode = IsThresholdModeSelected();

        StartTimerButton.Visibility = oneShot ? Visibility.Visible : Visibility.Collapsed;
        StartTimerButton.IsEnabled = connected && oneShot;
        ThresholdTextBox.IsEnabled = connected && !oneShot;
        SendThresholdButton.IsEnabled = connected && !oneShot;
        ThresholdSettingsBorder.Visibility = thresholdMode ? Visibility.Visible : Visibility.Collapsed;
        RangeMinTextBox.IsEnabled = connected && oneShot;
        RangeMaxTextBox.IsEnabled = connected && oneShot;
        SendRangeButton.IsEnabled = connected && oneShot;
        CaptureMinButton.IsEnabled = connected && oneShot;
        CaptureMaxButton.IsEnabled = connected && oneShot;
        RangeSettingsBorder.Visibility = oneShot ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void MainWindow_Closed(object? sender, EventArgs e)
    {
        SaveCurrentSettings();
        await _client.DisposeAsync();
    }

    private void SaveCurrentSettings()
    {
        if (ModeCombo.SelectedItem is ComboBoxItem item && item.Tag is string modeText && byte.TryParse(modeText, out var mode))
        {
            _settings.Mode = mode;
        }

        if (ushort.TryParse(ThresholdTextBox.Text?.Trim(), out var threshold))
        {
            _settings.ThresholdMm = threshold;
        }

        if (ushort.TryParse(RangeMinTextBox.Text?.Trim(), out var rangeMin))
        {
            _settings.RangeMinMm = rangeMin;
        }

        if (ushort.TryParse(RangeMaxTextBox.Text?.Trim(), out var rangeMax))
        {
            _settings.RangeMaxMm = rangeMax;
        }

        _settings.GraphMaxDistanceMetres = _maxGraphDistanceMetres;
        _settings.GraphWindowSeconds = _graphWindowSeconds;
        _settings.Save();
    }

    private async Task<bool> SendModeAsync()
    {
        if (!_client.IsConnected)
        {
            return false;
        }

        if (ModeCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string modeText || !byte.TryParse(modeText, out var mode))
        {
            Log("Mode: invalid selection.");
            return false;
        }

        var ok = await _client.WriteModeAsync(mode);
        Log($"Mode write {mode}: {(ok ? "Success" : "Failed")}");
        return ok;
    }

    private async Task<bool> SendThresholdAsync()
    {
        if (!_client.IsConnected)
        {
            return false;
        }

        if (!ushort.TryParse(ThresholdTextBox.Text?.Trim(), out var threshold) || threshold == 0)
        {
            Log("Threshold: enter a valid value in mm (> 0).");
            return false;
        }

        var ok = await _client.WriteThresholdAsync(threshold);
        Log($"Threshold write {threshold} mm: {(ok ? "Success" : "Failed")}");
        return ok;
    }

    private async Task<bool> SendRangeAsync()
    {
        if (!_client.IsConnected)
        {
            return false;
        }

        if (!ushort.TryParse(RangeMinTextBox.Text?.Trim(), out var minMm) || !ushort.TryParse(RangeMaxTextBox.Text?.Trim(), out var maxMm))
        {
            Log("Range: enter valid numeric min/max values in mm.");
            return false;
        }

        if (!ushort.TryParse(ThresholdTextBox.Text?.Trim(), out var thresholdMm) || thresholdMm == 0)
        {
            thresholdMm = 1;
        }

        if (maxMm < minMm + thresholdMm)
        {
            var message = $"Max must be at least min + threshold ({minMm} + {thresholdMm} = {minMm + thresholdMm} mm).";
            Log($"Range: {message}");
            MessageBox.Show(message, "Invalid Range", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var ok = await _client.WriteRangeAsync(minMm, maxMm);
        Log($"Range write {minMm}..{maxMm} mm: {(ok ? "Success" : "Failed")}");
        return ok;
    }

    private void Client_DistanceReceived(object? sender, DistanceSampleEventArgs e)
    {
        var sample = e.Sample;

        Dispatcher.Invoke(() =>
        {
            if (!IsOneShotModeSelected())
            {
                RecordGraphSample(sample.DistanceMm);
            }

            if (IsOneShotModeSelected())
            {
                if (!_oneShotAwaitingCapture)
                {
                    Log($"One-shot: ignored extra reading {sample.DistanceMm} mm (already captured this start).");
                    return;
                }

                _oneShotAwaitingCapture = false;
                _elapsedStopwatch.Stop();

                var oneShotElapsedMs = (uint)_elapsedStopwatch.ElapsedMilliseconds;
                DistanceText.Text = $"{sample.DistanceMm} mm";
                SensorTimestampText.Text = sample.SensorTimestampMs.HasValue ? $"{sample.SensorTimestampMs.Value} ms" : "legacy payload";
                ElapsedText.Text = TfLumaBleClientLib.FormatElapsed(oneShotElapsedMs);
                Log($"Distance {sample.DistanceMm} mm @ {ElapsedText.Text} (PC-timed since Start)");
                return;
            }

            DistanceText.Text = $"{sample.DistanceMm} mm";
            SensorTimestampText.Text = sample.SensorTimestampMs.HasValue ? $"{sample.SensorTimestampMs.Value} ms" : "legacy payload";
            if (_elapsedStopwatch.IsRunning)
            {
                var elapsedMs = (uint)_elapsedStopwatch.ElapsedMilliseconds;
                ElapsedText.Text = TfLumaBleClientLib.FormatElapsed(elapsedMs);
            }
            else
            {
                ElapsedText.Text = "--";
            }

            Log($"Distance {sample.DistanceMm} mm @ {ElapsedText.Text}");
        });
    }

    private async Task DisconnectAsync()
    {
        await _client.DisconnectAsync();
        ResetToInitialState();
        Log("Disconnected.");
    }

    private void SetConnectedState(bool connected)
    {
        ModeCombo.IsEnabled = connected;
        StartTimerButton.IsEnabled = connected;
        ScanButton.IsEnabled = !connected;
        ConnectButton.IsEnabled = true;
        ConnectButton.Content = connected ? "Disconnect" : "Connect";
        UpdateModeDependentControls();
    }

    private void ResetToInitialState()
    {
        SetConnectedState(false);
        _tfLunaFoundInScan = false;
        _oneShotAwaitingCapture = false;
        _elapsedStopwatch.Reset();
        _graphSamples.Clear();
        ConnectButton.IsEnabled = false;
        StatusText.Text = "Disconnected";
        StatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
        DistanceText.Text = "-- mm";
        SensorTimestampText.Text = "-- ms";
        ElapsedText.Text = "--";
        RenderDistanceGraph();
    }

    private void SetBusy(bool busy, string? message = null)
    {
        ConnectButton.IsEnabled = !busy && (_client.IsConnected || _tfLunaFoundInScan);
        ScanButton.IsEnabled = !busy && !_client.IsConnected;

        if (BusyOverlay is not null)
        {
            BusyOverlay.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        }

        if (BusyMessageText is not null)
        {
            BusyMessageText.Text = message ?? "Working...";
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (LogBorder is not null && LogBorder.Visibility == Visibility.Visible)
        {
            LogList.Items.Clear();
            Log("Log cleared.");
            return;
        }

        if (DistanceGraphCanvas is not null && DistanceGraphCanvas.Visibility == Visibility.Visible)
        {
            _graphSamples.Clear();
            RenderDistanceGraph();
            Log("Graph cleared.");
        }
    }

    private void ViewToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewToggleButton is null || ClearButton is null || GraphSettingsPanel is null || LogBorder is null || GraphPanel is null || DistanceGraphCanvas is null || GraphPlaceholderText is null)
        {
            return;
        }

        var showLog = LogBorder.Visibility == Visibility.Visible;
        showLog = !showLog;
        ViewToggleButton.Content = showLog ? "View Graph" : "View Activity";
        ClearButton.Content = showLog ? "Clear Activity" : "Clear Graph";
        GraphSettingsPanel.Visibility = showLog ? Visibility.Collapsed : Visibility.Visible;

        LogBorder.Visibility = showLog ? Visibility.Visible : Visibility.Collapsed;
        DistanceGraphCanvas.Visibility = showLog ? Visibility.Collapsed : Visibility.Visible;
        GraphPlaceholderText.Visibility = Visibility.Collapsed;

        if (!showLog)
        {
            RenderDistanceGraph();
        }
    }

    private void DistanceGraphCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RenderDistanceGraph();
    }

    private void UpdateGraphVisibility()
    {
        if (ViewToggleButton is null || ClearButton is null || GraphSettingsPanel is null || DistanceGraphCanvas is null || GraphPlaceholderText is null || LogBorder is null || GraphPanel is null)
        {
            return;
        }

        var showLog = LogBorder.Visibility == Visibility.Visible;
        ViewToggleButton.Content = showLog ? "View Graph" : "View Activity";
        ClearButton.Content = showLog ? "Clear Activity" : "Clear Graph";
        GraphSettingsPanel.Visibility = showLog ? Visibility.Collapsed : Visibility.Visible;
        LogBorder.Visibility = showLog ? Visibility.Visible : Visibility.Collapsed;
        DistanceGraphCanvas.Visibility = showLog ? Visibility.Collapsed : Visibility.Visible;
        GraphPlaceholderText.Visibility = Visibility.Collapsed;

        if (!showLog)
        {
            RenderDistanceGraph();
        }
    }

    private void RecordGraphSample(ushort distanceMm)
    {
        var nowUtc = DateTime.UtcNow;
        _graphSamples.Enqueue((nowUtc, distanceMm));

        while (_graphSamples.Count > 0 && (nowUtc - _graphSamples.Peek().TimestampUtc).TotalSeconds > _graphWindowSeconds)
        {
            _graphSamples.Dequeue();
        }

        RenderDistanceGraph();
    }

    private void RenderDistanceGraph()
    {
        if (DistanceGraphCanvas is null || ViewToggleButton is null)
        {
            return;
        }

        if (DistanceGraphCanvas.Visibility != Visibility.Visible)
        {
            return;
        }

        DistanceGraphCanvas.Children.Clear();

        var width = DistanceGraphCanvas.ActualWidth;
        var height = DistanceGraphCanvas.ActualHeight;
        if (width <= 1 || height <= 1)
        {
            return;
        }

        var axisBrush = Brushes.LightSlateGray;
        var gridBrush = Brushes.Silver;
        var plotBrush = Brushes.DodgerBlue;

        const int yGridLines = 6;
        const int xGridLines = 8;

        for (var i = 0; i <= yGridLines; i++)
        {
            var y = (i / (double)yGridLines) * height;
            DistanceGraphCanvas.Children.Add(new Line
            {
                X1 = 0,
                X2 = width,
                Y1 = y,
                Y2 = y,
                Stroke = gridBrush,
                StrokeThickness = 1,
                Opacity = 0.75
            });
        }

        for (var i = 0; i <= xGridLines; i++)
        {
            var x = (i / (double)xGridLines) * width;
            DistanceGraphCanvas.Children.Add(new Line
            {
                X1 = x,
                X2 = x,
                Y1 = 0,
                Y2 = height,
                Stroke = gridBrush,
                StrokeThickness = 1,
                Opacity = 0.75
            });
        }

        var xAxis = new Line
        {
            X1 = 0,
            X2 = width,
            Y1 = height,
            Y2 = height,
            Stroke = axisBrush,
            StrokeThickness = 1.5
        };
        DistanceGraphCanvas.Children.Add(xAxis);

        var yAxis = new Line
        {
            X1 = 0,
            X2 = 0,
            Y1 = 0,
            Y2 = height,
            Stroke = axisBrush,
            StrokeThickness = 1.5
        };
        DistanceGraphCanvas.Children.Add(yAxis);

        var y0Label = new TextBlock
        {
            Text = "0 m",
            Foreground = Brushes.DimGray,
            FontSize = 10,
            Margin = new Thickness(2, 0, 0, 0)
        };
        Canvas.SetLeft(y0Label, 4);
        Canvas.SetTop(y0Label, height - 14);
        DistanceGraphCanvas.Children.Add(y0Label);

        var yMaxLabel = new TextBlock
        {
            Text = $"{_maxGraphDistanceMetres} m",
            Foreground = Brushes.DimGray,
            FontSize = 10,
            Margin = new Thickness(2, 0, 0, 0)
        };
        Canvas.SetLeft(yMaxLabel, 4);
        Canvas.SetTop(yMaxLabel, 2);
        DistanceGraphCanvas.Children.Add(yMaxLabel);

        var xLabel = new TextBlock
        {
            Text = $"0s",
            Foreground = Brushes.DimGray,
            FontSize = 10
        };
        Canvas.SetLeft(xLabel, 4);
        Canvas.SetTop(xLabel, height - 18);
        DistanceGraphCanvas.Children.Add(xLabel);

        var xMaxLabel = new TextBlock
        {
            Text = $"{_graphWindowSeconds}s",
            Foreground = Brushes.DimGray,
            FontSize = 10
        };
        Canvas.SetLeft(xMaxLabel, Math.Max(4, width - 30));
        Canvas.SetTop(xMaxLabel, height - 18);
        DistanceGraphCanvas.Children.Add(xMaxLabel);

        if (_graphSamples.Count == 0)
        {
            return;
        }

        var oldest = _graphSamples.Peek().TimestampUtc;
        var points = new PointCollection();
        var isThresholdMode = IsThresholdModeSelected();

        foreach (var sample in _graphSamples)
        {
            var elapsedSeconds = (sample.TimestampUtc - oldest).TotalSeconds;
            var x = (elapsedSeconds / Math.Max(_graphWindowSeconds, 1)) * width;
            var normalizedDistance = Math.Clamp(sample.DistanceMm / (double)Math.Max(_maxGraphDistanceMetres * 1000, 1), 0, 1);
            var y = height - (normalizedDistance * height);

            if (!isThresholdMode)
            {
                points.Add(new Point(x, y));
                continue;
            }

            if (points.Count == 0)
            {
                points.Add(new Point(x, y));
                continue;
            }

            var previous = points[points.Count - 1];
            points.Add(new Point(x, previous.Y));
            points.Add(new Point(x, y));
        }

        DistanceGraphCanvas.Children.Add(new Polyline
        {
            Stroke = plotBrush,
            StrokeThickness = 2,
            Points = points
        });
    }

    private void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        LogList.Items.Add(line);
        if (LogList.Items.Count > 800)
        {
            LogList.Items.RemoveAt(0);
        }

        LogList.ScrollIntoView(line);
    }
}
