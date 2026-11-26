using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public sealed class OsowiecSurveyDeskFit : UIBehaviour
{
    protected override void OnEnable()
    {
        base.OnEnable();
        Fit();
        Canvas.willRenderCanvases+=Fit;
    }

    protected override void OnDisable()
    {
        Canvas.willRenderCanvases-=Fit;
        base.OnDisable();
    }

    protected override void OnRectTransformDimensionsChange()
    {
        base.OnRectTransformDimensionsChange();
        Fit();
    }

    void Fit()
    {
        var desk=(RectTransform)transform;
        if(!(desk.parent is RectTransform viewport))return;
        var bounds=viewport.rect.size;
        var page=desk.rect.size;
        if(page.x<=0 || page.y<=0 || bounds.x<=0 || bounds.y<=0)return;
        var scale=Mathf.Min(1f,Mathf.Min(bounds.x/page.x,bounds.y/page.y));
        if(!Mathf.Approximately(desk.localScale.x,scale))
            desk.localScale=new Vector3(scale,scale,1f);
    }
}
