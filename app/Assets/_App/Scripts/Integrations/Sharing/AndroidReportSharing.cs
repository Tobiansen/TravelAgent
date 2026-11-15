using System;
using System.Collections;
using UnityEngine;

namespace TravelAgent.Integrations.Sharing
{
    /// <summary>A Sharesheet result never establishes delivery or claim submission.</summary>
    public sealed class AndroidReportSharing : IReportSharing
    {
        private bool sharing = false;

        public IEnumerator Share(string completedExportFolder, Action<ReportShareResult> completed, Action<string> progress = null)
        {
            if (sharing) { completed?.Invoke(new ReportShareResult { error = "A report share is already in progress. Your local export is retained." }); yield break; }
#if UNITY_ANDROID && !UNITY_EDITOR
            sharing = true;
            AndroidJavaClass bridge = null;
            ReportShareResult result = null;
            try
            {
                bridge = new AndroidJavaClass("com.travelagent.ble.ReportSharingBridge");
                while (!string.IsNullOrEmpty(bridge.CallStatic<string>("poll"))) { }
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    bridge.CallStatic("share", activity, completedExportFolder);
            }
            catch (Exception) { result = new ReportShareResult { error = "Android sharing could not start. Your completed local export is retained." }; }
            try
            {
                while (result == null)
                {
                    string json = "";
                    try { json = bridge.CallStatic<string>("poll"); }
                    catch (Exception) { result = new ReportShareResult { error = "Android sharing status is unavailable. Your local export is retained." }; }
                    if (!string.IsNullOrEmpty(json))
                    {
                        try
                        {
                            var update = JsonUtility.FromJson<ReportShareResult>(json);
                            if (update == null) result = new ReportShareResult { error = "Sharing returned an unreadable result. Local evidence is retained." };
                            else if (update.type == "opened") progress?.Invoke("Choose an app for the evidence ZIP. Delivery is not confirmed; your local export is retained.");
                            else result = update;
                        }
                        catch (Exception) { result = new ReportShareResult { error = "Sharing returned an unreadable result. Local evidence is retained." }; }
                    }
                    if (result == null) yield return null;
                }
            }
            finally { bridge?.Dispose(); sharing = false; }
            completed?.Invoke(result);
#else
            completed?.Invoke(new ReportShareResult { error = "The Android share sheet is available on a phone. The completed export stays in its local folder." });
            yield break;
#endif
        }
    }
}
