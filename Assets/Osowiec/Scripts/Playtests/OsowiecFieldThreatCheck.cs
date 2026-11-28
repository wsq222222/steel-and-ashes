using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Installs only from the explicit standalone QA flag in OsowiecHitCheck.
public sealed class OsowiecFieldThreatCheck : MonoBehaviour
{
    static void Require(bool valid, string label)
    {
        if (!valid) throw new InvalidOperationException(label);
    }

    void OnEnable() => Application.logMessageReceived += WatchErrors;
    void OnDisable() => Application.logMessageReceived -= WatchErrors;
    void WatchErrors(string message, string trace, LogType type)
    {
        if (type == LogType.Exception) Application.Quit(2);
    }

    static void Place(Unit unit, Vector3 location, Vector3 direction)
    {
        var motor = unit.GetComponent<CharacterController>();
        bool active = motor && motor.enabled;
        if (active) motor.enabled = false;
        unit.transform.SetPositionAndRotation(location, Quaternion.LookRotation(direction));
        if (active) motor.enabled = true;
        Physics.SyncTransforms();
    }

    IEnumerator Start()
    {
        Application.runInBackground = true;
        string[] args=Environment.GetCommandLineArgs();
        int frames=Array.IndexOf(args,"-osowiec-threat-frames");
        string output=frames>=0 && frames+1<args.Length ? args[frames+1] : null;
        if(output!=null) Directory.CreateDirectory(output);
        GameManager.SetGameMode(GameMode.PlayerVsBot);
        SceneManager.LoadScene("Osowiec");
        for (int frame = 0; frame < 6; frame++) yield return null;
        var op = OsowiecOperation.Instance;
        var game = GameManager.Instance;
        var deployment = FindFirstObjectByType<ArmyDeploymentController>();
        var paper = FindFirstObjectByType<OsowiecPaperPlacement>();
        Require(op && game && deployment && paper, "Mission fixture missing");
        Require(deployment.TryPlaceOfferAtGridCell(0, OsowiecDeploymentSectors.Cells[0][0]), "Could not place player");
        var player = FindObjectsByType<Unit>(FindObjectsSortMode.None).Single(u => u.owner == Player.Player1);
        var sentries = FindObjectsByType<Unit>(FindObjectsSortMode.None).Where(u => u.owner == Player.Player2).ToArray();
        var npc = sentries.First(u => u.name == "Дозорный");
        Require(!OsowiecOperation.InGas(npc) && !OsowiecOperation.InGas(sentries.First(u => u.name == "Сменный дозор")),
            "The quiet route guards spawn inside the cloud");
        Require(sentries.All(u=>!OsowiecOperation.InGas(u)), "An initial active NPC spawns in gas");
        foreach (var other in sentries) if (other != npc) other.gameObject.SetActive(false);
        paper.Confirm();
        yield return null;
        Vector3 a = new(-16,1,12), b = new(17,1,12);
        float depth = OsowiecOperation.GasSightDepth(a,b);
        Require(Mathf.Abs(depth - 26) < .02f && Mathf.Abs(depth - OsowiecOperation.GasSightDepth(b,a)) < .001f,
            "Asymmetric or incomplete cloud intersection");
        Require(!OsowiecFieldKit.Obscured(new Vector3(-13,1,12),new Vector3(-11,1,12)) &&
            OsowiecFieldKit.Obscured(a,b), "Visible cloud fringe or blocked sight line incorrect");
        Place(npc,new Vector3(2,.2f,13),Vector3.right);
        Place(player,new Vector3(-7,.2f,13),Vector3.left);
        Vector3 exit = OsowiecBotBrain.ExitCloud(npc);
        Require(!OsowiecOperation.GasAt(exit + Vector3.up*.5f), "NPC has no dry exit");
        float initialFilter = op.RemainingFilter(npc);
        int hp = player.GetHealth();
        yield return new WaitForSeconds(1.48f);
        Require(op.MaskOn(npc) && initialFilter-op.RemainingFilter(npc) >= 1.35f && player.GetHealth()<hp &&
            !op.relief.AlarmRaised,"Player tactical time, unobserved crossing, or NPC filter");
        float before = op.RemainingFilter(npc);
        game.PauseGame();
        yield return new WaitForSecondsRealtime(.24f);
        Require(op.RemainingFilter(npc) == before, "Paused filter advanced");
        game.ResumeGame();
        Time.timeScale = .25f;
        float simulated = Time.time;
        yield return new WaitForSecondsRealtime(.85f);
        float elapsed = Time.time - simulated;
        Require(elapsed < .55f && Mathf.Abs((before - op.RemainingFilter(npc)) - elapsed) < .20f, "Filter ignored slow time");
        Place(player,new Vector3(-38,.12f,-16),Vector3.left);
        int npcHealth = npc.GetHealth();
        Time.timeScale = 4;
        float escapeBy = Time.realtimeSinceStartup + 9;
        while (OsowiecOperation.InGas(npc) && Time.realtimeSinceStartup < escapeBy) yield return null;
        Require(!OsowiecOperation.InGas(npc) && op.RemainingFilter(npc)>0 && npc.GetHealth()==npcHealth &&
            !game.IsBotTurn(),"A waiting NPC exhausted its filter instead of walking to air");
        Debug.Log($"[Field threat] {sentries.Length} initial defenders on dry ground. Waiting sentry reached air with " +
            $"{op.RemainingFilter(npc):F1} filter seconds and {npc.GetHealth()}/{npcHealth} health.");
        Time.timeScale = 1;
        Place(npc,new Vector3(-45,.11f,-16),Vector3.right);
        Place(player,new Vector3(-38,.11f,-16),Vector3.left);
        yield return new WaitForSeconds(.45f);
        Require(!op.relief.AlarmRaised, "Instant alarm");
        yield return new WaitForSeconds(1.5f);
        Require(op.relief.AlarmRaised, "Sustained visible contact failed to raise a late alarm");
        Require(op.SquadSees(npc), "Allied current contact missing");
        var planner = OsowiecPlanner.Instance;
        planner.Select(player);
        planner.Preview(player.transform.position + Vector3.right * 2);
        planner.ConfirmOrder();
        Require(CameraManager.Instance.IsActionMode(), "Player control missing");
        var gun = npc.GetEquippedWeapon();
        var ownGun = player.GetEquippedWeapon();
        Require(ownGun.TryFire(player,player.transform.position+Vector3.up,Vector3.up), "Exposed player did not fire");
        var brain = BotController.Instance.GetComponent<OsowiecBotBrain>();
        Require(brain.TryGetHeardSector(npc,out var sector) &&
            Vector3.Distance(sector,player.transform.position)>1, "Sound revealed the exact camera position");
        op.BeginAim(npc,player,true);
        int rounds = gun.AmmoInMag;
        Require(op.ThreatSource(player)==npc && op.ThreatSeconds(player)>1 &&
            !op.ReleaseAim(npc,player) && gun.AmmoInMag==rounds,"Damage accepted during the warning");
        if(output!=null)
        {
            yield return new WaitForEndOfFrame();
            OsowiecMapCheck.SaveFrame(output,"03-предупреждение-о-выстреле");
        }
        var kit = OsowiecFieldKit.Instance;
        kit.Impact(OsowiecKitItem.Smoke,player,(npc.transform.position+player.transform.position)*.5f);
        Require(!BotController.Instance.CanSeeTargetForBrain(npc,player) && op.CloudContact(npc)==player,
            "Smoke did not hide a previously acquired target");
        int healthBeforeReply = player.GetHealth();
        Place(player,player.transform.position+Vector3.forward*2.4f,Vector3.left);
        yield return new WaitForSeconds(OsowiecOperation.AimSeconds+.28f);
        Require(gun.AmmoInMag==rounds-1 && player.GetHealth()==healthBeforeReply && !op.CloudContact(npc),
            $"Expected one shot at remembered point, rounds {rounds}->{gun.AmmoInMag}, health {healthBeforeReply}->{player.GetHealth()}");
        Debug.Log($"[Field threat] Smoke reply spent {rounds-gun.AmmoInMag} round. Moving target kept {player.GetHealth()} health.");
        Require(!op.SquadSees(npc) && FogWarIntelTracker.Instance.TryGetDisplayPosition(npc,out _,out bool exact) && !exact,
            "Smoke left an exact live map contact");
        var keys = InputSystem.AddDevice<Keyboard>();
        InputSystem.QueueStateEvent(keys,new KeyboardState(Key.T));
        yield return null;
        yield return null;
        Require(op.MaskOn(player), "T did not equip the controlled soldier's mask");
        Place(npc,new Vector3(-45,.12f,-7),Vector3.right);
        Place(player,new Vector3(-38,.12f,-7),Vector3.left);
        Physics.SyncTransforms();
        op.BeginAim(npc,player,true);
        Require(!op.ThreatSource(player) && !op.CanShootThisPhase(npc) && gun.AmmoInMag==rounds-1,
            "A guard could shoot twice during the same player activation");
        CameraManager.Instance.SwitchToTacticalMode();
        Require(game.IsBotTurn() && !op.ReleaseAim(npc,player) && !op.CanShootThisPhase(npc),
            "Reaction allowed a second shot on the adjacent enemy activation");
        Place(npc,new Vector3(2,.2f,13),Vector3.right);
        Vector3 gasStart = npc.transform.position;
        Place(player,new Vector3(-7,.2f,13),Vector3.left);
        before = op.RemainingFilter(npc);
        float allyFilter = op.RemainingFilter(player);
        yield return new WaitForSeconds(.35f);
        Require(op.RemainingFilter(npc)<before-.18f && op.RemainingFilter(player)<allyFilter-.18f,
            "Bot turn froze NPC or allied filter");
        InputSystem.RemoveDevice(keys);
        float deadline = Time.realtimeSinceStartup + 24;
        while (game.IsBotTurn() && Time.realtimeSinceStartup<deadline) yield return null;
        Require(!game.IsBotTurn() && Vector3.ProjectOnPlane(npc.transform.position-gasStart, Vector3.up).magnitude>.75f,
            "NPC did not take a turn to leave the cloud");
        Debug.Log($"[Field threat] NPC moved {Vector3.ProjectOnPlane(npc.transform.position-gasStart,Vector3.up).magnitude:F2} m on its own turn.");
        Place(player,new Vector3(-38,.12f,-16),Vector3.right);
        planner.Select(player);
        planner.Preview(player.transform.position+Vector3.right*2);
        planner.ConfirmOrder();
        Require(op.LocalViewUnit()==player, "Casualty fixture missing first-person view");
        player.TakeDamage(10000,npc);
        Require(CameraManager.Instance.IsActionMode() && !game.IsGameOver() && op.LastLoss.Contains(npc.GetLocalizedDisplayName()),
            "View switched before showing the source of the loss");
        if(output!=null)
        {
            yield return new WaitForEndOfFrame();
            OsowiecMapCheck.SaveFrame(output,"04-причина-потери");
        }
        yield return new WaitForSecondsRealtime(1.8f);
        Require(game.IsGameOver() && !CameraManager.Instance.IsActionMode(),"Casualty review never released the camera");
        Debug.Log($"PASS field threat: both filters, tactical/bot gas, slow time, pause, dry posts and retreat, cloud depth {depth:F1}, quiet crossing, late alarm, approximate sound, warning, single smoke reply, last-known map, cancelled reaction and death review.");
        Application.Quit(0);
    }
}
