using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

// -osowiec-hit-check exercises the same imported meshes, weapon configurations and shot entry point as the player.
public sealed class OsowiecHitCheck : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        var args = Environment.GetCommandLineArgs();
        if (OsowiecHelmetViewCheck.Install(args)) return;
        bool fieldThreat = Array.IndexOf(args,"-osowiec-field-threat-check") >= 0;
        bool roles = Array.IndexOf(args,"-osowiec-role-check") >= 0;
        bool escort = Array.IndexOf(args,"-osowiec-escort-threat-check") >= 0;
        if (!fieldThreat && !roles && !escort && Array.IndexOf(args,"-osowiec-hit-check") < 0) return;
        var host = new GameObject("Osowiec hit registration check");
        DontDestroyOnLoad(host);
        if (escort) host.AddComponent<OsowiecEscortThreatCheck>();
        else if (fieldThreat) host.AddComponent<OsowiecFieldThreatCheck>();
        else host.AddComponent<OsowiecHitCheck>();
    }

    IEnumerator Start()
    {
        GameManager.SetGameMode(GameMode.PlayerVsBot);
        SceneManager.LoadScene("Osowiec");
        float deadline = Time.realtimeSinceStartup + 60;
        while (!OsowiecOperation.Instance || !GameManager.Instance.IsArmyDeploymentPhase())
        {
            if (Time.realtimeSinceStartup > deadline) { Fail("Mission failed to load"); yield break; }
            yield return null;
        }
        try
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-osowiec-role-check") >= 0)
                OsowiecRoleCheck.Run();
            else
            {
                Run();
                Debug.Log("PASS Osowiec hit check: shell / bare head / body / limb / miss / cover / animated head");
            }
            Application.Quit(0);
        }
        catch (Exception e) { Fail(e.ToString()); }
    }

    static void Fail(string message) { Debug.LogError("FAIL Osowiec hit check: " + message); Application.Quit(2); }
    static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    static OsowiecHitPart Part(Unit u, OsowiecHitArea kind) =>
        u.GetComponentsInChildren<OsowiecHitPart>().First(p=>p.area==kind);
    static Vector3 Center(OsowiecHitPart part) => part.GetComponent<Collider>().bounds.center;

    static void CheckWornShell(Unit unit, int expected)
    {
        var renderers = unit.GetComponentsInChildren<Renderer>(true);
        var worn = renderers.Where(r => r.enabled && !r.forceRenderingOff && r.gameObject.activeInHierarchy &&
            (r.name == "IssuedHelmet" || r.GetComponentInParent<OsowiecHitPart>()?.area == OsowiecHitArea.Helmet));
        Require(worn.Count() == expected, $"{unit.name} shows {worn.Count()} helmet renderers, expected {expected}");
        Require(renderers.OfType<SkinnedMeshRenderer>().Any(r => r.enabled && r.gameObject.activeInHierarchy &&
            r.name == "Uniform_and_equipment" && r.bones.Any(joint => joint && joint.name == "Head")),
            $"{unit.name} lost its skinned head and face");
    }

    static void HelmetRoster(ArmyDeploymentController deployment, Unit[] defenders)
    {
        var templates = defenders.GroupBy(u => u.chessType).Select(g => g.First().gameObject).ToList();
        Require(templates.Count == 3, "Expected three opposing unit classes");
        for (int i = 0; i < 3; i++)
        {
            Require(deployment.TryGetOffer(i, out var offer), "Missing deployment offer " + i);
            templates.Add(offer.unitPrefab);
        }
        foreach (var template in templates)
        {
            var unit = Instantiate(template, Vector3.up * 170, Quaternion.identity).GetComponent<Unit>();
            unit.gameObject.SetActive(true);
            var presentation = unit.GetComponent<TacticalUnitPresentation>() ?? unit.gameObject.AddComponent<TacticalUnitPresentation>();
            presentation.SetBodyVisualVisible(false);
            presentation.SetBodyVisualVisible(true);
            var shell = unit.GetComponentsInChildren<OsowiecHitPart>().SingleOrDefault(p => p.area == OsowiecHitArea.Helmet);
            int expected = unit.owner == Player.Player2 || unit.chessType != ChessUnitType.Guardian ? 1 : 0;
            Require((shell != null) == (expected == 1), "Wrong headgear " + template.name);
            CheckWornShell(unit, expected);
            if (shell)
            {
                var before = FindObjectsByType<Rigidbody>(FindObjectsSortMode.None);
                shell.DropHelmet(unit, Vector3.right);
                presentation.SetBodyVisualVisible(false);
                presentation.SetBodyVisualVisible(true);
                shell.DropHelmet(unit, Vector3.right);
                CheckWornShell(unit, 0);
                var loose = FindObjectsByType<Rigidbody>(FindObjectsSortMode.None).Except(before).Single(b => b.name == "Сбитая каска");
                Require(loose.GetComponent<MeshFilter>().sharedMesh == shell.helmetMesh, "Wrong dropped mesh " + template.name);
                loose.gameObject.SetActive(false);
                Destroy(loose.gameObject);
            }
            Debug.Log($"[Osowiec helmet check] {template.name}, worn after show={expected}, retained after drop=0");
            unit.gameObject.SetActive(false);
            Destroy(unit.gameObject);
        }
    }

    static void Run()
    {
        var deployment = FindFirstObjectByType<ArmyDeploymentController>();
        Require(deployment.TryGetOffer(0,out var assault), "Missing player prefab");
        var defenders = FindObjectsByType<Unit>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(u => u.owner == Player.Player2 && u.GetEquippedWeapon() && u.GetComponent<OsowiecHitBody>()).ToArray();
        HelmetRoster(deployment, defenders);
        var shooter = defenders.First(u=>!u.GetEquippedWeapon().Config.isMarksmanRifle);
        var marksman = defenders.First(u=>u.GetEquippedWeapon().Config.isMarksmanRifle);
        var game=GameManager.Instance;
        var camera=CameraManager.Instance;
        var grid=ChessGrid.Instance;
        bool placed=false;
        for(int y=0;y<grid.height && !placed;y++)
            for(int x=0;x<grid.width && !placed;x++)
            {
                var cell=new Vector2Int(x,y);
                if(deployment.IsCellInDeploymentZone(cell)) placed=deployment.TryPlaceOfferAtGridCell(0,cell);
            }
        Require(placed,"Could not place player");
        FindFirstObjectByType<OsowiecPaperPlacement>().Confirm();
        var player=FindObjectsByType<Unit>(FindObjectsSortMode.None).First(u=>u.owner==Player.Player1);
        var playerGun=player.GetEquippedWeapon();
        Require(!playerGun.TryFire(player,player.transform.position,Vector3.forward),"Map accepted player fire");
        var planner=OsowiecPlanner.Instance;
        planner.Select(player);
        foreach(var offset in new[]{Vector3.right,Vector3.left,Vector3.forward,Vector3.back})
        {
            planner.Preview(player.transform.position+offset);
            if(planner.PlannedMetres>.05f) break;
        }
        planner.ConfirmOrder();
        Require(camera.IsActionMode() && camera.GetCurrentControlledUnit()==player,"FPS order failed");
        int rounds=playerGun.AmmoInMag;
        player.Attack();
        Require(playerGun.AmmoInMag==rounds-1,"FPS input did not fire");
        // Same handler used by Tab/middle mouse while in FPS. This also switches turn owner.
        camera.SwitchToTacticalMode();
        Require(!camera.IsActionMode() && game.IsBotTurn(),"Tab handler did not pass turn");
        Require(!OsowiecFireTurn.Allows(player),"Contrary turn still accepts player fire");
        Vector3 station = new Vector3(0, 140, 0);
        Unit Target()
        {
            station += Vector3.right * 6;
            var target = Instantiate(assault.unitPrefab, station, Quaternion.identity).GetComponent<Unit>();
            target.owner = Player.Player1;
            Physics.SyncTransforms();
            return target;
        }
        void Shoot(Unit shooterTemplate, Unit target, Vector3 point)
        {
            var gunner = Instantiate(shooterTemplate, point + Vector3.forward * 3 - Vector3.up*1.4f,
                Quaternion.LookRotation(Vector3.back));
            gunner.gameObject.SetActive(true);
            var gun = gunner.GetEquippedWeapon();
            int rounds = gun.AmmoInMag;
            Require(gun.TryFire(gunner, point + Vector3.forward*3, Vector3.back),
                $"Shot rejected: {gunner.name} team {gunner.owner} current {game.currentPlayer} allows {OsowiecFireTurn.Allows(gunner)} mag {gun.AmmoInMag}");
            Require(gun.AmmoInMag == rounds - 1, "Shot did not spend a round");
            gunner.gameObject.SetActive(false);
            Destroy(gunner.gameObject);
        }

        var idlePlayer = Target();
        var idleGun = idlePlayer.GetEquippedWeapon();
        Require(!idleGun.TryFire(idlePlayer,station+Vector3.up,Vector3.forward) &&
            !idleGun.TryFire(idlePlayer,station+Vector3.up,Vector3.forward),"Out-of-turn player fired twice");
        var patient = Target();
        int initial = patient.GetHealth();
        var helmet = Part(patient, OsowiecHitArea.Helmet);
        Bounds shell = helmet.GetComponent<Collider>().bounds;
        Vector3 crown = shell.center + Vector3.up * shell.extents.y * .73f;
        int BeforeDrop() => FindObjectsByType<Rigidbody>(FindObjectsSortMode.None).Count(b=>b.name=="Сбитая каска");
        int debris = BeforeDrop();
        var presentation = patient.GetComponent<TacticalUnitPresentation>() ?? patient.gameObject.AddComponent<TacticalUnitPresentation>();
        presentation.SetBodyVisualVisible(true);
        CheckWornShell(patient, 1);
        int Damage(Unit attacker, OsowiecHitArea area) =>
            OsowiecFieldTurn.ShotDamage(attacker.GetEquippedWeapon().Config.damagePerHit,area,false,3,false,1f);
        Shoot(marksman, patient, crown);
        int afterShell=initial-Damage(marksman,OsowiecHitArea.Helmet);
        presentation.SetBodyVisualVisible(false);
        presentation.SetBodyVisualVisible(true);
        CheckWornShell(patient, 0);
        Require(patient.GetHealth()==afterShell && !helmet.gameObject.activeSelf && BeforeDrop()==debris+1, "Helmet failed to separate cleanly");
        var drop = FindObjectsByType<Rigidbody>(FindObjectsSortMode.None).First(b=>b.name=="Сбитая каска");
        Require(drop.GetComponent<MeshFilter>().sharedMesh==helmet.helmetMesh && drop.useGravity, "Dropped mesh mismatch");
        Shoot(shooter, patient, crown);
        Require(BeforeDrop()==debris+1 && patient.GetHealth()==afterShell,"Duplicate helmet or false hit above bare skull");

        var head = Part(patient,OsowiecHitArea.Head);
        var joint = head.transform.parent;
        joint.localRotation *= Quaternion.Euler(0, 0, 24f);
        Physics.SyncTransforms();
        var cheek = Center(head) - Vector3.up*.036f;
        Shoot(shooter,patient,cheek);
        Require(patient.GetHealth()==afterShell-Damage(shooter,OsowiecHitArea.Head),"Exposed head after helmet loss used wrong profile");
        patient = Target();
        var protectedHead = Part(patient,OsowiecHitArea.Head);
        var capsule = protectedHead.GetComponent<CapsuleCollider>();
        Vector3 Point(float height) => capsule.transform.TransformPoint(capsule.center+Vector3.up*(height-.5f)*capsule.height);
        foreach (var direction in new[]{Vector3.forward,Vector3.right,(Vector3.right+Vector3.forward).normalized})
        {
            foreach (float height in new[]{.40f,.55f,.70f})
            {
                var point = Point(height);
                Require(OsowiecHitBody.Trace(marksman,new Ray(point+direction*3,-direction),4,~0,out _,out var covered)
                    && covered && covered.area==OsowiecHitArea.Helmet,"Missing helmet protection at "+height+" from "+direction);
            }
        }
        Shoot(marksman,patient,Point(.19f));
        Require(patient.GetHealth()==initial-Damage(marksman,OsowiecHitArea.Head),"Exposed lower head did not use capped critical damage");

        patient = Target();
        initial = patient.GetHealth();
        var trunk = Center(Part(patient, OsowiecHitArea.Torso));
        Shoot(marksman,patient,trunk + Vector3.right*1.1f);
        Require(patient.GetHealth()==initial,"Near miss damaged target");
        var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plate.name="Opaque test obstacle";
        plate.transform.position=trunk+Vector3.forward;
        plate.transform.localScale=new Vector3(.7f,.8f,.15f);
        Shoot(marksman,patient,trunk);
        Require(patient.GetHealth()==initial,"Shot passed through solid cover");
        plate.SetActive(false);
        Destroy(plate);
        Shoot(shooter,patient,trunk);
        Require(Mathf.Abs(initial-patient.GetHealth()-shooter.GetEquippedWeapon().Config.damagePerHit)<=(shooter.chessType==ChessUnitType.Pawn?2:1) && patient.GetHealth()>0,"Carbine body damage");
        patient = Target();
        Shoot(marksman,patient,Center(Part(patient,OsowiecHitArea.Torso)));
        Require(patient.GetHealth()==initial-Damage(marksman,OsowiecHitArea.Torso) && patient.GetHealth()>0,"Marksman torso incorrectly lethal");
        patient = Target();
        var shin = patient.GetComponentsInChildren<OsowiecHitPart>().First(p=>p.name=="Hit LShin");
        Shoot(marksman,patient,Center(shin));
        Require(patient.GetHealth()==initial-Damage(marksman,OsowiecHitArea.Limb),"Limb used incorrect profile");

        patient = Target();
        var waiting = Instantiate(shooter, patient.transform.position + Vector3.forward*3, Quaternion.LookRotation(Vector3.back));
        waiting.gameObject.SetActive(true);
        var tool = waiting.GetEquippedWeapon();
        int loaded = tool.AmmoInMag;
        Physics.SyncTransforms();
        var staged=BotController.Instance.PerformAttackInternal(waiting,patient);
        Require(staged.MoveNext() && staged.Current is IEnumerator,"Missing bot attack coroutine");
        var attack=(IEnumerator)staged.Current;
        Require(attack.MoveNext() && attack.Current is WaitForSeconds,"Bot never entered its aiming delay");
        game.SwitchTurn();
        Require(!game.IsBotTurn(),"Bot kept its turn");
        Require(!attack.MoveNext() && tool.AmmoInMag==loaded,"Queued bot shot escaped after turn transfer");
        Require(!tool.TryFire(waiting, waiting.transform.position+Vector3.up,Vector3.back) &&
            !tool.TryFire(waiting,waiting.transform.position+Vector3.up,Vector3.back) && patient.GetHealth()==initial,
            "Ended bot turn still permits fire");
        Debug.Log("[Osowiec hit check] FPS, Tab, stale shots from both owners, queued bot attack passed.");
    }
}
