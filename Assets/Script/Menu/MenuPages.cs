using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Shows one page at a time and keeps keyboard selection sensible.
// Going back to a page re-selects the button you left from.
public class MenuPages
{
    private class Page
    {
        public GameObject root;
        public Selectable first;
        public Selectable lastSelected;
    }

    private readonly Dictionary<string, Page> pages =
        new Dictionary<string, Page>();

    private readonly string rootName;

    public string Current { get; private set; }

    public bool IsAtRoot => Current == rootName;


    public MenuPages(string rootPageName)
    {
        rootName = rootPageName;
    }

    public void Add(string name, GameObject root, Selectable first)
    {
        Page page = new Page();
        page.root = root;
        page.first = first;

        pages[name] = page;
        root.SetActive(false);
    }

    public void ShowRoot()
    {
        Show(rootName);
    }

    public void Show(string name)
    {
        if (!pages.ContainsKey(name))
            return;

        RememberSelection();

        foreach (KeyValuePair<string, Page> entry in pages)
            entry.Value.root.SetActive(entry.Key == name);

        Current = name;

        Page page = pages[name];
        Selectable target = page.lastSelected != null ? page.lastSelected : page.first;

        MenuUI.Select(target);
    }

    public void HideAll()
    {
        foreach (KeyValuePair<string, Page> entry in pages)
            entry.Value.root.SetActive(false);

        Current = null;
    }

    // Keep the highlight on something if the mouse leaves every button
    public void RestoreSelectionIfLost()
    {
        if (Current == null || EventSystem.current == null)
            return;

        if (EventSystem.current.currentSelectedGameObject != null)
            return;

        Page page = pages[Current];
        Selectable target = page.lastSelected != null ? page.lastSelected : page.first;

        MenuUI.Select(target);
    }

    private void RememberSelection()
    {
        if (Current == null || EventSystem.current == null)
            return;

        GameObject selected = EventSystem.current.currentSelectedGameObject;

        if (selected == null)
            return;

        Selectable selectable = selected.GetComponent<Selectable>();

        if (selectable != null && selected.transform.IsChildOf(pages[Current].root.transform))
            pages[Current].lastSelected = selectable;
    }
}
