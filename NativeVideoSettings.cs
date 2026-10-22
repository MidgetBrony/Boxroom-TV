using System;
using System.Linq;
using MelonLoader;
using ModsPanel;
using SteamShelf.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Boxroom_TV;

/// <summary>Hosts playback settings below BOXROOM's existing Video library UI.</summary>
internal static class NativeVideoSettings
{
    private static ModSection section;
    private static RectTransform nativeContent;
    private static Transform originalParent;
    private static Vector2 originalMin, originalMax, originalPivot, originalPosition, originalSize;
    private static RectOffset originalPadding;
    private static int originalSibling;
    private static LayoutElement addedSizing;
    private static GameObject scrollHost;
    private static float nextAttempt;

    internal static void Register(ModSection settings) => section = settings;

    internal static void Tick()
    {
        if (section == null || scrollHost != null || Time.unscaledTime < nextAttempt) return;
        nextAttempt = Time.unscaledTime + 1f;
        try
        {
            UI_Container screen = Resources.FindObjectsOfTypeAll<UI_Container>()
                .FirstOrDefault(value => value != null && value.name == "Screen_Videos" && value.gameObject.scene.IsValid());
            if (screen == null) return;
            Transform card = screen.transform.Find("Videos");
            RectTransform content = card?.Find("Content") as RectTransform;
            VerticalLayoutGroup nativeLayout = content?.GetComponent<VerticalLayoutGroup>();
            TMP_Text body = content?.Find("Videos Count")?.GetComponent<TMP_Text>();
            if (nativeLayout == null || body == null)
                throw new InvalidOperationException("Native Video library content was not found.");

            nativeContent = content;
            originalParent = content.parent; originalSibling = content.GetSiblingIndex();
            originalMin = content.anchorMin; originalMax = content.anchorMax; originalPivot = content.pivot;
            originalPosition = content.anchoredPosition; originalSize = content.sizeDelta;
            originalPadding = nativeLayout.padding;

            RectTransform scrollRoot = Rect("Boxroom-TV Library Settings Scroll", card);
            scrollHost = scrollRoot.gameObject;
            Fill(scrollRoot, 0, 0, 75, 16);
            ScrollRect scroll = scrollHost.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.scrollSensitivity = 40;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            RectTransform viewport = Rect("Viewport", scrollRoot); Fill(viewport, 0, 24, 0, 0);
            viewport.gameObject.AddComponent<Image>().color = new Color(1, 1, 1, 0.001f);
            viewport.gameObject.AddComponent<RectMask2D>();
            RectTransform stack = Rect("Content", viewport);
            stack.anchorMin = new Vector2(0, 1); stack.anchorMax = Vector2.one;
            stack.pivot = new Vector2(0.5f, 1); stack.sizeDelta = Vector2.zero;
            VerticalLayoutGroup layout = stack.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 16; layout.padding = new RectOffset(0, 0, 0, 24);
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            stack.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport; scroll.content = stack;
            scrollHost.AddComponent<UI_ScrollRectFollowSelection>();

            // Keep the original components, path binding, buttons and callbacks.
            // The heading stays fixed on the native card above the viewport.
            content.SetParent(stack, false);
            nativeLayout.padding = new RectOffset(originalPadding.left, originalPadding.right, 0, originalPadding.bottom);
            if (content.GetComponent<LayoutElement>() == null)
            {
                addedSizing = content.gameObject.AddComponent<LayoutElement>();
                addedSizing.flexibleHeight = 0;
            }
            RectTransform settingsRoot = Rect("TV Playback Settings", stack);
            VerticalLayoutGroup settingsLayout = settingsRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            settingsLayout.padding = new RectOffset(30, 30, 0, 0);
            settingsLayout.childControlWidth = settingsLayout.childControlHeight = true;
            settingsLayout.childForceExpandWidth = true; settingsLayout.childForceExpandHeight = false;
            ModsPanelApi.RenderEmbeddedSection(section, settingsRoot, body);

            RectTransform barRoot = Rect("Scrollbar", scrollRoot);
            barRoot.anchorMin = new Vector2(1, 0); barRoot.anchorMax = Vector2.one;
            barRoot.pivot = new Vector2(1, 0.5f); barRoot.sizeDelta = new Vector2(20, 0);
            barRoot.gameObject.AddComponent<Image>().color = new Color(0.22f, 0.45f, 0.49f, 0.12f);
            RectTransform handle = Rect("Handle", barRoot); Fill(handle);
            Image handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.color = new Color32(56, 116, 125, 255);
            Scrollbar bar = barRoot.gameObject.AddComponent<Scrollbar>();
            bar.direction = Scrollbar.Direction.BottomToTop; bar.handleRect = handle; bar.targetGraphic = handleImage;
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            section.VisibleInModSettings = false;
            LayoutRebuilder.ForceRebuildLayoutImmediate(stack);
            screen.GetComponent<UI_NavigationGroup>()?.Rebuild();
        }
        catch (Exception exception)
        {
            Restore();
            nextAttempt = Time.unscaledTime + 10f;
            MelonLogger.Warning("Boxroom-TV Video settings: " + exception.Message);
        }
    }

    internal static void Restore()
    {
        if (nativeContent != null && originalParent != null)
        {
            nativeContent.SetParent(originalParent, false); nativeContent.SetSiblingIndex(originalSibling);
            nativeContent.anchorMin = originalMin; nativeContent.anchorMax = originalMax; nativeContent.pivot = originalPivot;
            nativeContent.sizeDelta = originalSize; nativeContent.anchoredPosition = originalPosition;
            nativeContent.GetComponent<VerticalLayoutGroup>().padding = originalPadding;
            if (addedSizing != null) Object.Destroy(addedSizing);
        }
        if (scrollHost != null) { scrollHost.SetActive(false); Object.Destroy(scrollHost); }
        nativeContent = null; originalParent = null; addedSizing = null; scrollHost = null;
        if (section != null) section.VisibleInModSettings = true;
    }

    private static RectTransform Rect(string name, Transform parent)
    {
        GameObject obj = new(name, typeof(RectTransform)); obj.layer = parent.gameObject.layer;
        RectTransform rect = (RectTransform)obj.transform; rect.SetParent(parent, false); return rect;
    }

    private static void Fill(RectTransform rect, float left = 0, float right = 0, float top = 0, float bottom = 0)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom); rect.offsetMax = new Vector2(-right, -top);
    }
}
