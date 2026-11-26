using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class OsowiecDeploymentStyle
{
    private const string Folder = "Assets/Osowiec/HQ/";
    private static readonly Color Ink = new(.085f, .091f, .078f, .985f);
    private static readonly Color Plate = new(.157f, .161f, .125f);
    private static readonly Color Highlight = new(.249f, .223f, .149f);
    private static readonly Color Brass = new(.79f, .635f, .379f);
    private static readonly Color Ivory = new(.96f, .906f, .798f);
    private static readonly Color Muted = new(.72f, .73f, .65f);

    public static void Apply(ArmyDeploymentController controller)
    {
        if (controller == null) throw new ArgumentNullException(nameof(controller));
        var source = new SerializedObject(controller);
        if (source.FindProperty("deploymentRoot").objectReferenceValue is GameObject oldRoot)
            oldRoot.SetActive(false);

        var canvas = new GameObject("Osowiec field deployment", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster)).GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 38;
        var scaler = canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        var root = new GameObject("Deployment backing controls",typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(canvas.transform,false);
        root.anchorMin=Vector2.zero;root.anchorMax=Vector2.one;
        root.offsetMin=Vector2.zero;root.offsetMax=Vector2.zero;

        var header = Box(root, "Command strip", new(.5f, 1), Vector2.zero, new(0, 92), Ink);
        header.anchorMin = Vector2.up;
        header.anchorMax = Vector2.one;
        var headerRule = Box(header, "Header rule", new(.5f, 0), Vector2.zero, new(0, 2), Brass);
        headerRule.anchorMin = Vector2.zero;
        headerRule.anchorMax = Vector2.right;
        Label(header, "Operation name", "ОСОВЕЦ", new(0, 1), new(36, -13), new(262, 23), 18).color = Brass;
        Label(header, "Deployment title", "Расстановка отряда", new(0, 1), new(34, -35), new(530, 48), 39);
        Phase(header, "Briefing", "БРИФИНГ", -235, false);
        Phase(header, "Deploy", "РАССТАНОВКА", 0, true);
        Phase(header, "Order", "БОЕВОЙ ПРИКАЗ", 235, false);
        Label(header, "Theatre", "АРТИЛЛЕРИЙСКАЯ БАТАРЕЯ", new(1, .5f), new(-36, 0), new(350, 45), 22,
            TextAlignmentOptions.MidlineRight).color = Muted;

        var field = Box(root, "Terrain briefing", new(0, 1), new(34, -119), new(340, 259), Ink);
        Box(field, "Brief margin", new(0, 1), Vector2.zero, new(3, 259), Brass);
        Label(field, "Field label", "УЧАСТОК ПОДХОДА", new(0, 1), new(20, -17), new(298, 29), 19).color = Brass;
        var area = Label(field, "Start location", OsowiecDeploymentSectors.Titles[0], new(0, 1), new(19, -48), new(308, 42), 28);
        Box(field, "Brief divider", new(0, 1), new(20, -96), new(300, 1), Muted);
        var order = Label(field, "Field orders", OsowiecDeploymentSectors.Descriptions[0],
            new(0, 1), new(20, -108), new(293, 82), 20, TextAlignmentOptions.TopLeft);
        order.color=Muted;
        var switchPlate=Box(field,"Change start sector",new(0,1),new(20,-201),new(300,40),Highlight,true);
        var switchButton=switchPlate.gameObject.AddComponent<Button>();
        switchButton.targetGraphic=switchPlate.GetComponent<Image>();
        Label(switchPlate,"Sector control","Сменить участок   [Q]",new(.5f,.5f),Vector2.zero,new(288,38),21,TextAlignmentOptions.Center);
        var lower = Box(root, "Personnel dossier strip", new(.5f, 0), Vector2.zero, new(0, 193), Ink);
        lower.anchorMin = Vector2.zero;
        lower.anchorMax = Vector2.right;
        var rule = Box(lower, "Footer brass rule", new(.5f, 1), Vector2.zero, new(0, 2), Brass);
        rule.anchorMin = Vector2.up;
        rule.anchorMax = Vector2.one;
        Label(lower, "Reserve caption", "ЛИЧНЫЙ СОСТАВ", new(0, 1), new(35, -22), new(287, 32), 22).color = Brass;
        Label(lower, "Deployment instruction", "Карта бойца → позиция\nили перетащите карту",
            new(0, 1), new(35, -63), new(290, 66), 22, TextAlignmentOptions.TopLeft);
        var recycle = Label(lower, "Return hint", "ПКМ по бойцу — снять\nShift + сброс — заменить",
            new(1, 1), new(-36, -60), new(320, 67), 21, TextAlignmentOptions.TopRight);
        recycle.color = Muted;

        var formation = Box(root, "Formation order", new(1, 1), new(-35, -119), new(356, 348), Ink);
        Box(formation, "Order margin", new(1, 1), Vector2.zero, new(3, 348), Brass);
        Label(formation, "Formation label", "ПРИКАЗ НА ВЫДВИЖЕНИЕ", new(0, 1), new(20, -16), new(316, 29), 19).color = Brass;
        var capacity = Label(formation, "Formation capacity", "Позиции", new(0, 1), new(20, -54), new(212, 32), 26);
        var slots = DrawSlots(formation, OsowiecDeploymentSectors.Cells[0].Length, 1);
        Box(formation, "Formation divider", new(0, 1), new(20, -105), new(316, 1), Muted);
        Label(formation, "Reserve label", "ОСТАЛОСЬ СНАБЖЕНИЯ", new(0, 1), new(20, -121), new(208, 28), 17).color = Brass;
        int total = source.FindProperty("armyPointBudget").intValue;
        var counter = Label(formation, "Remaining supply", $"{total} / {total}", new(1, 1), new(-20, -111),
            new(122, 41), 30, TextAlignmentOptions.MidlineRight);
        var meter = Meter(formation);
        var confirm = ConfirmButton(formation);
        var hint = Label(formation, "Confirmation hint", "", new(0, 1), new(20, -269), new(316, 69), 20,
            TextAlignmentOptions.TopLeft);
        hint.color = Muted;

        var hand = RectAt(lower, "Squad card row", new(.5f, .5f), new(0, 0), new(1040, 137));
        var layout = hand.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.spacing = 14;
        var backing = Box(root, "Placement feedback backing", new(.5f, 0), new(0, 210), new(862, 50), Ink);
        var feedback = Label(backing, "Placement feedback", "", new(.5f, .5f), Vector2.zero, new(850, 50), 23,
            TextAlignmentOptions.Center);
        var details = Dossier(root);
        var card = CardPrefab();
        root.gameObject.AddComponent<OsowiecDeploymentReadout>().Bind(controller, counter, feedback, total,
            backing.gameObject, capacity, slots, area, order, switchButton);
        source.FindProperty("deploymentRoot").objectReferenceValue = canvas.gameObject;
        source.FindProperty("cardPrefab").objectReferenceValue = card;
        source.FindProperty("cardHandContainer").objectReferenceValue = hand;
        source.FindProperty("returnDropZone").objectReferenceValue = lower;
        source.FindProperty("budgetSlider").objectReferenceValue = meter;
        source.FindProperty("confirmButton").objectReferenceValue = confirm;
        source.FindProperty("confirmHintText").objectReferenceValue = hint;
        source.FindProperty("unitInfoPanel").objectReferenceValue = details;
        source.FindProperty("defaultUnitInfoPanelSprite").objectReferenceValue = null;
        source.FindProperty("createMinimalUiIfMissing").boolValue = false;
        var offers = source.FindProperty("offers");
        for (int index = 0; index < offers.arraySize; index++)
            offers.GetArrayElementAtIndex(index).FindPropertyRelative("infoPanelSprite").objectReferenceValue = null;
        source.ApplyModifiedPropertiesWithoutUndo();
        root.gameObject.SetActive(false);
        OsowiecHeadquartersBuilder.InstallPaperDeployment(canvas,controller,confirm,total);
        EditorUtility.SetDirty(controller);
    }

    private static DeploymentUnitInfoPanelUI Dossier(Transform parent)
    {
        var panel = Box(parent, "Unit dossier", new(1, 1), new(-35, -481), new(356, 311), Ink);
        Label(panel, "Dossier heading", "ПОЛЕВАЯ КАРТОЧКА", new(0, 1), new(22, -21), new(353, 30), 21).color = Brass;
        Box(panel, "Dossier rule", new(0, 1), new(22, -57), new(312, 2), Brass);
        Label(panel, "Health label", "Здоровье", new(0, 1), new(22, -71), new(117, 30), 21).color = Muted;
        Label(panel, "Damage label", "Урон", new(0, 1), new(210, -71), new(65, 30), 21).color = Muted;
        var health = Label(panel, "Health value", "", new(0, 1), new(134, -71), new(67, 30), 22);
        var damage = Label(panel, "Damage value", "", new(0, 1), new(277, -71), new(57, 30), 22);
        var skill = Label(panel, "Ability heading", "", new(0, 1), new(22, -122), new(310, 42), 26);
        var description = Label(panel, "Ability description", "", new(0, 1), new(22, -171), new(310, 98), 20,
            TextAlignmentOptions.TopLeft);
        description.color = Muted;
        var cooldown = Label(panel, "Ability cooldown", "", new(0, 1), new(22, -271), new(313, 27), 19);
        cooldown.color = Brass;
        var details = parent.gameObject.AddComponent<DeploymentUnitInfoPanelUI>();
        details.ConfigureRuntime(panel.gameObject, panel.GetComponent<Image>(), damage, health, skill, description, cooldown, null);
        EditorUtility.SetDirty(details);
        return details;
    }

    private static DeploymentCardUI CardPrefab()
    {
        var card = Box(null, "Osowiec field card", Vector2.one * .5f, Vector2.zero, new(316, 128), Plate, true);
        Box(card, "Card accent", new(.5f, 1), Vector2.zero, new(316, 2), Brass);
        var insignia = Box(card, "Printed character", new(0, .5f), new(13, 0), new(82, 102), Ink);
        var sprite = DragSprite();
        var icon = Box(insignia, "Printed silhouette", new(.5f, .56f), Vector2.zero, new(76, 76),
            new Color(1, 1, 1, .86f)).GetComponent<Image>();
        icon.sprite = sprite;
        RawImage photograph = null;
        var atlas = OsowiecFieldCast.Art<Texture2D>("FieldPortraitAtlas.png");
        if (atlas != null)
        {
            var portraitRect = RectAt(insignia, "Field portrait", new(.5f, .5f), Vector2.zero, new(82, 102));
            photograph = portraitRect.gameObject.AddComponent<RawImage>();
            photograph.raycastTarget = false;
            photograph.texture = atlas;
            photograph.uvRect = new UnityEngine.Rect(.034f, 0, .267f, 1);
            icon.color = Color.clear;
        }
        var seal = Box(insignia, "Role stamp", new(1, 0), new(-3, 3), new(28, 27), Brass);
        var glyph = Label(seal, "Role initial", "", new(.5f, .5f), Vector2.zero, new(26, 26), 17,
            TextAlignmentOptions.Center);
        glyph.color = Ink;
        var title = Label(card, "Unit name", "", new(0, 1), new(110, -17), new(190, 52), 25, TextAlignmentOptions.TopLeft);
        Box(card, "Card stat rule", new(0, 0), new(110, 43), new(191, 1), Muted);
        var description = Label(card, "Travel budget", "", new(0, 0), new(110, 11), new(128, 27), 20);
        description.color = Muted;
        var cost = Label(card, "Unit cost", "", new(1, 0), new(-14, 12), new(48, 30), 25, TextAlignmentOptions.MidlineRight);
        cost.color = Brass;
        var cardRuntime = card.gameObject.AddComponent<DeploymentCardUI>();
        cardRuntime.SetRuntimeRefs(icon, title, cost);
        card.gameObject.AddComponent<OsowiecDeploymentCardFace>().Bind(title, description, glyph, card.GetComponent<Image>(),
            icon, sprite, Plate, Highlight, photograph);
        var prefab = PrefabUtility.SaveAsPrefabAsset(card.gameObject, Folder + "OsowiecDeploymentCard.prefab");
        Object.DestroyImmediate(card.gameObject);
        if (prefab == null) throw new InvalidOperationException("Unable to save the Osowiec deployment card.");
        return prefab.GetComponent<DeploymentCardUI>();
    }

    private static Sprite DragSprite()
    {
        const string path = Folder + "DeploymentFieldSilhouette.asset";
        Sprite found = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
        if (found != null) return found;
        const int size = 88;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Deployment ink stamp", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
        };
        var stamp = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float head = Mathf.Pow((x - 47) / 15.5f, 2) + Mathf.Pow((y - 52) / 19f, 2);
            float shell = Mathf.Pow((x - 46) / 24f, 2) + Mathf.Pow((y - 61) / 17f, 2);
            float shoulder = Mathf.Pow((x - 42) / 31f, 2) + Mathf.Pow((y - 10) / 21f, 2);
            bool profile = head < 1 && (x < 56 || y > 38) || x >= 49 && x <= 65 && y >= 44 && y <= 50;
            bool helmet = shell < 1 && y > 58 || x >= 22 && x <= 71 && y is >= 57 and <= 60;
            bool coat = shoulder < 1 && y > 11 || x is >= 36 and <= 52 && y is >= 23 and <= 43;
            if (helmet || profile || coat)
            {
                float grain = .07f * Mathf.PerlinNoise(x * .41f, y * .37f);
                bool collar = coat && y < 34 && y > 18 && Mathf.Abs(x - 43) > 8 && Mathf.Abs(x - 43) < 14;
                stamp[y * size + x] = Color.Lerp(Brass, Ivory, (helmet ? .22f : collar ? .44f : .02f) + grain);
            }
        }
        tex.SetPixels(stamp);
        tex.Apply();
        AssetDatabase.CreateAsset(tex, path);
        var sprite = Sprite.Create(tex, new UnityEngine.Rect(0, 0, size, size), Vector2.one * .5f);
        sprite.name = "Deployment stamp";
        AssetDatabase.AddObjectToAsset(sprite, tex);
        return sprite;
    }

    private static Slider Meter(Transform parent)
    {
        var meter = Box(parent, "Supply meter", new(0, 1), new(20, -162), new(316, 11), Plate);
        var fill = Box(meter, "Available supply", new(.5f, .5f), Vector2.zero, Vector2.zero, Brass);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = Vector2.one;
        var slider = meter.gameObject.AddComponent<Slider>();
        slider.fillRect = fill;
        slider.interactable = false;
        slider.transition = Selectable.Transition.None;
        return slider;
    }

    private static Button ConfirmButton(Transform parent)
    {
        var plate = Box(parent, "Confirm squad", new(0, 1), new(20, -197), new(316, 58), Brass, true);
        var button = plate.gameObject.AddComponent<Button>();
        button.targetGraphic = plate.GetComponent<Image>();
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1, 1, .83f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = new Color(.76f, .74f, .65f);
        colors.disabledColor = new Color(.51f, .51f, .49f);
        colors.fadeDuration = .1f;
        button.colors = colors;
        Label(plate, "Button label", "Подтвердить отряд", new(.5f, .5f), Vector2.zero, new(306, 61), 28,
            TextAlignmentOptions.Center).color = Ink;
        return button;
    }

    private static void Phase(Transform parent, string name, string text, float x, bool current)
    {
        var plate = Box(parent, name, new(.5f, .5f), new(x, 0), new(220, 45), current ? Highlight : Plate);
        Box(plate, "Phase rule", new(.5f, 0), Vector2.zero, new(220, current ? 3 : 1), current ? Brass : Muted);
        Label(plate, "Phase label", text, new(.5f, .5f), Vector2.zero, new(210, 43), 20,
            TextAlignmentOptions.Center).color = current ? Ivory : Muted;
    }

    private static TMP_Text[] DrawSlots(Transform parent, int columns, int rows)
    {
        var labels = new TMP_Text[columns * rows];
        for (int y = 0; y < rows; y++)
        for (int x = 0; x < columns; x++)
        {
            var slot = Box(parent, "Formation slot", new(1, 1), new(-20 - (columns - 1 - x) * 28, -51 - y * 23),
                new(24, 19), Brass);
            Box(slot, "Slot field", new(.5f, .5f), Vector2.zero, new(22, 17), Ink);
            labels[y * columns + x] = Label(slot, "Occupant", "", new(.5f, .5f), Vector2.zero,
                new(22, 17), 15, TextAlignmentOptions.Center);
        }
        return labels;
    }

    private static RectTransform RectAt(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.sizeDelta = size;
        rect.anchoredPosition = pos;
        return rect;
    }

    private static RectTransform Box(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size,
        Color color, bool raycast = false)
    {
        var rect = RectAt(parent, name, anchor, pos, size);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = raycast;
        return rect;
    }

    private static TextMeshProUGUI Label(Transform parent, string name, string copy, Vector2 anchor, Vector2 pos,
        Vector2 size, float fontSize, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
    {
        var rect = RectAt(parent, name, anchor, pos, size);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = fontSize >= 34 ? OsowiecHeadquartersBuilder.HeadingFont() : OsowiecHeadquartersBuilder.FieldFont();
        text.fontSize = fontSize;
        text.text = copy;
        text.alignment = alignment;
        text.color = Ivory;
        text.raycastTarget = false;
        return text;
    }
}
