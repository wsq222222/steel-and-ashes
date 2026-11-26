using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public static class OsowiecResultStyle
{
    static readonly Color Ink = new(.075f, .087f, .069f);
    static readonly Color Brass = new(.74f, .60f, .34f);
    static readonly Color Paper = new(.93f, .89f, .77f);
    static readonly Color Muted = new(.69f, .70f, .60f);
    static readonly Color Rule = new(.29f, .30f, .22f);

    public static void Apply(GameManager manager, OsowiecOperation operation)
    {
        var root = new GameObject("Osowiec result", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        root.GetComponent<Canvas>().sortingOrder = 60;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = .8f;

        Plate(root.transform, "Backdrop", Vector2.zero, Vector2.zero, Ink, true);
        var dossier = Plate(root.transform,"After-action report",Vector2.zero,new Vector2(1920,1080),Paper);
        var journal = new GameObject("Field journal",typeof(RectTransform),typeof(RawImage)).GetComponent<RawImage>();
        journal.rectTransform.SetParent(dossier,false);
        journal.rectTransform.sizeDelta = new Vector2(1920,1080);
        journal.texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/OsowiecJournal/journal.png");
        journal.raycastTarget = false;
        Color print = new Color(.17f,.13f,.09f);
        Color seal = new Color(.46f,.16f,.12f);
        Label(dossier,"Report type",new(-395,327),new(580,32),"ОПЕРАТИВНЫЙ ОТДЕЛ   /   ФОРТ I",seal,21,true);
        Label(dossier,"Report heading",new(-395,238),new(580,100),"Рапорт",print,71,true);
        Plate(dossier,"Report rule",new(-395,175),new(580,2),Brass);
        operation.resultNote=Label(dossier,"Outcome",new(-395,75),new(580,148),"",print,32,true);
        var result=Label(dossier,"Result",new(-395,-80),new(580,85),"Итог операции",seal,37,true);
        Label(dossier,"Ledger title",new(345,260),new(590,110),"Ведомость",print,57,true);
        Plate(dossier,"Ledger rule",new(345,196),new(590,2),Brass);
        operation.resultRoster=Label(dossier,"Squad count",new(345,60),new(590,234),"",print,32,true);
        AddButton(dossier,"Вернуться в штаб",new(344,-142),Brass,Ink,operation.ReturnToHeadquarters);
        AddButton(dossier,"Повторить операцию",new(344,-225),new Color(.76f,.67f,.50f),Ink,manager.RestartGame);

        var state = new SerializedObject(manager);
        if (state.FindProperty("gameOverPanel").objectReferenceValue is GameObject oldPanel)
            oldPanel.SetActive(false);
        state.FindProperty("gameOverPanel").objectReferenceValue = root;
        state.FindProperty("winnerText").objectReferenceValue = result;
        state.ApplyModifiedPropertiesWithoutUndo();
        root.SetActive(false);
    }

    static RectTransform Plate(Transform parent, string name, Vector2 position, Vector2 size, Color color,
        bool stretch = false)
    {
        var item = new GameObject(name, typeof(RectTransform), typeof(Image));
        item.transform.SetParent(parent, false);
        var rect = item.GetComponent<RectTransform>();
        rect.anchorMin = stretch ? Vector2.zero : new Vector2(.5f, .5f);
        rect.anchorMax = stretch ? Vector2.one : new Vector2(.5f, .5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        item.GetComponent<Image>().color = color;
        return rect;
    }

    static TextMeshProUGUI Label(Transform parent, string name, Vector2 position, Vector2 size,
        string value, Color color, float fontSize, bool leftAligned = false)
    {
        var item = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        item.transform.SetParent(parent, false);
        var text = item.GetComponent<TextMeshProUGUI>();
        text.rectTransform.sizeDelta = size;
        text.rectTransform.anchoredPosition = position;
        text.font = fontSize >= 36 ? OsowiecHeadquartersBuilder.HeadingFont() : OsowiecHeadquartersBuilder.FieldFont();
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = leftAligned ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center;
        text.text = value;
        text.raycastTarget = false;
        return text;
    }

    static void AddButton(Transform parent, string label, Vector2 position, Color background, Color foreground,
        UnityAction action)
    {
        var box = Plate(parent, label, position, new Vector2(340, 62), background);
        var button = box.gameObject.AddComponent<Button>();
        button.targetGraphic = box.GetComponent<Image>();
        UnityEventTools.AddPersistentListener(button.onClick, action);
        Label(box, "Label", Vector2.zero, new Vector2(326, 55), label, foreground, 24);
    }
}
