# TF-Luma BLE

This repository contains three related .NET projects that implement clients for a TF-Luna distance sensor exposed over BLE,
plus an Arduino/Raspberry Pico sketch used to run the sensor firmware.

> Ultimately this project will be used with the [djaus2/SwissTimingGemini7SegDisplay](https://github.com/djaus2/SwissTimingGemini7SegDisplay) project to trigger the end of a sprint race.  
>
> ***Latest:* V1.3.5** Major functional changes but IS a work in progress.  
Thinking of using second core of pico to just continuosly get distance that can be read by first core via a callback
>
> **V1.3** Serial debug messages in Pico sketch now in module that can be turned off/on from WPF app. Sketch state (including this) saved to flash.
>
> **V1.2** Focus upon Threshold mode. Only log where beam is intercepted closer than that when Reset/Start. Also can turn Pico Serial Debug Off/On
>
> **V1.1** Major changes to WPF UI including Graph mode. Better button state management in UI.  Some changes to Pico sketch to match. Console unchanged.  
*Documentation needs to catch up.*

## C# Projects

- BleConsoleClient - a cross-platform console-style Windows BLE client that discovers the TF-Luna BLE service, subscribes to distance notifications and provides an interactive command prompt. See BleConsoleClient/README.md for details. Targets net10.0-windows10.0.19041.0.
- BleWpfClient - a Windows WPF desktop client with a GUI for scanning, connecting, and visualizing distance samples. See BleWpfClient/README.md for details. Targets net10.0-windows10.0.19041.0.
- TfLunaBleClientLib - shared library used by the console and WPF clients to perform BLE scanning, connection, subscription and control operations. See TfLunaBleClientLib/README.md. Targets net8.0-windows10.0.19041.0 and net10.0-windows10.0.19041.0.

## RPI Pico sketch

The RPI Pico sketch included with this project implements the BLE GATT service and characteristics used by the clients above. Key details:
> See its ReadMe for details.

- Service UUID: 0000A000-0000-1000-8000-00805F9B34FB
- Distance notification characteristic UUID: 0000A001-0000-1000-8000-00805F9B34FB
- Mode/threshold/write UUIDs: 0000A006 and 0000A007 (see device firmware)
- Distance notification payload (little-endian): bytes 0..1 = distance in mm (uint16), bytes 2..5 = timestamp in ms since board boot (uint32, optional)
- On startup the firmware writes a serial message: `BLE server advertising` which is useful when troubleshooting from a serial console.

### Flashing and testing notes

- Use your usual toolchain for the Pico (Arduino IDE with Pico core, picotool, or the Pico SDK) to flash the sketch.
- Open a serial console (commonly 115200 baud) to observe firmware messages such as `BLE server advertising`.
- On Windows, run one of the client apps (console or WPF) and scan for BLE advertisements. If the service is not found, ensure the board is powered and advertising and check serial output for errors.

