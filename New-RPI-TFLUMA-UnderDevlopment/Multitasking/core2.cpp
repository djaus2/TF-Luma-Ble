#include "common.h"
#include "tf-luma.h"

#include <math.h>
callback_state  CallBackState = none;
callback_state  SharedState = paused;
CallbackFn CallMeBack = NULL;

volatile uint32_t distance = 0; //Distances in mm
volatile double elapsedMut = 0; //Distances in mm
uint32_t distanceLast = 0;
uint32_t distanceHysterisis = DISTANCE_THRESHOLD;
uint32_t closetDistance = 4000;
mutex_t distanceMutex;

uint32_t startUs = micros();
uint32_t endUs = micros();

bool isCapturing = false;
bool isOneShot = true;

TFLuma tfluma;

void Core2Setup()
{
    mutex_init(&distanceMutex);
    if (!tfluma.begin()) {
        Serial.println("\n\t\tTF-Luna init failed");
  }
}

void Core2ExclusiveSetup()
{
  uint32_t sync2 = rp2040.fifo.pop();
  // Do any Core2 startups here that must be done exclusively

  rp2040.fifo.push_nb(sync2);
}

// Call callback if set and CallBackState is running
void CallCallback(uint32_t param)
{
    if(CallMeBack!= NULL)
    {
        if(CallBackState == running)
            CallMeBack(param);
    }
}


void Expect2GetCallback()
{
  uint32_t setupMsg = rp2040.fifo.pop();
  if (setupMsg == CALLBACK_SETUP) {
      CallMeBack = (CallbackFn)rp2040.fifo.pop();
      CallBackState = running;
  }
}




void setup1()
{  
  while(!Serial);
  Serial.print("\n\t\tCore2 started\n");

  Core2Setup();
  delay(3000);

  if (!Wait4IncomingSynchFromOtherCore(SYNCHVAL1)) {
      Serial.println("\t\tSYNCHVAL1 mismatch");
  }

  // Receive the callback function pointer from the main core
  Expect2GetCallback();

  if (!Wait4IncomingSynchFromOtherCore(SYNCHVAL2)) {
      Serial.println("\t\tSYNCHVAL2 mismatch");
  }
  Core2ExclusiveSetup();
  uint16_t fr = tfluma.getFrameRate();
  Serial.printf("\t\tFrame rate: %lu\n",fr);
 Serial.println("\n\t\tCore2 Setup done\n");
}

