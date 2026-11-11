using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Makes the mouse and the keyboard share one highlight.
// Hovering a button selects it, so arrow keys continue from there.
public class MenuHoverSelect : MonoBehaviour, IPointerEnterHandler
{
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (EventSystem.current == null)
            return;

        Selectable selectable = GetComponent<Selectable>();

        if (selectable == null || !selectable.IsInteractable())
            return;

        EventSystem.current.SetSelectedGameObject(gameObject);
    }
}
