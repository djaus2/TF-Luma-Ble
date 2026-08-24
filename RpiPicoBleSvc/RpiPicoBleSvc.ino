#include <BLE.h>
#include "tf-luma.h"

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

BLEService* tfLunaService = nullptr;
BLECharacteristic* distanceCharacteristic = nullptr;
BLECharacteristic* modeCharacteristic = nullptr;
BLECharacteristic* thresholdCharacteristic = nullptr;
BLECharacteristic* rangeMinCharacteristic = nullptr;
BLECharacteristic* rangeMaxCharacteristic = nullptr;
BLECharacteristic* startCharacteristic = nullptr;

uint8_t bleMode = 1;      // 0 = always publish, 1 = threshold hysteresis, 3 = one-shot in-range capture
uint16_t thresholdMm = 100;
uint16_t rangeMinMm = 300;
uint16_t rangeMaxMm = 2000;
uint16_t lastDistanceMm = 0;
bool hasLastDistance = false;
bool oneShotCaptureActive = false;

void onModeWrite(BLECharacteristic* characteristic) {
  uint8_t mode = characteristic->getUInt8();
  if (mode <= 2) {
    bleMode = mode;
  }
}

void onThresholdWrite(BLECharacteristic* characteristic) {
  uint16_t value = characteristic->getUInt16();
  if (value == 0) {
    value = 1;
  }

  thresholdMm = value;
  characteristic->setValue((uint16_t)thresholdMm);

  Serial.print("Threshold updated to ");
  Serial.print(thresholdMm);
  Serial.println(" mm");
}

void onRangeMinWrite(BLECharacteristic* characteristic) {
  uint16_t value = characteristic->getUInt16();
  if (value == 0) {
    value = 1;
  }
  if (value > rangeMaxMm) {
    value = rangeMaxMm;
  }

  rangeMinMm = value;
  characteristic->setValue((uint16_t)rangeMinMm);

  Serial.print("Range min updated to ");
  Serial.print(rangeMinMm);
  Serial.println(" mm");
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

  Serial.print("Range max updated to ");
  Serial.print(rangeMaxMm);
  Serial.println(" mm");
}

void onStartWrite(BLECharacteristic* characteristic) {
  uint8_t trigger = characteristic->getUInt8();
  if (trigger == 0) {
    oneShotCaptureActive = false;
    Serial.println("Range capture cancelled");
    return;
  }

  oneShotCaptureActive = true;
  Serial.print("One-shot range capture started for ");
  Serial.print(rangeMinMm);
  Serial.print(".. ");
  Serial.print(rangeMaxMm);
  Serial.println(" mm");
}

void publishDistance(uint16_t distanceMm, uint32_t timestampMs) {
  if (distanceCharacteristic == nullptr) {
    return;
  }

  // Payload format (little-endian):
  // [0..1] distanceMm (uint16), [2..5] timestampMs since boot (uint32)
  uint8_t payload[6];
  payload[0] = static_cast<uint8_t>(distanceMm & 0xFF);
  payload[1] = static_cast<uint8_t>((distanceMm >> 8) & 0xFF);
  payload[2] = static_cast<uint8_t>(timestampMs & 0xFF);
  payload[3] = static_cast<uint8_t>((timestampMs >> 8) & 0xFF);
  payload[4] = static_cast<uint8_t>((timestampMs >> 16) & 0xFF);
  payload[5] = static_cast<uint8_t>((timestampMs >> 24) & 0xFF);

  // In Arduino-Pico, setting the characteristic value is enough for a
  // subscribed client to receive a notification when BLENotify is enabled.
  distanceCharacteristic->setValue(payload, sizeof(payload));
}
TFLuma tfluma;
void setup() {
  Serial.begin(115200);
  delay(500);
  Serial.println("Starting TF-Luna BLE server...");
  if (!tfluma.begin()) {
    Serial.println("TF-Luna init failed");
  }
  BLE.begin(DEVICE_NAME);

  tfLunaService = new BLEService(SERVICE_UUID);
  distanceCharacteristic = new BLECharacteristic(DISTANCE_UUID, BLERead | BLENotify, "Distance+Timestamp");
  modeCharacteristic = new BLECharacteristic(MODE_UUID, BLERead | BLEWrite, "Mode");
  thresholdCharacteristic = new BLECharacteristic(THRESHOLD_UUID, BLERead | BLEWrite, "Threshold mm");
  rangeMinCharacteristic = new BLECharacteristic(RANGE_MIN_UUID, BLERead | BLEWrite, "Min range mm");
  rangeMaxCharacteristic = new BLECharacteristic(RANGE_MAX_UUID, BLERead | BLEWrite, "Max range mm");
  startCharacteristic = new BLECharacteristic(START_UUID, BLERead | BLEWrite, "Start capture");

  modeCharacteristic->onWrite(onModeWrite);
  thresholdCharacteristic->onWrite(onThresholdWrite);
  rangeMinCharacteristic->onWrite(onRangeMinWrite);
  rangeMaxCharacteristic->onWrite(onRangeMaxWrite);
  startCharacteristic->onWrite(onStartWrite);

  modeCharacteristic->setValue((uint8_t)bleMode);
  thresholdCharacteristic->setValue((uint16_t)thresholdMm);
  rangeMinCharacteristic->setValue((uint16_t)rangeMinMm);
  rangeMaxCharacteristic->setValue((uint16_t)rangeMaxMm);
  startCharacteristic->setValue((uint8_t)0);

  tfLunaService->addCharacteristic(distanceCharacteristic);
  tfLunaService->addCharacteristic(modeCharacteristic);
  tfLunaService->addCharacteristic(thresholdCharacteristic);
  tfLunaService->addCharacteristic(rangeMinCharacteristic);
  tfLunaService->addCharacteristic(rangeMaxCharacteristic);
  tfLunaService->addCharacteristic(startCharacteristic);

  BLE.server()->addService(tfLunaService);
  BLE.startAdvertising(true);

  Serial.println("BLE server advertising");
}

void loop() {
  uint16_t distanceMm = 0;
  uint16_t signalStrength = 0;
  int16_t temperatureC = 0;
  uint32_t statusFlags = 0;

  if (!tfluma.readDistance(distanceMm, signalStrength, temperatureC, statusFlags)) {
    Serial.println("TF-Luna read failed");
    delay(200);
    return;
  }

  if (bleMode == 3) {
    if (oneShotCaptureActive && distanceMm >= rangeMinMm && distanceMm <= rangeMaxMm) {
      publishDistance(distanceMm, millis());
      Serial.print("One-shot in-range hit: ");
      Serial.print(distanceMm);
      Serial.println(" mm");
      oneShotCaptureActive = false;
    }

    delay(100);
    return;
  }

  if (!hasLastDistance) {
    hasLastDistance = true;
    lastDistanceMm = distanceMm;
    publishDistance(distanceMm, millis());
  } else {
    int32_t delta = abs(static_cast<int32_t>(distanceMm) - static_cast<int32_t>(lastDistanceMm));

    if (bleMode == 0 || delta >= static_cast<int32_t>(thresholdMm)) {
      lastDistanceMm = distanceMm;
      publishDistance(distanceMm, millis());
    }
  }

  Serial.print("Distance: ");
  Serial.print(distanceMm);
  Serial.print(" mm, Strength: ");
  Serial.print(signalStrength);
  Serial.print(", Temp C: ");
  Serial.print(temperatureC);
  Serial.print(", Status: 0x");
  Serial.println(statusFlags, HEX);

  delay(500);
}

