using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace TfLuna.BleClientLib;

public sealed record AdvertisementInfo(ulong Address, string Name, bool HasTargetService, short Rssi);

public sealed record ProbeResult(string DeviceName, ulong Address, string Status, IReadOnlyList<Guid> Services);

public sealed record DistanceSample(ushort DistanceMm, uint? SensorTimestampMs, uint? ElapsedMs, string TimeDisplay);

public sealed class DistanceSampleEventArgs : EventArgs
{
	public DistanceSampleEventArgs(DistanceSample sample)
	{
		Sample = sample;
	}

	public DistanceSample Sample { get; }
}

public sealed class TfLunaBleClient : IAsyncDisposable
{
	private const string DefaultDeviceName = "TF-Luna";

	private static readonly Guid ServiceUuid = Guid.Parse("0000A000-0000-1000-8000-00805F9B34FB");
	private static readonly Guid DistanceUuid = Guid.Parse("0000A001-0000-1000-8000-00805F9B34FB");
	private static readonly Guid ModeUuid = Guid.Parse("0000A006-0000-1000-8000-00805F9B34FB");
	private static readonly Guid ThresholdUuid = Guid.Parse("0000A007-0000-1000-8000-00805F9B34FB");

	private readonly string _deviceName;
	private readonly object _stateSync = new();

	private BluetoothLEDevice? _device;
	private GattDeviceService? _service;
	private GattCharacteristic? _distanceCharacteristic;
	private GattCharacteristic? _modeCharacteristic;
	private GattCharacteristic? _thresholdCharacteristic;

	private bool _elapsedEnabled;
	private bool _hasLastSensorTimestamp;
	private uint _lastSensorTimestampMs;
	private uint _startSensorTimestampMs;

	public TfLunaBleClient(string? deviceName = null)
	{
		_deviceName = string.IsNullOrWhiteSpace(deviceName) ? DefaultDeviceName : deviceName;
	}

	public event EventHandler<DistanceSampleEventArgs>? DistanceReceived;

	public bool IsConnected => _device is not null && _service is not null;

	public string? ConnectedDeviceName => _device?.Name;

	public ulong? ConnectedBluetoothAddress => _device?.BluetoothAddress;

	public async ValueTask DisposeAsync()
	{
		await DisconnectAsync();
	}

	public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
	{
		await DisconnectAsync();

		var device = await FindTfLunaDeviceAsync(cancellationToken);
		if (device is null)
		{
			return false;
		}

		var serviceResult = await device.GetGattServicesForUuidAsync(ServiceUuid, BluetoothCacheMode.Uncached).AsTask(cancellationToken);
		if (serviceResult.Status != GattCommunicationStatus.Success || serviceResult.Services.Count == 0)
		{
			device.Dispose();
			return false;
		}

		var service = serviceResult.Services[0];
		var distance = await FindCharacteristicAsync(service, DistanceUuid, cancellationToken);
		var mode = await FindCharacteristicAsync(service, ModeUuid, cancellationToken);
		var threshold = await FindCharacteristicAsync(service, ThresholdUuid, cancellationToken);

		if (distance is null || mode is null || threshold is null)
		{
			service.Dispose();
			device.Dispose();
			return false;
		}

		distance.ValueChanged += DistanceCharacteristic_ValueChanged;
		var cccd = await distance.WriteClientCharacteristicConfigurationDescriptorAsync(
			GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask(cancellationToken);
		if (cccd != GattCommunicationStatus.Success)
		{
			distance.ValueChanged -= DistanceCharacteristic_ValueChanged;
			service.Dispose();
			device.Dispose();
			return false;
		}

		_device = device;
		_service = service;
		_distanceCharacteristic = distance;
		_modeCharacteristic = mode;
		_thresholdCharacteristic = threshold;

		lock (_stateSync)
		{
			_elapsedEnabled = false;
			_hasLastSensorTimestamp = false;
			_lastSensorTimestampMs = 0;
			_startSensorTimestampMs = 0;
		}

		return true;
	}

	public async Task DisconnectAsync()
	{
		try
		{
			if (_distanceCharacteristic is not null)
			{
				_distanceCharacteristic.ValueChanged -= DistanceCharacteristic_ValueChanged;
				await _distanceCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
					GattClientCharacteristicConfigurationDescriptorValue.None);
			}
		}
		catch
		{
		}

		_distanceCharacteristic = null;
		_modeCharacteristic = null;
		_thresholdCharacteristic = null;

		_service?.Dispose();
		_service = null;

		_device?.Dispose();
		_device = null;
	}

	public async Task<bool> WriteModeAsync(byte mode, CancellationToken cancellationToken = default)
	{
		if (_modeCharacteristic is null || mode > 2)
		{
			return false;
		}

		var writer = new DataWriter();
		writer.WriteByte(mode);
		var result = await _modeCharacteristic.WriteValueWithResultAsync(writer.DetachBuffer()).AsTask(cancellationToken);
		return result.Status == GattCommunicationStatus.Success;
	}

