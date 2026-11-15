using System;
using System.Collections;
using UnityEngine;

namespace TravelAgent.Integrations.Photos
{
    public sealed class AndroidPhotoService : IPhotoService
    {
        private bool selecting = false;

        public IEnumerator Select(Action<PhotoSelection> completed)
        {
            if (selecting) { completed?.Invoke(new PhotoSelection { error = "A photo selection is already open." }); yield break; }
#if UNITY_ANDROID && !UNITY_EDITOR
            selecting = true;
            AndroidJavaClass bridge = null;
            PhotoSelection result = null;
            try
            {
                bridge = new AndroidJavaClass("com.travelagent.ble.PhotoPickerBridge");
                // Discard an abandoned prior request before intentionally opening a new picker.
                while (!string.IsNullOrEmpty(bridge.CallStatic<string>("poll"))) { }
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    bridge.CallStatic("launch", activity);
            }
            catch (Exception) { result = new PhotoSelection { error = "Android image picker could not start. Please retry." }; }

            while (result == null)
            {
                string json = "";
                try { json = bridge.CallStatic<string>("poll"); }
                catch (Exception) { result = new PhotoSelection { error = "Android image selection could not be read. Please retry." }; }
                if (!string.IsNullOrEmpty(json))
                {
                    try { result = JsonUtility.FromJson<PhotoSelection>(json); }
                    catch (Exception) { result = new PhotoSelection { error = "Android returned an invalid image selection." }; }
                }
                if (result == null) yield return null;
            }
            bridge?.Dispose(); selecting = false;
            completed?.Invoke(result);
#else
            completed?.Invoke(new PhotoSelection { error = "The Android photo picker is available on an Android phone." });
            yield break;
#endif
        }
    }
}
