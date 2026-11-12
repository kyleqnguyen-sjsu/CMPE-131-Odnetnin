using UnityEngine;
using UnityEngine.EventSystems;


// Fires an action when pressed, then keeps firing while it is held down.
// Used for the small "-" and "+" buttons in the photo mode menu.
public class PhotoModeHoldButton :
    MonoBehaviour,
    IPointerDownHandler,
    IPointerUpHandler,
    IPointerExitHandler
{
    public System.Action onStep;

    // Seconds before the repeating starts, and the gap between repeats
    private const float FirstDelay = 0.4f;
    private const float RepeatDelay = 0.07f;

    private bool held;
    private float nextStepTime;


    public void OnPointerDown(PointerEventData eventData)
    {
        held = true;
        nextStepTime = Time.unscaledTime + FirstDelay;

        Fire();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        held = false;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        held = false;
    }

    void OnDisable()
    {
        held = false;
    }

    void Update()
    {
        if (!held)
            return;

        if (Time.unscaledTime < nextStepTime)
            return;

        nextStepTime = Time.unscaledTime + RepeatDelay;

        Fire();
    }

    void Fire()
    {
        if (onStep != null)
            onStep();
    }
}
