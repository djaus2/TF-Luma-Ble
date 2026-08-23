using TfLuna.BleClientLib;

namespace BleConsoleClient;

internal static class Program
{
    private const ushort DefaultStartupThresholdMm = 100;

    public static async Task Main(string[] args)
    {
        await using var client = new TfLunaBleClient();
        client.DistanceReceived += (_, eventArgs) =>
        {
            var sample = eventArgs.Sample;
            Console.WriteLine($"Distance: {sample.DistanceMm} mm @ {sample.TimeDisplay}");
        };

        var probeIndex = Array.FindIndex(args, a => a.Equals("probe", StringComparison.OrdinalIgnoreCase) || a.Equals("--probe", StringComparison.OrdinalIgnoreCase));
        if (probeIndex >= 0)
        {
            if (probeIndex + 1 >= args.Length)
            {
                Console.WriteLine("Usage: dotnet run -- --probe 88:A2:9E:12:22:96");
                return;
            }

            var probe = await client.ProbeAsync(args[probeIndex + 1]);
            if (probe is null)
            {
                Console.WriteLine("Could not open device from the provided address.");
                return;
            }

            Console.WriteLine($"Connected to {probe.DeviceName} ({TfLunaBleClient.FormatBluetoothAddress(probe.Address)})");
            Console.WriteLine($"Service enumeration status: {probe.Status}");
            if (probe.Services.Count == 0)
            {
                Console.WriteLine("No GATT services exposed by this device.");
            }
            else
            {
                Console.WriteLine("Available GATT services:");
                foreach (var service in probe.Services)
                {
                    Console.WriteLine($"  {service}");
                }
            }

            return;
        }

        if (args.Any(a => a.Equals("scan", StringComparison.OrdinalIgnoreCase) || a.Equals("--scan", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine("Scanning BLE advertisements for 8 seconds...");
            var devices = await client.ScanAsync(TimeSpan.FromSeconds(8));
            PrintScan(devices);
            return;
        }

        var startupThresholdMm = ResolveStartupThreshold(args);
        Console.WriteLine($"Startup threshold: {startupThresholdMm} mm");

        Console.WriteLine("Searching for TF-Luna BLE service...");
        var connected = await client.ConnectAsync();
        if (!connected)
        {
            Console.WriteLine("TF-Luna service not found.");
            Console.WriteLine("Make sure the board is advertising and Bluetooth is enabled on this PC.");
            Console.WriteLine("Running an 8-second BLE scan for diagnostics...");
            var devices = await client.ScanAsync(TimeSpan.FromSeconds(8));
            PrintScan(devices);
            return;
        }

        var connectedName = client.ConnectedDeviceName ?? "(unknown)";
        var connectedAddress = client.ConnectedBluetoothAddress.HasValue
            ? TfLunaBleClient.FormatBluetoothAddress(client.ConnectedBluetoothAddress.Value)
            : "(unknown)";

        Console.WriteLine($"Connected to device: {connectedName} ({connectedAddress})");

        var modeOk = await client.WriteModeAsync(1);
        var thresholdOk = await client.WriteThresholdAsync(startupThresholdMm);

        Console.WriteLine($"Write mode=1: {(modeOk ? "Success" : "Failed")}");
        Console.WriteLine($"Write threshold={startupThresholdMm} mm: {(thresholdOk ? "Success" : "Failed")}");

        Console.WriteLine();
        PrintHelp();

        while (true)
        {
            Console.Write("> ");
            var line = Console.ReadLine();
            if (line is null)
            {
                continue;
            }

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            var command = parts[0].Trim();
            var commandKey = char.ToLowerInvariant(command[0]);

            if (command.Equals("quit", StringComparison.OrdinalIgnoreCase) || commandKey == 'q')
            {
                break;
            }

            if ((command.Equals("mode", StringComparison.OrdinalIgnoreCase) || commandKey == 'm') && parts.Length == 2 && byte.TryParse(parts[1], out var modeValue))
            {
                var ok = await client.WriteModeAsync(modeValue);
                Console.WriteLine($"Write mode={modeValue}: {(ok ? "Success" : "Failed")}");
                continue;
            }

            if ((command.Equals("threshold", StringComparison.OrdinalIgnoreCase) || commandKey == 't') && parts.Length == 2 && ushort.TryParse(parts[1], out var thresholdValue))
            {
                var ok = await client.WriteThresholdAsync(thresholdValue);
                Console.WriteLine($"Write threshold={thresholdValue} mm: {(ok ? "Success" : "Failed")}");
                continue;
            }

            if (command.Equals("start", StringComparison.OrdinalIgnoreCase) || commandKey == 's')
            {
                var started = client.TryStartElapsedFromLatestSample();
                if (started)
                {
                    Console.WriteLine("Start marker set to latest sample timestamp.");
                }
                else
                {
                    Console.WriteLine("No distance sample received yet. Wait for a reading, then run start.");
                }

                continue;
            }

            if (command.Equals("help", StringComparison.OrdinalIgnoreCase) || commandKey == 'h')
            {
                PrintHelp();
                continue;
            }

            Console.WriteLine("Unknown command.");
        }

        await client.DisconnectAsync();
    }

    private static void PrintScan(IReadOnlyList<AdvertisementInfo> snapshot)
    {
        if (snapshot.Count == 0)
        {
            Console.WriteLine("No BLE advertisements were detected during the scan window.");
            return;
        }

        Console.WriteLine($"Detected {snapshot.Count} BLE advertiser(s):");
        foreach (var item in snapshot.OrderBy(i => string.IsNullOrWhiteSpace(i.Name) ? "~" : i.Name, StringComparer.OrdinalIgnoreCase))
        {
            var displayName = string.IsNullOrWhiteSpace(item.Name) ? "(no local name)" : item.Name;
            var marker = item.HasTargetService ? " [has target service UUID]" : string.Empty;
            Console.WriteLine($"  {TfLunaBleClient.FormatBluetoothAddress(item.Address)}  {displayName}  RSSI {item.Rssi} dBm{marker}");
        }

        var tfLunaCandidates = snapshot
            .Where(i => i.HasTargetService || i.Name.Contains("TF-Luna", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (tfLunaCandidates.Count == 0)
        {
            Console.WriteLine("No TF-Luna-like advertisers found in this scan.");
        }
        else
        {
            Console.WriteLine($"TF-Luna candidates found: {tfLunaCandidates.Count}");
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Connected. Commands:");
        Console.WriteLine("  m <0|1|2>   mode");
        Console.WriteLine("  t <mm>      threshold in mm");
        Console.WriteLine("  s           start elapsed time from latest sample");
        Console.WriteLine("  h           help");
        Console.WriteLine("  q           quit");
    }

    private static ushort ResolveStartupThreshold(string[] args)
    {
        if (TryGetThresholdFromArgs(args, out var thresholdFromArg))
        {
            return thresholdFromArg;
        }

        Console.Write($"Enter startup threshold in mm [{DefaultStartupThresholdMm}]: ");
        var input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input))
        {
            return DefaultStartupThresholdMm;
        }

        if (ushort.TryParse(input.Trim(), out var thresholdFromPrompt) && thresholdFromPrompt > 0)
        {
            return thresholdFromPrompt;
        }

        Console.WriteLine($"Invalid threshold input. Using default {DefaultStartupThresholdMm} mm.");
        return DefaultStartupThresholdMm;
    }

    private static bool TryGetThresholdFromArgs(string[] args, out ushort thresholdMm)
    {
        thresholdMm = 0;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.Equals("--threshold", StringComparison.OrdinalIgnoreCase) || arg.Equals("threshold", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && ushort.TryParse(args[i + 1], out var parsed) && parsed > 0)
                {
                    thresholdMm = parsed;
                    return true;
                }

                return false;
            }

            if (arg.StartsWith("--threshold=", StringComparison.OrdinalIgnoreCase))
            {
                var value = arg.Substring("--threshold=".Length);
                if (ushort.TryParse(value, out var parsed) && parsed > 0)
                {
                    thresholdMm = parsed;
                    return true;
                }

                return false;
            }
        }

        return false;
    }
}
