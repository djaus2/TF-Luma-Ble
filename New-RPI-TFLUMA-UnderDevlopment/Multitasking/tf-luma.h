#ifndef TF_LUMA_H
#define TF_LUMA_H

#include <Arduino.h>
#include <TFLI2C.h>      // TFLuna-I2C Library v.0.2.0

// These are the default pins
#define I2C0_SDA 8
#define I2C0_SCL 9

enum TFLumaMode{trig,cont};

class TFLuma {
public:
  TFLuma();
  bool begin();
  bool readDistance(uint16_t &distanceMm,
                    uint16_t &signalStrength,
                    int16_t &temperatureC,
                    uint32_t &statusFlags);
                    
  bool setMode(TFLumaMode mode);
  bool enable(bool _enable );
  void sample();
  uint16_t getFrameRate();
  bool setFrameRate(uint16_t frameRate);
  TFLumaMode Mode = cont;

private:
  bool _initialized;
  uint16_t tfFrame = TFL_DEF_FPS;
};



#endif
