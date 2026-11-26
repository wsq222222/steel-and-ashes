using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

// Explicit player QA: a virtual mouse keeps the desktop pointer and focus untouched.
public sealed class OsowiecMapCheck : MonoBehaviour
{
    private string output;
    private Mouse pointer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        var args = Environment.GetCommandLineArgs();
        int flag = Array.IndexOf(args, "-osowiec-map-check");
        if (Application.isEditor || flag < 0 || flag + 1 >= args.Length) return;
        var host = new GameObject("Tactical map HUD verification");
        DontDestroyOnLoad(host);
        host.AddComponent<OsowiecMapCheck>().output = args[flag + 1];
    }

    private void OnEnable() => Application.logMessageReceived += OnLog;
    private void OnDisable() => Application.logMessageReceived -= OnLog;

    private void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Exception) Application.Quit(2);
    }

    private void MovePointer(Vector2 guiPoint)
    {
        var screenPoint = new Vector2(guiPoint.x, Screen.height - guiPoint.y);
        InputSystem.QueueStateEvent(pointer, new MouseState { position = screenPoint });
    }

    private IEnumerator AwaitHover(OsowiecMapMarkers markers, bool hasTooltip)
    {
        float deadline = Time.realtimeSinceStartup + 4;
        do
        {
            yield return new WaitForEndOfFrame();
            if (!string.IsNullOrEmpty(markers.HoverTitle) == hasTooltip) yield break;
            yield return null;
        } while(Time.realtimeSinceStartup < deadline);
        throw new InvalidOperationException($"Hover state timed out at {pointer.position.ReadValue()}; title '{markers.HoverTitle}'.");
    }

    private void SaveFrame(string label) => SaveFrame(output,label);

    internal static void SaveFrame(string directory,string label)
    {
        var texture = ScreenCapture.CaptureScreenshotAsTexture();
        try
        {
            var pixels=texture.GetPixels32();
            int darkest=765, brightest=0;
            for(int i=0;i<pixels.Length;i+=997)
            {
                var pixel=pixels[i];
                if(pixel.a==0)continue;
                int value=pixel.r+pixel.g+pixel.b;
                darkest=Math.Min(darkest,value);brightest=Math.Max(brightest,value);
            }
            if(brightest-darkest<36) throw new InvalidOperationException("The graphical player returned a blank frame: "+label);
            File.WriteAllBytes(Path.Combine(directory,label+".png"),texture.EncodeToPNG());
        }
        finally { Destroy(texture); }
    }

    internal static IEnumerator ApplyCaptureSize()
    {
        var args=Environment.GetCommandLineArgs();
        int w=Array.IndexOf(args,"-screen-width"),h=Array.IndexOf(args,"-screen-height");
        if(w<0 || h<0 || w+1>=args.Length || h+1>=args.Length ||
           !int.TryParse(args[w+1],out int width) || !int.TryParse(args[h+1],out int height))yield break;
        if(Screen.width==width && Screen.height==height)yield break;
        Screen.SetResolution(width,height,FullScreenMode.Windowed);
        for(int i=0;i<45 && (Screen.width!=width || Screen.height!=height);i++)yield return null;
        if(Screen.width!=width || Screen.height!=height)throw new InvalidOperationException("Requested capture size not applied.");
    }

    private IEnumerator Start()
    {
        Application.runInBackground = true;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        pointer = InputSystem.AddDevice<Mouse>("Map QA pointer");
        pointer.MakeCurrent();
        GameManager.SetGameMode(GameMode.PlayerVsBot);
        SceneManager.LoadScene("Osowiec");
        for(int i=0;i<8;i++) yield return null;
        var args=Environment.GetCommandLineArgs();
        yield return ApplyCaptureSize();
        output = Path.Combine(output,$"Карта-{Screen.width}x{Screen.height}");
        Directory.CreateDirectory(output);
        var placement = FindFirstObjectByType<ArmyDeploymentController>();
        if(Array.IndexOf(args,"-osowiec-deployment-sheet")>=0)
        {
            yield return CheckSurveySheet(placement);
            InputSystem.RemoveDevice(pointer);Application.Quit(0);yield break;
        }
        for(int slot=0;slot<3;slot++)
        {
            int area = slot==0 ? 0 : 1;
            if(!placement.TryPlaceOfferAtGridCell(slot,OsowiecDeploymentSectors.Cells[area][slot]))
                throw new InvalidOperationException($"Could not place soldier {slot} in sector {area}");
        }
        FindFirstObjectByType<OsowiecPaperPlacement>().Confirm();
        for(int i=0;i<6;i++) yield return null;
        yield return new WaitForSecondsRealtime(1f);
        var planner = OsowiecPlanner.Instance;
        var soldier = FindObjectsByType<Unit>(FindObjectsSortMode.InstanceID).First(u=>u.owner==Player.Player1);
        planner.Select(soldier);
        var camera = CameraManager.Instance.GetTacticalCamera();
        if (!camera.enabled || CameraManager.Instance.IsActionMode() || GameManager.Instance.IsArmyDeploymentPhase())
            throw new InvalidOperationException("Tactical camera inactive after confirming deployment");
        if(Array.IndexOf(args,"-osowiec-vitals-check")>=0)
        {
            yield return CheckVitals(planner);
            InputSystem.RemoveDevice(pointer);
            Application.Quit(0);
            yield break;
        }
        Vector2 background = new(Screen.width * .47f,Screen.height * .16f);
        MovePointer(background);
        for(int i=0;i<3;i++) yield return null;
        var markers = OsowiecOperation.Instance.MapMarkers;
        yield return AwaitHover(markers,false);
        SaveFrame("01-карта");
        OsowiecMapSymbol[] symbols = {OsowiecMapSymbol.Phone,OsowiecMapSymbol.Gas,
            OsowiecMapSymbol.Gate,OsowiecMapSymbol.Battery};
        var titles = new HashSet<string>();
        var rectangles = new List<Rect>();
        foreach(var symbol in symbols)
        {
            if (!markers.TryGetRect(symbol,out Rect target))
                throw new InvalidOperationException($"Marker missing for {symbol}.");
            if (target.width<=0 || target.height<=0 || target.width>Screen.height*.09f ||
                target.height>Screen.height*.09f || !new Rect(0,0,Screen.width,Screen.height).Contains(target.center))
                throw new InvalidOperationException($"Marker {symbol} is not compact or lies outside the frame: {target}");
            if (rectangles.Any(r=>r.Overlaps(target)))
                throw new InvalidOperationException($"Marker {symbol} overlaps another point of interest: {target}.");
            rectangles.Add(target);
            MovePointer(target.center);
            yield return AwaitHover(markers,true);
            string title=markers.HoverTitle;
            if (!titles.Add(title)) throw new InvalidOperationException($"Hover failed to change to {symbol}: {title}");
            if (symbol==OsowiecMapSymbol.Gas) SaveFrame("02-подсказка");
            MovePointer(background);
            yield return AwaitHover(markers,false);
            if(planner.SelectedUnit!=soldier) throw new InvalidOperationException("Inspecting a site changed the selected soldier");
        }
        var spawned = Instantiate(soldier);
        spawned.transform.position = soldier.transform.position + Vector3.back * 2;
        for(int i=0;i<3;i++) yield return null;
        bool HasIcon(Unit unit) => FindObjectsByType<TacticalWorldUnitIcon>(FindObjectsInactive.Include,FindObjectsSortMode.None)
            .Any(icon=>icon.TargetUnit==unit);
        if(!HasIcon(spawned)) throw new InvalidOperationException("Spawned soldier did not receive a map icon.");
        spawned.gameObject.SetActive(false);
        for(int i=0;i<3;i++) yield return null;
        if(HasIcon(spawned)) throw new InvalidOperationException("Inactive soldier still has an icon.");
        spawned.gameObject.SetActive(true);
        for(int i=0;i<3;i++) yield return null;
        if(!HasIcon(spawned)) throw new InvalidOperationException("Reactivated soldier lost its map icon.");
        Destroy(spawned.gameObject);
        for(int i=0;i<3;i++) yield return null;
        var intel=FogWarIntelTracker.Instance;
        foreach(var unit in FindObjectsByType<Unit>(FindObjectsSortMode.None))
            if(unit.owner==Player.Player2 && unit.GetHealth()>0 && HasIcon(unit)!=(intel && intel.ShouldShowEnemyOnTacticalMap(unit)))
                throw new InvalidOperationException("Enemy icon visibility disagrees with reconnaissance.");
        yield return CheckRoleViews(planner);
        string report=$"PASS tactical map {Screen.width}x{Screen.height} build={Application.buildGUID}. "+
            $"Four distinct hover titles, four compact non-overlapping markers, selected soldier unchanged; spawn, deactivate, reactivate and enemy visibility passed. "+
            $"Sizes {string.Join(", ",rectangles.Select(r=>$"{r.width:F0}×{r.height:F0}"))}. "+
            string.Join(" / ",titles);
        File.WriteAllText(Path.Combine(output,"Проверка.txt"),report);
        Debug.Log(report);
        InputSystem.RemoveDevice(pointer);
        Application.Quit(0);
    }

    IEnumerator CheckSurveySheet(ArmyDeploymentController placement)
    {
        var roster=FindFirstObjectByType<OsowiecPaperPlacement>();
        var desk=roster.transform.Find("Deployment survey/Survey desk");
        var map=(RectTransform)desk.Find("Plan of the operation");
        var projection=OsowiecSurveyProjection.Current;
        var events=UnityEngine.EventSystems.EventSystem.current;
        void Click(Transform target,bool remove,int expected)
        {
            var pos=RectTransformUtility.WorldToScreenPoint(null,target.position);
            var data=new UnityEngine.EventSystems.PointerEventData(events){position=pos,
                button=remove?UnityEngine.EventSystems.PointerEventData.InputButton.Right:UnityEngine.EventSystems.PointerEventData.InputButton.Left};
            var hits=new List<UnityEngine.EventSystems.RaycastResult>();events.RaycastAll(data,hits);
            var receiver=hits.Count>0?hits[0].gameObject.GetComponentInParent<OsowiecPaperSlot>():null;
            if(!receiver || receiver.index!=expected)throw new InvalidOperationException("Covered deployment cell "+target.name+" at "+pos);
            UnityEngine.EventSystems.ExecuteEvents.Execute(receiver.gameObject,data,UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
        }
        Canvas.ForceUpdateCanvases();roster.SelectOffer(0);
        yield return new WaitForEndOfFrame();
        var chartCorners=new Vector3[4];map.GetWorldCorners(chartCorners);
        var rosterCorners=new Vector3[4];((RectTransform)desk.Find("Personnel roll")).GetWorldCorners(rosterCorners);
        var page=(RectTransform)desk;
        var pageCorners=new Vector3[4];page.GetWorldCorners(pageCorners);
        float referenceWidth=Mathf.Min(Screen.width,Screen.height*(1920f/1080f),page.rect.width*Mathf.Abs(page.parent.lossyScale.x));
        if(pageCorners[2].x-pageCorners[0].x<referenceWidth*.97f)
            throw new InvalidOperationException("Deployment table did not grow to the available viewport.");
        if(chartCorners[0].x<=rosterCorners[2].x || chartCorners[2].x>Screen.width ||
            chartCorners[0].y<0 || chartCorners[2].y>Screen.height)
            throw new InvalidOperationException($"Deployment map {chartCorners[0]:F1} to {chartCorners[2]:F1}, roster ends at {rosterCorners[2]:F1}");
        int per=OsowiecDeploymentSectors.Cells[0].Length;
        for(int i=0;i<per*OsowiecDeploymentSectors.Cells.Length;i++)
        {
            var cell=OsowiecDeploymentSectors.Cells[i/per][i%per];
            var location=ChessGrid.Instance.GridToWorldPosition(cell.x,cell.y);
            var pin=(RectTransform)map.Find("Survey cell "+i);
            if(Vector2.Distance(pin.anchorMin,projection.Anchor(location))>.00005f)
                throw new InvalidOperationException("Geographic cell moved from its physical spawn "+cell);
            foreach(var hit in new Transform[]{pin,desk.Find("Position "+i)})
            {
                Click(hit,false,i);
                if(!placement.DeployedAt(cell))throw new InvalidOperationException("Did not deploy on "+cell);
                Click(hit,true,i);
                if(placement.DeployedAt(cell))throw new InvalidOperationException("Did not clear "+cell);
            }
        }
        Click(map.Find("Survey cell 0"),false,0);
        roster.SelectOffer(1);Click(desk.Find("Position 4"),false,4);
        yield return new WaitForEndOfFrame();SaveFrame("01-расстановка");
        roster.Confirm();for(int i=0;i<4;i++)yield return null;
        var planner=OsowiecPlanner.Instance;
        var unit=FindObjectsByType<Unit>(FindObjectsSortMode.None).First(u=>u.owner==Player.Player1 && u.chessType==ChessUnitType.Pawn);
        planner.Select(unit);
        var keys=InputSystem.AddDevice<Keyboard>();keys.MakeCurrent();
        InputSystem.QueueStateEvent(keys,new KeyboardState(Key.M));yield return null;yield return null;
        InputSystem.QueueStateEvent(keys,new KeyboardState());
        if(!planner.SurveyOpen)throw new InvalidOperationException("Unfolding the field sheet failed");
        OsowiecPlanner.SurveyRects(out _,out var plot);
        var point=projection.Point(plot,OsowiecOperation.Telephone);
        var screen=new Vector2(point.x,Screen.height-point.y);
        InputSystem.QueueStateEvent(pointer,new MouseState{position=screen,buttons=1});yield return null;yield return null;
        InputSystem.QueueStateEvent(pointer,new MouseState{position=screen});yield return null;
        var requested=planner.RequestedCorners;
        if(requested==null || Vector3.ProjectOnPlane(requested[^1]-OsowiecOperation.Telephone,Vector3.up).magnitude>.55f)
            throw new InvalidOperationException("The paper order did not reach the printed phone.");
        yield return new WaitForEndOfFrame();SaveFrame("02-бумажный-план");
        InputSystem.RemoveDevice(keys);
        Debug.Log($"PASS deployment sheet {Screen.width}x{Screen.height} {Application.buildGUID}: six surveyed and six enlarged buttons, placement/removal, two sectors and real paper order to the phone.");
    }
    private IEnumerator CheckVitals(OsowiecPlanner planner)
    {
        var op=OsowiecOperation.Instance;
        var units=op.FieldRoster.Where(u=>u && u.isActiveAndEnabled && u.GetHealth()>0).ToArray();
        var allies=units.Where(u=>u.owner==Player.Player1).OrderBy(u=>u.chessType).ToArray();
        var enemy=units.First(u=>u.owner==Player.Player2);
        foreach(var u in units) if(u.owner==Player.Player2 && u!=enemy) u.gameObject.SetActive(false);
        var keys=InputSystem.AddDevice<Keyboard>();
        planner.Select(allies[1]);
        planner.Preview(allies[1].transform.position+Vector3.right);
        planner.ConfirmOrder();
        if(op.LocalViewUnit()!=allies[1]) throw new InvalidOperationException("Could not equip the second soldier");
        InputSystem.QueueStateEvent(keys,new KeyboardState(Key.T));
        yield return null; yield return null;
        InputSystem.QueueStateEvent(keys,new KeyboardState());
        if(!op.MaskOn(allies[1])) throw new InvalidOperationException("Personal mask did not latch");
        CameraManager.Instance.SwitchToTacticalMode();
        float deadline=Time.realtimeSinceStartup+25;
        while(GameManager.Instance.IsBotTurn() && Time.realtimeSinceStartup<deadline)yield return null;
        if(GameManager.Instance.IsBotTurn())throw new TimeoutException("Waiting for control of the map");
        void Place(Unit u,Vector3 at,Vector3 look)
        {
            if(UnityEngine.AI.NavMesh.SamplePosition(at,out var spot,2.5f,UnityEngine.AI.NavMesh.AllAreas)) at=spot.position;
            var cc=u.GetComponent<CharacterController>();
            cc.enabled=false; u.transform.SetPositionAndRotation(at+Vector3.up*.04f,Quaternion.LookRotation(look));
            cc.enabled=true; u.RefreshGridPositionFromWorld();
        }
        Place(allies[0],new Vector3(1,0,14),Vector3.right);
        Place(allies[1],new Vector3(10,0,8.5f),Vector3.right);
        Place(allies[2],new Vector3(-13,.1f,4),Vector3.left);
        Place(enemy,new Vector3(-20,.1f,4),Vector3.right);
        allies[0].TakeDamage(allies[0].GetHealth()-Mathf.RoundToInt(allies[0].GetMaxHealth()*.27f));
        allies[1].TakeDamage(16);
        planner.Select(allies[0]);
        Physics.SyncTransforms();
        yield return new WaitForSeconds(.35f);
        yield return new WaitForEndOfFrame();
        SaveFrame("01-состояние-отряда");
        var icons=TacticalWorldIconsController.Instance;
        if(!op.SquadSees(enemy) || !icons.HasVitalPanel(enemy) || allies.Any(u=>!icons.HasVitalPanel(u)) ||
            !new OsowiecMapVitals(allies[0]).Critical || !OsowiecOperation.InGas(allies[0]))
            throw new InvalidOperationException($"Missing condition: enemy seen={op.SquadSees(enemy)}, panel={icons.HasVitalPanel(enemy)}, " +
                "allies="+string.Join(" / ",allies.Select(u=>$"{u.name} panel={icons.HasVitalPanel(u)} pos={u.transform.position:F1} {new OsowiecMapVitals(u).Summary}")));
        allies[2].transform.rotation=Quaternion.LookRotation(Vector3.right);
        yield return new WaitForSeconds(.36f);
        if(op.SquadSees(enemy) || icons.HasVitalPanel(enemy) ||
            !FogWarIntelTracker.Instance.TryGetDisplayPosition(enemy,out _,out bool exact) || exact)
            throw new InvalidOperationException("A last-known marker disclosed current condition");
        InputSystem.QueueStateEvent(keys,new KeyboardState(Key.M));
        yield return null; yield return null;
        InputSystem.QueueStateEvent(keys,new KeyboardState());
        if(!planner.SurveyOpen) throw new InvalidOperationException("Paper plan did not open on M");
        yield return new WaitForEndOfFrame();
        SaveFrame("02-состояние-на-плане");
        InputSystem.RemoveDevice(keys);
        string details=string.Join(" | ",allies.Select(u=>u.GetUnitDisplayName()+" "+new OsowiecMapVitals(u).Summary));
        File.WriteAllText(Path.Combine(output,"Проверка-состояния.txt"),"PASS "+details+"; lost enemy retains only a last-known marker.");
        Debug.Log("PASS map vitals: "+details+"; last-known enemy has no live values.");
    }

    private IEnumerator CheckRoleViews(OsowiecPlanner planner)
    {
        var cameras=CameraManager.Instance;
        var actualModels=new HashSet<Mesh>();
        var allies=FindObjectsByType<Unit>(FindObjectsSortMode.InstanceID).Where(u=>u.owner==Player.Player1).ToArray();
        if(allies.Length!=3) throw new InvalidOperationException($"Expected three role variants, found {allies.Length}");
        foreach(var ally in allies)
        {
            var weapon=ally.GetEquippedWeapon();
            var skin=weapon.Config.firstPersonPrefab;
            if(!skin || weapon.Config.isMarksmanRifle!=(ally.chessType==ChessUnitType.Bishop))
                throw new InvalidOperationException($"Role assignment missing for {ally.name}");
            var thirdPersonMesh=weapon.GetComponentsInChildren<MeshFilter>(true).First(m=>m.name=="Field_carbine").sharedMesh;
            if(!actualModels.Add(thirdPersonMesh)) throw new InvalidOperationException("Allies share the same exterior mesh.");
            planner.Select(ally);
            Vector3[] nearby={Vector3.left,Vector3.right,Vector3.forward,Vector3.back};
            foreach(Vector3 direction in nearby)
            {
                planner.Preview(ally.transform.position+direction*1.4f);
                planner.ConfirmOrder();
                if(cameras.IsActionMode()) break;
            }
            if (cameras.GetCurrentControlledUnit()!=ally || !cameras.IsActionMode())
                throw new InvalidOperationException("Unable to enter first person as "+ally.name);
            yield return new WaitForSecondsRealtime(.65f);
            yield return new WaitForEndOfFrame();
            var muzzle=OsowiecWeaponView.VisibleMuzzle(ally);
            var camera=cameras.GetActionCamera();
            var meshes=camera.GetComponentsInChildren<MeshFilter>().Where(m=>m.name=="Field_carbine" && m.GetComponent<Renderer>().enabled).ToArray();
            if(!muzzle || !muzzle.IsChildOf(camera.transform) || meshes.Length!=1 || meshes[0].sharedMesh!=thirdPersonMesh)
                throw new InvalidOperationException("First-person and world model mismatch for "+ally.name);
            SaveFrame("03-"+ally.GetUnitDisplayName());
            if(actualModels.Count==allies.Length) break;
            cameras.ForceSwitchToTacticalMode();
            // Ending an actor's action hands initiative to the AI in a real player run.
            float oldScale=Time.timeScale;
            Time.timeScale=5f;
            float limit=Time.realtimeSinceStartup+35f;
            while(GameManager.Instance.IsBotTurn() && Time.realtimeSinceStartup<limit) yield return null;
            Time.timeScale=oldScale;
            if(GameManager.Instance.IsBotTurn()) throw new TimeoutException("Opponent did not finish its turn.");
            for(int i=0;i<3;i++) yield return null;
        }
    }

}

