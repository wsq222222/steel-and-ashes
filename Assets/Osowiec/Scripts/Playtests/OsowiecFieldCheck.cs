using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;

// Independent Windows fixture. All scene manipulation here requires an explicit QA flag.
public sealed class OsowiecFieldCheck : MonoBehaviour
{
    string output;
    bool finished;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        var args=Environment.GetCommandLineArgs();
        int at=Array.IndexOf(args,"-osowiec-field-check");
        if(Application.isEditor || at<0 || at+1>=args.Length) return;
        var host=new GameObject("Проверка полевой сцены");
        DontDestroyOnLoad(host);
        host.AddComponent<OsowiecFieldCheck>().output=args[at+1];
    }

    void OnEnable() => Application.logMessageReceived+=OnLog;
    void OnDisable() => Application.logMessageReceived-=OnLog;
    void OnLog(string text,string stack,LogType type)
    {
        if(type!=LogType.Exception || finished) return;
        finished=true;
        Application.Quit(2);
    }

    IEnumerator Start()
    {
        Directory.CreateDirectory(output);
        Application.runInBackground=true;
        bool graphics=SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null;
        if (Environment.GetCommandLineArgs().Contains("-osowiec-battery-watch"))
        {
            yield return OsowiecBatteryWatchCheck.Run();
            finished=true;
            Application.Quit(0);
            yield break;
        }
        if (graphics && Environment.GetCommandLineArgs().Contains("-osowiec-phone-check"))
        {
            yield return TelephoneInteraction();
            finished=true;
            Application.Quit(0);
            yield break;
        }
        if (graphics && Environment.GetCommandLineArgs().Contains("-osowiec-surface-survey"))
        {
            SceneManager.LoadScene("Osowiec");
            for (int n = 0; n < 8; n++) yield return null;
            var cutaway = FindFirstObjectByType<OsowiecFortCutaway>();
            var fort = cutaway.transform;
            if (fort.GetComponentsInChildren<TextMesh>(true).Length != 0)
                throw new InvalidOperationException("World-space location captions remain on the fort.");
            // These camera locations view the roofs from outside their cutaway volumes.
            cutaway.enabled = false;
            foreach (var roof in cutaway.surfaces) if (roof) roof.enabled = true;
            Shot("01-подпорная-стена", new Vector3(-34,21,-4), new Vector3(-8.7f,2,-4),50);
            Shot("02-бревна-и-гребень", new Vector3(-14.4f,6.5f,-5.5f), new Vector3(-8.75f,5.1f,-2),46);
            Shot("03-крыша-каземата", new Vector3(-17,15,-30), new Vector3(0,2.6f,-12),50);
            Shot("04-дерн-каземата", new Vector3(-6,5.4f,-17.1f), new Vector3(-2.5f,4.05f,-12.6f),54);
            Shot("05-крыша-лазарета", new Vector3(-53,8.6f,-20), new Vector3(-48.2f,1.9f,-10),54);
            Shot("06-полотнище-и-заплаты", new Vector3(-51.8f,4.5f,-14), new Vector3(-49.9f,2.2f,-10.2f),55);
            Shot("07-полевой-телефон", new Vector3(-13,1.7f,-4.56f), new Vector3(-15.73f,1.39f,-4.82f),62);
            Debug.Log("PASS fort surfaces and unlettered props " + Application.buildGUID);
            finished = true;
            Application.Quit(0);
            yield break;
        }
        if(graphics && Array.IndexOf(Environment.GetCommandLineArgs(),"-osowiec-signal-survey")>=0)
        {
            SceneManager.LoadScene("Osowiec");
            for(int n=0;n<8;n++)yield return null;
            var signal=GameObject.Find("Signals").GetComponent<MeshRenderer>();
            if(!signal.sharedMaterials.Any(m=>m.shader.name=="Steel and Ashes/Battery signal cloth"))
                throw new InvalidOperationException("Yard signal flag material missing.");
            Time.timeScale=1;
            Shot("01-двор",new Vector3(29.4f,2.0f,-2.6f),new Vector3(33.4f,2.45f,1.8f),54);
            for(int frame=0;frame<6;frame++)
            {
                Shot("ткань-"+frame,new Vector3(31.90f,2.62f,.29f),new Vector3(33.51f,3.17f,1.85f),30);
                yield return new WaitForSeconds(.22f);
            }
            Debug.Log("PASS yard signal flag "+Application.buildGUID);
            finished=true;Application.Quit(0);yield break;
        }
        if(graphics && Array.IndexOf(Environment.GetCommandLineArgs(),"-osowiec-river-survey")>=0)
        {
            SceneManager.LoadScene("Osowiec");
            for(int n=0;n<8;n++)yield return null;
            int original=QualitySettings.GetQualityLevel();
            OsowiecFieldSettings.ReducedEffectsOverride=false;
            try
            {
                QualitySettings.SetQualityLevel(QualitySettings.names.Length-1,false);
                for(int n=0;n<4;n++)yield return null;
                if(QualitySettings.names.Length<2 || Shader.GetGlobalFloat("_OsowiecLandscapeLow")>.5f ||
                    Shader.GetGlobalTexture("_OsowiecWaterSky")==null)
                    throw new InvalidOperationException("The Windows high quality river path or its overcast sky is missing.");
                Shot("01-река-с-вала",new Vector3(25.4f,13.7f,10.4f),new Vector3(33.7f,5.4f,20.7f),60);
                Shot("02-с-высоты-глаз",new Vector3(-34,1.81f,-8.95f),new Vector3(-36,-.19f,5.1f),66);
                Shot("03-отмель-вблизи",new Vector3(-35.0f,1.38f,-4.75f),new Vector3(-36.5f,-.38f,.40f),58);
                QualitySettings.SetQualityLevel(0,false);
                for(int n=0;n<4;n++)yield return null;
                if(Shader.GetGlobalFloat("_OsowiecLandscapeLow")<.5f)
                    throw new InvalidOperationException("River low quality setting missing.");
                Shot("04-река-низкое-качество",new Vector3(25.4f,13.7f,10.4f),new Vector3(33.7f,5.4f,20.7f),60);
            }
            finally
            {
                QualitySettings.SetQualityLevel(original,false);
                OsowiecFieldSettings.ReducedEffectsOverride=null;
            }
            Debug.Log("PASS river overlook, bank and low quality "+Application.buildGUID);
            finished=true;Application.Quit(0);yield break;
        }
        if(graphics && Array.IndexOf(Environment.GetCommandLineArgs(),"-osowiec-roof-survey")>=0)
        {
            SceneManager.LoadScene("Osowiec");
            for(int n=0;n<8;n++)yield return null;
            var yard=GameObject.Find("Северный вал · островки мокрой овсяницы");
            var paving=GameObject.Find("Casemate brick floor");
            if(!yard || yard.GetComponent<MeshFilter>().sharedMesh.vertexCount<1000 ||
                !paving.GetComponent<Renderer>().sharedMaterial.IsKeywordEnabled("_EARTH_APRON"))
                throw new InvalidOperationException("Missing roof survey or muddy paving.");
            Shot("01-дерн-на-валу",new Vector3(25.4f,13.7f,10.4f),new Vector3(33.7f,5.4f,20.7f),60);
            Shot("02-дерн-вблизи",new Vector3(33.1f,8.0f,16.8f),new Vector3(34.8f,7.52f,20.1f),61);
            Shot("03-вход-снаружи",new Vector3(-12.65f,1.54f,-11.97f),new Vector3(-8,.25f,-12.18f),67);
            var cut=FindFirstObjectByType<OsowiecFortCutaway>();
            if(cut){cut.enabled=false;foreach(var roof in cut.surfaces)if(roof)roof.enabled=false;}
            Shot("04-переход-земля-кирпич",new Vector3(-10.32f,7.5f,-14.4f),new Vector3(-8.8f,.02f,-11.96f),52);
            Debug.Log("PASS northern turf and muddy thresholds "+Application.buildGUID);
            finished=true;Application.Quit(0);yield break;
        }
        if(graphics && Array.IndexOf(Environment.GetCommandLineArgs(),"-osowiec-landscape-only")>=0)
        {
            yield return OsowiecLandscapeCheck.Run(output);
            finished=true;Application.Quit(0);yield break;
        }
        if(graphics && Array.IndexOf(Environment.GetCommandLineArgs(),"-osowiec-scenery-only")>=0)
        {
            yield return ScenerySurvey();
            finished=true;Application.Quit(0);yield break;
        }
        bool wardRain = Environment.GetCommandLineArgs().Contains("-osowiec-ward-rain");
        if(graphics && (wardRain || Environment.GetCommandLineArgs().Contains("-osowiec-ward-walk")))
        {
            GameManager.SetGameMode(GameMode.PlayerVsBot);
            SceneManager.LoadScene("Osowiec");
            for(int i=0;i<8;i++)yield return null;
            yield return CheckWardRain();
            if(wardRain){finished=true;Application.Quit(0);yield break;}
        }
        if(graphics && Array.IndexOf(Environment.GetCommandLineArgs(),"-osowiec-ward-walk")>=0)
        {
            yield return OsowiecWardWalkCheck.Run(output);
            finished=true;Application.Quit(0);yield break;
        }
        if(graphics && Array.IndexOf(Environment.GetCommandLineArgs(),"-osowiec-battery-only")>=0)
        {
            SceneManager.LoadScene("Osowiec");
            for(int i=0;i<7;i++)yield return null;
            foreach(var cutaway in FindObjectsByType<OsowiecFortCutaway>())
            {
                cutaway.enabled=false;
                foreach(var roof in cutaway.surfaces) if(roof)roof.enabled=true;
            }
            Shot("01-двор-и-галерея",new Vector3(28.7f,2.15f,-1.80f),new Vector3(42.4f,2.58f,2.9f),64);
            Shot("02-нижний-ход",new Vector3(45.83f,-1.19f,-3.89f),new Vector3(46.10f,-1.31f,5.59f),66);
            var galleryGuard=FindObjectsByType<Unit>(FindObjectsInactive.Include).Single(u=>u.name=="Дозор у бастиона");
            if(galleryGuard.transform.position.y<3.4f) throw new InvalidOperationException("Reserve observer is off the gallery.");
            galleryGuard.gameObject.SetActive(true);
            for(int i=0;i<4;i++)yield return null;
            Shot("03-верхняя-галерея",new Vector3(48.18f,5.14f,3.07f),new Vector3(32.4f,2.05f,1.09f),64);
            Shot("04-амбразура",new Vector3(33.08f,1.78f,-4.02f),new Vector3(38.56f,2.33f,-5.34f),60);
            Shot("05-кровля-галереи",new Vector3(37.4f,14.3f,-7.1f),new Vector3(45.5f,6.25f,1.1f),43);
            Shot("06-купол-и-галерея",new Vector3(34.9f,9.3f,-17.2f),new Vector3(40.7f,3.65f,-6.8f),51);
            Shot("07-торец-вала",new Vector3(60.3f,6.5f,12.8f),new Vector3(52.4f,2.8f,20.1f),52);
            Shot("08-опоры-мачты",new Vector3(31.4f,7.0f,16.5f),new Vector3(28.7f,4.6f,20.6f),47);
            Shot("09-полевые-мешки",new Vector3(26.55f,1.08f,3.5f),new Vector3(28f,.31f,5f),43);
            Shot("10-срез-торфа-над-входом",new Vector3(-14.1f,4.05f,-16.2f),new Vector3(-9.65f,2.7f,-12.7f),54);
            Debug.Log("PASS battery photographs "+Application.buildGUID);
            finished=true;Application.Quit(0);yield break;
        }
        if(graphics && Array.IndexOf(Environment.GetCommandLineArgs(),"-osowiec-ward-only")>=0)
        {
            GameManager.SetGameMode(GameMode.PlayerVsBot);
            SceneManager.LoadScene("Osowiec");
            for(int i=0;i<7;i++)yield return null;
            yield return InspectOccupiedWard();
            finished=true;Application.Quit(0);yield break;
        }
        if(graphics)
        {
            for(int frame=0;frame<6;frame++) yield return null;
            yield return ScreenPage("01-штаб");
            GameManager.Instance.ResumeGame();
            yield return null;
            var eye=CameraManager.Instance.GetActionCamera().transform;
            var aim=eye.localRotation;
            eye.localRotation=Quaternion.Euler(19,0,0);
            yield return ScreenPage("12-штаб-руки");
            eye.localRotation=Quaternion.Euler(72,0,0);
            yield return ScreenPage("13-штаб-шинель");
            eye.localRotation=aim;
            Shot("16-балки-штаба",new Vector3(2.20f,1.65f,-2.55f),new Vector3(4.15f,2.02f,-.42f),52);
            var book=FindFirstObjectByType<OsowiecJournalNavigation>(FindObjectsInactive.Include);
            GameManager.Instance.PauseGame(); GameManager.Instance.OpenSettings();
            book.Audio(); yield return ScreenPage("02-звук");
            book.Picture(); yield return ScreenPage("03-изображение");
            book.Controls(); yield return ScreenPage("04-управление");
            GameManager.Instance.ApplySettings(); GameManager.Instance.ResumeGame();
            FindFirstObjectByType<HubComputerTerminal>(FindObjectsInactive.Include).OpenMissionUI();
            yield return ScreenPage("05-приказ");
        }
        GameManager.SetGameMode(GameMode.PlayerVsBot);
        SceneManager.LoadScene("Osowiec");
        for(int i=0;i<10;i++) yield return null;
        var scene=OsowiecOperation.Instance;
        if(!scene || !scene.gate) throw new InvalidOperationException("Missing generated mission or gate.");
        var ward=FindFirstObjectByType<OsowiecReliefColumn>();
        if(!ward) throw new InvalidOperationException("Missing relief column.");
        var deploy=FindFirstObjectByType<ArmyDeploymentController>();
        var paper=FindFirstObjectByType<OsowiecPaperPlacement>();
        if(!paper) throw new InvalidOperationException("Missing deployment sheet.");
        paper.Confirm();
        if(!GameManager.Instance.IsArmyDeploymentPhase())throw new InvalidOperationException("Empty roster started the operation.");
        if(graphics){
            yield return ScreenPage("06a-пустой-отряд");
            var row=paper.GetComponentsInChildren<OsowiecPaperDrag>().Single(r=>r.offer==1);
            UnityEngine.EventSystems.ExecuteEvents.Execute(row.gameObject,new UnityEngine.EventSystems.PointerEventData(
                UnityEngine.EventSystems.EventSystem.current),UnityEngine.EventSystems.ExecuteEvents.pointerEnterHandler);
            yield return ScreenPage("06b-роль-при-наведении");
        }
        paper.Hide();
        if(!paper.transform.Find("Field overview controls").gameObject.activeInHierarchy ||
           paper.transform.Find("Deployment backing controls").gameObject.activeInHierarchy)
            throw new InvalidOperationException("The field survey has the wrong visible controls.");
        paper.NextSector();
        if(deploy.Sectors.Sector!=1)throw new InvalidOperationException("Northern deployment survey did not open.");
        if(graphics)yield return ScreenPage("06c-обзор-низины");
        paper.Show();
        if(!paper.transform.Find("Deployment survey").gameObject.activeInHierarchy)throw new InvalidOperationException("Did not return to the paper.");
        if(!deploy.TryPlaceOfferAtGridCell(2,OsowiecDeploymentSectors.Cells[0][0]))
            throw new InvalidOperationException("Field test could not deploy the scout.");
        var player=FindObjectsByType<Unit>(FindObjectsSortMode.None).Single(u=>u.owner==Player.Player1);
        if(graphics) yield return ScreenPage("06-расстановка");
        paper.Confirm();
        for(int i=0;i<5;i++) yield return null;
        Time.timeScale=1;
        if(graphics)
        {
            var journal=FindFirstObjectByType<OsowiecObjectiveJournal>(FindObjectsInactive.Include);
            journal.Open(); yield return ScreenPage("07-задачи");
            journal.Close();
            GameManager.Instance.PauseGame(); yield return ScreenPage("08-пауза");
            GameManager.Instance.ResumeGame();
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-osowiec-layout-only")>=0)
            {
                Debug.Log($"PASS menu capture {Screen.width}x{Screen.height}, Build={Application.buildGUID}");
                finished=true;Application.Quit();yield break;
            }
        }
        foreach(var hole in FindObjectsByType<OsowiecFortCutaway>())
        { hole.enabled=false; foreach(var renderer in hole.surfaces) if(renderer) renderer.enabled=true; }
        if(SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            Shot("08-берег-Бебжи",new Vector3(-34,1.81f,-8.95f),new Vector3(-36,-.19f,5.1f),66);
            Shot("09-пойма",new Vector3(-76,75,-66),new Vector3(-21,.2f,8),62);
            Shot("10-низина-сверху",new Vector3(-6,23,5),new Vector3(1,.55f,12),65);
            Shot("11-внутри-облака",new Vector3(-4,1.4f,12),new Vector3(12,1.55f,13),64);
            Shot("12-старица",new Vector3(-28,3.8f,-21),new Vector3(-37,-.3f,-32),65);
            Shot("01-дверь-закрыта",new Vector3(-4.9f,1.53f,-12.08f),new Vector3(0,1.65f,-12),58);
            Shot("14-каземат-кладка",new Vector3(-5.89f,1.55f,-12.06f),new Vector3(-4.55f,1.27f,-13.61f),58);
            Shot("15-бетон-батареи",new Vector3(27.21f,.87f,10.93f),new Vector3(28.92f,.34f,10.20f),54);
            InspectBank();
            yield return InspectOccupiedWard();
            Shot("04-выход-из-окопа",new Vector3(-44,1.72f,-15.8f),new Vector3(-32,.96f,-15.0f),70);
            Shot("05-форт",new Vector3(22.1f,1.74f,-4.18f),new Vector3(35,3.45f,17.9f),67);
            // Isolate the decal appearance on this patch of wet decking.
            // RouteMatrix separately tests contacts during character movement.
            var tracks=FindFirstObjectByType<OsowiecWetTracks>();
            if(!tracks) throw new InvalidOperationException("Missing ground tracks.");
            int boots=tracks.PlantedBoots, wheels=tracks.WheelSegments;
            for(int i=0;i<9;i++)
                OsowiecWetTracks.Boot(new Vector3(-43+i*.42f,.14f,-16+(i%2==0?.12f:-.12f)),Vector3.right,i%2==0);
            for(int i=0;i<16;i++)
                foreach(float z in new[]{-16.54f,-15.47f})
                {
                    var from=new Vector3(-43+i*.23f,.14f,z);
                    OsowiecWetTracks.Wheel(from,from+Vector3.right*.23f);
                }
            yield return null; yield return null;
            if(tracks.PlantedBoots-boots<7 || tracks.WheelSegments-wheels<20)
                throw new InvalidOperationException($"Track substrate: {tracks.PlantedBoots-boots} prints, {tracks.WheelSegments-wheels} wheel segments.");
            var fromEye=new Vector3(-42.2f,.92f,-17.15f);
            var onDeck=new Vector3(-41.3f,.10f,-16.03f);
            Shot("06-отпечатки-и-колеи",fromEye,onDeck,55);
            var rainSequence=Path.Combine(output,"Дождь");
            Directory.CreateDirectory(rainSequence);
            float startRain=tracks.RainTime;
            float timeout=Time.realtimeSinceStartup+32;
            Time.timeScale=12;
            for(int frame=0;frame<=30;frame++)
            {
                while(tracks.RainTime-startRain<frame*2.05f){
                    if(Time.realtimeSinceStartup>timeout)throw new TimeoutException("Rain clock stopped.");
                    yield return null;
                }
                Shot(Path.Combine("Дождь",frame.ToString("D3")),fromEye,onDeck,55);
            }
            Debug.Log($"PASS rainfall sequence: {tracks.RainTime-startRain:F1} scene seconds on timber.");
            Time.timeScale=1;
        }
        var units=FindObjectsByType<Unit>(FindObjectsSortMode.InstanceID);
        var npc=units.First(u=>u.owner==Player.Player2 && u.GetEquippedWeapon() && u.chessType==ChessUnitType.Молот);
        foreach(var u in units) if(u.owner==Player.Player2 && u!=npc) u.gameObject.SetActive(false);
        Move(npc,new Vector3(-45,.11f,-16),Vector3.right);
        Move(player,new Vector3(-38,.11f,-16),Vector3.left);
        Physics.SyncTransforms();
        if(graphics)
        {
            var planner=OsowiecPlanner.Instance;
            planner.Select(player);
            planner.Preview(player.transform.position+Vector3.left*2.4f);
            yield return ScreenPage("10-приказ-на-ход");
            planner.ConfirmOrder();
            if(!CameraManager.Instance.IsActionMode()) throw new InvalidOperationException("HUD field capture failed to enter FPS.");
            yield return ScreenPage("11-боевой-интерфейс");
            CameraManager.Instance.ForceSwitchToTacticalMode();
            Move(player,new Vector3(-38,.11f,-16),Vector3.left);
            Physics.SyncTransforms();
        }
        if(graphics)
            Shot("07-разведчик",new Vector3(-39.52f,1.69f,-16.43f),new Vector3(-38,1.46f,-16),44);
        var bot=FindFirstObjectByType<BotController>();
        if(!bot.CanSeeTargetForBrain(npc,player)) throw new InvalidOperationException("Open trench fixture has no sight.");
        Vector3 start=npc.transform.position;
        int ammo=npc.GetEquippedWeapon().AmmoInMag;
        var game=GameManager.Instance;
        if(!game.IsBotTurn())game.SwitchTurn();
        float turnDeadline=Time.realtimeSinceStartup+35;
        bool warned=false;
        while(game.IsBotTurn() && Time.realtimeSinceStartup<turnDeadline)
        {
            if(scene.ThreatSource(player)==npc && scene.ThreatSeconds(player)>0)warned=true;
            yield return null;
        }
        float walked=Vector3.ProjectOnPlane(npc.transform.position-start,Vector3.up).magnitude;
        if(game.IsBotTurn() || !warned || walked>.2f || ammo-npc.GetEquippedWeapon().AmmoInMag!=1)
            throw new InvalidOperationException($"Sentry at 7 m: warned={warned}, returned={!game.IsBotTurn()}, travelled {walked:F3}, ammo {ammo} to {npc.GetEquippedWeapon().AmmoInMag}.");
        Move(npc,new Vector3(-2.5f,.11f,-12),Vector3.right);
        Move(player,new Vector3(2.5f,.11f,-12),Vector3.left);
        Physics.SyncTransforms();
        if(bot.CanSeeTargetForBrain(npc,player)) throw new InvalidOperationException("NPC sees through the closed leaf.");
        npc.gameObject.SetActive(false);
        float gateSide = -1;
        Move(player,new Vector3(gateSide*.96f,.12f,-12.15f),Vector3.right);
        Physics.SyncTransforms();
        var orders=OsowiecPlanner.Instance;
        orders.Select(player);
        orders.ConfirmOrder();
        if(scene.LocalViewUnit()!=player)throw new InvalidOperationException("Could not control the soldier beside the gate.");
        var keys=InputSystem.AddDevice<Keyboard>();
        InputSystem.QueueStateEvent(keys,new KeyboardState(Key.E));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(keys,new KeyboardState());
        yield return null;
        InputSystem.RemoveDevice(keys);
        if (!scene.ChargeSet || scene.GateOpen) throw new InvalidOperationException("E did not attach the gate case.");
        var caseNode=scene.gate.Find("FieldCase(Clone)");
        if(!caseNode || caseNode.GetComponentsInChildren<Renderer>().Length<4 ||
            caseNode.GetComponentsInChildren<Renderer>().Any(r=>!r.sharedMaterial || !r.sharedMaterial.shader ||
                (graphics && !r.sharedMaterial.shader.isSupported)))
            throw new InvalidOperationException("Missing charge mesh or unsupported materials.");
        Vector3 gateEye=new(-3.7f,1.48f,-12.27f), gateFocus=new(0,1.29f,-12.04f);
        if(graphics) Shot("01b-полевой-подсумок",gateEye,gateFocus,43);
        float expiry=Time.realtimeSinceStartup+12f;
        while(!scene.GateOpen && Time.realtimeSinceStartup<expiry) yield return null;
        if(!scene.GateOpen || scene.gate.GetComponent<BoxCollider>().enabled ||
            !scene.gate.GetComponentInChildren<OsowiecGateBreak>())throw new InvalidOperationException("Destructible gate did not clear.");
        if(graphics) { yield return new WaitForSeconds(.18f); Shot("02a-разрушение",gateEye,gateFocus,53); }
        yield return new WaitForSeconds(1.1f);
        Physics.SyncTransforms();
        Move(player,new Vector3(-2.4f,.12f,-12.7f),Vector3.right);
        Physics.SyncTransforms();
        if(Physics.Linecast(new Vector3(-1,1.1f,-12.1f),new Vector3(1,1.1f,-12.1f),
            Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))throw new InvalidOperationException("Collision still blocks the opening.");
        if(graphics) Shot("02-дверь-открыта",gateEye,gateFocus,58);
        Debug.Log("PASS field case, destruction, clear doorway and persistent debris.");
        Debug.Log($"PASS Osowiec field check Build={Application.buildGUID} open range 7m; NPC displacement {walked:F3}; fired from position; opaque closed gate hides target.");
        finished=true;Application.Quit(0);
    }

    static void Move(Unit unit,Vector3 where,Vector3 forward)
    {
        var cc=unit.GetComponent<CharacterController>();
        if(cc)cc.enabled=false;
        unit.transform.SetPositionAndRotation(where,Quaternion.LookRotation(forward));
        if(cc)cc.enabled=true;
    }

    IEnumerator InspectOccupiedWard()
    {
        var care=FindFirstObjectByType<OsowiecWardCare>();
        if(!care || care.residents.Length!=2 || care.residents.Any(c=>!c.gameObject.activeInHierarchy)
            || care.ward.occupiedBeds.Any(b=>b.activeSelf))
            throw new InvalidOperationException("The unserved ward must have two residents and three receiving beds.");
        var original=Camera.main;
        if(original)original.tag="Untagged";
        var view=new GameObject("Ward inspection visitor");
        view.tag="MainCamera";
        var camera=view.AddComponent<Camera>();camera.depth=-50;
        view.transform.position=new Vector3(-51.93f,1.08f,-12.08f);
        var target=new Vector3(-51.7f,.10f,-8.64f);
        view.transform.LookAt(target);
        try
        {
            var before=care.Contacts;
            float deadline=Time.realtimeSinceStartup+18;
            while(care.Contacts-before<12 || care.PalmGap>.055f || care.ward.ResidentAtHand<0)
            {
                if(Time.realtimeSinceStartup>deadline)
                    throw new InvalidOperationException($"Ward dressing: contacts={care.Contacts-before}, wrist error={care.PalmGap:F3}m");
                yield return null;
            }
            // Read the rendered pose after the generic animator and the bedside IK.
            yield return new WaitForEndOfFrame();
            var resident=care.ward.ResidentAtHand;
            float glove=care.GloveToBlanket();
            if(glove>.045f)throw new InvalidOperationException($"Visible gloves miss the patient's blanket by {glove:F3} m");
            var handCamera=new Vector3(-51.0f+resident*1.82f,.78f,-10.73f);
            var handFocus=new Vector3(-52.37f+resident*1.82f,.25f,-8.90f);
            Shot("03-лазарет",view.transform.position,new Vector3(-49.95f,.17f,-8.4f),70);
            Shot("03b-перевязка",handCamera,handFocus,49);
            Debug.Log($"[Osowiec ward] two resting patients, three receiving places, wrist error {care.PalmGap:F3} m, rendered glove to blanket {glove:F3} m on resident {resident+1}");
        }
        finally
        {
            view.tag="Untagged";camera.enabled=false;Destroy(view);
            if(original)original.tag="MainCamera";
        }
    }

    public static IEnumerator CheckWardRain()
    {
        var eaves=FindAnyObjectByType<OsowiecWardEaves>();
        var cloth=eaves.GetComponent<MeshRenderer>();
        var stream=eaves.GetComponentInChildren<ParticleSystem>();
        var streaks=stream.GetComponent<ParticleSystemRenderer>();
        var grains=new ParticleSystem.Particle[64];
        Vector3[] sources=eaves.outlets.Select(eaves.transform.TransformPoint).ToArray();
        IEnumerator Flow(string view)
        {
            var seen=new bool[sources.Length];
            float until=Time.realtimeSinceStartup+20;
            while(seen.Any(received=>!received) && Time.realtimeSinceStartup<until)
            {
                yield return null;
                int live=stream.GetParticles(grains);
                for(int j=0;j<live;j++)
                for(int k=0;k<sources.Length;k++)
                {
                    var delta=grains[j].position-sources[k];
                    if(Mathf.Abs(delta.x)<.13f && Mathf.Abs(delta.z)<.09f && delta.y<-.012f && grains[j].velocity.y<0)
                        seen[k]=true;
                }
            }
            var cameras=CameraManager.Instance;
            var camera=cameras.IsActionMode()?cameras.GetActionCamera():cameras.GetTacticalCamera();
            float distance=Vector3.Distance(camera.transform.position,cloth.bounds.center);
            string observation=$"{view} ({camera.name}, {distance:F1} m): {seen.Count(received=>received)}/6 outlets; cloth={cloth.enabled}, drops={streaks.enabled}";
            if(sources.Length!=6 || !cloth.enabled || !streaks.enabled || seen.Any(received=>!received))
                throw new InvalidOperationException("Rain stopped at "+observation);
            Debug.Log("PASS falling canvas water: "+observation);
        }

        var deploy=FindAnyObjectByType<ArmyDeploymentController>();
        var cell=OsowiecDeploymentSectors.Cells[0][0];
        if(!deploy.TryPlaceOfferAtGridCell(2,cell))throw new InvalidOperationException("Missing ward visitor.");
        var actor=deploy.DeployedAt(cell);
        FindAnyObjectByType<OsowiecPaperPlacement>().Confirm();
        for(int i=0;i<4;i++)yield return null;
        var planner=OsowiecPlanner.Instance;
        planner.Select(actor);
        yield return new WaitForSecondsRealtime(1.1f); // Observe after the camera has risen from the deployment table.
        yield return Flow("tactical camera");
        var cc=actor.GetComponent<CharacterController>();
        cc.enabled=false;actor.transform.position=new Vector3(-48.2f,.12f,-10.1f);cc.enabled=true;
        actor.RefreshGridPositionFromWorld();Physics.SyncTransforms();
        for(int i=0;i<6;i++)yield return null;
        if(cloth.enabled || streaks.enabled || stream.particleCount!=0)
            throw new InvalidOperationException("The ward cutaway still shows falling eave water.");
        planner.ConfirmOrder();
        for(int i=0;i<5;i++)yield return null;
        if(!CameraManager.Instance.IsActionMode())throw new InvalidOperationException("Ward visitor failed to switch to FPS.");
        yield return Flow("FPS camera");
        Debug.Log("PASS ward eaves: six drains release falling drops in tactical and FPS cameras, hidden room clears all drops. "+Application.buildGUID);
    }

    IEnumerator ScenerySurvey()
    {
        GameManager.SetGameMode(GameMode.PlayerVsBot);
        SceneManager.LoadScene("Osowiec");
        for(int i=0;i<8;i++)yield return null;
        var cut=FindAnyObjectByType<OsowiecFortCutaway>();
        var deploy=FindAnyObjectByType<ArmyDeploymentController>();
        if(cut.rooms==null || cut.rooms.Length<6 || !deploy.TryPlaceOfferAtGridCell(2,OsowiecDeploymentSectors.Cells[0][0]))
            throw new InvalidOperationException("Missing latest cutaway data or start cell");
        var actor=FindObjectsByType<Unit>(FindObjectsSortMode.None).Single(u=>u.owner==Player.Player1);
        FindAnyObjectByType<OsowiecPaperPlacement>().Confirm();
        for(int i=0;i<4;i++)yield return null;
        var planner=OsowiecPlanner.Instance;planner.Select(actor);
        void Place(Vector3 feet,Vector3 towards)
        {
            var cc=actor.GetComponent<CharacterController>();
            cc.enabled=false;actor.transform.SetPositionAndRotation(feet,Quaternion.LookRotation(Vector3.ProjectOnPlane(towards-feet,Vector3.up)));
            cc.enabled=true;actor.RefreshGridPositionFromWorld();Physics.SyncTransforms();
        }
        void Check(string room)
        {
            var hidden=cut.surfaces.Where(s=>s && !s.enabled).ToArray();
            var expected=cut.Covering(actor.transform.position);
            if(room==null)
            {
                if(hidden.Length!=0)throw new InvalidOperationException("A ceiling is hidden outside.");
            }
            else if(expected==null || expected.name!=room || hidden.Length!=expected.hide.Length || hidden.Except(expected.hide).Any())
                throw new InvalidOperationException($"Expected '{room}'; over player '{expected?.name}', hidden {string.Join(",",hidden.Select(r=>r.name))}");
        }
        Place(new Vector3(31,.06f,.5f),new Vector3(42,1,1));
        yield return new WaitForEndOfFrame();Check(null);
        Shot("01-сверху-снаружи",new Vector3(28,20,-15),new Vector3(41,1.5f,1),58);
        Shot("09-разгрузка-во-дворе",new Vector3(34.20f,2.22f,-4.64f),new Vector3(36.32f,.68f,-2.12f),62);
        Shot("10-скамья-у-лестницы",new Vector3(34.01f,1.31f,4.48f),new Vector3(35.83f,.54f,2.88f),56);
        Place(new Vector3(40,.15f,-8),new Vector3(35,1,-5));
        yield return null;yield return new WaitForEndOfFrame();Check("Южный зал");
        Shot("02-сверху-местный-разрез",new Vector3(28,20,-15),new Vector3(41,1.5f,1),58);
        Place(new Vector3(46,-2.82f,.3f),new Vector3(46,-1,5));
        yield return null;yield return new WaitForEndOfFrame();Check("Нижний ход");
        Place(new Vector3(46,3.60f,1.1f),new Vector3(48,4,2));
        yield return null;yield return new WaitForEndOfFrame();Check("Верхняя галерея");
        Place(new Vector3(32.7f,.09f,-4.44f),new Vector3(35.4f,.85f,-5.66f));
        planner.ConfirmOrder();for(int i=0;i<5;i++)yield return null;
        if(!actor.IsControlled() || !CameraManager.Instance.IsActionMode())throw new InvalidOperationException("FPS view missing.");
        Check(null);
        yield return new WaitForEndOfFrame();OsowiecMapCheck.SaveFrame(output,"03-мастерская-от-первого-лица");
        Shot("04-караульное-имущество",new Vector3(46.25f,4.74f,.16f),new Vector3(48.34f,4.63f,2.06f),64);
        Shot("07-северные-ящики",new Vector3(37.19f,1.79f,7.61f),new Vector3(35.49f,.72f,6.15f),54);
        var events=FindAnyObjectByType<OsowiecGroundEvents>();
        var fresh=events.hollows[0].GetComponentsInChildren<MeshRenderer>(true).Single(r=>r.name=="Debris on fresh crater rim 0");
        if(fresh.gameObject.activeInHierarchy)throw new InvalidOperationException("Fresh fragments appeared before the event.");
        // Keep the observer in the lower passage while the normal ground-event clock runs.
        Place(new Vector3(46,-2.82f,.3f),new Vector3(46,-1.7f,5));
        float expiry=Time.realtimeSinceStartup+45f;
        while(!fresh.gameObject.activeInHierarchy && Time.realtimeSinceStartup<expiry)yield return null;
        if(!fresh.gameObject.activeInHierarchy)throw new InvalidOperationException("Crater fragments did not appear with the new rim.");
        yield return new WaitForSeconds(4.2f);
        Shot("05-новая-воронка",new Vector3(-6.65f,1.14f,11.36f),new Vector3(-5.35f,.11f,13.02f),61);
        Shot("08-свежие-обломки",new Vector3(-6.03f,.65f,12.37f),new Vector3(-5.52f,.14f,12.98f),68);
        Shot("06-старый-край",new Vector3(-11.90f,1.18f,14.03f),new Vector3(-9.65f,.18f,14.85f),54);
        Debug.Log("PASS local roof cuts, exterior, both gallery levels, real workshop FPS, new and old crater rims "+Application.buildGUID);
    }

    IEnumerator TelephoneInteraction()
    {
        yield return OsowiecMapCheck.ApplyCaptureSize();
        GameManager.SetGameMode(GameMode.PlayerVsBot);
        SceneManager.LoadScene("Osowiec");
        for(int i=0;i<10;i++) yield return null;
        var deployment=FindAnyObjectByType<ArmyDeploymentController>();
        if(!deployment.TryPlaceOfferAtGridCell(2,OsowiecDeploymentSectors.Cells[0][0]))
            throw new InvalidOperationException("Could not deploy telephone observer.");
        var actor=FindObjectsByType<Unit>().Single(u=>u.owner==Player.Player1);
        FindAnyObjectByType<OsowiecPaperPlacement>().Confirm();
        for(int i=0;i<4;i++) yield return null;

        var game=OsowiecOperation.Instance;
        var state=typeof(OsowiecOperation).GetField("usingTelephone",
            System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        bool Open() => (bool)state.GetValue(game);
        float Place(Vector3 at)
        {
            if(!UnityEngine.AI.NavMesh.SamplePosition(at,out var floor,1.65f,UnityEngine.AI.NavMesh.AllAreas))
                throw new InvalidOperationException("No playable ground at "+at);
            Vector3 direction=new Vector3(-15.73f,1.3f,-4.82f)-floor.position;
            Move(actor,floor.position+Vector3.up*.045f,Vector3.ProjectOnPlane(direction,Vector3.up));
            actor.RefreshGridPositionFromWorld();
            actor.ClampActionLookPitchAbsolute(7f,7f);
            Physics.SyncTransforms();
            return Vector3.Distance(actor.transform.position,OsowiecOperation.Telephone);
        }
        IEnumerator Capture(string label)
        {
            for(int i=0;i<5;i++) yield return null;
            yield return new WaitForEndOfFrame();
            OsowiecMapCheck.SaveFrame(output,label);
        }
        float far=Place(new Vector3(-15.6f,0,-9.8f));
        var planner=OsowiecPlanner.Instance;
        planner.Select(actor);
        planner.ConfirmOrder();
        for(int i=0;i<4;i++) yield return null;
        if(!actor.IsControlled() || !CameraManager.Instance.IsActionMode())
            throw new InvalidOperationException("Telephone check requires the playable soldier and FPS HUD.");
        // Resume simulation after bypassing the opening deployment presentation.
        Time.timeScale=1;
        yield return new WaitForSecondsRealtime(.7f);
        var keys=InputSystem.AddDevice<Keyboard>("Field telephone QA");
        keys.MakeCurrent();
        IEnumerator PressE()
        {
            InputSystem.QueueStateEvent(keys,new KeyboardState(Key.E));
            yield return null; yield return null;
            InputSystem.QueueStateEvent(keys,new KeyboardState());
            yield return null; yield return null;
        }
        try
        {
            yield return PressE();
            if(far<=4.5f || Open()) throw new InvalidOperationException("E reached the telephone at "+far+" m.");
            yield return Capture("01-вдали");
            float near=Place(new Vector3(-13.05f,0,-4.5f));
            if(near>3.2f || Open()) throw new InvalidOperationException("Incorrect telephone approach at "+near+" m.");
            yield return Capture("02-у-телефона");
            yield return PressE();
            if(!Open()) throw new InvalidOperationException("E did not open the field switchboard.");
            yield return Capture("03-коммутатор");
            yield return PressE();
            if(Open()) throw new InvalidOperationException("Second E did not hang up.");
            string report=$"PASS {Application.buildGUID} {Screen.width}x{Screen.height}. "
                +$"Controlled soldier, graphical FPS HUD. Distance {far:F2} m: E leaves the switchboard closed. "
                +$"Distance {near:F2} m: E opens the switchboard; a second E hangs up. "
                +"Native frames captured before approach, at the handset and after E.";
            Debug.Log(report);
            File.WriteAllText(Path.Combine(output,"Telephone.txt"),report);
        }
        finally { InputSystem.RemoveDevice(keys); }
    }

    IEnumerator ScreenPage(string label)
    {
        yield return null;
        yield return new WaitForEndOfFrame();
        var dir=Path.Combine(output,$"Меню-{Screen.width}x{Screen.height}");
        Directory.CreateDirectory(dir);
        var screenshot=ScreenCapture.CaptureScreenshotAsTexture();
        try { File.WriteAllBytes(Path.Combine(dir,label+".png"),screenshot.EncodeToPNG()); }
        finally { Destroy(screenshot); }
    }

    void InspectBank()
    {
        static bool Topsoil(Collider collider)
        {
            return collider is MeshCollider mesh && mesh.sharedMesh &&
                mesh.sharedMesh.name.StartsWith("Ground_",StringComparison.Ordinal);
        }

        static Vector3 SurveySurface(float x,float z)
        {
            var hits=Physics.RaycastAll(new Vector3(x,15,z),Vector3.down,30,
                Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
            var ground=hits.Where(hit=>Topsoil(hit.collider)).OrderBy(hit=>hit.distance).FirstOrDefault();
            if(!ground.collider) throw new InvalidOperationException($"No surveyed soil at {x}, {z}.");
            return ground.point;
        }

        // The playable approach north-west of the casemate has a bare, west-facing
        // cut. The dawn catches the relief and the taller grass along its crest.
        Vector3 eye=SurveySurface(-13.6f,-8.55f)+Vector3.up*1.47f;
        Vector3 face=SurveySurface(-11.15f,-6.65f);
        Vector3 direction=face-eye;
        if(!Physics.Raycast(eye,direction.normalized,out var hit,direction.magnitude+.16f,
             Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore) || !Topsoil(hit.collider))
            throw new InvalidOperationException("A foreground object hides the casemate approach bank.");
        Shot("13-земляной-откос",eye,face,46);
    }

    void Shot(string label,Vector3 where,Vector3 focus,float fov) => Shot(output,label,where,focus,fov);

    internal static void Shot(string output,string label,Vector3 where,Vector3 focus,float fov)
    {
        var host=new GameObject("Камера проверки");
        host.transform.SetPositionAndRotation(where,Quaternion.LookRotation(focus-where));
        var cam=host.AddComponent<Camera>();
        cam.enabled=false;cam.cullingMask=~(1<<5);cam.nearClipPlane=.035f;cam.farClipPlane=400;
        cam.clearFlags=CameraClearFlags.Skybox;cam.fieldOfView=fov;cam.allowHDR=true;
        cam.GetUniversalAdditionalCameraData().renderPostProcessing=true;
        var rt=RenderTexture.GetTemporary(1440,810,24,RenderTextureFormat.ARGB32);
        var old=RenderTexture.active;
        var frame=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
        try
        {
            cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;
            frame.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);frame.Apply();
            File.WriteAllBytes(Path.Combine(output,label+".png"),frame.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active=old;cam.targetTexture=null;
            RenderTexture.ReleaseTemporary(rt);Destroy(frame);Destroy(host);
        }
    }
}
