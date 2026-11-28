using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Полноэкранный оверлей иконок юнитов в тактике (Battlefield-style): позиции через WorldToScreenPoint.
/// </summary>
public class TacticalWorldIconsController : MonoBehaviour
{
    public static TacticalWorldIconsController Instance;

    [Header("UI")]
    [SerializeField] private RectTransform iconsRoot;
    [SerializeField] private TacticalWorldUnitIcon iconPrefab;
    [SerializeField] private float worldOffsetY = 2f;
    [SerializeField] private Texture2D fieldPortraitAtlas;

    [Header("Опции")]
    [SerializeField] private bool hideIconsWhenBehindCamera = true;
    [SerializeField] private float nearIconSizePixels = 64f;
    [SerializeField] private float farIconSizePixels = 34f;
    [SerializeField] private float nearOrthographicSize = 25f;
    [SerializeField] private float farOrthographicSize = 70f;

    [Header("Attack Range Ring")]
    [SerializeField] private bool showRangedAttackRangeOnHover = true;
    [SerializeField] private Color rangeRingColor = new Color(1f, 0.25f, 0.15f, 0.9f);
    [SerializeField] private float rangeRingWidth = 0.12f;
    [SerializeField] private float rangeRingYOffset = 0.08f;
    [SerializeField] private int rangeRingSegments = 96;

    private readonly Dictionary<Unit, TacticalWorldUnitIcon> icons = new Dictionary<Unit, TacticalWorldUnitIcon>();
    private readonly Dictionary<Unit, FieldIcon> fieldIcons = new Dictionary<Unit, FieldIcon>();
    private readonly List<IconLayoutEntry> visibleIconLayout = new List<IconLayoutEntry>();
    private readonly List<Rect> occupiedFieldRects=new();
    private readonly Dictionary<Unit,Rect> vitalPanels=new();
    private Unit[] sceneUnits;
    private uint rosterRevision;
    private readonly HashSet<Unit> visibleUnits = new();
    private readonly List<Unit> removedUnits = new();
    private Unit lastHoverFromIcon;
    private LineRenderer rangeRing;
    private Material runtimeRangeRingMaterial;

    private struct FieldIcon
    {
        public UnityEngine.UI.Outline Contour;
        public RawImage Photograph;
    }

    private struct IconLayoutEntry
    {
        public TacticalWorldUnitIcon Icon;
        public RectTransform Rect;
        public Vector2 ScreenPosition;
        public bool Exact;
    }

    internal bool OverlapsPortrait(Rect topLeftRect)
    {
        if (!iconsRoot || !iconsRoot.gameObject.activeInHierarchy) return false;
        foreach(var panel in vitalPanels.Values) if(topLeftRect.Overlaps(panel)) return true;
        foreach (var entry in visibleIconLayout)
        {
            if (!entry.Rect || !entry.Rect.gameObject.activeInHierarchy) continue;
            var half=Vector2.Scale(entry.Rect.rect.size,entry.Rect.lossyScale)*.5f;
            var c=new Vector2(entry.ScreenPosition.x,Screen.height-entry.ScreenPosition.y);
            if (topLeftRect.Overlaps(new Rect(c-half,2*half))) return true;
        }
        return false;
    }

    public bool IsConfigured()
    {
        return iconPrefab != null && iconsRoot != null;
    }

