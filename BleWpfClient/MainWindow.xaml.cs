using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using TfLuna.BleClientLib;

namespace BleWpfClient;

public partial class MainWindow : Window
{
    private readonly TfLumaBleClientLib _client = new();
    private readonly Stopwatch _elapsedStopwatch = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private bool _tfLunaFoundInScan;
    private bool _oneShotAwaitingCapture;

    public MainWindow()
    {
        InitializeComponent();
        Closed += MainWindow_Closed;

        _client.DistanceReceived += Client_DistanceReceived;

        ApplySavedSettingsToControls();
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
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
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
            SetBusy(true);
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

        SetBusy(true);
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

    private async void SendModeButton_Click(object sender, RoutedEventArgs e)
    {
        await SendModeAsync();
    }

    private async void SendThresholdButton_Click(object sender, RoutedEventArgs e)
    {
        await SendThresholdAsync();
    }

    private async void SendRangeButton_Click(object sender, RoutedEventArgs e)
    {
        await SendRangeAsync();
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

        Log("Start: elapsed timer started.");
    }

    private bool IsOneShotModeSelected()
    {
        return ModeCombo.SelectedItem is ComboBoxItem item && item.Tag is string modeText && modeText == "3";
    }

    private void ModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateModeDependentControls();
    }

    private void UpdateModeDependentControls()
    {
        if (SendThresholdButton is null || SendRangeButton is null || ThresholdTextBox is null || RangeMinTextBox is null || RangeMaxTextBox is null)
        {
            // Fires from InitializeComponent before later-declared controls exist yet.
            return;
        }

        var connected = _client.IsConnected;
        var oneShot = IsOneShotModeSelected();

        ThresholdTextBox.IsEnabled = connected && !oneShot;
        SendThresholdButton.IsEnabled = connected && !oneShot;
        RangeMinTextBox.IsEnabled = connected && oneShot;
        RangeMaxTextBox.IsEnabled = connected && oneShot;
        SendRangeButton.IsEnabled = connected && oneShot;
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

        if (!ushort.TryParse(RangeMinTextBox.Text?.Trim(), out var minMm) || !ushort.TryParse(RangeMaxTextBox.Text?.Trim(), out var maxMm) || minMm == 0 || maxMm == 0 || minMm > maxMm)
        {
            Log("Range: enter valid min/max values where min <= max and > 0.");
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
        SendModeButton.IsEnabled = connected;
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
        ConnectButton.IsEnabled = false;
        StatusText.Text = "Disconnected";
        StatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
        DistanceText.Text = "-- mm";
        SensorTimestampText.Text = "-- ms";
        ElapsedText.Text = "--";
    }

    private void SetBusy(bool busy)
    {
        ConnectButton.IsEnabled = !busy && (_client.IsConnected || _tfLunaFoundInScan);
        ScanButton.IsEnabled = !busy && !_client.IsConnected;
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
