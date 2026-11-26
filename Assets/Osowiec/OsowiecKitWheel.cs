using UnityEngine;

public enum OsowiecKitItem { Rifle, Grenade, Smoke, Bayonet, Gas }

internal sealed class OsowiecKitWheel
{
    static readonly string[] Names = { "Карабин", "Осколочная", "Дым", "Штык", "Газовая" };
    static readonly Color Paper = new(.96f,.90f,.73f);
    static readonly Color Brass = new(.83f,.69f,.41f);
    static readonly Color Shell = new(.12f,.15f,.13f,.94f);
    static readonly Color Selected = new(.44f,.28f,.16f,.97f);
    readonly Texture2D[] icons = new Texture2D[7];
    readonly Texture2D[] slices = new Texture2D[7];
    GUIStyle label;
    Vector2 pointer;
    float previousScale;
    public bool Open { get; private set; }
    public OsowiecKitItem Hover { get; private set; }
    public bool OnSector { get; private set; }

    public static string CancelName => PlayerPrefs.GetInt("OsowiecKitCancel",0) switch { 1=>"СКМ",2=>"C",_=>"ПКМ" };
    public static bool CancelPressed => PlayerPrefs.GetInt("OsowiecKitCancel",0) switch
    {
        1 => UnityEngine.InputSystem.Mouse.current?.middleButton.wasPressedThisFrame == true,
        2 => UnityEngine.InputSystem.Keyboard.current?.cKey.wasPressedThisFrame == true,
        _ => UnityEngine.InputSystem.Mouse.current?.rightButton.wasPressedThisFrame == true
    };
    public static string Name(OsowiecKitItem item) => Names[(int)item];
    static int Count(Unit unit) => unit.chessType == ChessUnitType.Pawn ? 4 : 3;
    static OsowiecKitItem Slot(Unit unit, int index) =>
        index == 2 && unit.chessType == ChessUnitType.Pawn ? OsowiecKitItem.Gas : (OsowiecKitItem)index;
    public void Begin(OsowiecKitItem equipped)
    {
        Open = true;
        pointer = Vector2.zero;
        Hover = equipped;
        OnSector = false;
        previousScale = Time.timeScale;
        Time.timeScale = previousScale * .18f;
    }

    public void Point(Unit unit, OsowiecKitItem equipped, Vector2 delta)
    {
        pointer = Vector2.ClampMagnitude(pointer + delta / OsowiecHudPaint.Scale, 132);
        OnSector = pointer.magnitude >= 20;
        float angle = Mathf.Repeat(Mathf.Atan2(pointer.x,pointer.y) * Mathf.Rad2Deg,360);
        int count = Count(unit);
        Hover = OnSector ? Slot(unit, Mathf.RoundToInt(angle * count / 360) % count) : equipped;
    }

    public void Close()
    {
        if (!Open) return;
        Open = false;
        if (Mathf.Approximately(Time.timeScale, previousScale * .18f))
            Time.timeScale = previousScale;
    }

    public void Dispose()
    {
        Close();
        foreach(var texture in slices) if(texture) Object.Destroy(texture);
        if(glass) Object.Destroy(glass);
        if(hub) Object.Destroy(hub);
    }

    Texture2D Sector(int count,int slot)
    {
        int index = count==3 ? slot : 3+slot;
        if (slices[index]) return slices[index];
        const int size = 384;
        var image = new Texture2D(size,size,TextureFormat.RGBA32,false);
        image.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color32[size*size];
        float half = Mathf.PI / count;
        float a = slot*Mathf.PI*2/count;
        float sin=Mathf.Sin(a), cos=Mathf.Cos(a);
        for (int y=0;y<size;y++)
        for (int x=0;x<size;x++)
        {
            float u = x - (size-1)*.5f;
            float v = y - (size-1)*.5f;
            float radius = Mathf.Sqrt(u*u+v*v);
            float angle=Mathf.Atan2(u*cos-v*sin,u*sin+v*cos);
            float edge = (half-Mathf.Abs(angle))*radius - 2f;
            float margin = Mathf.Min(radius-67, Mathf.Min(189-radius,edge));
            float alpha = Mathf.Clamp01(margin+.5f);
            // Concentric engraved borders and index marks follow each sector.
            float tick = Mathf.Abs(Mathf.Sin(24*angle));
            float shade = margin < 1.6f || radius > 180 && tick < .14f ? .98f :
                radius > 177 && radius < 179 ? .84f : .67f;
            pixels[y*size+x] = new Color(shade,shade,shade,alpha);
        }
        image.SetPixels32(pixels);
        image.Apply(false,true);
        return slices[index]=image;
    }