uint16_t distanceMm = 0;
uint16_t signalStrength = 0;
int16_t temperatureC = 0;
uint32_t statusFlags = 0;
uint16_t background = 4000;
// ------------------------------------------------------------
// Core 0 loop: guarded callback execution
// ------------------------------------------------------------
void loop1() {
    if(rp2040.fifo.available() > 0) {
        uint32_t msg = rp2040.fifo.pop();
        uint32_t param ;
       if ((msg & 0xF0000000UL) == (CALLBACK_BASE & 0xF0000000UL))
        {
            Serial.printf("\t\tCallback cmd: %s\n", callbackCmdText(msg));
            switch (msg)
            {
                case CALLBACK_CALL:
                    // Direct to callback from main
                    param = rp2040.fifo.pop();
                    Serial.printf("\t\tGot Callback param: %lu\n", (unsigned long)param);
                    CallCallback(param);
                    break;
                case CALLBACK_PAUSE:
                    CallBackState = paused;
                    break;
                case CALLBACK_CONTINUE:
                    CallBackState = running;
                    break;
                case CALLBACK_DISPOSE:
                    CallBackState = none;
                    CallMeBack = NULL;
                    break;
                default:
                    //Ignore
                    break;
            }
        }
        else if((msg & 0x0F000000UL) == (SHARED_BASE & 0x0F000000UL))
        {
            Serial.printf("\t\tShared cmd: %s\n", sharedCmdText(msg));
            switch (msg)
            {
                case SHARED_SET:
                    // Direct to callback from main
                    param = rp2040.fifo.pop();
                    Serial.printf("\t\tGot hysteresis: %lu mm\n", (unsigned long)param);
                    distanceHysterisis = param;
                    break;
                case SHARED_START:
                    SharedState = running;
                    break;
                case SHARED_PAUSE:
                    SharedState = paused;
                    break;
                case SHARED_CONTINUE:
                    SharedState = running;
                    break;
                case SHARED_DISPOSE:
                    SharedState = none;
                    break;
                default:
                    //Ignore
                    break;
            }
        }
        else if((msg & 0x00F00000UL) == (TFLUMA_MODE_BASE & 0x00F00000UL))
        {
            Serial.printf("\t\tTFLuma cmd: %s\n", TFLUMA_MODEText(msg));
            switch (msg)
            {
                case TFLUMA_MODE_ONESHOT:
                    isOneShot = true;
                    isCapturing= false;
                    startUs = micros();
                    break;
                 case TFLUMA_MODE_NOT_ONESHOT:
                    isOneShot = false;
                    isCapturing = false;
                     startUs = micros();
                    break;                  
                case TFLUMA_MODE_TRIG:
                    tfluma.setMode(trig);
                    break;
                case TFLUMA_MODE_CONT:
                    tfluma.setMode(cont);
                    break;
                case TFLUMA_MODE_ENABLE:
                    tfluma.enable(true);
                    break;
                case TFLUMA_MODE_DISABLE:
                    tfluma.enable(false);
                    break;
                case TFLUMA_MODE_GETFRAMERATE:
                {
                    uint16_t fps = tfluma.getFrameRate();
                    Serial.printf("\t\tFrame rate: %u fps\n", (unsigned)fps);
                    break;
                }
                case TFLUMA_SET_BACKGROUND:   
                if (!tfluma.readDistance(distanceMm, signalStrength, temperatureC, statusFlags)) {
                    Serial.println("\n\t\ttTF-Luna read failed");
                    delay(200);
                    return;
                }

                background= distanceMm-2*distanceHysterisis;
                Serial.printf("\t\tBackground distance set to: %u mm\n", (unsigned)background);
                break;
            }
        }
    }
    else
    {
        /*// Do some Core2 processing and action callback with the value;
        int ran = random(0, 1000);
        if (ran > 990)
            CallCallback(ran);*/
        /*
        Serial.printf("\t\trunning: %u SharedState: %u SharedState == running is %u\n", (unsigned)running, (unsigned)SharedState, (unsigned)(SharedState == running));
        */
        if(SharedState == running)
        {
            // Produce shared distance property
            //static 
            uint32_t readDistance = 0;
            //readDistance += 700;

            uint16_t distanceMm = 0;
            uint16_t signalStrength = 0;
            int16_t temperatureC = 0;
            uint32_t statusFlags = 0;  
                
            if (!tfluma.readDistance(distanceMm, signalStrength, temperatureC, statusFlags)) {
                Serial.println("\n\t\ttTF-Luna read failed");
                delay(200);
                return;
            }

            if(distanceMm==0)
            {
                //Serial.println("\t\tInvalid 0 distance\n");
                return;
            }
            if(distanceMm > (background + distanceHysterisis))
            {
                return;
            }

           if(!isOneShot)
           {
                endUs = micros();
                double elapsed = (double)(endUs - startUs) / 1000000.0;                    
                mutex_enter_blocking(&distanceMutex);
                  distance = distanceMm;
                  elapsedMut = elapsed;
                mutex_exit(&distanceMutex);

                double diff = fabs((double)distanceMm - (double)distanceLast);
                bool exceeded = (diff >= distanceHysterisis);// && (signalStrength >=1000);
                if (exceeded) {
                    //Serial.printf("\t\tdistanceMm: %lu distanceLast: %lu distanceHysterisis: %lu diff: %f exceeded: %u\n", (unsigned long)distanceMm, (unsigned long)distanceLast, (unsigned long)distanceHysterisis, diff, (unsigned)exceeded);
                    //Serial.printf("\t\tTF-Luna: distanceMm=%lu signalStrength=%lu\n", (unsigned long)distanceMm, (unsigned long)signalStrength);//, (long)temperatureC, (unsigned long)statusFlags);
                    distanceLast = distanceMm;
                    rp2040.fifo.push_nb(DISTANCE_CHANGED);
                }
           }
           else
           {  
              if(distanceMm < background)
              {
                
                if(isCapturing)
                {
                   
                    if(distanceMm<distanceLast)
                    {
                        //Serial.print("\t\t\t\t\t\tLt ");
                         //Serial.print(distanceMm);Serial.print(" "); Serial.print(distanceLast);Serial.print(" "); Serial.println(background);
                        distanceLast = distanceMm;
                        
                    }
                    else
                    {
                       // Serial.print("\t\t\t\t\t\t\t\t\tgt ");
                        // Serial.print(distanceMm);Serial.print(" "); Serial.print(distanceLast);Serial.print(" "); Serial.println(background);
                    }
                    //Serial.println("\t\t\t\t\t\tGt");
                }
                else
                { 
                    //Serial.println("\t\t\t\t\t\tStarting");
                    distanceLast = distanceMm;
                    isCapturing = true;
                    endUs = micros();
                }

              }
              else 
              {
                if(isCapturing)
                {
                    //Serial.print("\t\t\t\t\t\tDone"); Serial.print(" ");
                    //Serial.print(distanceMm);Serial.print(" "); Serial.print(distanceLast);Serial.print(" "); Serial.println(background);
                    isCapturing = false;
                    double elapsed = (double)(endUs - startUs) / 1000000.0;
                    mutex_enter_blocking(&distanceMutex);
                        distance = distanceLast;
                        elapsedMut = elapsed;
                    mutex_exit(&distanceMutex);
                    rp2040.fifo.push_nb(DISTANCE_CHANGED);
                    distanceLast = background + distanceHysterisis;
                }
              }
           }
        }
    }

    //delay(1);
}




