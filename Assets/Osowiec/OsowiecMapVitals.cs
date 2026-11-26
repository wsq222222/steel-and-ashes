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
    internal Rect Place(Rect anchor,List<Rect> occupied)
    {
        float s=Scale;
        var size=new Vector2((ShowFilter?126:72)*s,(ShowFilter?36:20)*s);
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
        Color red=new(.92f,.49f,.36f), amber=new(.94f,.76f,.42f), green=new(.65f,.77f,.55f);
        OsowiecHudPaint.Fill(box,new Color(.105f,.115f,.09f,.97f));
        var band=new Rect(box.x,box.y,box.width,2*s);
        OsowiecHudPaint.Fill(band,new Color(.32f,.31f,.23f));
        band.width*=Mathf.Clamp01((float)hp/maximum);
        OsowiecHudPaint.Fill(band,Critical?red:green);
        label??=new GUIStyle(GUIStyle.none){font=OsowiecHudPaint.Body,fontStyle=FontStyle.Bold,
            alignment=TextAnchor.MiddleCenter,clipping=TextClipping.Clip};
        label.fontSize=Mathf.Max(11,Mathf.RoundToInt(15*s));
        label.normal.textColor=new Color(.97f,.94f,.82f);
        var line=new Rect(box.x,box.y+2*s,box.width,17*s);
        if(Critical)
        {
            OsowiecHudPaint.Fill(new Rect(line.x+4*s,line.center.y-1.5f*s,9*s,3*s),red);
            OsowiecHudPaint.Fill(new Rect(line.x+7*s,line.center.y-4.5f*s,3*s,9*s),red);
            line.xMin+=10*s;
        }
        GUI.Label(line,$"{hp}/{maximum}",label);
        if(!ShowFilter) return;
        label.fontSize=Mathf.Max(10,Mathf.RoundToInt(13*s));
        label.normal.textColor=GasAlert?red:amber;
        GUI.Label(new Rect(box.x,box.y+18*s,box.width,17*s),GasLabel,label);
    }
}
