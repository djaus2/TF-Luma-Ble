
#include <Arduino.h>     // every sketch needs this
#include <Wire.h>        // instantiate the Wire library
#include <TFLI2C.h>      // TFLuna-I2C Library v.0.2.0
#include "tf-luma.h"

#define I2C0_SDA 0
#define I2C0_SCL 1

TFLI2C tflI2C;

int16_t tfAddr = TFL_DEF_ADR;

TFLuma::TFLuma() : _initialized(false) {
}

bool TFLuma::begin() {
    Wire.setSDA(I2C0_SDA);
    Wire.setSCL(I2C0_SCL);
    Wire.begin(); 
  _initialized = true;
  return true;
}

bool TFLuma::readDistance(uint16_t &distanceMm,
                          uint16_t &signalStrength,
                          int16_t &temperatureC,
                          uint32_t &statusFlags) {
  if (!_initialized) {
    statusFlags = 0x0002;  // not initialized
    return false;
  }

  int16_t distanceCm = 0;
  int16_t flux = 0;
  int16_t tempC = 0;

  if (!tflI2C.getData(distanceCm, flux, tempC, tfAddr)) {
    statusFlags = 0x0004;  // I2C read failed
    return false;
  }

  if (distanceCm < 0) {
    statusFlags = 0x0008;  // invalid distance from sensor
    return false;
  }

  distanceMm = static_cast<uint16_t>(distanceCm) * 10;
  signalStrength = (flux < 0) ? 0 : static_cast<uint16_t>(flux);
  temperatureC = tempC;
  statusFlags = 0x0001;  // valid sample
  return true;
}
