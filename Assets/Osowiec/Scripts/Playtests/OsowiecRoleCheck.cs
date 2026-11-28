using System;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

internal static class OsowiecRoleCheck
{
    static void Require(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }

    internal static void Run()
    {
        var deployment = Object.FindFirstObjectByType<ArmyDeploymentController>();
        for (int i=0;i<3;i++)
            Require(deployment.TryPlaceOfferAtGridCell(i, OsowiecDeploymentSectors.Cells[0][i]), "Deployment " + i);
        Object.FindFirstObjectByType<OsowiecPaperPlacement>().Confirm();
        // This test passes the opposing side deterministically after each normal player activation.
        GameManager.SetGameMode(GameMode.PlayerVsPlayer);
        var team = Object.FindObjectsByType<Unit>(FindObjectsSortMode.None).Where(u => u.owner == Player.Player1).ToArray();
        var hammer = team.Single(u => u.chessType == ChessUnitType.Молот);
        var observer = team.Single(u => u.chessType == ChessUnitType.Циклоп);
        var hound = team.Single(u => u.chessType == ChessUnitType.Гончая);
        var planner = OsowiecPlanner.Instance;
        var camera = CameraManager.Instance;
        void EndTurn()
        {
            camera.SwitchToTacticalMode();
            Require(GameManager.Instance.currentPlayer == Player.Player2, "End of activation");
            GameManager.Instance.SwitchTurn();
        }
        Require(hammer.GetHealth() > hound.GetHealth() && hammer.MoveSpeed < hound.MoveSpeed, "Role stats");
        var targetRoot = GameObject.CreatePrimitive(PrimitiveType.Cube);
        targetRoot.transform.position = Vector3.up * 150;
        var target = targetRoot.AddComponent<Unit>();
        target.owner = Player.Player2;
        int Shoot(Unit shooter, float range)
        {
            target.Heal(target.GetMaxHealth());
            int before = target.GetHealth();
            Require(shooter.GetEquippedWeapon().TryFire(shooter, targetRoot.transform.position + Vector3.forward * range,
                Vector3.back), "Authorised shot rejected");
            return before - target.GetHealth();
        }
        planner.Select(hammer);
        Require(planner.ReachableCorners == null, "Unexpected first order");
        planner.ConfirmOrder();
        Require(camera.GetCurrentControlledUnit() == hammer && camera.IsActionMode(), "Enter without route");
        hammer.SetRemainingMoveMeters(0);
        OsowiecMarch.ApplyContact(hammer, true);
        OsowiecMarch.ApplyContact(hammer, false);
        Require(hammer.GetRemainingMoveMeters() >= OsowiecPlanner.FullBudget(hammer)*.5f &&
            !hammer.FieldTurn.Attacked, "Contact after moving must leave movement and an attack");
        int heavyHit = Shoot(hammer, 12);
        Require(OsowiecFieldKit.HoldPosition(hammer), "Assault moved after firing");
        hammer.SetRemainingMoveMeters(1);
        OsowiecMarch.ApplyContact(hammer, true);
        OsowiecMarch.ApplyContact(hammer, false);
        Require(hammer.GetRemainingMoveMeters()==1 && hammer.FieldTurn.Attacked &&
            !hammer.GetEquippedWeapon().TryFire(hammer, hammer.transform.position, Vector3.up),
            "A second contact must not restore another action or movement");
        EndTurn();
        planner.Select(hound);
        planner.ConfirmOrder();
        hound.SetRemainingMoveMeters(3);
        int lightHit = Shoot(hound, 12);
        Require(!OsowiecFieldKit.HoldPosition(hound) && !OsowiecMarch.Free(hound), "Scout lost her remaining movement or gained free movement");
        hound.ConsumeMoveMeters(1.25f);
        Require(Math.Abs(hound.GetRemainingMoveMeters()-1.75f)<.01f &&
            !hound.GetEquippedWeapon().TryFire(hound,hound.transform.position,Vector3.up), "Scout overspent movement or fired twice");
        Require(Math.Abs(heavyHit - hammer.Damage) <= 2 && Math.Abs(lightHit - hound.Damage) <= 1 && heavyHit > lightHit, "Role projectile damage");
        EndTurn();
        Require(!hound.FieldTurn.Attacked && !hound.FieldTurn.RifleSpent,"Scout did not reset on turn change");
        Require(!observer.FieldTurn.Observed, "Waiting in the roster charged observation");
        planner.Select(observer);
        planner.Preview(OsowiecOperation.Telephone);
        float route = planner.RequestedMetres;
        Require(route > 2, "No real route from deployment");
        planner.ConfirmOrder();
        const float full = 18;
        observer.FieldTurn.Walk(full);
        EndTurn();
        Require(!observer.FieldTurn.Observed && observer.FieldTurn.Breath(full) == 0f, "Walking charged observation or exertion carried over");
        planner.Select(hound);
        planner.Select(observer);
        Require(Mathf.Abs(planner.RequestedMetres - route) < .05f && planner.ReachableCorners != null, "Lost destination");
        planner.ConfirmOrder();
        Require(Mathf.Abs(observer.GetRemainingMoveMeters() - full) < .01f, "Full movement after a long march");
        EndTurn();
        Require(observer.FieldTurn.Observed, "Stationary own activation did not charge");
        planner.Select(hound);
        planner.ConfirmOrder();
        EndTurn();
        Require(observer.FieldTurn.Observed, "Another unit spent the stored observation");
        planner.Select(observer);
        planner.ConfirmOrder();
        int bonus = Shoot(observer, 14);
        Require(bonus == OsowiecFieldTurn.ShotCap && !observer.FieldTurn.Observed, "Observed long shot");
        EndTurn();
        Require(!observer.FieldTurn.Observed, "Firing regenerated observation");
        planner.Select(observer);
        planner.ConfirmOrder();
        EndTurn();
        Require(OsowiecPlanner.MoveBudget(observer) == full && observer.FieldTurn.Observed, "New turn / new observation");
        bool close = observer.FieldTurn.Fire();
        Require(OsowiecFieldTurn.ShotDamage(observer.Damage,OsowiecHitArea.Torso,close,5,false,1f) == observer.Damage &&
            !observer.FieldTurn.Fire(), "Close shot / one-shot consumption");
        var transition = new OsowiecFieldTurn();
        Require(!transition.EnterContact(false, 1) && !transition.EnterContact(true, 2) && transition.EnterContact(false, 3), "First contact after marching");
        Require(!transition.EnterContact(true, 4) && !transition.EnterContact(false, 5), "Contact granted twice in one activation");
        transition.Finish(false);
        Require(!transition.EnterContact(true, 6) && transition.EnterContact(false, 7), "Contact did not reset on the next turn");
        foreach (var trooper in new[]{hammer,observer,hound})
        {
            int prototype = trooper.GetEquippedWeapon().Config.damagePerHit;
            int Normal(OsowiecHitArea area) => OsowiecFieldTurn.ShotDamage(prototype,area,false,14,false,1f);
            int fullStack = OsowiecFieldTurn.ShotDamage(prototype,OsowiecHitArea.Head,true,14,true,1.2f);
            int shellStack = OsowiecFieldTurn.ShotDamage(prototype,OsowiecHitArea.Helmet,true,14,true,1.2f);
            Require(Normal(OsowiecHitArea.Torso)*3>=125 && Normal(OsowiecHitArea.Head)<85 &&
                fullStack==84 && shellStack==24, "Profile cap "+trooper.name);
            Debug.Log($"[Osowiec damage] {trooper.chessType}: torso={Normal(OsowiecHitArea.Torso)} limb={Normal(OsowiecHitArea.Limb)} head={Normal(OsowiecHitArea.Head)} helmet={Normal(OsowiecHitArea.Helmet)} markedTorso={OsowiecFieldTurn.ShotDamage(prototype,OsowiecHitArea.Torso,false,14,true,1f)} max={fullStack}/{shellStack}");
        }
        Object.Destroy(targetRoot);
        Debug.Log($"PASS Osowiec roles: HP {hammer.GetHealth()}/{hound.GetHealth()}, damage {heavyHit}/{lightHit}, " +
            $"speed {hammer.MoveSpeed}/{hound.MoveSpeed}, observer hit {bonus}, full movement on consecutive turns, continued order {route:F1} m.");
    }
}
