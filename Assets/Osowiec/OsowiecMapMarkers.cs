using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

internal enum OsowiecMapSymbol { Phone, Gas, Gate, Battery, JunctionNorth, JunctionSouth, Wagon, Supplies, Aid }

internal sealed class OsowiecMapMarkers
{
    struct Marker { public OsowiecMapSymbol symbol; public Rect rect; public string title; public string detail; }
    readonly List<Marker> items = new(12);
    readonly Texture2D[] art = new Texture2D[4];
    static readonly string[] names = { "Phone", "Gas", "Gate", "Battery" };
    static readonly Color paper = new(.97f,.88f,.68f);
    static readonly Color edge = new(.70f,.59f,.39f);
    GUIStyle letter, titleStyle, detailStyle;
    Camera camera;
    float size;
    internal string HoverTitle { get; private set; } = "";

    internal OsowiecMapMarkers()
    {
        for(int i=0;i<art.Length;i++) art[i]=Resources.Load<Texture2D>("MapSymbols/"+names[i]);
    }

    internal void Begin(Camera view)
    {
        camera=view; items.Clear(); HoverTitle="";
        size=Mathf.Clamp(31*OsowiecHudPaint.Scale,23,48);
    }

    internal bool TryGetRect(OsowiecMapSymbol symbol,out Rect rect)
    {
        foreach(var pin in items) if(pin.symbol==symbol) { rect=pin.rect;return true; }
        rect=default;return false;
    }

    internal void Add(OsowiecMapSymbol symbol,Vector3 world,string title,string detail="")
    {
        if(!camera) return;
        var projected=camera.WorldToScreenPoint(world+Vector3.up*.8f);
        if(projected.z<=camera.nearClipPlane || !camera.pixelRect.Contains((Vector2)projected)) return;
        var anchor=new Vector2(projected.x,Screen.height-projected.y);
        var candidate=new Rect(anchor.x-size*.5f,anchor.y-size*.5f,size,size);
        if(Occupied(candidate))
        {
            bool free=false;
            for(int ring=1;ring<=2 && !free;ring++)
            for(int i=0;i<8;i++)
            {
                float angle=i*Mathf.PI*.25f;
                candidate.center=anchor+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*size*ring;
                if(candidate.xMin>=0 && candidate.yMin>=0 && candidate.xMax<=Screen.width &&
                    candidate.yMax<=Screen.height && !Occupied(candidate)) { free=true;break; }
            }
            if(!free) return;
        }
        items.Add(new Marker{symbol=symbol,rect=candidate,title=title,detail=detail});
    }

    bool Occupied(Rect area)
    {
        foreach(var pin in items) if(pin.rect.Overlaps(area)) return true;
        return TacticalWorldIconsController.Instance && TacticalWorldIconsController.Instance.OverlapsPortrait(area);
    }

    internal void Paint()
    {
        var mouse=Mouse.current;
        Vector2 cursor=mouse!=null ? mouse.position.ReadValue() : new Vector2(-10000,-10000);
        cursor.y=Screen.height-cursor.y;
        int hover=-1;
        for(int i=0;i<items.Count;i++)
        {
            var pin=items[i];
            bool hot=pin.rect.Contains(cursor);
            if(hot) hover=i;
            var previous=GUI.color;
            GUI.color=Color.white;
            GUI.DrawTexture(pin.rect,Texture2D.whiteTexture,ScaleMode.StretchToFill,true,0,hot ? paper : edge,0, size*.5f);
            var inside=new Rect(pin.rect.x+1.4f,pin.rect.y+1.4f,size-2.8f,size-2.8f);
            GUI.color=Color.white;
            GUI.DrawTexture(inside,Texture2D.whiteTexture,ScaleMode.StretchToFill,true,0,new Color(.095f,.10f,.073f,.96f),0,size*.5f);
            int id=(int)pin.symbol;
            if(id<art.Length && art[id])
            {
                float inset=size*.12f;
                GUI.color=hot?Color.white:paper;
                GUI.DrawTexture(new Rect(pin.rect.x+inset,pin.rect.y+inset,size-2*inset,size-2*inset),art[id],ScaleMode.ScaleToFit);
            }
            else
            {
                letter??=new GUIStyle(GUIStyle.none){font=OsowiecHudPaint.Body,alignment=TextAnchor.MiddleCenter,fontStyle=FontStyle.Bold};
                letter.fontSize=Mathf.RoundToInt(size*.61f);
                letter.normal.textColor=paper;
                GUI.Label(pin.rect,pin.symbol switch {
                    OsowiecMapSymbol.JunctionNorth => "С", OsowiecMapSymbol.JunctionSouth => "Ю",
                    OsowiecMapSymbol.Wagon => "П", OsowiecMapSymbol.Supplies => "Сн", OsowiecMapSymbol.Aid => "+", _=>"·"},letter);
            }
            GUI.color=previous;
        }
        if(hover>=0) Tooltip(items[hover]);
    }

