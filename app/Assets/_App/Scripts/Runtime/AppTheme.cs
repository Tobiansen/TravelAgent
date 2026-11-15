using TMPro;
using UnityEngine;

namespace TravelAgent
{
    [CreateAssetMenu(menuName = "Travel Agent/Theme")]
    public sealed class AppTheme : ScriptableObject
    {
        public Color background = new Color32(16, 17, 20, 255);
        public Color ink = new Color32(244, 242, 241, 255);
        public Color muted = new Color32(168, 168, 177, 255);
        public Color accent = new Color32(255, 51, 71, 255);
        public Color card = new Color32(27, 29, 34, 255);
        public Color secondary = new Color32(40, 43, 50, 255);
        public Color input = new Color32(20, 22, 27, 255);
        public Color disabled = new Color32(52, 55, 63, 255);
        public Color border = new Color32(58, 61, 69, 255);
        public Color onAccent = new Color32(244, 242, 241, 255);
        public Color warning = new Color32(255, 51, 71, 255);
        public TMP_FontAsset font;
        [HideInInspector] public int layoutRevision;
        [HideInInspector] public Color appliedAccent = new Color32(20,112,96,255);
        [HideInInspector] public Color appliedInk = new Color32(20,44,43,255);
        [HideInInspector] public Color appliedMuted = new Color32(93,112,107,255);
        [HideInInspector] public Color appliedBackground = new Color32(242,245,241,255);
        [HideInInspector] public Color appliedCard = Color.white;
        [HideInInspector] public Color appliedSecondary = new Color32(40,43,50,255);
        [HideInInspector] public Color appliedInput = new Color32(20,22,27,255);
        [HideInInspector] public Color appliedDisabled = new Color32(52,55,63,255);
        [HideInInspector] public Color appliedBorder = new Color32(58,61,69,255);
        [HideInInspector] public Color appliedOnAccent = Color.white;
    }
}
