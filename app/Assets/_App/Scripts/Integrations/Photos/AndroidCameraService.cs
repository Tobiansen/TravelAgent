using System;
using System.Collections;
using UnityEngine;

namespace TravelAgent.Integrations.Photos
{
    public sealed class AndroidCameraService : ICameraService
    {
        public IEnumerator Capture(string context, Action<PhotoSelection> completed)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidJavaClass bridge = null;
            AndroidJavaObject activity = null;
            PhotoSelection result = null;
            string id = "";
            try
            {
                bridge = new AndroidJavaClass("com.travelagent.ble.CameraCaptureBridge");
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer")) activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                result = Decode(bridge.CallStatic<string>("launch", activity, context));
                if (result != null && result.pending && !result.recovered && string.IsNullOrEmpty(result.error)) { id = result.requestId; result = null; }
            }
            catch (Exception) { result = Failure("The Android camera could not start. Your existing photos are retained."); }
            while (result == null)
            {
                try { result = Decode(bridge.CallStatic<string>("poll", activity, id)); }
                catch (Exception) { result = Failure("The camera result is retained for recovery. Reopen the app to review it."); }
                if (result == null) yield return new WaitForSecondsRealtime(.1f);
            }
            activity?.Dispose(); bridge?.Dispose();
            completed?.Invoke(result);
#else
            completed?.Invoke(Failure("Camera capture is available on an Android phone. Choose an existing photo in the Editor."));
            yield break;
#endif
        }
        public IEnumerator Recover(Action<PhotoSelection> completed)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            PhotoSelection result = null;
            AndroidJavaClass bridge = null;
            AndroidJavaObject activity = null;
            try
            {
                bridge = new AndroidJavaClass("com.travelagent.ble.CameraCaptureBridge");
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer")) activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                if (activity == null) result = Failure("The Android camera activity is unavailable. No evidence was attached.");
            }
            catch (Exception) { result = Failure("Camera recovery could not start. Retained evidence has not been attached or discarded."); }
            try
            {
                float started = Time.realtimeSinceStartup;
                while (result == null || result.status == "processing")
                {
                    try { result = Decode(bridge.CallStatic<string>("recover", activity)); }
                    catch (Exception) { result = Failure("A retained camera request could not be read. No evidence was attached."); }
                    if (result == null || result.status != "processing" || Time.realtimeSinceStartup - started > 30) break;
                    yield return new WaitForSecondsRealtime(.1f);
                }
            }
            finally { activity?.Dispose(); bridge?.Dispose(); }
            completed?.Invoke(result);
#else
            completed?.Invoke(null); yield break;
#endif
        }
        public void Release(string requestId)
        {
            if (string.IsNullOrEmpty(requestId)) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var bridge = new AndroidJavaClass("com.travelagent.ble.CameraCaptureBridge"))
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    string error = bridge.CallStatic<string>("release", activity, requestId);
                    if (!string.IsNullOrEmpty(error)) Debug.LogWarning("Camera source remains retained for review; journey copies are unchanged.");
                }
            }
            catch (Exception) { Debug.LogWarning("Camera source remains retained for review; journey copies are unchanged."); }
#endif
        }
        private static PhotoSelection Failure(string message) { return new PhotoSelection { error = message }; }
#if UNITY_ANDROID && !UNITY_EDITOR
        private static PhotoSelection Decode(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var result = JsonUtility.FromJson<PhotoSelection>(json);
            return result ?? Failure("Android returned an invalid camera result.");
        }
#endif
    }
}
