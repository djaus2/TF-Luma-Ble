#include <BLE.h>
#include "tf-luma.h"
#include "serialdebug.h"

const char* DEVICE_NAME = "TF-Luna";

// Use explicit 16-bit UUIDs because this BLE stack may not parse 128-bit
// UUID strings reliably when creating BLEUUID objects.
const BLEUUID SERVICE_UUID(0xA000);
const BLEUUID DISTANCE_UUID(0xA001);
const BLEUUID MODE_UUID(0xA006);
const BLEUUID THRESHOLD_UUID(0xA007);
const BLEUUID RANGE_MIN_UUID(0xA008);
const BLEUUID RANGE_MAX_UUID(0xA009);
const BLEUUID START_UUID(0xA00A);
const BLEUUID DEBUG_UUID(0xA00B);

BLEService* tfLunaService = nullptr;
BLECharacteristic* distanceCharacteristic = nullptr;
BLECharacteristic* modeCharacteristic = nullptr;
BLECharacteristic* thresholdCharacteristic = nullptr;
BLECharacteristic* rangeMinCharacteristic = nullptr;
BLECharacteristic* rangeMaxCharacteristic = nullptr;
BLECharacteristic* startCharacteristic = nullptr;
BLECharacteristic* debugCharacteristic = nullptr;

uint8_t bleMode = 1;      // 0 = always publish, 1 = threshold hysteresis, 3 = one-shot in-range capture
uint16_t thresholdMm = 100;
uint16_t rangeMinMm = 300;
uint16_t rangeMaxMm = 2000;
uint16_t lastDistanceMm = 0;
bool hasLastDistance = false;
bool oneShotCaptureActive = false;
bool resetMeasurementRequested = false;
bool measurementRunning = false;  // mode 1 only reports distance once Start has been received


void onModeWrite(BLECharacteristic* characteristic) {
  uint8_t mode = characteristic->getUInt8();
  if (mode == 0 || mode == 1 || mode == 3) {
    if (mode != bleMode) {
      hasLastDistance = false;
      lastDistanceMm = 0;
      measurementRunning = false;
      Serialprintln("Distance reporting baseline reset");
    }

    bleMode = mode;
    oneShotCaptureActive = false;
    Serialprint("Mode updated to ");
    Serialprint(bleMode);
    Serialprint("; threshold reporting is ");
    Serialprintln(bleMode == 1 ? "enabled" : "disabled");
  }
}

void onThresholdWrite(BLECharacteristic* characteristic) {
  uint16_t value = characteristic->getUInt16();
  if (value == 0) {
    value = 1;
  }

  thresholdMm = value;
  hasLastDistance = false;
  lastDistanceMm = 0;
  characteristic->setValue((uint16_t)thresholdMm);

  Serialprint("Threshold updated to ");
  Serialprint(thresholdMm);
  Serialprintln(" mm; distance reporting baseline reset");
}

void onRangeMinWrite(BLECharacteristic* characteristic) {
  uint16_t value = characteristic->getUInt16();
  if (value > rangeMaxMm) {
    value = rangeMaxMm;
  }

  rangeMinMm = value;
  characteristic->setValue((uint16_t)rangeMinMm);

  Serialprint("Range min updated to ");
  Serialprint(rangeMinMm);
  Serialprintln(" mm");
}

void onRangeMaxWrite(BLECharacteristic* characteristic) {
  uint16_t value = characteristic->getUInt16();
  if (value == 0) {
    value = 1;
  }
  if (value < rangeMinMm) {
    value = rangeMinMm;
  }

  rangeMaxMm = value;
  characteristic->setValue((uint16_t)rangeMaxMm);

  Serialprint("Range max updated to ");
  Serialprint(rangeMaxMm);
  Serialprintln(" mm");
}

void onStartWrite(BLECharacteristic* characteristic) {
  uint8_t trigger = characteristic->getUInt8();
  if (trigger == 0) {
    oneShotCaptureActive = false;
    measurementRunning = false;
    resetMeasurementRequested = true;
    Serialprintln("Reset received; measuring distance for reset response");
    return;
  }

  if (bleMode != 3) {
    measurementRunning = true;
    Serialprintln("Measurement session start received");
    return;
  }

  oneShotCaptureActive = true;
  Serialprint("One-shot range capture started for ");
  Serialprint(rangeMinMm);
  Serialprint(".. ");
  Serialprint(rangeMaxMm);
  Serialprintln(" mm");
}

