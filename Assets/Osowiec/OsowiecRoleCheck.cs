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
        var hammer = team.Single(u => u.chessType == ChessUnitType.Pawn);
        var observer = team.Single(u => u.chessType == ChessUnitType.Bishop);
        var hound = team.Single(u => u.chessType == ChessUnitType.Guardian);
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
        hammer.SetRemainingMoveMeters(1);
        OsowiecMarch.ApplyContact(hammer, true);
        OsowiecMarch.ApplyContact(hammer, false);
        Require(hammer.GetRemainingMoveMeters()==1 && hammer.FieldTurn.Attacked &&
            !hammer.GetEquippedWeapon().TryFire(hammer, hammer.transform.position, Vector3.up),
            "A second contact must not restore another action or movement");
        EndTurn();
        planner.Select(hound);
        planner.ConfirmOrder();
        int lightHit = Shoot(hound, 12);
        Require(Math.Abs(heavyHit - hammer.Damage) <= 2 && Math.Abs(lightHit - hound.Damage) <= 1 && heavyHit > lightHit, "Role projectile damage");
        EndTurn();
        Require(!observer.FieldTurn.Observed, "Waiting in the roster charged observation");
        planner.Select(observer);
        planner.Preview(OsowiecOperation.Telephone);
        float route = planner.RequestedMetres;
        Require(route > 2, "No real route from deployment");
        planner.ConfirmOrder();
        const float full = 18;
        observer.FieldTurn.Walk(full);
        EndTurn();
        Require(observer.FieldTurn.Observed && observer.FieldTurn.Breath(full) == 0f, "Quiet own turn / fresh activation");
        planner.Select(hound);
        planner.Select(observer);
        Require(Mathf.Abs(planner.RequestedMetres - route) < .05f && planner.ReachableCorners != null, "Lost destination");
        planner.ConfirmOrder();
        Require(Mathf.Abs(observer.GetRemainingMoveMeters() - full) < .01f, "Full movement after a long march");
        int bonus = Shoot(observer, 14);
        Require(Math.Abs(bonus - observer.Damage * 2) <= 2 && !observer.FieldTurn.Observed, "Observed long shot");
        EndTurn();
        Require(!observer.FieldTurn.Observed, "Firing regenerated observation");
        planner.Select(observer);
        planner.ConfirmOrder();
        EndTurn();
        Require(OsowiecPlanner.MoveBudget(observer) == full && observer.FieldTurn.Observed, "New turn / new observation");
        bool close = observer.FieldTurn.Fire();
        Require(OsowiecFieldTurn.ShotDamage(observer.Damage, close, 5) == observer.Damage &&
            !observer.FieldTurn.Fire(), "Close shot / one-shot consumption");
        var transition = new OsowiecFieldTurn();
        Require(!transition.EnterContact(false, 1) && !transition.EnterContact(true, 2) && transition.EnterContact(false, 3), "First contact after marching");
        Require(!transition.EnterContact(true, 4) && !transition.EnterContact(false, 5), "Contact granted twice in one activation");
        transition.Finish(false);
        Require(!transition.EnterContact(true, 6) && transition.EnterContact(false, 7), "Contact did not reset on the next turn");
        Object.Destroy(targetRoot);
        Debug.Log($"PASS Osowiec roles: HP {hammer.GetHealth()}/{hound.GetHealth()}, damage {heavyHit}/{lightHit}, " +
            $"speed {hammer.MoveSpeed}/{hound.MoveSpeed}, observer hit {bonus}, full movement on consecutive turns, continued order {route:F1} m.");
    }
}
