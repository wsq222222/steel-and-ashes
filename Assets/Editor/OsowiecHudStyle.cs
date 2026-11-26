using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class OsowiecHudStyle
{
    private static readonly Color Ink = new(.07f, .078f, .066f, .94f);
    private static readonly Color Brass = new(.77f, .62f, .36f);
    private static readonly Color Ivory = new(.94f, .895f, .78f);
    private static readonly Color Warning = new(.80f, .31f, .22f);

    public static void Apply()
    {
        var quest = Find<MissionQuestUI>();
        if (quest != null) ApplyQuest(new SerializedObject(quest));
        var action = Find<ActionModeUI>();
        if (action != null) ApplyAction(new SerializedObject(action));
        var tactical = Find<TacticalModeUI>();
        if (tactical != null) ApplyTactical(new SerializedObject(tactical));
    }

    private static void ApplyQuest(SerializedObject state)
    {
        var panel = Reference<GameObject>(state, "visibilityRoot");
        if (panel == null) panel = ((MissionQuestUI)state.targetObject).gameObject;
        var rect=Panel(panel,new(0,1),new(20,-18),new(448,76));
        if(panel.TryGetComponent<Image>(out var paper))
            Solid(paper,new Color(.13f,.145f,.115f,.86f));
        var title=Reference<TMP_Text>(state,"titleText");
        Caption(title,rect,new(14,-5),new(417,24),17,TextAlignmentOptions.MidlineLeft,Brass);
        var goal=Reference<TMP_Text>(state,"objectivesText");
        Caption(goal,rect,new(14,-30),new(418,41),21,TextAlignmentOptions.MidlineLeft,Ivory);
        if(goal)
        {
            goal.font=OsowiecHeadquartersBuilder.HeadingFont();
            goal.enableAutoSizing=true; goal.fontSizeMin=16; goal.fontSizeMax=21;
            goal.overflowMode=TextOverflowModes.Ellipsis;
        }
    }

    private static void ApplyAction(SerializedObject state)
    {
        foreach(string field in new[]{"ammoPanel","integrityPanel"})
        {
            var legacy=Reference<GameObject>(state,field);
            if(!legacy) continue;
            if(!legacy.TryGetComponent<CanvasGroup>(out var opacity)) opacity=legacy.AddComponent<CanvasGroup>();
            opacity.alpha=0; opacity.interactable=false; opacity.blocksRaycasts=false;
        }
        var health=Reference<Slider>(state,"healthSlider");
        if(health)
        {
            if(!health.TryGetComponent<CanvasGroup>(out var opacity)) opacity=health.gameObject.AddComponent<CanvasGroup>();
            opacity.alpha=0; opacity.interactable=false; opacity.blocksRaycasts=false;
        }
        var enemy = Reference<GameObject>(state, "enemyHealthPanel");
        var ability = Reference<GameObject>(state, "abilityPanel");
        StylePlateWithoutMoving(enemy);
        StylePlateWithoutMoving(ability);
        foreach (var name in new[] { "enemyHealthText", "enemyNameText", "abilityNameText",
                     "abilityCooldownText", "abilityDescriptionText", "reloadText" })
            Refont(Reference<TMP_Text>(state, name), Ivory);

        state.FindProperty("defaultColor").colorValue = Ivory;
        state.FindProperty("enemyColor").colorValue = Warning;
        state.FindProperty("allyColor").colorValue = new Color(.66f, .75f, .49f);
        state.FindProperty("readyColor").colorValue = Brass;
        state.FindProperty("cooldownColor").colorValue = Warning;
        state.ApplyModifiedPropertiesWithoutUndo();
    }

    private static RectTransform GraphicBand(Transform parent, string name, Color color)
    {
        var item = new GameObject(name, typeof(RectTransform), typeof(Image));
        item.transform.SetParent(parent, false);
        var rect = (RectTransform)item.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        Solid(item.GetComponent<Image>(), color);
        return rect;
    }

    private static void ApplyTactical(SerializedObject state)
    {
        var turn = Panel(Reference<GameObject>(state, "turnPanel"), new(.5f, 1), new(0, -25), new(246, 48));
        Caption(Reference<TMP_Text>(state, "turnText"), turn, new(12, -3), new(222, 45), 24,
            TextAlignmentOptions.Center, Ivory);
    }

    private static RectTransform Panel(GameObject plate, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        if (plate == null || !(plate.transform is RectTransform rect)) return null;
        StylePlateWithoutMoving(plate);
        Place(rect, anchor, pos, size);
        return rect;
    }

    private static void StylePlateWithoutMoving(GameObject panel)
    {
        if (panel == null) return;
        var image = panel.GetComponent<Image>();
        if (image != null) Solid(image, Ink);
        if (panel.transform.Find("Field plate rule") != null) return;
        var rule = new GameObject("Field plate rule", typeof(RectTransform), typeof(Image));
        rule.transform.SetParent(panel.transform, false);
        var rect = (RectTransform)rule.transform;
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(.5f, 1);
        rect.sizeDelta = new Vector2(0, 2);
        Solid(rule.GetComponent<Image>(), Brass);
    }

    private static void Solid(Image image, Color color)
    {
        image.sprite = null;
        image.type = Image.Type.Simple;
        image.color = color;
        image.raycastTarget = false;
        EditorUtility.SetDirty(image);
    }

    private static void Caption(TMP_Text text, RectTransform parent, Vector2 pos, Vector2 size, float fontSize,
        TextAlignmentOptions alignment, Color color)
    {
        if (text == null || parent == null) return;
        if (text.transform.parent != parent) text.transform.SetParent(parent, false);
        Place(text.rectTransform, new(0, 1), pos, size);
        text.fontSize = fontSize;
        text.enableAutoSizing = false;
        text.alignment = alignment;
        text.margin = Vector4.zero;
        text.overflowMode = TextOverflowModes.Overflow;
        Refont(text, color);
    }

    private static void Refont(TMP_Text text, Color color)
    {
        if (text == null) return;
        text.font = OsowiecHeadquartersBuilder.FieldFont();
        text.color = color;
        text.raycastTarget = false;
        EditorUtility.SetDirty(text);
    }

    private static void Place(RectTransform rect, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        if (rect == null) return;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
        rect.anchoredPosition = pos;
        rect.sizeDelta = size;
        EditorUtility.SetDirty(rect);
    }

    private static T Find<T>() where T : Component => Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
    private static T Reference<T>(SerializedObject state, string name) where T : Object =>
        state.FindProperty(name)?.objectReferenceValue as T;
}