    public void SetIconsRootVisible(bool visible)
    {
        if (iconsRoot != null)
            iconsRoot.gameObject.SetActive(visible);
    }

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        if (runtimeRangeRingMaterial != null)
            Destroy(runtimeRangeRingMaterial);
    }

    private void LateUpdate()
    {
        if (CameraManager.Instance == null) return;
        if (GameManager.Instance != null && GameManager.Instance.IsPaused())
        {
            if (iconsRoot != null) iconsRoot.gameObject.SetActive(false);
            ClearHoveredIcon();
            HideAttackRangeRing();
            return;
        }
        if (GameManager.Instance != null && GameManager.Instance.IsGameOver())
        {
            SetIconsRootVisible(false);
            ClearHoveredIcon();
            HideAttackRangeRing();
            return;
        }

        bool tactical = !CameraManager.Instance.IsActionMode();
        if (!IsConfigured())
            return;

        // В фазе расстановки армии иконки не показываем — игрок видит модели юнитов.
        if (GameManager.Instance != null && GameManager.Instance.IsArmyDeploymentPhase())
        {
            if (iconsRoot != null) iconsRoot.gameObject.SetActive(false);
            ClearHoveredIcon();
            HideAttackRangeRing();
            return;
        }

        if (iconsRoot != null) iconsRoot.gameObject.SetActive(tactical);

        if (!tactical)
        {
            ClearHoveredIcon();
            HideAttackRangeRing();
            return;
        }

        if (GameManager.Instance != null && GameManager.Instance.IsBotTurn())
        {
            if (iconsRoot != null) iconsRoot.gameObject.SetActive(false);
            ClearHoveredIcon();
            HideAttackRangeRing();
            return;
        }

        Camera tacCam = CameraManager.Instance.GetTacticalCamera();
        if (tacCam == null) return;

        SyncIconsWithLivingUnits();
        UpdateIconScreenPositions(tacCam);
        UpdateAttackRangeRing(tacCam);
    }

    private void SyncIconsWithLivingUnits()
    {
        if (sceneUnits == null || rosterRevision != Unit.RosterRevision)
        {
            sceneUnits = FindObjectsByType<Unit>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            rosterRevision = Unit.RosterRevision;
        }
        visibleUnits.Clear();
        foreach (Unit u in sceneUnits)
        {
            if (u == null || !u.gameObject.activeInHierarchy || u.GetHealth() <= 0) continue;

            // PvBot fog-of-war: врагов показываем только после разведки (white), last known в gray.
            if (GameManager.Instance != null && GameManager.Instance.GetGameMode() == GameMode.PlayerVsBot &&
                u.owner == Player.Player2)
            {
                if (FogWarIntelTracker.Instance == null ||
                    !FogWarIntelTracker.Instance.ShouldShowEnemyOnTacticalMap(u))
                    continue;
            }

            visibleUnits.Add(u);
            if (!icons.ContainsKey(u))
                CreateIcon(u);
        }

        removedUnits.Clear();
        foreach (var kv in icons)
            if (!kv.Key || !visibleUnits.Contains(kv.Key))
                removedUnits.Add(kv.Key);

        foreach (Unit dead in removedUnits)
        {
            if (icons.TryGetValue(dead, out var icon) && icon) Destroy(icon.gameObject);
            icons.Remove(dead);
            fieldIcons.Remove(dead);
        }
    }

    private void CreateIcon(Unit unit)
    {
        if (iconPrefab == null || iconsRoot == null || unit == null) return;

        TacticalWorldUnitIcon icon = Instantiate(iconPrefab, iconsRoot);
        icon.Setup(unit, this);
        icons[unit] = icon;
        if (fieldPortraitAtlas != null)
            fieldIcons[unit] = BuildFieldIcon(icon, unit);
    }

    private FieldIcon BuildFieldIcon(TacticalWorldUnitIcon icon, Unit unit)
    {
        foreach (var image in icon.GetComponentsInChildren<Image>(true))
            image.enabled = false;

        var photograph = NewPlate(icon.transform, "Unit field portrait", 0f).gameObject.AddComponent<RawImage>();
        photograph.texture = fieldPortraitAtlas;
        photograph.raycastTarget = true;
        var rect = photograph.rectTransform;
        rect.anchorMin = new Vector2(.085f, 0);
        rect.anchorMax = new Vector2(.915f, 1);
        var contour = photograph.gameObject.AddComponent<UnityEngine.UI.Outline>();
        contour.effectDistance = new Vector2(1.4f,-1.4f);
        int column = unit.chessType switch
        {
            ChessUnitType.Bishop => 1,
            ChessUnitType.Guardian => 2,
            _ => 0
        };
        photograph.uvRect = new Rect(column / 3f, 0, 1f/3f, 1);
        return new FieldIcon { Contour = contour, Photograph = photograph };
    }

    private static RectTransform NewPlate(Transform parent, string name, float margin)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.one * margin;
        rect.offsetMax = Vector2.one * -margin;
        return rect;
    }

    private void RefreshFieldIcon(FieldIcon icon, Unit unit, bool exact)
    {
        bool ally = GameManager.Instance == null || unit.owner ==
            (GameManager.Instance.GetGameMode() == GameMode.PlayerVsBot ? Player.Player1 : GameManager.Instance.currentPlayer);
        bool hovered = lastHoverFromIcon == unit;
        icon.Contour.effectColor = ally
            ? new Color(.78f, .66f, .41f)
            : exact ? new Color(.69f, .35f, .24f) : new Color(.43f, .37f, .30f);
        icon.Photograph.color = ally ? new Color(1f, .98f, .88f)
            : exact ? new Color(.88f, .75f, .65f) : new Color(.58f, .54f, .48f);
        if (hovered) icon.Contour.effectColor = Color.Lerp(icon.Contour.effectColor, new Color(1f, .91f, .69f), .65f);
    }

    private void UpdateIconScreenPositions(Camera tacCam)
    {
        bool deployment = GameManager.Instance != null && GameManager.Instance.IsArmyDeploymentPhase();
        float zoomT = GetTacticalZoomT(tacCam);
        float iconSize = Mathf.Lerp(nearIconSizePixels, farIconSizePixels, zoomT);
        visibleIconLayout.Clear();

        foreach (var kv in icons)
        {
            Unit unit = kv.Key;
            TacticalWorldUnitIcon icon = kv.Value;
            if (unit == null || icon == null) continue;

            // Во время кино-показа хода бота: прячем BF-иконки для тех юнитов, чьи 3D-модели показываем.
            if (CameraManager.Instance != null && CameraManager.Instance.ShouldHideTacticalIconDuringCinematic(unit))
            {
                icon.gameObject.SetActive(false);
                if (lastHoverFromIcon == unit)
                    ClearHoveredIcon();
                continue;
            }
            else if (!icon.gameObject.activeSelf)
            {
                icon.gameObject.SetActive(true);
            }

            RectTransform rt = icon.transform as RectTransform;
            if (rt == null) continue;

            Vector3 world;
            bool exact = true;
            if (deployment && ChessGrid.Instance != null)
            {
                // В расстановке привязываем иконку строго к центру клетки,
                // иначе при высоких позициях юнита/пивота иконка визуально "уплывает" и перекрывает соседние клетки.
                Vector2Int gp = unit.currentGridPosition;
                world = ChessGrid.Instance.GridToWorldPosition(gp.x, gp.y) + Vector3.up * worldOffsetY;
            }
            else
            {
                Vector3 basePos = unit.transform.position;
                if (GameManager.Instance != null && GameManager.Instance.GetGameMode() == GameMode.PlayerVsBot &&
                    unit.owner == Player.Player2 &&
                    FogWarIntelTracker.Instance != null &&
                    FogWarIntelTracker.Instance.TryGetDisplayPosition(unit, out Vector3 displayPos, out exact))
                {
                    basePos = displayPos;
                }

                world = basePos + Vector3.up * worldOffsetY;
            }
            Vector3 screen = tacCam.WorldToScreenPoint(world);

            bool behind = hideIconsWhenBehindCamera && screen.z < 0.1f;
            icon.gameObject.SetActive(!behind);
            if (behind)
            {
                if (lastHoverFromIcon == unit)
                    ClearHoveredIcon();
                continue;
            }

            if (fieldIcons.TryGetValue(unit, out var fieldIcon)) RefreshFieldIcon(fieldIcon, unit, exact);
            else icon.RefreshVisuals();
            icon.SetSquareSize(iconSize);
            visibleIconLayout.Add(new IconLayoutEntry
            {
                Icon = icon,
                Rect = rt,
                ScreenPosition = new Vector2(screen.x, screen.y), Exact=exact
            });
        }

        foreach (IconLayoutEntry entry in visibleIconLayout)
        {
            if (entry.Icon == null || entry.Rect == null) continue;
            entry.Rect.position = new Vector3(entry.ScreenPosition.x, entry.ScreenPosition.y, 0f);
        }
        vitalPanels.Clear();
        if(!OsowiecOperation.Instance) return;
        occupiedFieldRects.Clear();
        if(OsowiecPlanner.Instance) OsowiecPlanner.Instance.ReserveFieldControls(occupiedFieldRects);
        int controls=occupiedFieldRects.Count;
        foreach(var entry in visibleIconLayout) occupiedFieldRects.Add(PortraitRect(entry));
        foreach(var entry in visibleIconLayout)
        {
            var unit=entry.Icon.TargetUnit;
            if(!unit || !entry.Exact || (unit.owner==Player.Player2 && !OsowiecOperation.Instance.SquadSees(unit))) continue;
            var anchor=PortraitRect(entry);
            if(anchor.xMin<0 || anchor.yMin<0 || anchor.xMax>Screen.width || anchor.yMax>Screen.height) continue;
            bool covered=false;
            for(int i=0;i<controls;i++) if(occupiedFieldRects[i].Contains(anchor.center)) {covered=true;break;}
            if(covered) continue;
            vitalPanels[unit]=new OsowiecMapVitals(unit).Place(anchor,occupiedFieldRects);
        }
    }

    internal bool HasVitalPanel(Unit unit) => unit && vitalPanels.ContainsKey(unit);
    static Rect PortraitRect(IconLayoutEntry entry)
    {
        Vector2 size=Vector2.Scale(entry.Rect.rect.size,entry.Rect.lossyScale);
        return new Rect(new Vector2(entry.ScreenPosition.x,Screen.height-entry.ScreenPosition.y)-size*.5f,size);
    }
    void OnGUI()
    {
        if(Event.current.type!=EventType.Repaint || !OsowiecOperation.Instance || !iconsRoot ||
            !iconsRoot.gameObject.activeInHierarchy || !CameraManager.Instance || CameraManager.Instance.IsActionMode() ||
            (OsowiecPlanner.Instance && OsowiecPlanner.Instance.SurveyOpen) ||
            !GameManager.Instance || GameManager.Instance.IsPaused() || GameManager.Instance.IsGameOver()) return;
        foreach(var entry in vitalPanels)
            if(entry.Key && (entry.Key.owner==Player.Player1 || OsowiecOperation.Instance.SquadSees(entry.Key)))
                new OsowiecMapVitals(entry.Key).Draw(entry.Value);
    }

    private float GetTacticalZoomT(Camera tacCam)
    {
        if (tacCam == null)
            return 0f;

        float zoomValue = tacCam.orthographic
            ? tacCam.orthographicSize
            : Mathf.Max(0f, tacCam.transform.position.y);

        float near = Mathf.Min(nearOrthographicSize, farOrthographicSize);
        float far = Mathf.Max(nearOrthographicSize, farOrthographicSize);
        if (Mathf.Approximately(near, far))
            return 0f;

        return Mathf.InverseLerp(near, far, zoomValue);
    }

    private void UpdateAttackRangeRing(Camera tacCam)
    {
        if (!showRangedAttackRangeOnHover)
        {
            HideAttackRangeRing();
            return;
        }

        Unit hovered = ResolveHoveredRangeUnit(tacCam);
        if (hovered == null || hovered.GetHealth() <= 0 || !hovered.TryGetRangedAttackRangeMeters(out float rangeMeters))
        {
            HideAttackRangeRing();
            return;
        }

        EnsureAttackRangeRing();
        if (rangeRing == null) return;

        rangeRing.gameObject.SetActive(true);
        rangeRing.startColor = rangeRingColor;
        rangeRing.endColor = rangeRingColor;
        rangeRing.startWidth = Mathf.Max(0.01f, rangeRingWidth);
        rangeRing.endWidth = Mathf.Max(0.01f, rangeRingWidth);

        int segments = Mathf.Clamp(rangeRingSegments, 24, 256);
        rangeRing.loop = true;
        rangeRing.positionCount = segments;

        Vector3 center = hovered.transform.position + Vector3.up * rangeRingYOffset;
        for (int i = 0; i < segments; i++)
        {
            float t = i / (float)segments;
            float angle = t * Mathf.PI * 2f;
            Vector3 p = center + new Vector3(Mathf.Cos(angle) * rangeMeters, 0f, Mathf.Sin(angle) * rangeMeters);
            rangeRing.SetPosition(i, p);
        }
    }

    private Unit ResolveHoveredRangeUnit(Camera tacCam)
    {
        if (lastHoverFromIcon != null && IsValidRangeHoverUnit(lastHoverFromIcon))
            return lastHoverFromIcon;
        lastHoverFromIcon = null;

        if (tacCam == null || Mouse.current == null)
            return null;

        Vector2 mousePosition = Mouse.current.position.ReadValue();
        Ray ray = tacCam.ScreenPointToRay(mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 1000f, ~0, QueryTriggerInteraction.Ignore))
            return null;

        Unit unit = hit.collider != null ? hit.collider.GetComponentInParent<Unit>() : null;
        if (unit == null && hit.collider != null)
            unit = hit.collider.GetComponent<Unit>();

        return IsValidRangeHoverUnit(unit) ? unit : null;
    }

    private static bool IsValidRangeHoverUnit(Unit unit)
    {
        if (unit == null || unit.GetHealth() <= 0)
            return false;

        if (GameManager.Instance != null && unit.owner != GameManager.Instance.currentPlayer)
            return false;

        return unit.TryGetRangedAttackRangeMeters(out _);
    }

    private void ClearHoveredIcon()
    {
        lastHoverFromIcon = null;
    }

    private void EnsureAttackRangeRing()
    {
        if (rangeRing != null) return;

        GameObject go = new GameObject("TacticalAttackRangeRing");
        go.transform.SetParent(transform, false);

        rangeRing = go.AddComponent<LineRenderer>();
        rangeRing.useWorldSpace = true;
        rangeRing.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rangeRing.receiveShadows = false;
        rangeRing.textureMode = LineTextureMode.Stretch;
        rangeRing.alignment = LineAlignment.View;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");

        if (shader != null)
        {
            runtimeRangeRingMaterial = new Material(shader);
            runtimeRangeRingMaterial.color = rangeRingColor;
            rangeRing.material = runtimeRangeRingMaterial;
        }

        go.SetActive(false);
    }

    private void HideAttackRangeRing()
    {
        if (rangeRing != null)
            rangeRing.gameObject.SetActive(false);
    }

    public void NotifyIconClicked(Unit unit)
    {
        if (CameraManager.Instance != null)
            CameraManager.Instance.TrySwitchToActionModeFromMap(unit);
    }

    public void NotifyIconPointerEnter(Unit unit)
    {
        lastHoverFromIcon = unit;
    }

    public void NotifyIconPointerExit(Unit unit)
    {
        if (lastHoverFromIcon == unit)
            lastHoverFromIcon = null;
    }

    public Unit GetHoveredIconUnit() => lastHoverFromIcon;
}
