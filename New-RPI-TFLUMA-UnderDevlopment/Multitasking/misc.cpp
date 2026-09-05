#include "misc.h"


// ------------------------------------------------------------
// Core 0 callback implementation
// ------------------------------------------------------------
void myCallback(uint32_t param) {
    Serial.printf("Callback invoked in Main by Core2 with param: %lu\n", (unsigned long)param);
}