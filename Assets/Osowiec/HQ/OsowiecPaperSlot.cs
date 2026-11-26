using UnityEngine;
using UnityEngine.EventSystems;

public sealed class OsowiecPaperSlot : MonoBehaviour,IDropHandler,IPointerClickHandler,ISelectHandler,IPointerEnterHandler
{
    public OsowiecPaperPlacement board;
    public int index;
    public void OnSelect(BaseEventData e)=>board.FocusCell(index);
    public void OnPointerEnter(PointerEventData e)=>board.FocusCell(index);
    public void OnDrop(PointerEventData e)
    {
        if(e.pointerDrag && e.pointerDrag.GetComponent<OsowiecPaperDrag>())board.UseCell(index);
    }
    public void OnPointerClick(PointerEventData e)
    {
        if(e.button==PointerEventData.InputButton.Right)board.Remove(index);
    }
}