void publishDistance(uint16_t distanceMm, uint32_t timestampMs, char changeSign = ' ') {
  if (distanceCharacteristic == nullptr) {
    return;
  }

  // Payload format (little-endian):
  // [0..1] distanceMm (uint16), [2..5] timestampMs since boot (uint32), [6] change sign
  uint8_t payload[7];
  payload[0] = static_cast<uint8_t>(distanceMm & 0xFF);
  payload[1] = static_cast<uint8_t>((distanceMm >> 8) & 0xFF);
  payload[2] = static_cast<uint8_t>(timestampMs & 0xFF);
  payload[3] = static_cast<uint8_t>((timestampMs >> 8) & 0xFF);
  payload[4] = static_cast<uint8_t>((timestampMs >> 16) & 0xFF);
  payload[5] = static_cast<uint8_t>((timestampMs >> 24) & 0xFF);
  payload[6] = static_cast<uint8_t>(changeSign);

  // In Arduino-Pico, setting the characteristic value is enough for a
  // subscribed client to receive a notification when BLENotify is enabled.
  distanceCharacteristic->setValue(payload, sizeof(payload));
}
TFLuma tfluma;
void setup() {
  loadSendDebFromFlash();
  Serialbegin(115200);
  delay(500);
  Serialprintln("Starting TF-Luna BLE server...");
  if (!tfluma.begin()) {
    Serialprintln("TF-Luna init failed");
  }
  BLE.begin(DEVICE_NAME);

  tfLunaService = new BLEService(SERVICE_UUID);
  distanceCharacteristic = new BLECharacteristic(DISTANCE_UUID, BLERead | BLENotify, "Distance+Timestamp");
  modeCharacteristic = new BLECharacteristic(MODE_UUID, BLERead | BLEWrite, "Mode");
  thresholdCharacteristic = new BLECharacteristic(THRESHOLD_UUID, BLERead | BLEWrite, "Threshold mm");
  rangeMinCharacteristic = new BLECharacteristic(RANGE_MIN_UUID, BLERead | BLEWrite, "Min range mm");
  rangeMaxCharacteristic = new BLECharacteristic(RANGE_MAX_UUID, BLERead | BLEWrite, "Max range mm");
  startCharacteristic = new BLECharacteristic(START_UUID, BLERead | BLEWrite, "Start capture");
  debugCharacteristic = new BLECharacteristic(DEBUG_UUID, BLERead | BLEWrite, "Debug output enabled");

  modeCharacteristic->onWrite(onModeWrite);
  thresholdCharacteristic->onWrite(onThresholdWrite);
  rangeMinCharacteristic->onWrite(onRangeMinWrite);
  rangeMaxCharacteristic->onWrite(onRangeMaxWrite);
  startCharacteristic->onWrite(onStartWrite);
  debugCharacteristic->onWrite(onDebugWrite);

  modeCharacteristic->setValue((uint8_t)bleMode);
  thresholdCharacteristic->setValue((uint16_t)thresholdMm);
  rangeMinCharacteristic->setValue((uint16_t)rangeMinMm);
  rangeMaxCharacteristic->setValue((uint16_t)rangeMaxMm);
  startCharacteristic->setValue((uint8_t)0);
  debugCharacteristic->setValue((uint8_t)(SendDeb ? 1 : 0));

  tfLunaService->addCharacteristic(distanceCharacteristic);
  tfLunaService->addCharacteristic(modeCharacteristic);
  tfLunaService->addCharacteristic(thresholdCharacteristic);
  tfLunaService->addCharacteristic(rangeMinCharacteristic);
  tfLunaService->addCharacteristic(rangeMaxCharacteristic);
  tfLunaService->addCharacteristic(startCharacteristic);
  tfLunaService->addCharacteristic(debugCharacteristic);

  BLE.server()->addService(tfLunaService);
  BLE.startAdvertising(true);

  Serialprintln("BLE server advertising");
}

void loop() {
  uint16_t distanceMm = 0;
  uint16_t signalStrength = 0;
  int16_t temperatureC = 0;
  uint32_t statusFlags = 0;
      
  if (!tfluma.readDistance(distanceMm, signalStrength, temperatureC, statusFlags)) {
    Serialprintln("TF-Luna read failed");
    delay(200);
    return;
  }

  if (resetMeasurementRequested) {
    publishDistance(distanceMm, millis(), ' ');
    hasLastDistance = true;
    lastDistanceMm = distanceMm;
    Serialprint("Reset measurement: ");
    Serialprint(distanceMm);
    Serialprintln(" mm (sent to client)");
    resetMeasurementRequested = false;
  }

  if (bleMode == 3) {
    if (oneShotCaptureActive && distanceMm >= rangeMinMm && distanceMm <= rangeMaxMm) {
      publishDistance(distanceMm, millis(), ' ');
      Serialprint("One-shot in-range hit: ");
      Serialprint(distanceMm);
      Serialprintln(" mm");
      oneShotCaptureActive = false;
    }

    //delay(100);
    return;
  }

  bool reportDistance = false;
  int32_t reportDelta = 0;
  bool mode1Ready = bleMode != 1 || measurementRunning;
  if (mode1Ready) {
    if (!hasLastDistance) {
      hasLastDistance = true;
      lastDistanceMm = distanceMm;
      publishDistance(distanceMm, millis(), ' ');
      reportDistance = true;
    } else {
      int32_t delta = abs(static_cast<int32_t>(distanceMm) - static_cast<int32_t>(lastDistanceMm));

      if (bleMode == 0 || delta >= static_cast<int32_t>(thresholdMm)) {
        reportDelta = static_cast<int32_t>(distanceMm) - static_cast<int32_t>(lastDistanceMm);
        lastDistanceMm = distanceMm;
        publishDistance(distanceMm, millis(), bleMode == 1 ? (reportDelta > 0 ? '+' : '-') : ' ');
        reportDistance = true;
      }
    }
  }

  if (bleMode != 1 || reportDistance) {
    Serialprint("Distance: ");
    if (bleMode == 1 && reportDelta != 0) {
      Serialprint(reportDelta > 0 ? "+" : "-");
    }
    Serialprint(distanceMm);
    Serialprint(" mm, Strength: ");
    Serialprint(signalStrength);
    Serialprint(", Temp C: ");
    Serialprint(temperatureC);
    Serialprint(", Status: 0x");
    Serialprintln(statusFlags, HEX);
  }

  delay(500);
}

