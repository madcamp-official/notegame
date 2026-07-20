using UnityEngine;
using UnityEngine.UI;

namespace KeyboardWanderer.Demo
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    public sealed class KeyboardWandererButtonStateView : MonoBehaviour
    {
        [SerializeField] private Outline selectedOutline;
        [SerializeField] private Vector3 selectedScale = new Vector3(1.065f, 1.065f, 1f);

        public void Configure(Outline outline)
        {
            selectedOutline = outline;
            if (selectedOutline != null)
                selectedOutline.enabled = false;
        }

        public void SetSelected(bool selected)
        {
            transform.localScale = selected ? selectedScale : Vector3.one;
            if (selectedOutline != null)
                selectedOutline.enabled = selected;
        }
    }
}