	public async Task<bool> WriteThresholdAsync(ushort thresholdMm, CancellationToken cancellationToken = default)
	{
		if (_thresholdCharacteristic is null || thresholdMm == 0)
		{
			return false;
		}

		var writer = new DataWriter { ByteOrder = ByteOrder.LittleEndian };
		writer.WriteUInt16(thresholdMm);
		var result = await _thresholdCharacteristic.WriteValueWithResultAsync(writer.DetachBuffer()).AsTask(cancellationToken);
		return result.Status == GattCommunicationStatus.Success;
	}

	public bool TryStartElapsedFromLatestSample()
	{
		lock (_stateSync)
		{
			if (!_hasLastSensorTimestamp)
			{
				return false;
			}

			_startSensorTimestampMs = _lastSensorTimestampMs;
			_elapsedEnabled = true;
			return true;
		}
	}

	public async Task<IReadOnlyList<AdvertisementInfo>> ScanAsync(TimeSpan duration, CancellationToken cancellationToken = default)
	{
		return await CollectAdvertisementsAsync(duration, cancellationToken);
	}

	public async Task<ProbeResult?> ProbeAsync(string bluetoothAddress, CancellationToken cancellationToken = default)
	{
		if (!TryParseBluetoothAddress(bluetoothAddress, out var address))
		{
			return null;
		}

		var device = await OpenDeviceFromAddressAsync(address, cancellationToken);
		if (device is null)
		{
			return null;
		}

		using (device)
		{
			var result = await device.GetGattServicesAsync(BluetoothCacheMode.Uncached).AsTask(cancellationToken);
			var services = result.Status == GattCommunicationStatus.Success
				? result.Services.Select(s => s.Uuid).ToList()
				: new List<Guid>();

			return new ProbeResult(device.Name, device.BluetoothAddress, result.Status.ToString(), services);
		}
	}

	public async Task<IReadOnlyList<Guid>> GetConnectedServiceUuidsAsync(CancellationToken cancellationToken = default)
	{
		if (_device is null)
		{
			return Array.Empty<Guid>();
		}

		var result = await _device.GetGattServicesAsync(BluetoothCacheMode.Uncached).AsTask(cancellationToken);
		if (result.Status != GattCommunicationStatus.Success)
		{
			return Array.Empty<Guid>();
		}

		return result.Services.Select(s => s.Uuid).ToList();
	}

	private async Task<BluetoothLEDevice?> FindTfLunaDeviceAsync(CancellationToken cancellationToken)
	{
		var serviceSelector = GattDeviceService.GetDeviceSelectorFromUuid(ServiceUuid);
		var serviceMatches = await DeviceInformation.FindAllAsync(serviceSelector).AsTask(cancellationToken);
		foreach (var service in serviceMatches)
		{
			var device = await BluetoothLEDevice.FromIdAsync(service.Id).AsTask(cancellationToken);
			if (device is not null)
			{
				if (await HasTargetServiceAsync(device, cancellationToken))
				{
					return device;
				}

				device.Dispose();
			}
		}

		var selector = BluetoothLEDevice.GetDeviceSelector();
		var devices = await DeviceInformation.FindAllAsync(selector).AsTask(cancellationToken);
		var named = devices.Where(d => !string.IsNullOrWhiteSpace(d.Name) && d.Name.Contains(_deviceName, StringComparison.OrdinalIgnoreCase));

		foreach (var candidate in named)
		{
			var device = await BluetoothLEDevice.FromIdAsync(candidate.Id).AsTask(cancellationToken);
			if (device is null)
			{
				continue;
			}

			if (await HasTargetServiceAsync(device, cancellationToken))
			{
				return device;
			}

			device.Dispose();
		}

		var liveCandidates = await CollectAdvertisementsAsync(TimeSpan.FromSeconds(8), cancellationToken);
		var tfLunaCandidates = liveCandidates
			.Where(c => c.HasTargetService || c.Name.Contains(_deviceName, StringComparison.OrdinalIgnoreCase))
			.OrderByDescending(c => c.HasTargetService)
			.ThenByDescending(c => c.Rssi);

		foreach (var candidate in tfLunaCandidates)
		{
			var device = await OpenDeviceFromAddressAsync(candidate.Address, cancellationToken);
			if (device is null)
			{
				continue;
			}

			if (await HasTargetServiceAsync(device, cancellationToken))
			{
				return device;
			}

			device.Dispose();
		}

		return null;
	}

