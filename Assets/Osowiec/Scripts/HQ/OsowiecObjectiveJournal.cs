using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class OsowiecObjectiveJournal : MonoBehaviour
{
    public bool IsOpen => page && page.activeInHierarchy;
    [SerializeField] GameObject page, pauseNavigation;
    [SerializeField] TMP_Text objectives, dispatch;
    bool resumeOnClose;
    public void Bind(GameObject spread, GameObject pause, TMP_Text list, TMP_Text fieldNote)
    {
        page=spread; pauseNavigation=pause; objectives=list; dispatch=fieldNote;
    }

    public void Open()
    {
        if (!GameManager.Instance || GameManager.Instance.IsGameOver()) return;
        resumeOnClose=!GameManager.Instance.IsPaused();
        if (resumeOnClose) GameManager.Instance.PauseGame();
        objectives.text=MissionQuestUI.Instance ? MissionQuestUI.Instance.JournalLines() : "";
        var column=OsowiecOperation.Instance ? OsowiecOperation.Instance.relief : null;
        dispatch.text=column && column.Started
            ? $"Доставлено в лазарет   {column.Delivered} / 3\n{(column.Complete ? "Эвакуация завершена" : column.RouteName)}\n" +
              (column.Delivered<3 ? "Ожидают в лазарете   "+OsowiecWesternWard.PatientName(column.Delivered) : OsowiecWesternWard.Ledger(3))
            : "Эвакуация начнётся после восстановления связи с батареей.";
        pauseNavigation.SetActive(false);
        page.SetActive(true);
    }

    public void Close()
    {
        page.SetActive(false);
        pauseNavigation.SetActive(true);
        if (resumeOnClose) GameManager.Instance.ResumeGame();
    }

    void Update()
    {
        var game=GameManager.Instance;
        if (!game || !page) return;
        if (!game.IsPaused() && page.activeSelf) page.SetActive(false);
        if (Keyboard.current?.jKey.wasPressedThisFrame != true || game.IsGameOver() || game.IsArmyDeploymentPhase()) return;
        if (page.activeSelf) Close();
        else if (!game.IsPaused() || pauseNavigation.activeInHierarchy) Open();
    }
}
