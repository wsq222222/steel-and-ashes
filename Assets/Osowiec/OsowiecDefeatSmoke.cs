using System;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Separate Windows player regression run: -batchmode -nographics -osowiec-defeat-smoke.</summary>
public sealed class OsowiecDefeatSmoke : MonoBehaviour
{
    enum Step { Load, Deploy, Reload, Approach, PlayerWatch, Defeat, Restart }
    Step step;
    float deadline;
    GameManager original;
    Unit[] squad;
    Weapon carbine;
    string playerException;
    string expectedEntry;
    int routeFrame;
    Vector3[] deploymentPositions;
    int observedGuards;
    float watchStart;
    OsowiecAlarmArrivalProbe[] arrivalProbes;

    void OnEnable() => Application.logMessageReceived+=WatchExceptions;
    void OnDisable() => Application.logMessageReceived-=WatchExceptions;

    void WatchExceptions(string message,string stack,LogType type)
    {
        if (type==LogType.Exception && playerException==null) playerException=message+"\n"+stack;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (Application.isEditor || !Application.isBatchMode ||
            !Environment.GetCommandLineArgs().Contains("-osowiec-defeat-smoke")) return;
        var host=new GameObject("Batch defeat/restart check");
        DontDestroyOnLoad(host);
        host.AddComponent<OsowiecDefeatSmoke>();
    }

    void Advance(Step next)
    {
        step=next;
        deadline=Time.realtimeSinceStartup+60f;
        Debug.Log("[Osowiec defeat smoke] "+next);
    }

    static void Check(bool ok,string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }

    static Unit[] Allies() => FindObjectsByType<Unit>(FindObjectsSortMode.None)
        .Where(u=>u.owner==Player.Player1 && u.GetHealth()>0).ToArray();

    static bool Place(ArmyDeploymentController deployment,int offer)
    {
        var grid=ChessGrid.Instance;
        for(int y=0;y<grid.height;y++)
        for(int x=0;x<grid.width;x++)
        {
            var cell=new Vector2Int(x,y);
            if (deployment.IsCellInDeploymentZone(cell) && deployment.TryPlaceOfferAtGridCell(offer,cell)) return true;
        }
        return false;
    }

    void Update()
    {
        try { RunStep(); }
        catch(Exception error)
        {
            Debug.LogError("[Osowiec defeat smoke] FAIL "+step+" "+error);
            enabled=false;
            Application.Quit(2);
        }
    }