    Texture2D Icon(int index)
    {
        if (!icons[index])
        {
            string key = index < 4 ? ((OsowiecKitItem)index).ToString() :
                index == 4 ? "Leap" : index == 5 ? "Periscope" : "Rush";
            icons[index] = Resources.Load<Texture2D>("OsowiecKit/"+key);
        }
        return icons[index];
    }

    void Center(Rect rect,string message,float size, Color color,bool title=false)
    {
        label ??= new GUIStyle(GUIStyle.none) { alignment=TextAnchor.MiddleCenter,clipping=TextClipping.Clip };
        label.font = title ? OsowiecHudPaint.Heading : OsowiecHudPaint.Body;
        label.fontSize = Mathf.RoundToInt(size);
        label.normal.textColor = color;
        GUI.Label(rect,message,label);
    }

    static void Picture(Rect rect,Texture2D image, Color tint)
    {
        if (!image) return;
        var original=GUI.color;
        GUI.color=tint;
        GUI.DrawTexture(rect,image,ScaleMode.ScaleToFit);
        GUI.color=original;
    }

    public void DrawAbility(Unit unit,bool cooling,bool scope)
    {
        float s=OsowiecHudPaint.Scale;
        int icon=unit.chessType==ChessUnitType.Pawn?4:unit.chessType==ChessUnitType.Bishop?5:6;
        string title=icon==4?"Пневмотолчок":icon==5?"Перископ":"Рывок";
        Vector2 anchor=new(Screen.width-186*s,Screen.height-97*s);
        var rect=new Rect(anchor.x,anchor.y,164*s,58*s);
        OsowiecHudPaint.Panel(rect);
        var ink=cooling && !scope ? new Color(.66f,.65f,.54f) : Paper;
        Picture(new Rect(rect.x+8*s,rect.y+7*s,40*s,44*s),Icon(icon),ink);
        Center(new Rect(rect.x+52*s,rect.y+5*s,106*s,26*s),title,18*s,ink);
        Center(new Rect(rect.x+50*s,rect.y+30*s,110*s,20*s),scope?"Q · Сложить":cooling?"След. ход":icon==4?"Держать Q":"Q · Применить",14*s,Brass);
    }

