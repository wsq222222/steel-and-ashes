using UnityEngine;

public enum OsowiecKitItem { Rifle, Grenade, Smoke, Bayonet, Gas }

internal sealed class OsowiecKitWheel
{
    static readonly string[] Names = { "Карабин", "Осколочная", "Дым", "Штык", "Газовая" };
    static readonly Color Paper = new(.96f,.90f,.73f);
    static readonly Color Brass = new(.83f,.69f,.41f);
    static readonly Color Shell = new(.12f,.15f,.13f,.94f);
    static readonly Color Selected = new(.44f,.28f,.16f,.97f);
    readonly Texture2D[] icons = new Texture2D[8];
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
    public static string Name(OsowiecKitItem item) => OsowiecMenuLanguage.Text(Names[(int)item]);
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
                index == 4 ? "Leap" : index == 5 ? "Periscope" : index==6 ? "Rush" : "Gas";
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
        var rect=new Rect(Screen.width-302*s,Screen.height-119*s,279*s,85*s);
        OsowiecHudPaint.Panel(rect);
        var ink=cooling && !scope && icon!=5 ? new Color(.66f,.65f,.54f) : Paper;
        Picture(new Rect(rect.x+8*s,rect.y+10*s,63*s,63*s),Icon(icon),ink);
        string hint=scope?"Q · Сложить":icon==5?"Q · Наблюдать":cooling?"Следующий ход":icon==4?"Удерживайте Q":"Q · Применить";
        OsowiecHudPaint.Text(new Rect(rect.x+77*s,rect.y+7*s,198*s,39*s),OsowiecMenuLanguage.Text(title),26,ink);
        OsowiecHudPaint.Text(new Rect(rect.x+77*s,rect.y+43*s,198*s,32*s),OsowiecMenuLanguage.Text(hint),20,Brass);
    }

    public void DrawCharge(bool jump,bool throwing,bool clear,float amount)
    {
        float s=OsowiecHudPaint.Scale;
        float w=Mathf.Min(388*s,Screen.width*.63f);
        var panel=new Rect((Screen.width-w)*.5f,Screen.height*.72f,w,80*s);
        OsowiecHudPaint.Panel(panel);
        string action=jump?(clear?"Отпустите Q для прыжка":"Путь перекрыт"):throwing?"Отпустите ЛКМ для броска":"Удерживайте ЛКМ для броска";
        Color accent=jump&&!clear ? new Color(1,.60f,.40f) : Brass;
        OsowiecHudPaint.Text(new Rect(panel.x+14*s,panel.y+4*s,w-78*s,30*s),OsowiecMenuLanguage.Text(action),22,Paper);
        float charge=jump||throwing?amount:0;
        Center(new Rect(panel.xMax-72*s,panel.y+4*s,61*s,30*s),Mathf.RoundToInt(charge*100)+"%",22*s,accent);
        OsowiecHudPaint.Meter(new Rect(panel.x+14*s,panel.y+39*s,w-28*s,8*s),charge,accent);
        if(jump||throwing)
            OsowiecHudPaint.Text(new Rect(panel.x+14*s,panel.y+50*s,w-28*s,25*s),
                OsowiecMenuLanguage.Text(CancelName)+" · "+OsowiecMenuLanguage.Text("Отмена"),18,Paper);
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
    public void DrawRifleSight(bool target) => DrawGlass(false,target);
    public void DrawScope(Unit target,bool transferred,float range,bool zoomed,string hint)
    {
        var lens=DrawGlass(true,target);
        float s=OsowiecHudPaint.Scale;
        float middle=lens.center.x;
        string value=target ? OsowiecMenuLanguage.Format("{0}   ·   {1:0} м",OsowiecMenuLanguage.Text(target.GetUnitDisplayName()),range) : hint;
        OsowiecHudPaint.FittedPlate(new Vector2(middle,lens.yMax-84*s),value,460*s);
        Center(new Rect(middle-300*s,lens.yMax-43*s,600*s,30*s), OsowiecMenuLanguage.Text(transferred ? "Новая метка · следующий ход" : "ЛКМ · отметить") + "     " + OsowiecMenuLanguage.Text(zoomed ? "Колесо · 4×" : "Колесо · 2×") + "     " + OsowiecMenuLanguage.Text("Q · Сложить"),17*s,Paper);
    }

    Rect DrawGlass(bool periscope,bool target)
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
        float diameter=Mathf.Min(Screen.width,Screen.height)*(periscope?.82f:.79f);
        Vector2 middle=new(Screen.width*.5f,Screen.height*.5f);
        var lens=new Rect(middle.x-diameter*.5f,middle.y-diameter*.5f,diameter,diameter);
        Color shell=new(.047f,.053f,.04f,periscope?.97f:.65f);
        OsowiecHudPaint.Fill(new Rect(0,0,Screen.width,lens.y),shell);
        OsowiecHudPaint.Fill(new Rect(0,lens.yMax,Screen.width,Screen.height-lens.yMax),shell);
        OsowiecHudPaint.Fill(new Rect(0,lens.y,lens.x,diameter),shell);
        OsowiecHudPaint.Fill(new Rect(lens.xMax,lens.y,Screen.width-lens.xMax,diameter),shell);
        var color=GUI.color;
        GUI.color=new Color(color.r,color.g,color.b,color.a*(periscope?1f:.67f));
        GUI.DrawTexture(lens,glass);
        GUI.color=color;
        Color shadow=new(.10f,.10f,.075f,.94f);
        Color engraving=target?new Color(.98f,.75f,.38f):new Color(.84f,.78f,.61f,.94f);
        s=Mathf.Min(s,diameter/550f);
        void Etch(float x,float y,float w,float h)
        {
            var mark=new Rect(middle.x+x*s,middle.y+y*s,Mathf.Max(1,w*s),Mathf.Max(1,h*s));
            OsowiecHudPaint.Fill(new Rect(mark.x-1,mark.y-1,mark.width+2,mark.height+2),shadow);
            OsowiecHudPaint.Fill(mark,engraving);
        }
        // Fine open intersection keeps the centre visible against both bright sky and the dark trench.
        for(int side=-1;side<=1;side+=2)
        {
            Etch(side<0?-94:5,-.65f,89,1.3f);
            Etch(-.65f,side<0?-29:5,1.3f,24);
            for(int i=1;i<=4;i++)
                Etch(side*(15+i*15)-.5f,0,1,(i%2==0 ? 7 : 4));
        }
        return lens;
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
            Picture(new Rect(point.x-37,point.y-37,74,59),Icon(item==OsowiecKitItem.Gas?7:i),quantity==0?new Color(.55f,.53f,.45f):active?Color.white:Paper);
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

