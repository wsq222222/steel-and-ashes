using System;
using System.IO;
using System.Linq;
using TMPro;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class OsowiecHeadquartersBuilder
{
    public const string ScenePath = "Assets/Osowiec/OsowiecHQ.unity";
    private const string Folder = "Assets/Osowiec/HQ/";
    private const string SourceScene = "Assets/Scenes/HubBase.unity";

    private static readonly Color Metal = new(.075f, .087f, .081f);
    private static readonly Color Ledger = new(.117f, .125f, .109f);
    private static readonly Color Brass = new(.73f, .59f, .34f);
    private static readonly Color Ivory = new(.94f, .89f, .77f);
    private static readonly Color Note = new(.75f, .75f, .67f);

    [MenuItem("Steel & Ashes/Generate Osowiec Headquarters")]
    public static void Generate()
    {
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();
        var source = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Single);
        if (!EditorSceneManager.SaveScene(source, ScenePath, true))
            throw new InvalidOperationException($"Failed to create {ScenePath}");
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var manager = Require<GameManager>();
        var bootstrap = Require<HubSceneBootstrap>();
        var terminal = Require<HubComputerTerminal>();
        var hint = Require<HubInteractionHintUI>();
        var missionPicker = Require<MissionSelectUI>();
        var definition = GetOrCreateDefinition();
        var map = CreateOperationMap();
        var furniture = BuildBunker(scene.GetRootGameObjects(), terminal, bootstrap, map.texture);

        OsowiecStaffBodyBuilder.Install();

        var oldPanel = new SerializedObject(missionPicker).FindProperty("rootPanel")
            .objectReferenceValue as GameObject;
        if (oldPanel != null) oldPanel.SetActive(false);
        missionPicker.enabled = false;
        terminal.transform.SetParent(furniture, false);
        terminal.transform.localPosition = Vector3.zero;
        SetFloat(terminal, "interactRadius", 1.85f);

        // Use the existing hint lifecycle and terminal interaction.
        var hintPanel = new SerializedObject(hint).FindProperty("hintPanel").objectReferenceValue as GameObject;
        if (hintPanel != null) hintPanel.SetActive(false);
        var promptCanvas = CreateCanvas("Operations table prompt", 27);
        var backing = Box(promptCanvas.transform, "Prompt", new(.5f, .1f), new(520f, 50f), Metal);
        var hintText = Label(backing, "Hint", "[E]  Открыть карту операций", 25, TextAlignmentOptions.Center);
        hint.Initialize(backing.gameObject, hintText);
        EditorUtility.SetDirty(hint);
        // The legacy terminal has a hardcoded "computer" prompt; localize it for the table.
        var tableHint = promptCanvas.gameObject.AddComponent<OsowiecTableHint>();
        TextAt(promptCanvas.transform, "Controls", "WASD  Ходьба     ESC  Пауза", new(.15f, .052f),
            new(468, 44), 22, TextAlignmentOptions.MidlineLeft).color = Note;

        var brief = CreateCanvas("Field dossier canvas", 31);
        var selection = brief.gameObject.AddComponent<MissionSelectUI>();
        CreateBriefing(brief, selection, definition, map, tableHint);
        terminal.Initialize(selection);
        tableHint.Bind(terminal, furniture.parent.Find("FieldGramophone"), selection);
        EditorUtility.SetDirty(terminal);
        SetObjectReference(bootstrap, "missionSelectUI", selection);
        InstallPauseMenu(manager, true);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException($"Failed to save {ScenePath}");
        AssetDatabase.SaveAssets();
    }

    public static MissionObjectivesConfig GetOrCreateObjectives()
    {
        Directory.CreateDirectory(Folder);
        var config = Asset<MissionObjectivesConfig>("OsowiecObjectives.asset");
        config.panelTitle = "Задачи операции";
        config.objectiveTexts = new[]
        {
            "Покинуть стартовую траншею",
            "У телефона вызвать штаб  [E]",
            "Пройти через каземат или низину",
            "Подключить оба ввода во дворе  [E]",
            "Передать позывной у батареи  [E]",
            "Вывести три повозки в лазарет",
            "Удержать двор два хода"
        };
        EditorUtility.SetDirty(config);
        return config;
    }

    private static MissionDefinition GetOrCreateDefinition()
    {
        var definition = Asset<MissionDefinition>("OsowiecOperation.asset");
        definition.missionId = "osowiec";
        definition.displayName = "Осовец";
        definition.objectiveShort = "Связь с батареей оборвалась. Дойди до развилки и вызови штаб по телефону. Затем восстанови оба ввода, проведи три повозки в лазарет и удержи батарею.";
        definition.sceneName = "Osowiec";
        definition.objectivesConfig = GetOrCreateObjectives();
        EditorUtility.SetDirty(definition);
        return definition;
    }

    public static void InstallPauseMenu(GameManager manager, bool inHeadquarters)
    {
        InstallJournal(manager,inHeadquarters);
    }

    private static void CreateBriefing(Canvas canvas, MissionSelectUI picker, MissionDefinition definition, Sprite map, OsowiecTableHint table)
    {
        InstallFieldOrder(canvas,picker,definition,map,table);
    }

    private static void BriefingPhase(Transform parent, string copy, float anchor, bool current)
    {
        var phase = Box(parent, copy, new(anchor, .936f), new(218, 44), current ? new Color(.24f, .216f, .143f) : Metal, false);
        Line(phase, "Phase keyline", new(.5f, .015f), new(218, current ? 3 : 1), current ? Brass : Note);
        Label(phase, "Phase", copy, 20, TextAlignmentOptions.Center).color = current ? Ivory : Note;
    }

    private static void Legend(Transform parent, string copy, float anchor, Color color)
    {
        var key = Box(parent, copy, new(anchor, .184f), new(245, 33), Ledger, false);
        Line(key, "Map symbol", new(.083f, .5f), new(31, 4), color);
        TextAt(key, "Map term", copy, new(.58f, .5f), new(190, 29), 18,
            TextAlignmentOptions.MidlineLeft).color = Note;
    }

    private static Transform BuildBunker(GameObject[] roots, HubComputerTerminal terminal,
        HubSceneBootstrap bootstrap, Texture map)
    {
        foreach (var root in roots)
        {
            if (root == terminal.gameObject) continue;
            if (root.name.Equals("level", StringComparison.OrdinalIgnoreCase) ||
                root.name.StartsWith("Cube", StringComparison.Ordinal))
                root.SetActive(false);
            if (root.TryGetComponent<Light>(out var light) && light.type == LightType.Directional)
                light.enabled = false;
        }
        foreach (var renderer in terminal.GetComponentsInChildren<Renderer>(true))
            renderer.enabled = false;
        foreach (var collider in terminal.GetComponentsInChildren<Collider>(true))
            collider.enabled = false;

        var spawn = new SerializedObject(bootstrap).FindProperty("spawnPoint").objectReferenceValue as Transform;
        if (spawn == null) throw new InvalidOperationException("Missing headquarters spawn point");
        spawn.SetPositionAndRotation(new Vector3(0f, .08f, -3.8f), Quaternion.identity);
        EditorUtility.SetDirty(spawn);
        foreach (var avatar in roots.SelectMany(root => root.GetComponentsInChildren<Unit>(false)))
        {
            avatar.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
            var state = new SerializedObject(avatar);
            state.FindProperty("snapToGridOnStart").boolValue = false;
            state.FindProperty("constrainActionMovementToNavMesh").boolValue = false;
            state.FindProperty("startingWeaponPrefab").objectReferenceValue = null;
            state.FindProperty("equippedWeapon").objectReferenceValue = null;
            state.ApplyModifiedPropertiesWithoutUndo();
            foreach (var oldBody in avatar.GetComponentsInChildren<Renderer>(true))
            {
                oldBody.forceRenderingOff = true;
                oldBody.enabled = false;
                if (oldBody is SkinnedMeshRenderer rig) rig.sharedMesh = null;
                if (oldBody.TryGetComponent<MeshFilter>(out var meshFilter)) meshFilter.sharedMesh = null;
            }
        }
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.29f, .25f, .20f);
        RenderSettings.fog = false;

        var room = new GameObject("Timber signal dugout").transform;
        var oak = MaterialAt("Oak", new(.19f, .126f, .075f), .22f);
        var oakDark = MaterialAt("Oak shadow", new(.115f, .070f, .043f), .15f);
        var boardA = MaterialAt("Larch", new(.24f, .165f, .103f), .25f);
        var boardB = MaterialAt("Larch stained", new(.17f, .123f, .087f), .27f);
        OsowiecDugoutArt.Grain(oak);
        OsowiecDugoutArt.Grain(oakDark);
        OsowiecDugoutArt.Grain(boardA, true);
        OsowiecDugoutArt.Grain(boardB, true);
        var mortar = MaterialAt("Earth brick", new(.205f, .145f, .109f), .26f);
        var cloth = MaterialAt("Wool olive", new(.145f, .162f, .092f), .1f);
        var steel = MaterialAt("Iron", new(.11f, .12f, .11f), .41f);

        Slab(room, "Floor footing", new(0, -.16f, 0), new(9.2f, .32f, 12.3f), oakDark);
        OsowiecDugoutArt.LayFloor(room, steel);
        Slab(room, "Ceiling", new(0, 3.42f, 0), new(9.2f, .25f, 12.3f), oakDark);
        for (int i = 0; i < 10; i++)
            Slab(room, "Joist", new(0, 3.25f, -5.72f + i * 1.27f), new(9.1f, .2f, .24f), oak);
        foreach (int side in new[] { -1, 1 })
        {
            Slab(room, "Side wall", new(side * 4.51f, 1.62f, 0), new(.28f, 3.35f, 12.3f), oakDark);
            Slab(room, "Bulkhead", new(0, 1.62f, side * 6.04f), new(9.2f, 3.35f, .3f), mortar);
            for (int row = 0; row < 12; row++)
                Slab(room, "Timber shoring", new(side * 4.34f, .14f + row * .27f, 0), new(.14f, .255f, 12.25f),
                    row % 3 == 0 ? boardB : boardA, false);
            for (int i = 0; i < 5; i++)
                Slab(room, "Wall buttress", new(side * 4.15f, 1.6f, -5.57f + i * 2.82f), new(.26f, 3.2f, .27f), oakDark);
        }
        for (int row = 0; row < 9; row++)
        for (int brick = 0; brick < 19; brick++)
        {
            float x = -4.22f + brick * .46f + (row % 2) * .16f;
            Slab(room, "End wall facing", new(x, .19f + row * .34f, 5.86f), new(.434f, .306f, .105f),
                (brick + row) % 5 == 0 ? boardB : mortar, false);
        }
        OsowiecDugoutArt.Install(room, map);
        for (int i = 0; i < 3; i++)
        {
            Slab(room, "Ammo crate", new(3.21f, .23f + i * .38f, 4.63f),
                new(1.18f, .38f, .72f), i % 2 == 0 ? boardA : boardB);
            Slab(room, "Crate band", new(3.21f, .28f + i * .38f, 4.247f), new(1.14f, .05f, .023f), steel, false);
        }
        foreach (float y in new[] { .43f, 1.38f })
        {
            Slab(room, "Bunk frame", new(-3.49f, y, 3.85f), new(1.16f, .15f, 2.29f), oak);
            Slab(room, "Wool bedding", new(-3.49f, y + .11f, 3.85f), new(1.05f, .14f, 2.16f), cloth, false);
        }
        foreach (float x in new[] { -4.0f, -2.97f })
        foreach (float z in new[] { 2.81f, 4.9f })
            Slab(room, "Bunk rail", new(x, .8f, z), new(.13f, 1.6f, .13f), oakDark);

        OsowiecArchitectureDetails.BakeHQ(room);
        var mesh = room.gameObject.AddComponent<NavMeshSurface>();
        mesh.collectObjects = CollectObjects.Children;
        mesh.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
        mesh.BuildNavMesh();
        string meshPath = Folder + "DugoutNavigation.asset";
        var savedMesh = AssetDatabase.LoadAssetAtPath<UnityEngine.AI.NavMeshData>(meshPath);
        if (mesh.navMeshData != null && savedMesh != null)
        {
            var built = mesh.navMeshData;
            EditorUtility.CopySerialized(built, savedMesh);
            mesh.navMeshData = savedMesh;
            EditorUtility.SetDirty(savedMesh);
        }
        else if (mesh.navMeshData != null)
        {
            AssetDatabase.CreateAsset(mesh.navMeshData, meshPath);
        }
        var interaction = new GameObject("Operations table interaction").transform;
        interaction.SetParent(room);
        interaction.position = new Vector3(0, .7f, .49f);
        return interaction;
    }

    private static void Lantern(Transform parent, Vector3 origin, Material iron, Material flame, float power)
    {
        Slab(parent, "Lantern base", origin + new Vector3(0, .035f, 0), new(.24f, .07f, .22f), iron, false);
        Slab(parent, "Lantern globe", origin + new Vector3(0, .164f, 0), new(.143f, .205f, .145f), flame, false);
        Slab(parent, "Lantern cap", origin + new Vector3(0, .298f, 0), new(.224f, .065f, .22f), iron, false);
        foreach (int x in new[] { -1, 1 })
        foreach (int z in new[] { -1, 1 })
            Slab(parent, "Lantern cage", origin + new Vector3(x * .093f, .167f, z * .081f), new(.025f, .237f, .026f), iron, false);
        var light = new GameObject("Warm lantern light").AddComponent<Light>();
        light.transform.SetParent(parent);
        light.transform.position = origin + new Vector3(0, .23f, 0);
        light.type = LightType.Point;
        light.color = new Color(1f, .61f, .31f);
        light.range = 5.5f;
        light.intensity = power;
    }

    private static GameObject Slab(Transform parent, string name, Vector3 position, Vector3 scale, Material material,
        bool collider = true)
    {
        var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        slab.name = name;
        slab.transform.SetParent(parent);
        slab.transform.localPosition = position;
        slab.transform.localScale = scale;
        slab.GetComponent<MeshRenderer>().sharedMaterial = material;
        if (!collider) Object.DestroyImmediate(slab.GetComponent<BoxCollider>());
        return slab;
    }

    private static Material MaterialAt(string name, Color color, float gloss)
    {
        string path = Folder + name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        var material = existing != null ? existing : new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", gloss);
        if (existing == null) AssetDatabase.CreateAsset(material, path);
        EditorUtility.SetDirty(material);
        return material;
    }

    internal static void RefreshDugoutLightAndGrain()
    {
        var scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
        var room=scene.GetRootGameObjects().Single(go=>go.name=="Timber signal dugout").transform;
        OsowiecDugoutArt.LightDugout(room);
        OsowiecDugoutArt.Grain(MaterialAt("Oak",new(.19f,.126f,.075f),.22f));
        OsowiecDugoutArt.Grain(MaterialAt("Oak shadow",new(.115f,.070f,.043f),.15f));
        OsowiecDugoutArt.Grain(MaterialAt("Larch",new(.24f,.165f,.103f),.25f),true);
        OsowiecDugoutArt.Grain(MaterialAt("Larch stained",new(.17f,.123f,.087f),.27f),true);
        OsowiecArchitectureDetails.SawnTimber();
        EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("Could not save headquarters lighting.");
    }

    private static T Asset<T>(string name) where T : ScriptableObject
    {
        var found = AssetDatabase.LoadAssetAtPath<T>(Folder + name);
        if (found != null) return found;
        var asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, Folder + name);
        return asset;
    }

    private static T Require<T>(string named = null) where T : Component
    {
        var result = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(item => named == null || item.name == named);
        if (result == null) throw new InvalidOperationException($"Source HQ is missing {named ?? typeof(T).Name}");
        return result;
    }

    private static void SetObjectReference(Object target, string field, Object value)
    {
        var serialized = new SerializedObject(target);
        serialized.FindProperty(field).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetFloat(Object target, string field, float value)
    {
        var serialized = new SerializedObject(target);
        serialized.FindProperty(field).floatValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Canvas CreateCanvas(string name, int order)
    {
        var canvas = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
            typeof(GraphicRaycaster)).GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = order;
        var scaler = canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        return canvas;
    }

    private static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = anchor;
        rect.sizeDelta = size;
        return rect;
    }

    private static RectTransform Fill(Transform parent, string name, Color color, bool raycast = true)
    {
        var rect = Box(parent, name, new(.5f, .5f), Vector2.zero, color, raycast);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        return rect;
    }

    private static RectTransform Box(Transform parent, string name, Vector2 anchor, Vector2 size, Color color,
        bool raycast = true)
    {
        var rect = Rect(parent, name, anchor, size);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = raycast;
        return rect;
    }

    private static void Line(Transform parent, string name, Vector2 anchor, Vector2 size, Color color) =>
        Box(parent, name, anchor, size, color, false);

    private static TextMeshProUGUI TextAt(Transform parent, string name, string copy, Vector2 anchor,
        Vector2 size, float fontSize, TextAlignmentOptions align) =>
        MakeText(Rect(parent, name, anchor, size), copy, fontSize, align);

    private static TextMeshProUGUI Label(RectTransform parent, string name, string copy, float fontSize,
        TextAlignmentOptions align)
    {
        var rect = Rect(parent, name, new(.5f, .5f), Vector2.zero);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        return MakeText(rect, copy, fontSize, align);
    }

    private static TextMeshProUGUI MakeText(RectTransform rect, string copy, float fontSize, TextAlignmentOptions align)
    {
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = fontSize >= 36 ? HeadingFont() : FieldFont();
        text.text = copy;
        text.color = Ivory;
        text.fontSize = fontSize;
        text.alignment = align;
        text.raycastTarget = false;
        return text;
    }

    internal static TMP_FontAsset FieldFont() => Typeface("FiraSansCondensed-Medium");
    internal static TMP_FontAsset HeadingFont() => Typeface("Spectral-SemiBold");

    static TMP_FontAsset Typeface(string family)
    {
        string root = "Assets/Resources/OsowiecFonts/" + family;
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(root + " SDF.asset");
        if (font && font.HasCharacter('ё') && font.HasCharacter('Я') &&
            !new SerializedObject(font).FindProperty("m_ClearDynamicDataOnBuild").boolValue) return font;
        if (!font)
        {
            var source = AssetDatabase.LoadAssetAtPath<Font>(root + ".ttf");
            if (!source) throw new System.InvalidOperationException("Missing Osowiec typeface " + root);
            font = TMP_FontAsset.CreateFontAsset(source,64,8,UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                1024,1024,AtlasPopulationMode.Dynamic,true);
            font.name = family + " SDF";
            AssetDatabase.CreateAsset(font,root+" SDF.asset");
            AssetDatabase.AddObjectToAsset(font.material,font);
        }
        var settings = new SerializedObject(font);
        settings.FindProperty("m_ClearDynamicDataOnBuild").boolValue = false;
        settings.ApplyModifiedPropertiesWithoutUndo();
        font.TryAddCharacters("АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюя"+
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789№°+-.,!?%/():;[]·×‒–—‹›«»…←→",out _);
        foreach(var atlas in font.atlasTextures)
            if(atlas && !AssetDatabase.Contains(atlas)) AssetDatabase.AddObjectToAsset(atlas,font);
        EditorUtility.SetDirty(font);
        return font;
    }

    private static Button ButtonAt(Transform parent, string name, string copy, Vector2 anchor, UnityAction selected,
        float width = 500)
    {
        var plate = Box(parent, name, anchor, new(width, 66), new(.28f, .23f, .13f));
        var button = plate.gameObject.AddComponent<Button>();
        button.targetGraphic = plate.GetComponent<Image>();
        Paint(button);
        Label(plate, "Button label", copy, 30, TextAlignmentOptions.Center);
        if (selected != null) UnityEventTools.AddPersistentListener(button.onClick, selected);
        return button;
    }

    private static void Paint(Selectable control)
    {
        ColorBlock colors = control.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, .88f, .59f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = new Color(.72f, .63f, .51f);
        colors.disabledColor = new Color(.34f, .35f, .33f);
        colors.fadeDuration = .12f;
        control.colors = colors;
    }

    private static Slider SliderAt(Transform parent, string copy, float y, float minimum, float maximum)
    {
        var host = Box(parent, copy + " control", new(.592f, y), new(326, 52), Color.clear);
        var trough = Box(host, "Engraved track", new(.5f, .5f), new(302, 4), new Color(.33f,.27f,.19f), false);
        var fill = Fill(trough, "Amount", new Color(.60f,.43f,.22f), false);
        for(int step=0;step<5;step++)
            Line(trough,"Interval",new(step*.25f,.5f),new(1.4f,step%2==0 ? 12 : 8),new Color(.34f,.27f,.19f,.65f));
        var handleArea = Rect(host, "Handle area", new(.5f, .5f), new(302, 0));
        var handle = Box(handleArea, "Brass dial", new(.5f, .5f), new(29, 29), Color.white, false);
        var image = handle.GetComponent<Image>();
        const string thumbPath = Folder + "Art/Textures/DialThumb.png";
        if (AssetImporter.GetAtPath(thumbPath) is TextureImporter ti && ti.textureType != TextureImporterType.Sprite)
        {
            ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single;
            ti.alphaIsTransparency = true; ti.mipmapEnabled = false; ti.SaveAndReimport();
        }
        image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(thumbPath);
        var slider = host.gameObject.AddComponent<Slider>();
        slider.targetGraphic = image;
        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.minValue = minimum;
        slider.maxValue = maximum;
        Paint(slider);
        return slider;
    }

    private static void AddReadout(Transform parent, string name, float y, Slider slider, Func<float, string> format)
    {
        var label = TextAt(parent, name, format(slider.value), new(.877f, y), new(155, 45), 24, TextAlignmentOptions.Center);
        label.color = Note;
        var readout = label.gameObject.AddComponent<OsowiecSliderReadout>();
        readout.Bind(slider, label, name == "Quality value" ? OsowiecSliderReadout.Format.Quality
            : name == "FOV" ? OsowiecSliderReadout.Format.Degrees
            : name == "Sensitivity value" ? OsowiecSliderReadout.Format.Multiplier : OsowiecSliderReadout.Format.Percent);
        EditorUtility.SetDirty(readout);
    }

    private static Toggle ToggleAt(Transform parent, string copy, float y)
    {
        var row = Box(parent, copy, new(.5f, y), new(780, 49), Metal);
        var text = TextAt(row, "Toggle label", copy, new(.29f, .5f), new(406, 45), 25, TextAlignmentOptions.MidlineLeft);
        text.color = Ivory;
        var outline = Box(row, "Toggle box", new(.94f, .5f), new(32, 32), Note, false);
        var check = Box(outline, "Active", new(.5f, .5f), new(20, 20), Brass, false);
        var toggle = row.gameObject.AddComponent<Toggle>();
        toggle.graphic = check.GetComponent<Image>();
        toggle.targetGraphic = row.GetComponent<Image>();
        Paint(toggle);
        return toggle;
    }

    private static Sprite CreateOperationMap()
    {
        const string path = "Assets/Osowiec/Art/SurveySheet.png";
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new InvalidOperationException("Missing surveyed map " + path);
        if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path)
            ?? throw new InvalidOperationException("Survey sheet has no sprite " + path);
    }
}