    Texture2D glass,hub;
    Texture2D Hub()
    {
        if(hub) return hub;
        const int size=132;
        hub=new Texture2D(size,size,TextureFormat.RGBA32,false);
        var pixels=new Color32[size*size];
        for(int y=0;y<size;y++)
        for(int x=0;x<size;x++)
        {
            float r=new Vector2(x+.5f-size*.5f,y+.5f-size*.5f).magnitude;
            var color=r>63?Brass:Shell;
            color.a*=Mathf.Clamp01(65.5f-r);
            pixels[y*size+x]=color;
        }
        hub.SetPixels32(pixels);
        hub.Apply(false,true);
        return hub;
    }
    public void DrawScope(Unit target, bool transferred, float range, bool zoomed)
    {
        if(!glass)
        {
            const int size=256;
            glass=new Texture2D(size,size,TextureFormat.RGBA32,false);
            var pixels=new Color32[size*size];
            for(int y=0;y<size;y++)
            for(int x=0;x<size;x++)
            {
                float u=(x+.5f)/size*2-1,v=(y+.5f)/size*2-1;
                float r=Mathf.Sqrt(u*u+v*v);
                float vignette=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.63f,.99f,r));
                pixels[y*size+x]=r>.972f?new Color(.047f,.053f,.04f,.97f):
                    r>.957f||r>.920f&&r<.926f?new Color(.56f,.50f,.34f,.87f):
                    new Color(.10f,.18f,.10f,.05f+.48f*vignette);
            }
            glass.SetPixels32(pixels); glass.Apply(false,true);
        }
        float s=OsowiecHudPaint.Scale;
        float diameter=Screen.height*.82f;
        Vector2 middle=new(Screen.width*.5f,Screen.height*.5f);
        var lens=new Rect(middle.x-diameter*.5f,middle.y-diameter*.5f,diameter,diameter);
        Color shell=new(.047f,.053f,.04f,.97f);
        OsowiecHudPaint.Fill(new Rect(0,0,Screen.width,lens.y),shell);
        OsowiecHudPaint.Fill(new Rect(0,lens.yMax,Screen.width,Screen.height-lens.yMax),shell);
        OsowiecHudPaint.Fill(new Rect(0,lens.y,lens.x,diameter),shell);
        OsowiecHudPaint.Fill(new Rect(lens.xMax,lens.y,Screen.width-lens.xMax,diameter),shell);
        GUI.DrawTexture(lens,glass);
        Color ink=target?new Color(.92f,.68f,.36f):new Color(.17f,.18f,.13f);
        for(int side=-1;side<=1;side+=2)
        {
            for(int j=1;j<=7;j++)
            {
                float x=middle.x+side*(24+j*13)*s;
                OsowiecHudPaint.Fill(new Rect(x,middle.y,1.6f*s,(j%2==1?7:12)*s),ink);
            }
            OsowiecHudPaint.Fill(new Rect(middle.x+(side<0?-119:24)*s,middle.y,95*s,1.5f*s),ink);
        }
        OsowiecHudPaint.Fill(new Rect(middle.x-1*s,middle.y-13*s,2*s,26*s),ink);
        OsowiecHudPaint.Fill(new Rect(middle.x-13*s,middle.y-1*s,26*s,2*s),ink);
        string value=target ? target.GetUnitDisplayName()+"   ·   "+range.ToString("0")+" м" : "Наведите перекрестие на силуэт";
        OsowiecHudPaint.Plate(new Rect(middle.x-175*s,lens.yMax-78*s,350*s,32*s),value,true);
        Center(new Rect(middle.x-300*s,lens.yMax-43*s,600*s,30*s), (transferred ? "Метка · урон отряда +20%" : "ЛКМ · отметить") + (zoomed ? "     Колесо · 4×" : "     Колесо · 2×") + "     Q · сложить",17*s,Paper);
    }

    public void Draw(Unit unit,bool onF1)
    {
        var saved=GUI.matrix;
        float s=Mathf.Min(OsowiecHudPaint.Scale,Screen.height/580f);
        Vector2 middle=new(Screen.width*.5f-Mathf.Min(390*s,Screen.width*.23f),Screen.height*.46f);
        GUI.matrix=Matrix4x4.TRS(middle,Quaternion.identity,new Vector3(s,s,1));
        int count=Count(unit);
        var wheel=new Rect(-192,-192,384,384);
        for(int i=0;i<count;i++)
        {
            Picture(wheel,Sector(count,i),Hover==Slot(unit,i) ? Selected*1.8f : Shell*1.8f);
        }
        for(int i=0;i<count;i++)
        {
            float angle=i*Mathf.PI*2/count;
            Vector2 point=new(Mathf.Sin(angle)*128,-Mathf.Cos(angle)*128);
            var item=Slot(unit,i);
            bool active=Hover==item;
            int quantity=OsowiecFieldKit.Remaining(unit,item);
            Picture(new Rect(point.x-37,point.y-37,74,59),Icon(i),item==OsowiecKitItem.Gas && quantity>0 ? new Color(.74f,.84f,.35f) : quantity==0?new Color(.55f,.53f,.45f):active?Color.white:Paper);
            string label=item==OsowiecKitItem.Grenade && unit.chessType==ChessUnitType.Pawn ? "Усиленная" : Name(item);
            string name=label+(quantity>=0 ? " ×"+quantity : "");
            Center(new Rect(point.x-62,point.y+20,124,29),name,20,quantity==0?Brass:active?Color.white:Paper);
        }
        Picture(new Rect(-65,-65,130,130),Hub(),Color.white);
        bool pouch=OsowiecFieldKit.Throwable(Hover);
        string detail=Hover==OsowiecKitItem.Rifle?"ПАТРОНЫ":pouch?"В ПОДСУМКЕ":"БЛИЖНИЙ БОЙ";
        var weapon=unit.GetEquippedWeapon();
        string value=pouch?"×"+OsowiecFieldKit.Remaining(unit,Hover):Hover==OsowiecKitItem.Rifle && weapon?
            weapon.AmmoInMag+" / "+weapon.AmmoReserve:"Штык";
        Center(new Rect(-61,-32,122,25),detail,13,Brass);
        Center(new Rect(-62,-9,124,41),value,24,Paper,true);
        if (OnSector)
        {
            Vector2 direction=pointer.normalized;
            OsowiecHudPaint.Fill(new Rect(direction.x*57-3,-direction.y*57-3,6,6),Brass);
        }
        OsowiecHudPaint.Fill(new Rect(-182,205,364,36),Shell);
        Center(new Rect(-182,205,364,36),"Отпустите "+(onF1?"F1":"G")+" для выбора   ·   "+CancelName+" отмена",18,Paper);
        GUI.matrix=saved;
    }
}

