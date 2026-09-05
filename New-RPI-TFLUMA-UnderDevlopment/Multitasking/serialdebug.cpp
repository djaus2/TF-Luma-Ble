#include "serialdebug.h"

#include <EEPROM.h>

bool SendDeb = true;

// Emulated EEPROM layout: [0] magic byte, [1] SendDeb (0/1), so the debug flag survives reboot.
namespace {
const int EEPROM_SIZE = 2;
const uint8_t EEPROM_MAGIC = 0xA5;
}

void loadSendDebFromFlash() {
  EEPROM.begin(EEPROM_SIZE);
  if (EEPROM.read(0) == EEPROM_MAGIC) {
    SendDeb = EEPROM.read(1) != 0;
  }
}

void saveSendDebToFlash() {
  EEPROM.write(0, EEPROM_MAGIC);
  EEPROM.write(1, SendDeb ? 1 : 0);
  EEPROM.commit();
}

void Serialbegin(unsigned long baud) {
  if (SendDeb) {
    Serial.begin(baud);
  }
}

void SerialStop() {
  Serial.end();
}

void Serialprintln() {
  if (SendDeb) {
    Serial.println();
  }
}

void whileNotSerial() {
  if (!SendDeb) {
    return;
  }

  while (!Serial) {
    delay(10);
  }
}

/*void onDebugWrite(BLECharacteristic* characteristic) {
  bool newState = characteristic->getUInt8() != 0;

  if (newState) {
    if (!SendDeb) {
      SendDeb = true;
      Serialbegin(115200);  // reopen the port since it was stopped when debug was off
    }
    Serialprintln("Debug output enabled");
  } else if (SendDeb) {
    Serialprintln("Debug output disabled");
    SerialStop();
    SendDeb = false;
  }

  saveSendDebToFlash();
  }*/