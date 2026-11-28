using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class OsowiecDeploymentCardFace : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
    IBeginDragHandler, IEndDragHandler, IPointerClickHandler
{
    [SerializeField] private TMP_Text title;
    [SerializeField] private TMP_Text description;
    [SerializeField] private TMP_Text mark;
    [SerializeField] private Image plate;
    [SerializeField] private Image portrait;
    [SerializeField] private RawImage photograph;
    [SerializeField] private Sprite dragMark;
    [SerializeField] private Color rest;
    [SerializeField] private Color hovered;

    private ArmyDeploymentController deployment;
    private OsowiecDeploymentReadout readout;
    private DeploymentOfferEntry offer;

    public void Bind(TMP_Text titleText, TMP_Text descText, TMP_Text roleMark, Image background,
        Image underlyingIcon, Sprite ghostSprite, Color normal, Color highlight, RawImage fieldPhoto = null)
    {
        title = titleText;
        description = descText;
        mark = roleMark;
        plate = background;
        portrait = underlyingIcon;
        photograph = fieldPhoto;
        dragMark = ghostSprite;
        rest = normal;
        hovered = highlight;
    }

    private void Start()
    {
        deployment = FindFirstObjectByType<ArmyDeploymentController>();
        readout = GetComponentInParent<OsowiecDeploymentReadout>();
        if (deployment == null || !deployment.TryGetOffer(transform.GetSiblingIndex(), out offer)) return;
        var unit = offer.unitPrefab.GetComponent<Unit>();
        if (unit == null) return;
        title.text = unit.GetUnitDisplayName();
        mark.text = unit.chessType switch
        {
            ChessUnitType.Pawn => "М",
            ChessUnitType.Bishop => "Ц",
            ChessUnitType.Guardian => "Г",
            _ => "О"
        };
        description.text = $"{OsowiecPlanner.MoveBudget(unit):0} м / ход";
        if (portrait != null) portrait.sprite = dragMark;
        if (photograph != null)
        {
            int column = unit.chessType switch
            {
                ChessUnitType.Bishop => 1,
                ChessUnitType.Guardian => 2,
                _ => 0
            };
            photograph.uvRect = new Rect(column / 3f + .033f, 0f, .267f, 1f);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (plate != null) plate.color = hovered;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (plate != null) plate.color = rest;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        readout?.CancelSelection();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!eventData.dragging && eventData.button == PointerEventData.InputButton.Left)
            readout?.PlaceInAvailableSlot(transform.GetSiblingIndex());
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (deployment == null || readout == null || offer == null) return;
        var screen = eventData.position;
        var returnZone = deployment.GetReturnDropZone();
        if (returnZone != null && RectTransformUtility.RectangleContainsScreenPoint(returnZone, screen, null))
            return;
        StartCoroutine(DescribeDrop());
    }

    private IEnumerator DescribeDrop()
    {
        yield return null;
        var card=GetComponent<DeploymentCardUI>();
        if (card) readout.Report(card.LastDropSucceeded ? $"{title.text} в строю" : card.LastDropMessage);
    }
}
