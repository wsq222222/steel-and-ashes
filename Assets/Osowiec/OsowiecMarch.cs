using UnityEngine;

// Field movement switches to turns as soon as either side makes contact.
internal static class OsowiecMarch
{
    static OsowiecOperation mission;
    static uint revision;
    static float nextScan, contactUntil;
    static bool clear;

    internal static bool Free(Unit unit)
    {
        var operation = OsowiecOperation.Instance;
        var game = GameManager.Instance;
        if (!operation || !game || !unit || !unit.isActiveAndEnabled || unit.owner != Player.Player1 ||
            unit.GetHealth() <= 0 || game.IsGameOver() || game.IsArmyDeploymentPhase() || game.IsBotTurn())
            return false;

        float now = Time.time;
        if (mission != operation)
        {
            mission = operation;
            contactUntil = nextScan = 0f;
            clear = false;
        }
        if (now >= nextScan || revision != Unit.RosterRevision)
        {
            revision = Unit.RosterRevision;
            nextScan = now + .15f;
            clear = ClearForSquad(operation);
            if (!clear) contactUntil = now + 2f;
        }
        return clear && now >= contactUntil;
    }

    internal static void UpdateContact(Unit actor)
    {
        var game = GameManager.Instance;
        if (!actor || !actor.IsControlled() || !actor.FieldTurn.Activated || !game ||
            game.IsPaused() || game.IsGameOver() || game.IsBotTurn() || !OsowiecOperation.Instance) return;
        ApplyContact(actor, Free(actor));
    }

    internal static void ApplyContact(Unit actor, bool free)
    {
        if (!actor.FieldTurn.EnterContact(free, Time.time)) return;
        float allowance = Mathf.Max(actor.GetRemainingMoveMeters(), OsowiecPlanner.FullBudget(actor) * .5f);
        actor.SetRemainingMoveMeters(allowance);
        if (!actor.FieldTurn.Attacked) OsowiecFieldKit.BeginCombat(actor);
        OsowiecAtmosphere.PlayContact(actor.transform.position);
    }

    static bool ClearForSquad(OsowiecOperation operation)
    {
        var roster = operation.FieldRoster;
        if (roster == null || roster.Length == 0) return false;
        bool hasSquad = false;
        var bots = BotController.Instance;
        foreach (var unit in roster)
        {
            if (!unit || !unit.isActiveAndEnabled || unit.GetHealth() <= 0) continue;
            if (unit.owner == Player.Player1)
            {
                hasSquad = true;
                if (OsowiecOperation.InGas(unit) || OsowiecGroundEvents.IsWarned(unit.transform.position) ||
                    operation.ThreatSeconds(unit) > 0f || operation.RecentlyFired(unit)) return false;
            }
            else
            {
                if (operation.SquadSees(unit)) return false;
                if (!bots) continue;
                foreach (var ally in roster)
                    if (ally && ally.isActiveAndEnabled && ally.owner == Player.Player1 && ally.GetHealth() > 0 &&
                        bots.CanSeeTargetForBrain(unit, ally)) return false;
            }
        }
        return hasSquad;
    }
}
