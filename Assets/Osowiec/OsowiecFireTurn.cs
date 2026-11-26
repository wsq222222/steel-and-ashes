using UnityEngine;

internal static class OsowiecFireTurn
{
    internal static bool Allows(Unit actor)
    {
        if (!actor) return false;
        if (actor.gameObject.scene.name != "Osowiec") return true;
        var game = GameManager.Instance;
        if (!actor.isActiveAndEnabled || actor.GetHealth() <= 0 || !game || game.IsPaused() || game.IsGameOver() ||
            game.IsArmyDeploymentPhase() || game.currentPlayer != actor.owner ||
            (MissionSelectUI.Instance && MissionSelectUI.Instance.IsOpen)) return false;
        if (actor.owner == Player.Player2) return game.IsBotTurn();
        var camera = CameraManager.Instance;
        return actor.IsControlled() && camera && camera.IsActionMode() && camera.GetCurrentControlledUnit()==actor;
    }

    internal static uint Stamp(Unit actor) =>
        actor && actor.gameObject.scene.name == "Osowiec" && GameManager.Instance ? GameManager.Instance.TurnSequence : 0;

    internal static bool StillActive(Unit actor, uint started) => Allows(actor) && started==Stamp(actor);
}
