#include <Arduino.h>     // every sketch needs this
#include <Wire.h>        // instantiate the Wire library
#include "tf-luma.h"
#include "serialdebug.h"


TFLI2C tflI2C;

int16_t tfAddr = TFL_DEF_ADR;

TFLuma::TFLuma() : _initialized(false) {
}

bool TFLuma::begin() {
  Wire.setSDA(I2C0_SDA);
  Wire.setSCL(I2C0_SCL);
  Wire.begin(); 
  _initialized = true;
  setMode(cont);
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

bool TFLuma::setMode(TFLumaMode mode)
{
  if(!_initialized)
    return false;
  switch(mode)
  {
    case trig:
      tflI2C.Set_Trig_Mode (tfAddr);
      break;
    case cont:
     tflI2C.Set_Cont_Mode (tfAddr);
      break;
  }
  return true;
}

bool TFLuma::enable(bool _enable)
{
  if(!_initialized)
    return false;
  if(_enable)
    tflI2C.Set_Enable(tfAddr);
  else
    tflI2C.Set_Disable(tfAddr);
  return true;
}

void TFLuma::sample()
{
    if(!_initialized)
        return;
   // tflI2C.Sample_Trig (tfAddr);
    return;
}

uint16_t TFLuma::getFrameRate()
{
  if(!_initialized)
     return 0;
  if(tflI2C.Get_Frame_Rate(tfFrame, tfAddr))
    return tfFrame;
  return 0;
}

bool TFLuma::setFrameRate(uint16_t frameRate)
{
  if(!_initialized)
     return false;
  tfFrame = frameRate;
  if(tflI2C.Set_Frame_Rate(tfFrame, tfAddr))
  {
    return true;
  }
  return false;
}
