# BleConsoleClient

Windows desktop BLE client for the TF-Luna Arduino BLE service in this workspace.

## What it does

- Finds the BLE service UUID `0000A000-0000-1000-8000-00805F9B34FB`
- Subscribes to distance notifications UUID `0000A001-0000-1000-8000-00805F9B34FB`
- Writes mode UUID `0000A006-0000-1000-8000-00805F9B34FB`
- Writes threshold UUID `0000A007-0000-1000-8000-00805F9B34FB`

Distance notification payload format (little-endian):

- bytes 0..1: distance in mm (uint16)
- bytes 2..5: timestamp in ms since board boot (uint32)

## Run

1. Ensure the board is powered and advertising.
2. From this folder:

```powershell
dotnet run
```

Startup threshold argument (mm):

```powershell
dotnet run -- --threshold 100
```

If no threshold argument is provided, the app prompts for it and uses `100` mm by default.

Scan only mode:

```powershell
dotnet run -- scan
```

## Console commands

- `m 0` continuous updates
- `m 1` threshold/hysteresis mode
- `m 2` reserved mode accepted by firmware
- `t 25` set 25 mm threshold
- `s` start elapsed-time baseline from the latest sample
- `h` show help
- `q` quit

Full command names still work too (`mode`, `threshold`, `start`, `help`, `quit`).

## Notes

- This project targets `net8.0-windows10.0.19041.0`.
- On first use, Windows may require Bluetooth permission prompts.

## Troubleshooting

- If you see `TF-Luna service not found`, open Arduino serial monitor and confirm it prints `BLE server advertising`.
- Verify the board stays powered and within range while running `dotnet run`.
- Turn Bluetooth off/on in Windows and run again if discovery appears stale.
- If needed, remove old cached pairings for `TF-Luna` in Windows Bluetooth settings and retry.
- When service discovery fails, the app now automatically runs an 8-second BLE advertisement scan and prints detected devices.
