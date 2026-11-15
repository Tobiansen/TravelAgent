using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TravelAgent
{
    /// <summary>Runtime layout adaptation; authored portrait values are captured and restored unchanged.</summary>
    public sealed class CompactLandscapeLayout : MonoBehaviour
    {
        public AppView view;
        [Tooltip("Portrait is the phone target. Compact chrome keeps the desktop landscape demo usable.")]
        public float compactHeaderHeight = 180;
        private readonly Dictionary<LayoutElement, Vector2> heights = new Dictionary<LayoutElement, Vector2>();
        private LayoutElement header, title, status, statusText, navigation, toggle, demo;
        private TMP_Text titleText;
        private float titleSize;
        private GameObject tagline, bagIllustration;
        private bool taglineVisible, illustrationVisible;
        private GridLayoutGroup grid;
        private Vector2 cellSize;
        private int gridColumns;
        private bool initialized;
        private Vector2Int lastSize;
        private void Start()
        {
            header=view.pageTitle.transform.parent.GetComponent<LayoutElement>();title=view.pageTitle.GetComponent<LayoutElement>();
            status=view.toast.transform.parent.GetComponent<LayoutElement>();statusText=view.toast.GetComponent<LayoutElement>();
            navigation=view.navigation[0].transform.parent.GetComponent<LayoutElement>();toggle=view.demoToggle.GetComponent<LayoutElement>();demo=view.demoPanel.GetComponent<LayoutElement>();
            foreach(var element in new[]{header,title,status,statusText,navigation,toggle,demo})Remember(element);
            foreach(var button in view.navigation)Remember(button.GetComponent<LayoutElement>());
            titleText=view.pageTitle;titleSize=titleText.fontSize;
            tagline=view.pageTitle.transform.parent.Find("Tagline")?.gameObject;taglineVisible=tagline&&tagline.activeSelf;
            bagIllustration=view.guardState.transform.parent.Find("SuitcaseIllustration")?.gameObject;illustrationVisible=bagIllustration&&bagIllustration.activeSelf;
            grid=view.demoPanel.GetComponent<GridLayoutGroup>();cellSize=grid.cellSize;gridColumns=grid.constraintCount;
            initialized=true;Apply();
        }
        private void Remember(LayoutElement value){if(value)heights[value]=new Vector2(value.minHeight,value.preferredHeight);}
        private void LateUpdate(){if(initialized && lastSize!=new Vector2Int(Screen.width,Screen.height))Apply();}
        private void Apply()
        {
            bool compact=Screen.width>Screen.height;
            foreach(var item in heights)if(item.Key){item.Key.minHeight=item.Value.x;item.Key.preferredHeight=item.Value.y;}
            titleText.fontSize=titleSize;
            if(tagline)tagline.SetActive(taglineVisible);
            if(bagIllustration)bagIllustration.SetActive(illustrationVisible);
            grid.constraintCount=gridColumns;grid.cellSize=cellSize;
            if(compact)
            {
                header.minHeight=header.preferredHeight=compactHeaderHeight;
                title.minHeight=title.preferredHeight=80;titleText.fontSize=Mathf.Min(48,titleSize);
                if(tagline)tagline.SetActive(false);if(bagIllustration)bagIllustration.SetActive(false);
                status.minHeight=70;status.preferredHeight=-1;statusText.minHeight=45;statusText.preferredHeight=-1;
                navigation.minHeight=navigation.preferredHeight=108;toggle.minHeight=toggle.preferredHeight=72;
                foreach(var button in view.navigation){var size=button.GetComponent<LayoutElement>();size.minHeight=size.preferredHeight=100;}
                grid.constraintCount=6;float width=((RectTransform)view.demoPanel.transform).rect.width;
                grid.cellSize=new Vector2(Mathf.Max(110,(width-grid.padding.horizontal-grid.spacing.x*5)/6),84);
                demo.minHeight=demo.preferredHeight=grid.padding.vertical+84*2+grid.spacing.y;
            }
            lastSize=new Vector2Int(Screen.width,Screen.height);
        }
    }
}
