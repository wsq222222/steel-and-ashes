using System;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Six fresh deployments using the physical player controller. Only a flagged headless player can run this.</summary>
[DefaultExecutionOrder(500)]
public sealed class OsowiecStartMatrix : MonoBehaviour
{
    enum Phase { Scene, Order, Confirm, TakeControl, Walk }
    Phase phase;
    int run, nextFrame;
    float deadline, spentAtStart, walked, tacticalWalked;
    Vector3 origin,last, waypoint, goal;
    bool passedNeighbour;
    Unit actor, neighbour;
    Gamepad pad;
    InputAction walk;
    string playerException;
    GameManager lastGame;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (Application.isEditor || !Application.isBatchMode ||
            !Environment.GetCommandLineArgs().Contains("-osowiec-start-matrix")) return;
        var host=new GameObject("Physical start matrix");
        DontDestroyOnLoad(host);
        host.AddComponent<OsowiecStartMatrix>();
    }

    void Start()
    {
        Application.targetFrameRate=60;
        pad=InputSystem.AddDevice<Gamepad>();
        walk=new InputAction("Physical start input",InputActionType.Value,"<Gamepad>/leftStick");
        walk.performed+=Move;
        walk.canceled+=Move;
        walk.Enable();
        Application.logMessageReceived+=Log;
        GameManager.SetGameMode(GameMode.PlayerVsBot);
        NewPhase(Phase.Scene);
        SceneManager.LoadScene("Osowiec");
    }

    void OnDestroy()
    {
        Application.logMessageReceived-=Log;
        walk?.Dispose();
        if (pad!=null && pad.added) InputSystem.RemoveDevice(pad);
    }

    void Move(InputAction.CallbackContext input) { if (actor) actor.OnMove(input); }
    void Log(string message,string stack,LogType type)
    {
        if (type==LogType.Exception && playerException==null) playerException=message+"\n"+stack;
    }
    static void Check(bool value,string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
    void NewPhase(Phase next)
    {
        phase=next;
        nextFrame=Time.frameCount+2;
        deadline=Time.realtimeSinceStartup+24f;
    }
    static readonly Vector2Int[] allCells=OsowiecDeploymentSectors.Cells.SelectMany(s=>s).ToArray();
    Vector2Int Cell => allCells[run];

    void LateUpdate()
    {
        try
        {
            Check(playerException==null,"player exception: "+playerException);
            if (Time.frameCount<nextFrame) return;
            Check(Time.realtimeSinceStartup<deadline,$"timed out in {phase}, walked {walked:F2} m at {last}");
            Advance();
        }
        catch(Exception error)
        {
            Debug.LogError($"[Osowiec physical starts] FAIL {Cell} {error}");
            enabled=false;
            Application.Quit(2);
        }
    }

    void Advance()
    {
        var game=GameManager.Instance;
        switch(phase)
        {
            case Phase.Scene:
                if (!game || game==lastGame || !game.IsArmyDeploymentPhase()) return;
                var deployment=FindFirstObjectByType<ArmyDeploymentController>();
                if (!deployment || deployment.GetPointsRemaining()==0) return;
                var slots=OsowiecDeploymentSectors.Cells[OsowiecDeploymentSectors.IndexOf(Cell)];
                var occupied=slots.First(slot=>slot!=Cell);
                if(deployment.Sectors.Sector!=OsowiecDeploymentSectors.IndexOf(Cell)) deployment.Sectors.Switch();
                Check(deployment.TryPlaceOfferAtGridCell(0,Cell) && deployment.TryPlaceOfferAtGridCell(1,occupied),
                    "failed to spawn both real capsules");
                var squad=FindObjectsByType<Unit>(FindObjectsSortMode.None).Where(u=>u.owner==Player.Player1).ToArray();
                actor=squad.Single(u=>u.chessType==ChessUnitType.Молот);
                neighbour=squad.Single(u=>u.chessType==ChessUnitType.Циклоп);
                var sheet=FindFirstObjectByType<OsowiecPaperPlacement>();
                Check(sheet,"deployment sheet missing");
                sheet.Confirm();
                Check(!game.IsArmyDeploymentPhase(),"still in deployment");
                lastGame=game;
                NewPhase(Phase.Order);
                return;
            case Phase.Order:
                OsowiecPlanner.Instance.Select(actor);
                OsowiecPlanner.Instance.Preview(OsowiecOperation.Telephone);
                NewPhase(Phase.Confirm);
                return;
            case Phase.Confirm:
                Check(OsowiecPlanner.Instance.PlannedMetres>5,$"no ordinary planned route from {actor.transform.position} to {OsowiecOperation.Instance.NextObjective}");
                OsowiecPlanner.Instance.ConfirmOrder();
                NewPhase(Phase.TakeControl);
                return;
            case Phase.TakeControl:
                Check(actor.IsControlled() && CameraManager.Instance.GetCurrentControlledUnit()==actor,
                    "map order did not enter FPS");
                Check(actor.GetComponent<CharacterController>().enabled && neighbour.GetComponent<CharacterController>().enabled,
                    "inactive player or neighbour collider");
                origin=last=actor.transform.position;
                spentAtStart=actor.GetRemainingMoveMeters();
                Check(Mathf.Abs(spentAtStart-OsowiecPlanner.MoveBudget(actor))<.1f,"incorrect initial movement budget");
                walked=tacticalWalked=0;
                bool atWard=OsowiecDeploymentSectors.IndexOf(Cell)==0;
                Vector3 along=atWard?Vector3.right:Vector3.back;
                Vector3 across=atWard?Vector3.forward:Vector3.right;
                Vector3 neighbourOffset=neighbour.transform.position-origin;
                passedNeighbour=Vector3.Dot(neighbourOffset,along)<=0 || Mathf.Abs(Vector3.Dot(neighbourOffset,across))>.8f;
                float clearance=Vector3.Dot(neighbourOffset,across)>=0?-1:1;
                if(atWard)clearance=Cell.y==15?1:-1;
                waypoint=origin+across*clearance;
                goal=(passedNeighbour?origin:waypoint)+along*4f;
                NewPhase(Phase.Walk);
                return;
            case Phase.Walk:
                Vector3 now=actor.transform.position;
                float displacement=Vector3.ProjectOnPlane(now-last,Vector3.up).magnitude;
                walked+=displacement;
                if (!OsowiecMarch.Free(actor)) tacticalWalked+=displacement;
                last=now;
                Vector3 toCorner=Vector3.ProjectOnPlane(waypoint-now,Vector3.up);
                if (!passedNeighbour && toCorner.magnitude<.13f) passedNeighbour=true;
                Vector3 remaining=Vector3.ProjectOnPlane(goal-now,Vector3.up);
                if (remaining.magnitude<.16f)
                {
                    InputState.Change(pad,new GamepadState());
                    float spent=spentAtStart-actor.GetRemainingMoveMeters();
                    Check(walked>3.7f && Mathf.Abs(spent-tacticalWalked)<.27f,
                        $"actual travel {walked:F3}, tactical {tacticalWalked:F3}, charged {spent:F3}");
                    Check(Mathf.Abs(now.y-origin.y)<.85f,"capsule fell through or mounted a wall");
                    Check(neighbour.GetHealth()>0 && !neighbour.IsControlled(),"neighbour lost");
                    Debug.Log($"[Osowiec physical starts] PASS cell={Cell} real capsule moved={walked:F2}m charged={spent:F2}m "+
                        $"rise={now.y-origin.y:F2}m neighbour={neighbour.currentGridPosition}");
                    if (++run==allCells.Length)
                    {
                        Debug.Log("[Osowiec physical starts] PASS all six deployed starts: occupied neighbour, map order, physical travel, budget.");
                        enabled=false;
                        Application.Quit(0);
                    }
                    else
                    {
                        actor=null;
                        neighbour=null;
                        NewPhase(Phase.Scene);
                        game.RestartGame();
                    }
                    return;
                }
                var direction=actor.transform.InverseTransformDirection((passedNeighbour ? remaining : toCorner).normalized);
                InputState.Change(pad,new GamepadState {leftStick=new Vector2(direction.x,direction.z)});
                return;
        }
    }
}
