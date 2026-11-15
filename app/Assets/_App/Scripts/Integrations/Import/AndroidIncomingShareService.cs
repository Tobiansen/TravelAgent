using System;
using UnityEngine;

namespace TravelAgent.Integrations.Import
{
    public sealed class AndroidIncomingShareService : IIncomingShareService
    {
        public IncomingShare Peek()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var bridge = new AndroidJavaClass("com.travelagent.ble.IncomingShareBridge"))
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    string json = bridge.CallStatic<string>("peek", activity);
                    if (string.IsNullOrEmpty(json)) return null;
                    return JsonUtility.FromJson<IncomingShare>(json) ?? Failure();
                }
            }
            catch (Exception) { return Failure(); }
#else
            return null;
#endif
        }
        public bool Acknowledge(string requestId)
        {
            if (string.IsNullOrEmpty(requestId)) return false;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var bridge = new AndroidJavaClass("com.travelagent.ble.IncomingShareBridge"))
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    return bridge.CallStatic<bool>("acknowledge", activity, requestId);
            }
            catch (Exception) { return false; }
#else
            return false;
#endif
        }
#if UNITY_ANDROID && !UNITY_EDITOR
        private static IncomingShare Failure() { return new IncomingShare { error = "Shared text could not be read. It has not been imported or acknowledged. Please retry." }; }
#endif
    }
}
