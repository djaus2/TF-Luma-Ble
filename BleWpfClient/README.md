# BleWpfClient

Windows WPF desktop BLE client for the TF-Luna BLE service in this workspace.

What it does

- Scans for TF-Luna BLE advertisers and optionally connects automatically when the service is found.
- Subscribes to distance notifications and displays received samples in the UI.
- Sends configuration to the device: mode, threshold, and one-shot range commands.
- Uses the shared TfLunaBleClientLib for all BLE interactions.

Service and characteristic UUIDs

- Service: `0000A000-0000-1000-8000-00805F9B34FB`
- Distance notifications: `0000A001-0000-1000-8000-00805F9B34FB`
- Mode write: `0000A006-0000-1000-8000-00805F9B34FB`
- Threshold write: `0000A007-0000-1000-8000-00805F9B34FB`

Distance notification payload format (little-endian)

- bytes 0..1: distance in mm (uint16)
- bytes 2..5: timestamp in ms since board boot (uint32) — optional

Run

1. Open the solution in Visual Studio and run BleWpfClient, or from this folder:

```powershell
dotnet run
```

Notes

- This project targets `net10.0-windows10.0.19041.0` and requires a Windows build host with Bluetooth support.
- The UI exposes controls to change mode, set threshold/range, start an elapsed timer, and trigger one-shot captures.
- The application uses the shared library in ../TfLunaBleClientLib; see that README for API details and examples.

Troubleshooting

- If the TF-Luna service is not found, ensure the board is powered and advertising, and check for serial output from the device (the firmware prints `BLE server advertising` on startup).
- Toggle Windows Bluetooth or remove cached pairings for `TF-Luna` if discovery appears stale.
