using UnityEngine;
using UnityEngine.EventSystems;

public sealed class OsowiecPaperDrag : MonoBehaviour,IBeginDragHandler,IDragHandler,IEndDragHandler,
    IPointerEnterHandler,IPointerExitHandler,ISelectHandler,IDeselectHandler
{
    public OsowiecPaperPlacement board;
    public int offer;
    public void OnPointerEnter(PointerEventData e)=>board.PreviewOffer(offer,true);
    public void OnPointerExit(PointerEventData e)=>board.PreviewOffer(-1,true);
    public void OnSelect(BaseEventData e)=>board.PreviewOffer(offer,false);
    public void OnDeselect(BaseEventData e)=>board.PreviewOffer(-1,false);
    public void OnBeginDrag(PointerEventData e)=>board.BeginDrag(offer,e);
    public void OnDrag(PointerEventData e)=>board.Drag(e);
    public void OnEndDrag(PointerEventData e)=>board.EndDrag();
}