	private static async Task<IReadOnlyList<AdvertisementInfo>> CollectAdvertisementsAsync(TimeSpan duration, CancellationToken cancellationToken)
	{
		var seen = new Dictionary<ulong, AdvertisementInfo>();
		var sync = new object();

		var watcher = new BluetoothLEAdvertisementWatcher
		{
			ScanningMode = BluetoothLEScanningMode.Active
		};

		watcher.Received += (_, eventArgs) =>
		{
			var name = eventArgs.Advertisement.LocalName ?? string.Empty;
			var hasTargetService = eventArgs.Advertisement.ServiceUuids.Contains(ServiceUuid);

			lock (sync)
			{
				if (seen.TryGetValue(eventArgs.BluetoothAddress, out var existing))
				{
					var mergedName = string.IsNullOrWhiteSpace(existing.Name) ? name : existing.Name;
					var mergedService = existing.HasTargetService || hasTargetService;
					var strongestRssi = eventArgs.RawSignalStrengthInDBm > existing.Rssi ? eventArgs.RawSignalStrengthInDBm : existing.Rssi;
					seen[eventArgs.BluetoothAddress] = new AdvertisementInfo(eventArgs.BluetoothAddress, mergedName, mergedService, strongestRssi);
				}
				else
				{
					seen[eventArgs.BluetoothAddress] = new AdvertisementInfo(eventArgs.BluetoothAddress, name, hasTargetService, eventArgs.RawSignalStrengthInDBm);
				}
			}
		};

		watcher.Start();
		try
		{
			await Task.Delay(duration, cancellationToken);
		}
		catch (TaskCanceledException)
		{
		}
		finally
		{
			watcher.Stop();
		}

		lock (sync)
		{
			return seen.Values.ToList();
		}
	}

	private static async Task<BluetoothLEDevice?> OpenDeviceFromAddressAsync(ulong address, CancellationToken cancellationToken)
	{
		var selector = BluetoothLEDevice.GetDeviceSelectorFromBluetoothAddress(address);
		var matches = await DeviceInformation.FindAllAsync(selector).AsTask(cancellationToken);
		var match = matches.FirstOrDefault();
		if (match is null)
		{
			return null;
		}

		return await BluetoothLEDevice.FromIdAsync(match.Id).AsTask(cancellationToken);
	}

	private static async Task<bool> HasTargetServiceAsync(BluetoothLEDevice device, CancellationToken cancellationToken)
	{
		var serviceResult = await device.GetGattServicesForUuidAsync(ServiceUuid, BluetoothCacheMode.Uncached).AsTask(cancellationToken);
		return serviceResult.Status == GattCommunicationStatus.Success && serviceResult.Services.Count > 0;
	}

	private static async Task<GattCharacteristic?> FindCharacteristicAsync(GattDeviceService service, Guid uuid, CancellationToken cancellationToken)
	{
		var result = await service.GetCharacteristicsForUuidAsync(uuid, BluetoothCacheMode.Uncached).AsTask(cancellationToken);
		if (result.Status != GattCommunicationStatus.Success || result.Characteristics.Count == 0)
		{
			return null;
		}

		return result.Characteristics[0];
	}

	private void DistanceCharacteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
	{
		var bytes = args.CharacteristicValue.ToArray();
		if (bytes.Length < 2)
		{
			return;
		}

		var distanceMm = BitConverter.ToUInt16(bytes, 0);
		uint? timestampMs = null;
		uint? elapsedMs = null;
		var timeDisplay = "(no timestamp)";

		if (bytes.Length >= 6)
		{
			timestampMs = BitConverter.ToUInt32(bytes, 2);

			lock (_stateSync)
			{
				_hasLastSensorTimestamp = true;
				_lastSensorTimestampMs = timestampMs.Value;

				if (_elapsedEnabled)
				{
					elapsedMs = unchecked(timestampMs.Value - _startSensorTimestampMs);
					timeDisplay = FormatElapsed(elapsedMs.Value);
				}
				else
				{
					timeDisplay = $"{timestampMs.Value} ms";
				}
			}
		}

		DistanceReceived?.Invoke(this, new DistanceSampleEventArgs(new DistanceSample(distanceMm, timestampMs, elapsedMs, timeDisplay)));
	}

	public static string FormatElapsed(uint elapsedMs)
	{
		var ts = TimeSpan.FromMilliseconds(elapsedMs);
		if (ts.Hours > 0)
		{
			return $"{ts.Hours:00}:{ts.Minutes:00}:{ts.Seconds:00}.{ts.Milliseconds:000}";
		}

		if (ts.Minutes > 0)
		{
			return $"{ts.Minutes:00}:{ts.Seconds:00}.{ts.Milliseconds:000}";
		}

		return $"{ts.Seconds:00}.{ts.Milliseconds:000}";
	}

	public static string FormatBluetoothAddress(ulong address)
	{
		var hex = address.ToString("X12");
		return string.Join(":", Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2)));
	}

	public static bool TryParseBluetoothAddress(string value, out ulong address)
	{
		var compact = value.Replace(":", string.Empty, StringComparison.Ordinal)
			.Replace("-", string.Empty, StringComparison.Ordinal)
			.Trim();

		return ulong.TryParse(compact, System.Globalization.NumberStyles.HexNumber, null, out address);
	}
}
