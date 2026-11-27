using System.Collections.Generic;
using UnityEngine;

internal readonly struct OsowiecMapVitals
{
    internal readonly int hp, maximum, filter;
    internal readonly bool inGas, mask;
    internal bool Critical => hp*3<=maximum;
    bool GasAlert => inGas && (!mask || filter<=0);
    bool ShowFilter => inGas || mask || filter<=5;
    internal string GasLabel => inGas && !mask ? "ГАЗ · НЕТ МАСКИ" : filter==0 ? (inGas?"ГАЗ · ФИЛЬТР ПУСТ":"ФИЛЬТР ПУСТ") :
        (inGas ? "ГАЗ · " : "") + "ФИЛЬТР " + filter + " с";
    internal string Summary => $"{hp}/{maximum} ОЗ  ·  " + (Critical ? "Тяжело ранен  ·  " : "") +
        (inGas && !mask ? $"Газ без маски  ·  Фильтр {filter} с" : ShowFilter ? GasLabel : $"Фильтр {filter} с");

    internal OsowiecMapVitals(Unit unit)
    {
        hp=unit.GetHealth(); maximum=Mathf.Max(1,unit.GetMaxHealth());
        var op=OsowiecOperation.Instance;
        inGas=OsowiecOperation.InGas(unit);
        mask=op.MaskOn(unit); filter=Mathf.CeilToInt(op.RemainingFilter(unit));
    }

    static float Scale => Mathf.Max(.82f,OsowiecHudPaint.Scale);
    static GUIStyle label;
    static Texture2D heart;
    internal Rect Place(Rect anchor,List<Rect> occupied)
    {
        float s=Scale;
        var size=new Vector2((ShowFilter?126:52)*s,(ShowFilter?36:20)*s);
        for(int i=0;i<16;i++)
        {
            int row=i/4;
            float y=anchor.yMax+(2+row*20)*s;
            Vector2 position=(i%4) switch {
                0=>new Vector2(anchor.center.x-size.x/2,y),
                1=>new Vector2(anchor.xMax+3*s,anchor.yMin+row*20*s),
                2=>new Vector2(anchor.xMin-size.x-3*s,anchor.yMin+row*20*s),
                _=>new Vector2(anchor.center.x-size.x/2,anchor.yMin-size.y-(2+row*20)*s)
            };
            Rect panel=new(Mathf.Clamp(position.x,1,Mathf.Max(1,Screen.width-size.x-1)),
                Mathf.Clamp(position.y,1,Mathf.Max(1,Screen.height-size.y-1)),size.x,size.y);
            bool blocked=false;
            foreach(var taken in occupied) if(taken.Overlaps(panel)){blocked=true;break;}
            if(!blocked || i==15) {occupied.Add(panel);return panel;}
        }
        return default;
    }

    internal void Draw(Rect box)
    {
        float s=Scale;
        Color red=new(.96f,.43f,.34f), amber=new(.94f,.76f,.42f);
        label??=new GUIStyle(GUIStyle.none){font=OsowiecHudPaint.Body,fontStyle=FontStyle.Bold,
            alignment=TextAnchor.MiddleLeft,clipping=TextClipping.Clip};
        label.fontSize=Mathf.Max(11,Mathf.RoundToInt(16*s));
        string value=hp.ToString();
        label.alignment=TextAnchor.MiddleLeft;
        float width=label.CalcSize(new GUIContent(value)).x;
        float x=box.center.x-(width+19*s)*.5f;
        if(!heart) heart=Resources.Load<Texture2D>("OsowiecKit/FieldHeart");
        var old=GUI.color;
        GUI.color=Critical?red:new Color(1,.78f,.65f);
        if(heart)GUI.DrawTexture(new Rect(x,box.y+2*s,16*s,16*s),heart);
        GUI.color=old;
        var line=new Rect(x+19*s,box.y,width+3*s,20*s);
        label.normal.textColor=new Color(.055f,.048f,.038f);
        for(int i=-1;i<=1;i++)for(int j=-1;j<=1;j++)
            if(i!=0 || j!=0)GUI.Label(new Rect(line.x+i*s,line.y+j*s,line.width,line.height),value,label);
        label.normal.textColor=new Color(1,.96f,.84f);
        GUI.Label(line,value,label);
        if(!ShowFilter) return;
        label.fontSize=Mathf.Max(10,Mathf.RoundToInt(13*s));
        label.alignment=TextAnchor.MiddleCenter;
        OsowiecHudPaint.Fill(new Rect(box.x,box.y+19*s,box.width,16*s),new Color(.10f,.10f,.08f,.90f));
        label.normal.textColor=GasAlert?red:amber;
        GUI.Label(new Rect(box.x,box.y+18*s,box.width,17*s),GasLabel,label);
    }
}