    void RunStep()
    {
        Check(playerException==null,"player exception: "+playerException);
        if (step==Step.Load)
        {
            string[] flags=Environment.GetCommandLineArgs();
            if (flags.Contains("-osowiec-report-tunnel")) expectedEntry="Через каземат";
            if (flags.Contains("-osowiec-report-lowland")) expectedEntry="Через низину";
            if (expectedEntry!=null)
                Check(SceneManager.GetActiveScene().name=="OsowiecHQ" && FindFirstObjectByType<OsowiecTableHint>(),
                    "normal headquarters/table missing at player launch");
            Advance(Step.Deploy);
            GameManager.SetGameMode(GameMode.PlayerVsBot);
            SceneManager.LoadScene("Osowiec");
            return;
        }
        Check(Time.realtimeSinceStartup<deadline,"timed out");
        var game=GameManager.Instance;
        switch(step)
        {
            case Step.Deploy:
                if (!game || !game.IsArmyDeploymentPhase() || !ChessGrid.Instance) return;
                var deployment=FindFirstObjectByType<ArmyDeploymentController>();
                if (!deployment || deployment.GetPointsRemaining()==0) return;
                Check(deployment.GetPointsRemaining()==12 && Allies().Length==0,"dirty initial deployment");
                Check(Place(deployment,0) && Place(deployment,1),"could not deploy both roles");
                squad=Allies();
                deploymentPositions=squad.Select(unit=>unit.transform.position).ToArray();
                Check(squad.Length==2 && deployment.GetPointsRemaining()==3,"incorrect squad or price");
                Check(squad.All(u=>u.GetEquippedWeapon().AmmoInMag==5 && u.GetEquippedWeapon().AmmoReserve==20),
                    "both issued weapons must have 5 + 20");
                var sheet=FindFirstObjectByType<OsowiecPaperPlacement>();
                Check(sheet,"deployment sheet missing");
                sheet.Confirm();
                Check(!game.IsArmyDeploymentPhase(),"deployment failed to close");
                original=game;
                carbine=squad[0].GetEquippedWeapon();
                OsowiecPlanner.Instance.Select(squad[0]);
                OsowiecPlanner.Instance.ConfirmOrder();
                Check(carbine.TryFire(squad[0], squad[0].transform.position + Vector3.up * 3, Vector3.up), "initial shot rejected");
                Check(carbine.TryStartReload(false),"reload refused with cartridges in reserve");
                Advance(Step.Reload);
                return;
            case Step.Reload:
                if (carbine.IsReloading) return;
                Check(carbine.AmmoInMag==5 && carbine.AmmoReserve==19,"reload did not consume exactly one cartridge");
                Check(squad[1].GetEquippedWeapon().AmmoReserve==20 && squad[1].GetEquippedWeapon().AmmoInMag==5,
                    "reload changed the other role's weapon");
                if (expectedEntry!=null)
                {
                    var op=OsowiecOperation.Instance;
                    Check(op && op.gate && GameObject.Find("Casemate brick floor")?.GetComponent<MeshFilter>()?.sharedMesh,
                        "mission gate or model missing");
                    var posts=op.relief.alarmGuard;
                    Check(posts.Length==2 && posts[0] && posts[1] && posts[0]!=posts[1] &&
                        posts.All(post=>post.TryGetComponent<Unit>(out var guard) && guard.owner==Player.Player2 &&
                            !post.activeInHierarchy) && op.relief.GuardsArrived==0,"missing or active alarm guards before route choice");
                    arrivalProbes=posts.Select(post=>post.AddComponent<OsowiecAlarmArrivalProbe>()).ToArray();
                    op.SendMessage("Answer",2);
                    squad[0].transform.position=op.gate.position;
                    if(expectedEntry=="Через каземат")
                    {
                        Check(op.TrySetCharge(squad[0]) && op.TrySetCharge(squad[0]) && op.ChargeSet && !op.GateOpen && !op.relief.AlarmRaised,
                            "charge must be installed only once with door and alarm still closed");
                    }
                    else Check(!op.GateOpen && !op.relief.AlarmRaised,"quiet route must start with a closed door");
                    Advance(Step.Approach);
                    return;
                }
                foreach(var member in squad) member.TakeDamage(10000);
                Advance(Step.Defeat);
                return;
            case Step.Approach:
                // Reproduce both ordinary route samples and a later crossing by the surviving partner.
                // Give the regular mission Update a frame at each position; it owns all report state.
                if(expectedEntry=="Через каземат" && !OsowiecOperation.Instance.GateOpen) return;
                Check(OsowiecOperation.Instance.relief.AlarmRaised==(expectedEntry=="Через каземат"),"route/alarm mismatch");
                switch(routeFrame++)
                {
                    case 0:
                        if(expectedEntry=="Через каземат")
                        {
                            var reported=OsowiecOperation.Instance.relief;
                            float shownUntil=reported.ReinforcementNoticeUntil;
                            reported.SoundAlarm();
                            Check(reported.ReinforcementCalls==1 && shownUntil-Time.time>5 &&
                                reported.ReinforcementNoticeUntil==shownUntil &&
                                Resources.Load<AudioClip>("OsowiecSound/ReinforcementBugle"),
                                "repeated alarm leaves one extended reinforcement notice and one bugle trigger");
                        }
                        squad[0].transform.position=OsowiecOperation.Casemate;
                        squad[1].transform.position=OsowiecOperation.Lowland;
                        break;
                    case 1:
                        squad[expectedEntry=="Через каземат"?0:1].transform.position=OsowiecOperation.Junction;
                        break;
                    case 2:
                        Check(OsowiecOperation.Instance.CompletedOrders==3,"first actor did not secure entry");
                        squad[0].transform.position=OsowiecOperation.Lowland;
                        squad[1].transform.position=OsowiecOperation.Casemate;
                        break;
                    case 3:
                        foreach(var member in squad) member.transform.position=OsowiecOperation.Junction;
                        break;
                    default:
                        // Withdraw to the original deployment points so neither spawn is watched.
                        // The real visibility and elapsed game time decide whether it arrives.
                        for(int i=0;i<squad.Length;i++)squad[i].transform.position=deploymentPositions[i];
                        Physics.SyncTransforms();
                        watchStart=Time.time;
                        Advance(Step.PlayerWatch);
                        break;
                }
                return;
            case Step.PlayerWatch:
                var column=OsowiecOperation.Instance.relief;
                int active=column.alarmGuard.Count(g=>g.activeInHierarchy);
                Check(!game.IsBotTurn() && active==column.GuardsArrived,"arrival count or turn mismatch");
                if(expectedEntry=="Через каземат"){
                    if(active!=observedGuards){
                        Check(active==observedGuards+1 && arrivalProbes[observedGuards].Activations==1 &&
                            !arrivalProbes[observedGuards].EnemyTurn,"alarm guard failed to enter separately on the field clock");
                        observedGuards=active;
                    }
                    if(active<2) return;
                }else{
                    Check(!column.AlarmRaised && active==0,"quiet route spawned an alarm guard");
                    if(Time.time-watchStart<5f) return;
                }
                Debug.Log($"[Osowiec reinforcement smoke] PASS route={expectedEntry} liveGuards={active}/2 without passing turn");
                foreach(var member in squad)member.TakeDamage(10000);
                Advance(Step.Defeat);
                return;
            case Step.Defeat:
                if (!game.IsGameOver()) return;
                var rules=OsowiecOperation.Instance;
                Check(rules.resultNote.text.Contains("Отряд потерян") && rules.resultRoster.text.Contains("В строю   0"),
                    "missing defeat report");
                if (expectedEntry!=null)
                {
                    Check(rules.resultRoster.text.Contains(expectedEntry),"wrong arrival in actual report: "+rules.resultRoster.text);
                    Debug.Log("[Osowiec entry smoke] PASS first arrival: "+expectedEntry+"; late switches ignored.");
                }
                var restart=FindObjectsByType<Button>(FindObjectsSortMode.None).Single(b=>b.name=="Повторить операцию" &&
                    Enumerable.Range(0,b.onClick.GetPersistentEventCount()).Any(i=>
                        b.onClick.GetPersistentTarget(i)==game && b.onClick.GetPersistentMethodName(i)==nameof(GameManager.RestartGame)));
                Check(restart.interactable,"restart disabled");
                restart.onClick.Invoke();
                Advance(Step.Restart);
                return;
            case Step.Restart:
                if (!game || game==original || !game.IsArmyDeploymentPhase()) return;
                var fresh=FindFirstObjectByType<ArmyDeploymentController>();
                if (!fresh || fresh.GetPointsRemaining()==0) return;
                Check(!game.IsGameOver() && !game.IsPaused() && fresh.GetPointsRemaining()==12 && Allies().Length==0 &&
                    Time.timeScale>0f,"restart retained defeated squad, budget or paused state");
                Check(FindObjectsByType<Unit>(FindObjectsSortMode.None).Count(u=>u.owner==Player.Player2 && u.GetHealth()>0)>=4,
                    "opponents missing after restart");
                Debug.Log("[Osowiec defeat smoke] PASS 4+20 -> 5+19; second role 5+20; defeat report 0; restart 12 points.");
                enabled=false;
                Application.Quit(0);
                return;
        }
    }
}

// Attach only to the two inactive actors in the Windows fixture. OnEnable samples
// the real turn at the instant the ordinary relief update activates the actor.
public sealed class OsowiecAlarmArrivalProbe : MonoBehaviour
{
    public int Activations { get; private set; }
    public bool EnemyTurn { get; private set; }

    void OnEnable(){
        Activations++;
        EnemyTurn=GameManager.Instance && GameManager.Instance.IsBotTurn();
    }
}
