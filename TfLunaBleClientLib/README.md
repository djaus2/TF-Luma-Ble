# TfLunaBleClientLib

Shared .NET library providing TF-Luna BLE client functionality for the console and WPF apps in this solution.

## What it provides

- Scanning for BLE advertisers and probing for the TF-Luna service.
- Connecting to the TF-Luna GATT service and subscribing to distance notifications.
- High-level helpers to write mode, threshold, range, and trigger one-shot captures.
- Eventing model to receive parsed DistanceSample values.

## The BLE service

> This is provided by the RPI Pico W/2W over Bluetooth.

Service UUID: `0000A000-0000-1000-8000-00805F9B34FB`
Device name: `TF-Luna`

### Modes

- **Mode 0 (Continuous):** publishes a reading every loop iteration.
- **Mode 1 (Threshold):** publishes only when the distance changes by at least `Threshold` mm since the last published reading.
- **Mode 2:** reserved, accepted but not currently implemented as a distinct behavior.
- **Mode 3 (One-Shot):** after writing `1` to the Start characteristic, the firmware publishes the *first* reading that falls within `[Range Min, Range Max]` mm, then automatically disarms until the next Start write.

### Characteristics

| Characteristic | UUID | Access | Purpose |
|---|---|---|---|
| Distance | `0000A001-...` | Read/Notify | Distance (mm) + onboard timestamp (ms) |
| Mode | `0000A006-...` | Read/Write | `0` continuous, `1` threshold hysteresis, `2` reserved, `3` one-shot in-range capture |
| Threshold | `0000A007-...` | Read/Write | Minimum change (mm) required to publish a new reading in mode `1` |
| Range Min | `0000A008-...` | Read/Write | Minimum distance (mm) accepted in one-shot mode `3` |
| Range Max | `0000A009-...` | Read/Write | Maximum distance (mm) accepted in one-shot mode `3` |
| Start | `0000A00A-...` | Read/Write | Write `1` to arm a single one-shot capture; write `0` to cancel |

## Key types & members

- TfLumaBleClientLib
  - Constructor: TfLumaBleClientLib(string? deviceName = null)
  - Task<bool> ConnectAsync(CancellationToken)
  - Task DisconnectAsync()
  - Task<bool> WriteModeAsync(byte mode, CancellationToken)
  - Task<bool> WriteThresholdAsync(ushort thresholdMm, CancellationToken)
  - Task<bool> WriteRangeAsync(ushort minMm, ushort maxMm, CancellationToken)
  - Task<bool> TriggerOneShotRangeCaptureAsync(CancellationToken)
  - event EventHandler<DistanceSampleEventArgs> DistanceReceived
  - Helpers: FormatBluetoothAddress, TryParseBluetoothAddress, FormatElapsed

- DistanceSample
  - DistanceMm (ushort)
  - SensorTimestampMs (uint?) - optional onboard timestamp
  - ElapsedMs (uint?) - elapsed ms computed by the library when enabled
  - TimeDisplay (string) - human readable time value

Usage example

```csharp
using TfLuna.BleClientLib;

var client = new TfLumaBleClientLib();
client.DistanceReceived += (s, e) => Console.WriteLine($"{e.Sample.DistanceMm} mm @ {e.Sample.TimeDisplay}");
if (await client.ConnectAsync())
{
	await client.WriteThresholdAsync(100);
	// ...
}
```

Start/Stop measurement abstraction

The library exposes higher-level methods to control measurement reporting so callers don't need to write raw Start characteristic values:

- StartMeasurementsAsync() — writes Start=1 and raises MeasurementStateChanged(true) when successful.
- StopMeasurementsAsync() — writes Start=0 and raises MeasurementStateChanged(false) when successful.
- IsMeasuring — property indicating the last-known measurement state.

Use these methods from your UI to start/stop continuous or threshold reporting without accessing GATT details directly.

Target frameworks

- net8.0-windows10.0.19041.0
- net10.0-windows10.0.19041.0

Notes

- The library depends on Windows Bluetooth APIs (Windows.Devices.Bluetooth.*) and therefore runs on Windows desktop.
- For details on the BLE UUIDs and payload format, see the example clients in ../BleConsoleClient and ../BleWpfClient.
