#include "dc_motor.h"

void DCMotor::begin(uint32_t pwmFreq) {
    pinMode(_in1Pin, OUTPUT); pinMode(_in2Pin, OUTPUT);
    digitalWrite(_in1Pin, LOW); digitalWrite(_in2Pin, LOW);
    const bool first = ledcAttach(_in1Pin, pwmFreq, 8);
    const bool second = ledcAttach(_in2Pin, pwmFreq, 8);
    _ready = first && second;
    if (!_ready) {
        if (first) ledcDetach(_in1Pin);
        if (second) ledcDetach(_in2Pin);
        pinMode(_in1Pin, OUTPUT); pinMode(_in2Pin, OUTPUT);
        digitalWrite(_in1Pin, LOW); digitalWrite(_in2Pin, LOW);
    }
    stop();
    Serial.printf("[MotorBench] PWM setup %s\n", _ready ? "ready" : "FAILED; outputs low");
}

void DCMotor::setSpeed(int16_t speed) {
    speed = constrain(speed, -255, 255);
    if (!_ready || speed == 0) { stop(); return; }
    // Disable the inactive half first, including during direction changes.
    if (speed > 0) {
        ledcWrite(_in2Pin, 0); ledcWrite(_in1Pin, speed);
    } else {
        ledcWrite(_in1Pin, 0); ledcWrite(_in2Pin, -speed);
    }
    _currentSpeed = speed;
}

void DCMotor::stop() {
    _currentSpeed = 0;
    if (_ready) { ledcWrite(_in1Pin, 0); ledcWrite(_in2Pin, 0); }
    else { digitalWrite(_in1Pin, LOW); digitalWrite(_in2Pin, LOW); }
}

void DCMotor::brake() {
    _currentSpeed = 0;
    if (_ready) { ledcWrite(_in1Pin, 255); ledcWrite(_in2Pin, 255); }
    else stop();
}
