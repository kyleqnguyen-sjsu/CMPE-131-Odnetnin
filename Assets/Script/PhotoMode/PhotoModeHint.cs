using UnityEngine;
using UnityEngine.EventSystems;


// Put on a menu row to make it friendlier:
//   - hovering it shows a help line at the bottom of the panel
//   - double-clicking it (if onDoubleClick is set) runs a reset
public class PhotoModeHint :
    MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerClickHandler
{
    public string hint;

    // Called with the hint text on hover, and with null when the mouse leaves
    public System.Action<string> onHintChanged;

    public System.Action onDoubleClick;


    public void OnPointerEnter(PointerEventData eventData)
    {
        if (onHintChanged != null)
            onHintChanged(hint);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (onHintChanged != null)
            onHintChanged(null);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.clickCount >= 2 && onDoubleClick != null)
            onDoubleClick();
    }

    void OnDisable()
    {
        if (onHintChanged != null)
            onHintChanged(null);
    }
}
