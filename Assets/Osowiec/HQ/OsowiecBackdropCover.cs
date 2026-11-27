using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[RequireComponent(typeof(RawImage))]
public sealed class OsowiecBackdropCover : UIBehaviour
{
    RawImage image;
    RectTransform viewport;
    protected override void OnEnable()
    {
        base.OnEnable();
        image=GetComponent<RawImage>();
        var canvas=GetComponentInParent<Canvas>(true);
        if(canvas)viewport=canvas.rootCanvas.transform as RectTransform;
        Canvas.willRenderCanvases+=Fit;
        Fit();
    }
    protected override void OnDisable(){Canvas.willRenderCanvases-=Fit;base.OnDisable();}
    void Fit()
    {
        if(!viewport || !image || !image.texture)return;
        var rect=image.rectTransform;
        var width=viewport.rect.width*viewport.lossyScale.x/rect.lossyScale.x;
        var height=viewport.rect.height*viewport.lossyScale.y/rect.lossyScale.y;
        if(width<=0 || height<=0)return;
        float aspect=(float)image.texture.width/image.texture.height;
        var size=new Vector2(Mathf.Max(width,height*aspect),Mathf.Max(height,width/aspect));
        if((rect.sizeDelta-size).sqrMagnitude>.01f)rect.sizeDelta=size;
    }
}
