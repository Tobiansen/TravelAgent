#pragma once
#include <Arduino.h>
#include <math.h>
#include "alarm_logic.h"
#include "motion_classifier.h"

namespace suitcase {
struct MotionReading {
    float deltaG=0,gyroDps=0;
    uint32_t elapsedMs=0,sampleAgeMs=0;
    bool sensorFresh=false;
};
class App {
    Controller control;
    Keypad keypad;
    bool first = true, healthy = false, sampleSane = false, quiet = false, moving = false;
    bool previousValid = false, errorFeedback = false, greenFeedback = false;
    bool highTone = false;
    Time sampledAt = 0, renderedAt = 0, telemetryAt = 0, toneAt = 0;
    Time feedbackAt = 0;
    uint32_t processedSample = 0, calibrationCount = 0;
    float previousX = 0, previousY = 0, previousZ = 0;
    float sumX = 0, sumY = 0, sumZ = 0, sumGx = 0, sumGy = 0, sumGz = 0;
    float referenceX = 0, referenceY = 0, referenceZ = 1;
    float referenceGx = 0, referenceGy = 0, referenceGz = 0;
    float magnitude = 0, deltaG = 0, gyroDps = 0;
    float liveGyroDps=0;
    uint32_t liveSampleAt=0;
    bool haveLiveSample=false;
    crumbguard::MotionWindow motionWindow;
    bool capturing=false, configurationLocked=false;
    bool guardPending=false, guardComplete=false, desiredArmed=false;
    GuardResult guardResult=GuardResult::Unchanged;

    void resetCalibration() {
        calibrationCount = 0;
        sumX = sumY = sumZ = sumGx = sumGy = sumGz = 0;
    }
    template<class Audio>
    void transitioned(State before, Time now, Audio& audio) {
        if (!first && before == control.state) return;
        first = false;
        if (control.state == State::Arming) resetCalibration();
        if (control.state == State::Armed && calibrationCount > 0) {
            motionWindow.clear();
            const float count = static_cast<float>(calibrationCount);
            referenceX = sumX / count; referenceY = sumY / count; referenceZ = sumZ / count;
            referenceGx = sumGx / count; referenceGy = sumGy / count; referenceGz = sumGz / count;
        }
        if (control.state == State::Alarm) { toneAt = now - 300U; highTone = false; }
        else audio.stop();
        Serial.printf("{\"type\":\"event\",\"payload\":{\"event_type\":\"state\",\"key\":\"alarm_state\",\"value\":{\"state\":\"%s\",\"reason\":\"%s\",\"uptime_ms\":%lu}}}\n",
                      name(control.state), name(control.reason), static_cast<unsigned long>(now));
    }

public:
    unsigned codeProgress() const {return control.entered();}
    void resetInput(){control.clearEntry();keypad=Keypad{};}
    void lockConfiguration(bool value){configurationLocked=value;resetInput();}
    bool setCodes(const char* a,const char* b){const bool ok=control.setCodes(a,b);resetInput();return ok;}
    bool queueGuard(bool armed){
        if(guardPending||guardComplete)return false;
        desiredArmed=armed;guardPending=true;return true;
    }
    bool takeGuardResult(GuardResult& result){
        if(!guardComplete)return false;
        guardComplete=false;result=guardResult;return true;
    }
    State currentState() const { return control.state; }
    Reason alarmReason() const { return control.reason; }
    bool sensorHealthy() const { return healthy; }
    void capture(bool value) { capturing=value; }
    bool modelTrained() const { return crumbguard::MotionModel::trained; }
    MotionReading motionReading(uint32_t now) const {
        MotionReading value;
        value.elapsedMs=liveSampleAt;value.sampleAgeMs=uint32_t(now-liveSampleAt);
        value.sensorFresh=haveLiveSample&&healthy&&value.sampleAgeMs<=300;
        if(value.sensorFresh){value.deltaG=fabsf(magnitude-1.0f);value.gyroDps=liveGyroDps;}
        return value;
    }
    template<class Imu>
    void report(Imu& imu, const char* key = "alarm_sample") {
        Serial.printf("{\"type\":\"event\",\"payload\":{\"event_type\":\"sensor\",\"key\":\"%s\",\"value\":{\"state\":\"%s\",\"fresh\":%s,\"delta_g\":%.3f,\"gyro_dps\":%.1f,\"g\":%.3f,\"quiet\":%s,\"taps\":%u,\"samples\":%lu,\"age_ms\":%lu,\"read_errors\":%lu}}}\n",
                      key, name(control.state), healthy ? "true" : "false", deltaG, gyroDps,
                      magnitude, quiet ? "true" : "false", control.entered(),
                      static_cast<unsigned long>(imu.getSampleCount()),
                      static_cast<unsigned long>(imu.getSampleAgeMs()),
                      static_cast<unsigned long>(imu.getReadErrors()));
    }

