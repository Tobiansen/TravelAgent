using System;
using UnityEngine;

namespace TravelAgent.Integrations.Import
{
    [Serializable]
    public sealed class GatewaySettings
    {
        public string url = "";
        public string token = "";
        public string error = "";
    }

    public static class AndroidGatewaySettings
    {
        public static GatewaySettings Load()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var bridge = new AndroidJavaClass("com.travelagent.ble.GatewaySettingsBridge"))
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    return JsonUtility.FromJson<GatewaySettings>(bridge.CallStatic<string>("load", activity)) ?? Failure();
            }
            catch (Exception) { return Failure(); }
#else
            return new GatewaySettings();
#endif
        }
        public static bool Save(string url, string token)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var bridge = new AndroidJavaClass("com.travelagent.ble.GatewaySettingsBridge"))
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    return bridge.CallStatic<bool>("save", activity, url, token);
            }
            catch (Exception) { return false; }
#else
            return false;
#endif
        }
        public static bool Clear()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var bridge = new AndroidJavaClass("com.travelagent.ble.GatewaySettingsBridge"))
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    return bridge.CallStatic<bool>("clear", activity);
            }
            catch (Exception) { return false; }
#else
            return false;
#endif
        }
#if UNITY_ANDROID && !UNITY_EDITOR
        private static GatewaySettings Failure() { return new GatewaySettings { error = "Saved gateway settings could not be read. Enter the current gateway address and session token again." }; }
#endif
    }
}
