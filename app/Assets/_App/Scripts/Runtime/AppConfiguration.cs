using UnityEngine;

namespace TravelAgent
{
    [CreateAssetMenu(menuName = "Travel Agent/App configuration")]
    public sealed class AppConfiguration : ScriptableObject
    {
        [Header("Independent providers")]
        [Tooltip("Editor defaults to a board simulator. Android defaults to Bluetooth.")]
        public bool editorSimulatedHardware = true;
        public bool fixtureGuidance = true;
        [Tooltip("Laptop Exa service. On Android use the laptop's LAN address, not localhost.")]
        public string backendBaseUrl = "http://127.0.0.1:8787";
        [Header("Presentation")]
        [Tooltip("Hackathon presentation: a new app launch starts a blank trip and motion history. Device and gateway credentials are retained.")]
        public bool freshDemoSessionOnLaunch = true;
        [Range(0, 3)] public int startupScreen = 3;
        public string bagName = "My suitcase";
        public bool showDemoControls = true;
        public Sprite beforeSample;
        public Sprite afterSample;
    }
}
