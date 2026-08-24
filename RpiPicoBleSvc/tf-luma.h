#ifndef TF_LUMA_H
#define TF_LUMA_H

#include <Arduino.h>

// These are the default pins
#define I2C0_SDA 8
#define I2C0_SCL 9

class TFLuma {
public:
  TFLuma();
  bool begin();
  bool readDistance(uint16_t &distanceMm,
                    uint16_t &signalStrength,
                    int16_t &temperatureC,
                    uint32_t &statusFlags);

private:
  bool _initialized;
};

#endif
