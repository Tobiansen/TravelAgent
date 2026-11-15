"""Passively record the integrated alarm's USB telemetry alongside phone BLE.

No commands are transmitted. Open only while disarmed; DTR=True/RTS=False are
configured before opening, matching the earlier board preflight. Stopping this
observer does not disarm the board. Actual rotation/crumb flow need observation.
"""
import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import time

import serial


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", default="COM4")
    parser.add_argument("--seconds", type=int, default=600)
    args = parser.parse_args()
    if not 1 <= args.seconds <= 1800:
        parser.error("seconds must be between 1 and 1800")
    directory = Path(__file__).resolve().parents[2] / "Artifacts" / "HardwareRuns"
    directory.mkdir(parents=True, exist_ok=True)
    stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    destination = directory / (stamp + "-telemetry.jsonl")
    keys = {"alarm_state", "code_entry", "dispenser_state", "alarm_sample", "alarm_status"}
    connection = serial.Serial(port=None, baudrate=115200, timeout=0.25)
    connection.port = args.port
    connection.dtr = True
    connection.rts = False
    count = 0
    last_status = None
    last_print = 0
    print(f"PASSIVE OBSERVER {args.port}; no commands sent; output {destination}", flush=True)
    try:
        connection.open()
        deadline = time.monotonic() + args.seconds
        with destination.open("x", encoding="utf-8") as output:
            while time.monotonic() < deadline:
                line = connection.readline(65537)
                if not line or len(line) > 65536:
                    continue
                try:
                    event = json.loads(line)
                    payload = event.get("payload", {})
                    key, value = payload.get("key"), payload.get("value")
                except (UnicodeError, ValueError, AttributeError):
                    continue
                if key not in keys or not isinstance(value, dict):
                    continue
                output.write(json.dumps({"receivedAtUtc": datetime.now(timezone.utc).isoformat(),
                                         "key": key, "value": value}) + "\n")
                output.flush()
                count += 1
                if key == "alarm_status":
                    last_status = value
                if key not in {"alarm_status", "alarm_sample"} or (key == "alarm_status" and time.monotonic() - last_print >= 5):
                    print(key + " " + json.dumps(value), flush=True)
                    if key == "alarm_status":
                        last_print = time.monotonic()
    except KeyboardInterrupt:
        pass
    finally:
        connection.close()
        print(json.dumps({"observerStopped": True, "events": count, "lastStatus": last_status,
                          "commandsSent": 0}), flush=True)


if __name__ == "__main__":
    main()
