#include "common.h"
#include "misc.h"

void CoreSetup()
{
  Serial.begin(115200);
  while(!Serial);
}

void CoreExclusiveSetup()
{

}

void CallbackSetup(CallbackFn callback)
{
  // Pass the callback function pointer to Core2 before second synch
  rp2040.fifo.push_nb(CALLBACK_SETUP);
  rp2040.fifo.push_nb((uint32_t)callback);
}

void setup()
{
  CoreSetup();
  Serial.println("\nMain Core started.\n");
  //Core2 does its setup waits 1 sec then waits for this synch:
  if(!InitiateSyncWithOtherCore(SYNCHVAL1))
  {
    Serial.println("Failed SYNCHVAL1");
  }

  CoreExclusiveSetup();

  CallbackSetup(myCallback);
  if(!InitiateSyncWithOtherCore(SYNCHVAL2))
  {
    Serial.println("Failed SYNCHVAL2");
  }

  if(!InitiateSyncWithOtherCore(SHARED_SET))
  {
    Serial.println("Failed SHARED_SET");
  }

  // Start TF-Luma with 300mm hysterysis
  rp2040.fifo.push_nb((uint32_t)SHARED_SET);
  rp2040.fifo.push_nb(10);
  delay(0);
  rp2040.fifo.push_nb((uint32_t)TFLUMA_SET_BACKGROUND);
  delay(0);
  rp2040.fifo.push_nb((uint32_t)SHARED_START);
  delay(0);
  
  Serial.println("\nMain Setup done\n");
} 

// ------------------------------------------------------------
// Core  loop: randomly trigger callback
// ------------------------------------------------------------
void loop() {
    // Consume distance events produced by Core2
    while (rp2040.fifo.available() > 0) {
        uint32_t msg = rp2040.fifo.pop();
        if (msg == DISTANCE_CHANGED) {
            mutex_enter_blocking(&distanceMutex);
            double d = distance;
            double t = elapsedMut;
            mutex_exit(&distanceMutex);
            if (d < 3000) {
                Serial.printf("distance: %.2f mm, time: %.6f s\n", d, t);
            }
        }
    }


    // Random trigger of callback
    /*if (random(0, 100) > 95) {
        rp2040.fifo.push_nb((uint32_t)CALLBACK_CALL);
        rp2040.fifo.push_nb(counter);
    }*/

    if (Serial.available()) {
        char c = Serial.read();
        if (c >= 'a' && c <= 'z') c -= 32;
        switch (c)
        {
          case 'S':
            rp2040.fifo.push_nb((uint32_t)TFLUMA_MODE_ONESHOT);
            Serial.println("(Re)Started One Shot");
            break;
          case 'T':
            rp2040.fifo.push_nb((uint32_t)TFLUMA_MODE_NOT_ONESHOT);
            Serial.println("Turned off One Shot mode");
            break;
        }

    }

   // delay(10);
}