using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class OsowiecDeploymentReadout : MonoBehaviour
{
    [SerializeField] private ArmyDeploymentController deployment;
    [SerializeField] private TMP_Text points;
    [SerializeField] private TMP_Text feedback;
    [SerializeField] private GameObject feedbackPanel;
    [SerializeField] private int total;
    [SerializeField] private TMP_Text capacity;
    [SerializeField] private TMP_Text[] slots;
    [SerializeField] private TMP_Text areaTitle, areaOrder;
    [SerializeField] private Button switchButton;
    private int previousSector=-1, selectedOffer=-1;
    private string lastHover;

    private int lastPoints = -1;
    private float clearAt;

    public void Bind(ArmyDeploymentController source, TMP_Text value, TMP_Text message, int budget,
        GameObject panel, TMP_Text positions, TMP_Text[] cells, TMP_Text title, TMP_Text order, Button advance)
    {
        deployment = source;
        points = value;
        feedback = message;
        feedbackPanel = panel;
        feedbackPanel.SetActive(false);
        total = budget;
        capacity = positions;
        slots = cells;
        areaTitle=title; areaOrder=order; switchButton=advance;
    }

    void OnEnable() { if(switchButton) switchButton.onClick.AddListener(SwitchSector); }
    void OnDisable()
    {
        if(switchButton) switchButton.onClick.RemoveListener(SwitchSector);
        CancelSelection();
    }
    void SwitchSector() { deployment?.Sectors?.Switch(); }
    public void CancelSelection() { selectedOffer=-1; deployment?.Sectors?.HideGhost(); }
    void Update()
    {
        if(!deployment || !deployment.Sectors || !GameManager.Instance || !GameManager.Instance.IsArmyDeploymentPhase() || GameManager.Instance.IsPaused()) return;
        if(Keyboard.current?.qKey.wasPressedThisFrame==true) SwitchSector();
        if(selectedOffer<0 || Mouse.current==null) return;
        if(Mouse.current.rightButton.wasPressedThisFrame) { CancelSelection(); return; }
        var pos=Mouse.current.position.ReadValue();
        deployment.Sectors.Preview(selectedOffer,pos);
        if(!Mouse.current.leftButton.wasPressedThisFrame || EventSystem.current?.IsPointerOverGameObject()==true) return;
        if(ArmyDeploymentController.TryScreenPointToGridCell(pos,out var cell) &&
           OsowiecDeploymentSectors.IndexOf(cell)==deployment.Sectors.Sector &&
           deployment.TryPlaceOfferAtGridCell(selectedOffer,cell,Keyboard.current?.shiftKey.isPressed==true))
        { CancelSelection(); Report("Боец на позиции"); }
    }

    private void LateUpdate()
    {
        if (deployment == null || points == null) return;
        int remaining = deployment.GetPointsRemaining();
        var sectors=deployment.Sectors;
        if(!sectors) return;
        if (remaining != lastPoints || previousSector!=sectors.Sector)
        {
            points.text = $"{remaining} / {total}";
            UpdateSlots();
            lastPoints = remaining;
            previousSector=sectors.Sector;
            areaTitle.text=OsowiecDeploymentSectors.Titles[sectors.Sector];
            areaOrder.text=OsowiecDeploymentSectors.Descriptions[sectors.Sector];
        }

        if(sectors.HoverOffer>=0)
        {
            string hover=sectors.HoverMessage;
            if(hover!=lastHover) { lastHover=hover; Report(string.IsNullOrEmpty(hover)?"Поставить бойца":hover); }
            clearAt=Time.unscaledTime+1;
        }
        else lastHover=null;
        if (feedbackPanel != null && feedbackPanel.activeSelf && Time.unscaledTime >= clearAt)
            feedbackPanel.SetActive(false);
    }

    private void UpdateSlots()
    {
        foreach (var slot in slots) slot.text = "";
        int count = 0;
        foreach (var unit in FindObjectsByType<Unit>())
        {
            if (unit.owner != Player.Player1 || unit.GetHealth() <= 0 ||
                !deployment.IsCellInDeploymentZone(unit.currentGridPosition)) continue;
            int index=System.Array.IndexOf(deployment.Sectors.Current,unit.currentGridPosition);
            if(index<0) continue;
            slots[index].text = unit.chessType switch
            {
                ChessUnitType.Pawn => "М",
                ChessUnitType.Bishop => "Ц",
                _ => "Г"
            };
            count++;
        }
        capacity.text = $"Позиции  {count} / {slots.Length}";
    }

    public void Report(string message)
    {
        if (feedback == null) return;
        feedback.text = message;
        if (feedbackPanel != null) feedbackPanel.SetActive(true);
        clearAt = Time.unscaledTime + 4f;
    }

    public void PlaceInAvailableSlot(int offer)
    {
        if (deployment == null || !deployment.TryGetOffer(offer, out var entry)) return;
        selectedOffer=offer;
        Report("Щёлкните по следам на выбранном участке");
    }
}
