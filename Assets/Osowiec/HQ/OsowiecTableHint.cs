using UnityEngine;
using UnityEngine.InputSystem;

public sealed class OsowiecTableHint : MonoBehaviour
{
    [SerializeField] private HubComputerTerminal terminal;
    [SerializeField] private Transform phonograph;
    [SerializeField] private MissionSelectUI dossier;

    public void Bind(HubComputerTerminal interaction, Transform stand, MissionSelectUI order)
    {
        terminal = interaction;
        phonograph = stand;
        dossier = order;
        terminal.enabled = false;
    }

    public void PrepareMission() => GameManager.SetGameMode(GameMode.PlayerVsBot);

    private void Update()
    {
        var hint = HubInteractionHintUI.Instance;
        var manager = CameraManager.Instance;
        var unit = manager != null ? manager.GetCurrentControlledUnit() : null;
        if (!hint) return;
        if (!unit || !terminal || (dossier && dossier.IsOpen) || (GameManager.Instance && GameManager.Instance.IsPaused()) ||
            (!manager.IsActionMode() && !manager.IsHubExploreMode()))
        {
            hint.HidePrompt();
            return;
        }
        Vector3 foot = unit.transform.position;
        Vector3 toTable = terminal.transform.position - foot;
        Vector3 toHorn = phonograph ? phonograph.position - foot : Vector3.one * 100;
        toHorn.y = toTable.y = 0;
        bool atGramophone = toHorn.sqrMagnitude <= 1.66f * 1.66f && toHorn.sqrMagnitude < toTable.sqrMagnitude;
        if (!atGramophone && toTable.sqrMagnitude > 1.72f * 1.72f) { hint.HidePrompt(); return; }
        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
        {
            if (atGramophone) OsowiecHeadquartersAudio.ToggleGramophone();
            else { terminal.OpenMissionUI(); hint.HidePrompt(); return; }
        }
        hint.ShowPrompt(atGramophone ? (OsowiecHeadquartersAudio.GramophoneOn ? "[E]  Выключить граммофон" : "[E]  Включить граммофон") : "[E]  Открыть карту операций");
    }
}
