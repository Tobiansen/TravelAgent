#pragma once
#include <Arduino.h>

// Project-local Arduino-ESP32 3.x port of the SDK's IN1/IN2 driver.
// Pin assignments come from the catalog template, not from this driver.
class DCMotor {
public:
    DCMotor(int in1Pin, int in2Pin): _in1Pin(in1Pin), _in2Pin(in2Pin) {}
    void begin(uint32_t pwmFreq = 25000);
    void setSpeed(int16_t speed);
    void forward(uint8_t speed = 255) { setSpeed(speed); }
    void reverse(uint8_t speed = 255) { setSpeed(-int16_t(speed)); }
    void stop();
    void brake();
    int16_t getSpeed() const { return _currentSpeed; } // Command, not measured RPM.
    bool isReady() const { return _ready; } // PWM setup only, not motor detection.
private:
    int _in1Pin, _in2Pin;
    int16_t _currentSpeed = 0;
    bool _ready = false;
};
