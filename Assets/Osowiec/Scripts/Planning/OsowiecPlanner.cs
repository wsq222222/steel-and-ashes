using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public sealed class OsowiecPlanner : MonoBehaviour
{
    public Texture2D portraits;
    public Texture2D survey;
    bool unfolded;
    internal bool SurveyOpen => unfolded && survey;
    Texture2D fieldTable;
    Unit[] battleRoster=System.Array.Empty<Unit>();
    float rosterAt;
    public static OsowiecPlanner Instance { get; private set; }
    public bool AcceptingOrder { get; private set; }
    Unit selected;
    Unit orderedUnit;
    Vector3 goal;
    Vector3[] path;
    float length;
    float requestedLength;
    float previewAllowance;
    Vector3[] activeRoute;
    int activeCorner;
    readonly Dictionary<Unit, Vector3> destinations = new();
    readonly List<Rect> paperConditions = new();
    bool crossesGas;
    bool lastGasState;
    public Unit SelectedUnit => selected;
    public float PlannedMetres => path != null ? length : 0;
    OsowiecRouteInk ink;
    Vector3[] fullRoute;
    public Vector3 OrderEndpoint => path!=null ? path[^1] : goal;
    public float RequestedMetres => requestedLength;
    internal Vector3[] ReachableCorners => path;
    internal Vector3[] RequestedCorners => fullRoute;

    internal static float FullBudget(Unit unit) => unit.chessType switch
    {
        ChessUnitType.Hammer => 15,
        ChessUnitType.Hound => 24,
        _ => 18
    };

    public static float MoveBudget(Unit unit) => FullBudget(unit);
    static float PlanningAllowance(Unit unit) => OsowiecMarch.Free(unit) ? float.PositiveInfinity : MoveBudget(unit);
    internal static void NoteAttack(Unit actor)
    {
        if (actor && actor.owner == Player.Player1) actor.FieldTurn.Attack();
    }

    void Awake()
    {
        Instance = this;
        fieldTable=Resources.Load<Texture2D>("OsowiecJournal/table");
        ink = gameObject.AddComponent<OsowiecRouteInk>();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Select(Unit unit)
    {
        selected = unit;
        ClearPreview();
        if (!unit || !destinations.TryGetValue(unit, out var destination)) return;
        if (Reached(unit, destination)) destinations.Remove(unit);
        else Preview(destination);
    }

    static bool Reached(Unit unit, Vector3 point) => Vector3.Distance(unit.transform.position, point) <= .8f;

    void ClearPreview()
    {
        path = null;
        fullRoute = null; requestedLength=0; length=0;
        ink.Clear();
    }

    bool CanPlan()
    {
        var game = GameManager.Instance;
        return game != null && !game.IsArmyDeploymentPhase() && !game.IsPaused() &&
            !game.IsGameOver() && !game.IsBotTurn() && CameraManager.Instance != null &&
            !CameraManager.Instance.IsActionMode();
    }

    void Update()
    {
        if (orderedUnit) OsowiecMarch.UpdateContact(orderedUnit);
        if (orderedUnit && activeRoute != null && Reached(orderedUnit, goal))
        {
            activeRoute = null;
            destinations.Remove(orderedUnit);
        }
        bool planning=CanPlan();
        ink.Display(planning ? CameraManager.Instance.GetTacticalCamera() : null,planning && !unfolded);
        if (!planning) return;
        if (selected && fullRoute != null && PlanningAllowance(selected) != previewAllowance)
            Preview(fullRoute[^1]);
        if(lastGasState!=OsowiecOperation.BankClosed)
        {
            lastGasState=OsowiecOperation.BankClosed;
            if(fullRoute!=null)
            {
                crossesGas=false;
                for(int i=1;i<fullRoute.Length;i++)
                    crossesGas|=OsowiecOperation.GasOnSegment(fullRoute[i-1],fullRoute[i]);
            }
        }
        var mouse = Mouse.current;
        var keys = Keyboard.current;
        if (keys != null && keys.mKey.wasPressedThisFrame && survey != null) unfolded = !unfolded;
        if (keys != null)
        {
            int slot = keys.digit1Key.wasPressedThisFrame ? 0 : keys.digit2Key.wasPressedThisFrame ? 1 :
                keys.digit3Key.wasPressedThisFrame ? 2 : -1;
            if (slot >= 0)
            {
                var roster = System.Array.FindAll(FindObjectsByType<Unit>(FindObjectsSortMode.InstanceID),
                    unit => unit.owner == Player.Player1 && unit.GetHealth() > 0);
                if (slot < roster.Length) Select(roster[slot]);
            }
        }
        if (unfolded)
        {
            if(Time.unscaledTime>=rosterAt)
            {
                battleRoster=System.Array.FindAll(FindObjectsByType<Unit>(FindObjectsSortMode.InstanceID),u=>u.owner==Player.Player1 && u.GetHealth()>0);
                rosterAt=Time.unscaledTime+.35f;
            }
            if(keys!=null && keys.enterKey.wasPressedThisFrame){ ConfirmOrder(); return; }
            if(keys!=null && keys.fKey.wasPressedThisFrame && selected && OsowiecOperation.Instance)
                Preview(OsowiecOperation.Instance.NextObjective);
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                var cursor=mouse.position.ReadValue();
                cursor.y=Screen.height-cursor.y;
                SurveyRects(out _,out var area);
                if(area.Contains(cursor) && !ConfirmRect().Contains(cursor) && !OrderDock().Contains(cursor))
                {
                    foreach(var unit in battleRoster)
                    {
                        if(unit && unit.GetHealth()>0 && Vector2.Distance(cursor,PaperPoint(area,unit.transform.position))<12*OsowiecHudPaint.Scale)
                        { Select(unit); return; }
                    }
                    if(selected) Preview(OsowiecSurveyProjection.Current.World(area,cursor));
                }
            }
            return;
        }
        if (selected == null || selected.GetHealth() <= 0) return;
        if (keys != null && keys.fKey.wasPressedThisFrame && OsowiecOperation.Instance != null)
            Preview(OsowiecOperation.Instance.NextObjective);
        if (mouse != null && mouse.leftButton.wasPressedThisFrame &&
            !(EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()))
        {
            var camera = CameraManager.Instance.GetTacticalCamera();
            Vector2 mousePosition = mouse.position.ReadValue();
            mousePosition.y = Screen.height - mousePosition.y;
            if (camera != null && !OrderDock().Contains(mousePosition) && !ConfirmRect().Contains(mousePosition) &&
                PickWalkable(camera.ScreenPointToRay(mouse.position.ReadValue()),out var coordinate))
                Preview(coordinate);
        }
        if (keys != null && keys.enterKey.wasPressedThisFrame) ConfirmOrder();
    }

    static bool PickWalkable(Ray ray,out Vector3 point)
    {
        point=default;
        // A solid wall or visible roof occludes the floor behind it. A selected actor is transparent to picking.
        var hits=Physics.RaycastAll(ray,180,~0,QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits,(a,b)=>a.distance.CompareTo(b.distance));
        foreach(var hit in hits)
        {
            if(hit.collider.GetComponentInParent<Unit>() || OsowiecFortCutaway.HiddenForPlanning(hit.collider)) continue;
            if(hit.normal.y<.55f || !NavMesh.SamplePosition(hit.point,out var nav,.65f,NavMesh.AllAreas)) return false;
            point=nav.position;
            return true;
        }
        return false;
    }

    internal void Preview(Vector3 point)
    {
        if (!selected) return;
        var proposal = new NavMeshPath();
        if (!NavMesh.SamplePosition(selected.transform.position, out var start, 1.5f, NavMesh.AllAreas) ||
            !NavMesh.SamplePosition(point, out var end, 1.5f, NavMesh.AllAreas) ||
            !NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, proposal) ||
            proposal.status != NavMeshPathStatus.PathComplete || proposal.corners.Length < 2)
            return;

        ClearPreview();
        crossesGas = false;
        fullRoute = proposal.corners;
        path = (Vector3[])fullRoute.Clone();
        for (int index = 1; index < path.Length; index++)
        {
            Vector3 segment = path[index] - path[index - 1];
            length += segment.magnitude;
            crossesGas |= OsowiecOperation.GasOnSegment(path[index-1],path[index]);
        }
        requestedLength = length;
        previewAllowance = PlanningAllowance(selected);
        if (length > previewAllowance)
        {
            float left = previewAllowance - .18f;
            for (int i = 1; i < path.Length; i++)
            {
                float segmentLength = Vector3.Distance(path[i - 1], path[i]);
                if (left <= segmentLength)
                {
                    path[i] = Vector3.MoveTowards(path[i - 1], path[i], left);
                    System.Array.Resize(ref path, i + 1);
                    length = previewAllowance - .18f;
                    break;
                }
                left -= segmentLength;
            }
        }
        ink.Set(fullRoute,length);
    }

    internal void ConfirmOrder()
    {
        if (!CanPlan() || selected == null || selected.GetHealth() <= 0)
            return;
        unfolded=false;
        AcceptingOrder = true;
        try
        {
            CameraManager.Instance.TrySwitchToActionModeFromMap(selected);
            if (CameraManager.Instance.GetCurrentControlledUnit() != selected || !CameraManager.Instance.IsActionMode()) return;
            orderedUnit = selected;
            orderedUnit.FieldTurn.Activated = true;
            OsowiecMarch.UpdateContact(orderedUnit);
            activeRoute = fullRoute;
            if (activeRoute != null)
            {
                goal = activeRoute[^1];
                destinations[orderedUnit] = goal;
                activeCorner = 1;
            }
        }
        finally { AcceptingOrder = false; }
        Select(null);
    }

    void OnGUI()
    {
        var cameras = CameraManager.Instance;
        if (cameras != null && cameras.IsActionMode() && orderedUnit != null && activeRoute != null &&
            cameras.GetCurrentControlledUnit() == orderedUnit && !GameManager.Instance.IsPaused())
        {
            var position = orderedUnit.transform.position;
            while (activeCorner < activeRoute.Length - 1 &&
                Vector3.Distance(position, activeRoute[activeCorner]) < 1.55f) activeCorner++;
            Vector3 next = activeRoute[activeCorner];
            float remaining = Vector3.Distance(position, next);
            for (int i = activeCorner + 1; i < activeRoute.Length; i++)
                remaining += Vector3.Distance(activeRoute[i-1], activeRoute[i]);
            Vector3 target = cameras.GetActionCamera().WorldToScreenPoint(next + Vector3.up * .45f);
            if (target.z > 0 && target.x > 0 && target.x < Screen.width && target.y > 0 && target.y < Screen.height)
            {
                OsowiecMapMarkers.Waypoint(new Vector2(target.x,Screen.height-target.y),remaining);
            }
        }
        if (!CanPlan()) return;
        if (unfolded && survey != null)
        {
            DrawSurvey();
            return;
        }
        DrawOrderDock();
    }

    internal static void SurveyRects(out Rect sheet,out Rect area)
    {
        float width=Mathf.Min(Screen.width*.90f,Screen.height*1.45f);
        var projection=OsowiecSurveyProjection.Current;
        float height=width/projection.Aspect;
        sheet=new Rect((Screen.width-width)/2,(Screen.height-height)/2,width,height);
        area=projection.Scaled(sheet,projection.plot);
    }

    static Vector2 PaperPoint(Rect area,Vector3 world)
    {
        return OsowiecSurveyProjection.Current.Point(area,world);
    }

    static void Stroke(Vector2 a,Vector2 b,float width,Color color)
    {
        var vector=b-a;
        if(vector.sqrMagnitude<.01f)return;
        var previous=GUI.matrix;
        GUIUtility.RotateAroundPivot(Mathf.Atan2(vector.y,vector.x)*Mathf.Rad2Deg,a);
        OsowiecHudPaint.Fill(new Rect(a.x,a.y-width*.5f,vector.magnitude,width),color);
        GUI.matrix=previous;
    }

    void DrawSurvey()
    {
        SurveyRects(out var sheet,out var area);
        if(fieldTable)GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height),fieldTable,ScaleMode.ScaleAndCrop);
        GUI.DrawTexture(sheet,survey);
        float scale=OsowiecHudPaint.Scale;
        var column=OsowiecOperation.Instance?OsowiecOperation.Instance.relief:null;
        if(column && column.MistVisible)
        {
            var cloud=OsowiecOperation.NorthBankMist;
            Vector2 top=PaperPoint(area,new Vector3(cloud.min.x,0,cloud.max.z)),bottom=PaperPoint(area,new Vector3(cloud.max.x,0,cloud.min.z));
            OsowiecHudPaint.Fill(Rect.MinMaxRect(top.x,top.y,bottom.x,bottom.y),new Color(.45f,.42f,.09f,column.MistActive?.62f:.28f));
            OsowiecHudPaint.Text(new Rect(top.x,top.y-19*scale,150*scale,20*scale),column.MistActive?"Газ на насыпи":"Порыв ветра",13,new Color(.30f,.17f,.07f),true);
        }
        float walked=0;
        if(fullRoute!=null)
        {
            for(int i=1;i<fullRoute.Length;i++)
            {
                var a=fullRoute[i-1]; var b=fullRoute[i]; float size=Vector3.Distance(a,b);
                if(size<.001f) continue;
                float solid=Mathf.Clamp(length-walked,0,size);
                if(solid>0) Stroke(PaperPoint(area,a),PaperPoint(area,Vector3.Lerp(a,b,solid/size)),4*scale,new Color(.23f,.26f,.11f));
                for(float m=solid;m<size;m+=1.15f)
                    Stroke(PaperPoint(area,Vector3.Lerp(a,b,m/size)),PaperPoint(area,Vector3.Lerp(a,b,Mathf.Min(size,m+.62f)/size)),3*scale,new Color(.52f,.20f,.13f));
                walked+=size;
            }
            var halt=PaperPoint(area,path[^1]); var target=PaperPoint(area,fullRoute[^1]);
            OsowiecHudPaint.Fill(new Rect(target.x-3*scale,target.y-3*scale,6*scale,6*scale),new Color(.50f,.13f,.08f));
            OsowiecHudPaint.Fill(new Rect(halt.x-4*scale,halt.y-4*scale,8*scale,8*scale),new Color(.18f,.22f,.09f));
        }
        paperConditions.Clear();
        var projection=OsowiecSurveyProjection.Current;
        foreach(var caption in projection.captions)paperConditions.Add(projection.Scaled(sheet,caption));
        foreach(var unit in battleRoster)
        {
            if(!unit || unit.GetHealth()<=0)continue;
            var pin=PaperPoint(area,unit.transform.position);
            paperConditions.Add(new Rect(pin.x-9*scale,pin.y-9*scale,18*scale,18*scale));
        }
        foreach(var unit in battleRoster)
        {
            if(!unit || unit.GetHealth()<=0)continue;
            var pin=PaperPoint(area,unit.transform.position);
            var paper=new Color(.85f,.76f,.55f);
            OsowiecHudPaint.Fill(new Rect(pin.x-9*scale,pin.y-9*scale,18*scale,18*scale),unit==selected?new Color(.55f,.19f,.12f):new Color(.23f,.25f,.13f));
            OsowiecHudPaint.Fill(new Rect(pin.x-3*scale,pin.y-3*scale,6*scale,6*scale),paper);
            var vitals=new OsowiecMapVitals(unit);
            vitals.Draw(vitals.Place(new Rect(pin.x-9*scale,pin.y-9*scale,18*scale,18*scale),paperConditions));
        }
        DrawOrderDock();
    }

    static Rect OrderDock()
    {
        float scale = OsowiecHudPaint.Scale;
        return new Rect(26 * scale, Screen.height - 141 * scale,
            Mathf.Min(565 * scale, Screen.width - 52 * scale), 116 * scale);
    }

    static Rect ConfirmRect()
    {
        float scale = OsowiecHudPaint.Scale;
        return new Rect(Screen.width - 310 * scale, Screen.height - 76 * scale, 282 * scale, 48 * scale);
    }

    internal void ReserveFieldControls(List<Rect> occupied)
    {
        if (!CanPlan()) return;
        occupied.Add(OrderDock());
        if (selected && selected.GetHealth()>0) occupied.Add(ConfirmRect());
    }

    static Texture2D orderPaper;
    static void OsowiecActionReadoutPaper(Rect area)
    {
        if(!orderPaper) orderPaper=Resources.Load<Texture2D>("OsowiecJournal/journal");
        OsowiecHudPaint.Fill(new Rect(area.x+2,area.y+3,area.width,area.height),new Color(.06f,.05f,.04f,.66f));
        if(orderPaper) GUI.DrawTextureWithTexCoords(area,orderPaper,new Rect(.15f,.24f,.29f,.48f));
        else OsowiecHudPaint.Fill(area,new Color(.84f,.76f,.60f));
        OsowiecHudPaint.Fill(new Rect(area.x,area.y,4*OsowiecHudPaint.Scale,area.height),new Color(.47f,.19f,.12f));
    }

    void DrawOrderDock()
    {
        var dock = OrderDock();
        float scale = OsowiecHudPaint.Scale;
        var brass = new Color(.80f, .67f, .43f);
        var paper = new Color(.94f, .90f, .80f);
        var muted = new Color(.71f, .73f, .62f);
        var safe = new Color(.61f, .73f, .47f);
        var warning = new Color(.92f, .49f, .37f);
        {
            OsowiecActionReadoutPaper(dock);
            brass=new Color(.41f,.18f,.11f); paper=new Color(.16f,.13f,.085f);
            muted=new Color(.33f,.29f,.19f); safe=new Color(.23f,.34f,.16f);warning=new Color(.57f,.19f,.11f);
        }

        float x = dock.x + 14 * scale;
        if (selected != null && selected.GetHealth() > 0 && portraits != null)
        {
            int column = selected.chessType switch { ChessUnitType.Hammer => 0, ChessUnitType.Cyclops => 2, _ => 1 };
            GUI.DrawTextureWithTexCoords(new Rect(x, dock.y + 13 * scale, 88 * scale, 88 * scale), portraits,
                new Rect(column / 3f, 0, 1 / 3f, 1));
            x += 103 * scale;
        }
        float width = dock.xMax - x - 14 * scale;
        bool hasUnit = selected != null && selected.GetHealth() > 0;
        float budget = hasUnit ? MoveBudget(selected) : 0;
        bool free = hasUnit && OsowiecMarch.Free(selected);
        Color status = crossesGas && path != null ? warning : safe;
        var condition=hasUnit?new OsowiecMapVitals(selected):default;
        OsowiecHudPaint.Text(new Rect(x,dock.y+11*scale,width,21*scale),
            hasUnit?condition.Summary:"ТАКТИЧЕСКИЙ ПРИКАЗ",15,hasUnit && condition.Critical?warning:brass,true);
        string title = !hasUnit ? "Выберите бойца на карте" : selected.GetLocalizedDisplayName();
        OsowiecHudPaint.Text(new Rect(x, dock.y + 34 * scale, width, 31 * scale), title, 24, paper, true);
        string detail = !hasUnit ? "ЛКМ или 1–3 — боец     M — бумажный план" :
            path == null ? (free ? "Марш" : $"Запас сил на ход {budget:F1} м") + "   ·   ЛКМ — точка     F — к цели" :
            $"Путь {requestedLength:F1} м   ·   " + (free ? "Свободный марш" : $"В этот ход {length:F1} м") +
            (crossesGas ? "   ·   Газ на маршруте" : "");
        OsowiecHudPaint.Text(new Rect(x, dock.y + 67 * scale, width, 22 * scale), detail, 17,
            path == null ? muted : status);
        if (hasUnit && fullRoute!=null && length + .02f < requestedLength)
            OsowiecHudPaint.Text(new Rect(dock.x+12*scale,dock.y-26*scale,dock.width,24*scale),
                "○  Стоп хода     ◇  Цель     ┄  Следующий ход",15,paper,true);
        if (hasUnit && !free)
            OsowiecHudPaint.Meter(new Rect(x, dock.y + 96 * scale, width, 4 * scale),
                path == null ? 0 : length / budget, status);

        if (hasUnit && ConfirmRect().xMin > dock.xMax &&
            OsowiecHudPaint.OrderButton(ConfirmRect(), path == null ? "К бойцу" : "Передать приказ"))
            ConfirmOrder();
    }
}