    template<class Imu, class Led, class Button, class Audio>
    void tick(Imu& imu, Led& left, Led& right, Button& a, Button& b, Audio& audio) {
        const Time now = millis();
        const bool fresh = imu.isConnected() && imu.hasFreshSample(300);
        healthy = fresh && sampleSane;
        if (elapsed(now, sampledAt) >= 20) {
            sampledAt = now;
            const uint32_t samples = imu.getSampleCount();
            if (fresh && samples != processedSample) {
                processedSample = samples;
                float ax, ay, az, gx, gy, gz;
                imu.readAll(&ax, &ay, &az, &gx, &gy, &gz, nullptr);
                liveSampleAt=now;haveLiveSample=true;
                magnitude = sqrtf(ax * ax + ay * ay + az * az);
                sampleSane = isfinite(magnitude) && magnitude >= 0.05f && magnitude <= 3.5f &&
                    isfinite(gx) && isfinite(gy) && isfinite(gz);
                healthy = fresh && sampleSane;
                if (!sampleSane) {
                    healthy = false;
                    quiet = moving = false;
                } else {
                    const float dx = ax - previousX, dy = ay - previousY, dz = az - previousZ;
                    const float stepG = previousValid ? sqrtf(dx * dx + dy * dy + dz * dz) : 1.0f;
                    previousX = ax; previousY = ay; previousZ = az; previousValid = true;
                    const float rawGyro = sqrtf(gx * gx + gy * gy + gz * gz);
                    liveGyroDps=rawGyro;
                    motionWindow.push(fabsf(magnitude-1.0f),stepG,rawGyro);
                    if(capturing && control.state==State::Disarmed) {
                        Serial.printf("{\"type\":\"event\",\"payload\":{\"event_type\":\"sensor\",\"key\":\"motion_frame\",\"value\":{\"uptime_ms\":%lu,\"abs_g\":%.6f,\"step_g\":%.6f,\"gyro\":%.6f}}}\n",
                            (unsigned long)now,fabsf(magnitude-1.0f),stepG,rawGyro);
                    }
                    quiet = fabsf(magnitude - 1.0f) < 0.10f && rawGyro < 12.0f && stepG < 0.04f;
                    const float rx = ax - referenceX, ry = ay - referenceY, rz = az - referenceZ;
                    const float rgx = gx - referenceGx, rgy = gy - referenceGy, rgz = gz - referenceGz;
                    deltaG = sqrtf(rx * rx + ry * ry + rz * rz);
                    gyroDps = sqrtf(rgx * rgx + rgy * rgy + rgz * rgz);
                    moving = deltaG > 0.12f || gyroDps > 20.0f;
                    if(crumbguard::MotionModel::trained) moving=motionWindow.ready() &&
                        motionWindow.probability()>=crumbguard::MotionModel::threshold;
                    if (control.state == State::Arming) {
                        if (quiet) {
                            sumX += ax; sumY += ay; sumZ += az;
                            sumGx += gx; sumGy += gy; sumGz += gz;
                            ++calibrationCount;
                        } else resetCalibration();
                    }
                }
            }
        }
        if (!healthy) quiet = moving = false;

        // Apply after refreshing sensor health, before the normal state/output
        // pass. The BLE layer consumes the result only after FullApp updates PWM.
        bool remoteDisarmed=false, skipKeypad=false;
        if(guardPending){
            guardPending=false;
            const State before=control.state;
            guardResult=desiredArmed&&configurationLocked
                ?GuardResult::ConfigurationLocked:control.setGuard(desiredArmed,now,healthy);
            if(guardResult==GuardResult::Applied||!desiredArmed){
                resetInput();skipKeypad=true;errorFeedback=greenFeedback=false;
                remoteDisarmed=!desiredArmed;
                transitioned(before,now,audio);
                if(remoteDisarmed)audio.stop();
            }
            guardComplete=true;
        }

        const uint8_t mask = (a.isPressed() ? 1 : 0) | (b.isPressed() ? 2 : 0);
        const Key tap = keypad.sample(mask, now);
        if (tap != Key::None && !configurationLocked && !skipKeypad) {
            const State before = control.state;
            const CodeResult result = control.tap(tap, now, healthy);
            const char* resultName = result == CodeResult::Arm ? "arm_accepted"
                : result == CodeResult::Disarm ? "disarmed"
                : result == CodeResult::Wrong ? "rejected"
                : result == CodeResult::SensorUnavailable ? "sensor_unavailable" : "partial";
            if (result == CodeResult::Wrong || result == CodeResult::SensorUnavailable) {
                errorFeedback = true; greenFeedback = false; feedbackAt = now;
            } else if (result == CodeResult::Disarm) {
                greenFeedback = true; errorFeedback = false; feedbackAt = now;
            }
            Serial.printf("{\"type\":\"event\",\"payload\":{\"event_type\":\"state\",\"key\":\"code_entry\",\"value\":{\"result\":\"%s\",\"taps\":%u,\"uptime_ms\":%lu}}}\n",
                          resultName, control.entered(), static_cast<unsigned long>(now));
            transitioned(before, now, audio);
        }
        const State beforeTick = control.state;
        control.tick(now, healthy, quiet && calibrationCount >= 20, moving);
        transitioned(beforeTick, now, audio);

        if (control.state == State::Alarm && elapsed(now, toneAt) >= 300) {
            toneAt = now;
            highTone = !highTone;
            audio.playTone(highTone ? 900 : 600, 320);
        }
        if (elapsed(now, feedbackAt) >= 600) errorFeedback = greenFeedback = false;
        if (remoteDisarmed || elapsed(now, renderedAt) >= 40) {
            renderedAt = now;
            left.clear(); right.clear();
            if (remoteDisarmed) {
                // Clear both grids now, including an unhealthy-sensor disarm.
                // The normal amber sensor diagnostic can resume on later passes.
            } else if (control.state == State::Alarm) {
                const bool phase = (now / 200U) % 2U;
                left.setAll(phase ? 255 : 0, 0, phase ? 0 : 255);
                right.setAll(phase ? 0 : 255, 0, phase ? 255 : 0);
            } else if (control.state == State::Warning) {
                if ((now / 200U) % 2U) { left.setAll(255, 100, 0); right.setAll(255, 100, 0); }
            } else if (greenFeedback) {
                left.setAll(0, 255, 0); right.setAll(0, 255, 0);
            } else if (errorFeedback || !healthy) {
                if ((now / 250U) % 2U) { left.setAll(255, 100, 0); right.setAll(255, 100, 0); }
            } else if (control.state == State::Arming) {
                const Time spent = elapsed(now, control.since);
                const int remaining = spent < 5000 ? 5 - static_cast<int>(spent / 1000) : 1;
                for (int i = 0; i < remaining; ++i) {
                    left.setPixel(i, 255, 100, 0); right.setPixel(i, 255, 100, 0);
                }
            } else if (control.state == State::Armed) {
                left.setPixel(4, 0, 0, 80); right.setPixel(4, 0, 0, 80);
            }
            // Entry feedback shows only the number of taps, not the key identities.
            for (uint8_t i = 0; i < control.entered(); ++i) {
                left.setPixel(i, 180, 180, 180); right.setPixel(i, 180, 180, 180);
            }
            left.show(); right.show();
        }
        if (elapsed(now, telemetryAt) >= 500) {
            telemetryAt = now;
            report(imu);
        }
    }
};
}  // namespace suitcase
