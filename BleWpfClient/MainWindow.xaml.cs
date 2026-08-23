using System.Windows;
using System.Windows.Controls;
using TfLuna.BleClientLib;

namespace BleWpfClient;

public partial class MainWindow : Window
{
    private readonly TfLunaBleClient _client = new();

    public MainWindow()
    {
        InitializeComponent();
        Closed += MainWindow_Closed;

        _client.DistanceReceived += Client_DistanceReceived;

        SetConnectedState(false);
        Log("Ready.");
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
                Log($"  {TfLunaBleClient.FormatBluetoothAddress(d.Address)}  {name}  RSSI {d.Rssi} dBm{hasService}");
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
        SetBusy(true);
        try
        {
            await _client.DisconnectAsync();

            var connected = await _client.ConnectAsync();
            if (!connected)
            {
                StatusText.Text = "Device not found";
                StatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
                Log("Connect: TF-Luna not found.");
                return;
            }

            var modeOk = await SendModeAsync();
            var thresholdOk = await SendThresholdAsync();

            SetConnectedState(true);
            var addr = _client.ConnectedBluetoothAddress.HasValue
                ? TfLunaBleClient.FormatBluetoothAddress(_client.ConnectedBluetoothAddress.Value)
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
            SetConnectedState(false);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void SendModeButton_Click(object sender, RoutedEventArgs e)
    {
        await SendModeAsync();
    }

    private async void SendThresholdButton_Click(object sender, RoutedEventArgs e)
    {
        await SendThresholdAsync();
    }

    private void StartTimerButton_Click(object sender, RoutedEventArgs e)
    {
        var started = _client.TryStartElapsedFromLatestSample();
        if (!started)
        {
            Log("Start: wait for first distance sample.");
            return;
        }

        ElapsedText.Text = "00.000";
        Log("Start: baseline set to latest sample timestamp.");
    }

    private async void MainWindow_Closed(object? sender, EventArgs e)
    {
        await _client.DisposeAsync();
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

    private void Client_DistanceReceived(object? sender, DistanceSampleEventArgs e)
    {
        var sample = e.Sample;

        Dispatcher.Invoke(() =>
        {
            DistanceText.Text = $"{sample.DistanceMm} mm";
            SensorTimestampText.Text = sample.SensorTimestampMs.HasValue ? $"{sample.SensorTimestampMs.Value} ms" : "legacy payload";
            ElapsedText.Text = sample.ElapsedMs.HasValue ? sample.TimeDisplay : "--";
            Log($"Distance {sample.DistanceMm} mm @ {sample.TimeDisplay}");
        });
    }

    private async Task DisconnectAsync()
    {
        await _client.DisconnectAsync();
        SetConnectedState(false);
        StatusText.Text = "Disconnected";
        StatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
    }

    private void SetConnectedState(bool connected)
    {
        SendModeButton.IsEnabled = connected;
        SendThresholdButton.IsEnabled = connected;
        StartTimerButton.IsEnabled = connected;
    }

    private void SetBusy(bool busy)
    {
        ConnectButton.IsEnabled = !busy;
        ScanButton.IsEnabled = !busy;
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
