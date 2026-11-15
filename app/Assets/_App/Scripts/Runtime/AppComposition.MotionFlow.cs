using System;
using System.Collections.Generic;
using TravelAgent.Core;
using UnityEngine;

namespace TravelAgent
{
    public sealed partial class AppComposition
    {
        private const int MotionCapacity = 3000;
        private MotionHistory liveMotion;
        private bool liveMotionEnabled, liveMotionConnected, motionDirty, motionPaused;
        private float lastMotionAt, nextMotionSave, nextMotionRender;

        public MotionHistory LiveMotionHistory => liveMotion;

        private void PrepareLiveMotion()
        {
            liveMotion = new MotionHistory(MotionCapacity);
            var stored = Data.motionSamples ?? new List<MotionSample>();
            Data.motionSamples = new List<MotionSample>();
            if (!simulated)
                foreach (var sample in stored)
                    if (liveMotion.Add(sample)) AppendMotionToStore(sample);
            liveMotion.Break();
            liveMotionEnabled = !simulated && liveMotion.Count > 0;
            liveMotionConnected = false; motionPaused = true; motionDirty = false;
            nextMotionSave = Time.unscaledTime + 5;
            nextMotionRender = 0;
            RefreshLiveMotionView();
        }

        private void AppendMotionToStore(MotionSample sample)
        {
            var samples = Data.motionSamples;
            if (samples.Count > 0 && (samples[samples.Count-1].deviceId != sample.deviceId || samples[samples.Count-1].bootId != sample.bootId)) samples.Clear();
            samples.Add(sample.Copy());
            if (samples.Count > MotionCapacity) samples.RemoveRange(0, samples.Count - MotionCapacity);
        }

        private void OnMotionSample(MotionSample sample)
        {
            if (simulated || Data == null || liveMotion == null || sample == null || Transport == null) return;
            var state = Transport.Snapshot;
            if (!state.connected || state.stale || !state.authenticated || sample.deviceId != state.deviceId || sample.bootId != state.bootId) return;
            if (!liveMotion.Add(sample)) return;
            AppendMotionToStore(sample);
            lastMotionAt = Time.unscaledTime;
            liveMotionEnabled = liveMotionConnected = true; motionPaused = false; motionDirty = true;
            if (Time.unscaledTime >= nextMotionRender)
            {
                nextMotionRender = Time.unscaledTime + .18f;
                RefreshLiveMotionView();
            }
        }

        private void RefreshLiveMotionConnection(DeviceSnapshot state)
        {
            if (liveMotion == null) return;
            bool supported = !simulated && Array.IndexOf(state.capabilities ?? Array.Empty<string>(), "liveMotion") >= 0;
            bool connected = supported && state.connected && state.authenticated && !state.stale;
            liveMotionEnabled |= supported;
            if (!connected && liveMotionConnected) { liveMotion.Break(); motionPaused = true; }
            liveMotionConnected = connected;
            RefreshLiveMotionView();
        }

        private void TickLiveMotion()
        {
            if (liveMotion == null || simulated) return;
            if (liveMotionConnected && !motionPaused && Time.unscaledTime - lastMotionAt > 2)
            {
                liveMotion.Break(); motionPaused = true;
                RefreshLiveMotionView();
            }
            if (motionDirty && Time.unscaledTime >= nextMotionSave)
            {
                nextMotionSave = Time.unscaledTime + 5;
                if (Save()) motionDirty = false;
            }
        }

        private void RefreshLiveMotionView()
        {
            if (!view || !view.timeline) return;
            view.timeline.SetMotionHistory(!simulated && liveMotionEnabled ? liveMotion : null, liveMotionConnected && !motionPaused);
        }

        private static string EventDisplayTime(BagEvent item) =>
            DateTimeOffset.TryParse(item.recordedAtUtc, out var time) ? time.ToUniversalTime().ToString("HH:mm:ss 'UTC'") :
            item.elapsedMs > 0 ? "Device +" + TimeSpan.FromMilliseconds(item.elapsedMs).ToString(@"hh\:mm\:ss") : "Time unconfirmed";
    }
}
