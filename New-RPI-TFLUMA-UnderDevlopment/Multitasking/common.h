#ifndef FLAGSSH
#define FLAGSSH

#include <Arduino.h>
#include <pico/mutex.h>

#define SYNCHVAL1  13703599
#define SYNCHVAL2  31415926
#define SYNCHVAL3  27182818

#define FAIL 0xFFFFFFFF

#define CALLBACK_BASE 0xB0000000UL
#define CALLBACK_SETUP    (CALLBACK_BASE + 1)
#define CALLBACK_PAUSE    (CALLBACK_BASE + 2)
#define CALLBACK_CONTINUE (CALLBACK_BASE + 3)
#define CALLBACK_DISPOSE  (CALLBACK_BASE + 4)
#define CALLBACK_CALL     (CALLBACK_BASE + 5)

inline const char* callbackCmdText(uint32_t cmd) {
    switch (cmd) {
        case CALLBACK_SETUP:    return "CALLBACK_SETUP";
        case CALLBACK_PAUSE:    return "CALLBACK_PAUSE";
        case CALLBACK_CONTINUE: return "CALLBACK_CONTINUE";
        case CALLBACK_DISPOSE:  return "CALLBACK_DISPOSE";
        case CALLBACK_CALL:     return "CALLBACK_CALL";
    }
    return "unknown";
}

#define SHARED_BASE 0x0B000000UL
#define SHARED_SET      (SHARED_BASE + 1)
#define SHARED_START    (SHARED_BASE + 2)
#define SHARED_PAUSE    (SHARED_BASE + 3)
#define SHARED_CONTINUE (SHARED_BASE + 4)
#define SHARED_DISPOSE  (SHARED_BASE + 5)

inline const char* sharedCmdText(uint32_t cmd) {
    switch (cmd) {
        case SHARED_SET:      return "SHARED_SET";
        case SHARED_START:    return "SHARED_START";
        case SHARED_PAUSE:    return "SHARED_PAUSE";
        case SHARED_CONTINUE: return "SHARED_CONTINUE";
        case SHARED_DISPOSE:  return "SHARED_DISPOSE";
    }
    return "unknown";
}

#define TFLUMA_MODE_BASE  0x00B00000UL
#define TFLUMA_MODE_TRIG          (TFLUMA_MODE_BASE + 1)
#define TFLUMA_MODE_CONT          (TFLUMA_MODE_BASE + 2)
#define TFLUMA_MODE_ENABLE        (TFLUMA_MODE_BASE + 3)
#define TFLUMA_MODE_DISABLE       (TFLUMA_MODE_BASE + 4)
#define TFLUMA_MODE_GETFRAMERATE  (TFLUMA_MODE_BASE + 5)
#define TFLUMA_SET_BACKGROUND     (TFLUMA_MODE_BASE + 6)
#define TFLUMA_MODE_ONESHOT       (TFLUMA_MODE_BASE + 7)
#define TFLUMA_MODE_NOT_ONESHOT   (TFLUMA_MODE_BASE + 8)


inline const char* TFLUMA_MODEText(uint32_t mode) {
    switch (mode) {
        case TFLUMA_MODE_BASE:         return "TFLUMA_MODE_BASE";
        case TFLUMA_MODE_TRIG:         return "TFLUMA_MODE_TRIG";
        case TFLUMA_MODE_CONT:         return "TFLUMA_MODE_CONT";
        case TFLUMA_MODE_ENABLE:       return "TFLUMA_MODE_ENABLE";
        case TFLUMA_MODE_DISABLE:      return "TFLUMA_MODE_DISABLE";
        case TFLUMA_MODE_GETFRAMERATE: return "TFLUMA_MODE_GETFRAMERATE";
        case TFLUMA_SET_BACKGROUND:    return "TFLUMA_SET_BACKGROUND";
        case TFLUMA_MODE_ONESHOT:      return "TFLUMA_MODE_ONESHOT";
        case TFLUMA_MODE_NOT_ONESHOT:  return "TFLUMA_MODE_NOT_ONESHOT";
    }
    return "unknown";
}

#define DISTANCE_CHANGED  0xC0000000UL
#define DISTANCE_THRESHOLD 300


// ------------------------------------------------------------
// Core 0 callback implementation
// ------------------------------------------------------------
typedef void (*CallbackFn)(uint32_t);

enum callback_state {none, running, paused};

inline const char* callbackStateText(callback_state s) {
    switch (s) {
        case none:    return "none";
        case running: return "running";
        case paused:  return "paused";
    }
    return "unknown";
}




static inline bool Wait4IncomingSynchFromOtherCore(uint32_t snchmsg) {
    uint32_t sync1 = rp2040.fifo.pop();
    bool ok = (sync1 == snchmsg);
    if(!ok)
    {

        Serial.printf("\n\t\tExpected: %lu Got: %lu\n", (unsigned long)snchmsg, (unsigned long)sync1);
        rp2040.fifo.push_nb(FAIL);
        return ok;
    }
    rp2040.fifo.push_nb(sync1);
    return ok;
} 

static inline bool InitiateSyncWithOtherCore(uint32_t snchmsg) {
  rp2040.fifo.push_nb(snchmsg);
  uint32_t sync2 = rp2040.fifo.pop();
  bool ok = (sync2 == snchmsg);
  if(!ok)
  {
        Serial.printf("\n\t\tExpected: %lu Got: %lu\n", (unsigned long)snchmsg, (unsigned long)sync2);
  }
  return ok;
}

// Shared producer-consumer memory for distance
extern volatile uint32_t distance;
extern volatile double elapsedMut;
extern mutex_t distanceMutex;



#endif