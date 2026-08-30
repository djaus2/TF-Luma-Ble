#pragma once

#include <BLE.h>


// Gates all Serial output; toggled remotely via the debug BLE characteristic
// and persisted in flash so it survives a reboot.
extern bool SendDeb;

void loadSendDebFromFlash();
void saveSendDebToFlash();

void Serialbegin(unsigned long baud);
void SerialStop();

// Blocks until the Serial port is ready, but only when debug output is enabled.
void whileNotSerial();

void onDebugWrite(BLECharacteristic* characteristic);

template <typename T>
void Serialprint(T value) {
  if (SendDeb) {
    Serial.print(value);
  }
}

template <typename T>
void Serialprint(T value, int format) {
  if (SendDeb) {
    Serial.print(value, format);
  }
}

void Serialprintln();

template <typename T>
void Serialprintln(T value) {
  if (SendDeb) {
    Serial.println(value);
  }
}

template <typename T>
void Serialprintln(T value, int format) {
  if (SendDeb) {
    Serial.println(value, format);
  }
}