    void Tooltip(Marker pin)
    {
        HoverTitle=pin.title;
        titleStyle??=new GUIStyle(GUIStyle.none){font=OsowiecHudPaint.Body,fontStyle=FontStyle.Bold,wordWrap=true};
        detailStyle??=new GUIStyle(GUIStyle.none){font=OsowiecHudPaint.Body,wordWrap=true};
        titleStyle.fontSize=Mathf.Clamp(Mathf.RoundToInt(18*OsowiecHudPaint.Scale),14,28);
        detailStyle.fontSize=Mathf.Max(13,titleStyle.fontSize-2);
        titleStyle.normal.textColor=paper;
        detailStyle.normal.textColor=new Color(.85f,.82f,.71f);
        var heading=new GUIContent(pin.title); var detail=new GUIContent(pin.detail);
        float pad=titleStyle.fontSize*.6f;
        float width=Mathf.Min(Mathf.Max(titleStyle.CalcSize(heading).x,detailStyle.CalcSize(detail).x)+2*pad,
            Mathf.Min(Screen.width-12,22*titleStyle.fontSize));
        float inner=width-2*pad;
        float titleH=titleStyle.CalcHeight(heading,inner);
        float bodyH=pin.detail.Length==0?0:detailStyle.CalcHeight(detail,inner)+3;
        float h=pad*1.6f+titleH+bodyH;
        var box=new Rect(Mathf.Clamp(pin.rect.center.x-width*.5f,4,Screen.width-width-4),
            pin.rect.yMax+6+h<Screen.height?pin.rect.yMax+6:Mathf.Max(4,pin.rect.yMin-6-h),width,h);
        OsowiecHudPaint.Panel(box);
        GUI.Label(new Rect(box.x+pad,box.y+pad*.8f,inner,titleH),heading,titleStyle);
        if(bodyH>0)GUI.Label(new Rect(box.x+pad,box.y+pad*.8f+titleH+3,inner,bodyH-3),detail,detailStyle);
    }

    static GUIStyle distanceStyle;
    internal static void Waypoint(Vector2 center,float metres)
    {
        float scale=Mathf.Clamp(OsowiecHudPaint.Scale,.76f,1.55f);
        float radius=8*scale;
        var previous=GUI.matrix;
        GUIUtility.RotateAroundPivot(45,center);
        var box=new Rect(center.x-radius,center.y-radius,radius*2,radius*2);
        GUI.DrawTexture(box,Texture2D.whiteTexture,ScaleMode.StretchToFill,true,0,new Color(.07f,.08f,.06f,.95f),3.6f*scale,0);
        var inner=new Rect(box.x+scale,box.y+scale,box.width-2*scale,box.height-2*scale);
        GUI.DrawTexture(inner,Texture2D.whiteTexture,ScaleMode.StretchToFill,true,0,paper,1.65f*scale,0);
        GUI.matrix=previous;
        distanceStyle??=new GUIStyle(GUIStyle.none){alignment=TextAnchor.MiddleCenter,font=OsowiecHudPaint.Body,fontStyle=FontStyle.Bold};
        distanceStyle.fontSize=Mathf.RoundToInt(16*scale);
        var rect=new Rect(center.x-45*scale,center.y+radius*1.5f,90*scale,22*scale);
        var label=metres<1.25f?"В точке":$"{metres:F1} м";
        distanceStyle.normal.textColor=new Color(.055f,.058f,.045f);
        for(int k=0;k<4;k++) {
            var shadow=rect;shadow.position+= new Vector2(k==0?-1:k==1?1:0,k==2?-1:k==3?1:0)*scale;
            GUI.Label(shadow,label,distanceStyle);
        }
        distanceStyle.normal.textColor=paper; GUI.Label(rect,label,distanceStyle);
    }
}
