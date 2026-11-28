using UnityEngine;
using UnityEngine.InputSystem;

// Status slip in the lower margin of the soldier's field book.
public static class OsowiecActionReadout
{
    static readonly Color Brown=new(.20f,.15f,.10f);
    static readonly Color Dull=new(.35f,.30f,.20f);
    static readonly Color Red=new(.50f,.18f,.11f);
    static readonly Color Green=new(.26f,.32f,.22f);
    static Texture2D book;
    static GUIStyle type;

    static void Print(Rect box,string value,float size,Color color,bool heading=false)
    {
        type??=new GUIStyle {alignment=TextAnchor.MiddleLeft,clipping=TextClipping.Clip};
        type.font=heading?OsowiecHudPaint.Heading:OsowiecHudPaint.Body;
        type.fontSize=Mathf.RoundToInt(size);
        type.normal.textColor=color;
        GUI.Label(box,OsowiecMenuLanguage.Text(value),type);
    }

    static void Datum(float x,string label,string value,Color color)
    {
        Print(new Rect(x,37,102,18),label,13,Dull);
        Print(new Rect(x,53,112,28),value,21,color,true);
    }

    public static void Draw(Unit actor,float filter,bool mask,string guidance)
    {
        if(OsowiecFieldKit.ScopeOpen) return;
        float s=Mathf.Min(OsowiecHudPaint.Scale,(Screen.width-24)/560f);
        var previous=GUI.matrix;
        GUI.matrix=Matrix4x4.TRS(new Vector3(20*s,Screen.height-137*s,0),Quaternion.identity,new Vector3(s,s,1));
        const float width=527;
        var rect=new Rect(0,0,width,110);
        bool browsing=OsowiecFieldKit.WheelOpen;
        if(!book) book=Resources.Load<Texture2D>("OsowiecJournal/journal");
        OsowiecHudPaint.Fill(new Rect(2,3,width,110),new Color(.06f,.05f,.03f,.6f));
        if(book) GUI.DrawTextureWithTexCoords(rect,book,new Rect(.17f,.27f,.25f,.39f));
        else OsowiecHudPaint.Fill(rect,new Color(.82f,.75f,.59f));
        OsowiecHudPaint.Fill(new Rect(0,0,5,110),Red);
        string role=actor.chessType==ChessUnitType.Молот?"Штурмовик":actor.chessType==ChessUnitType.Циклоп?"Наблюдатель":"Разведчик";
        Print(new Rect(14,3,176,31),actor.GetUnitDisplayName(),24,Brown,true);
        Print(new Rect(189,7,171,23),actor.FieldTurn.Observed ? OsowiecMenuLanguage.Text("×2 с 10 м · до 84") : role,17,
            actor.FieldTurn.Observed ? Green : Dull);
        Print(new Rect(365,7,156,23),"Tab   Закончить ход",16,Dull);
        OsowiecHudPaint.Fill(new Rect(14,33,width-29,1),new Color(.40f,.31f,.18f,.65f));
        var weapon=actor.GetEquippedWeapon();
        Datum(15,"ЗДОРОВЬЕ",actor.GetHealth()+" / "+actor.GetMaxHealth(),actor.GetHealth()<40?Red:Brown);
        bool free = OsowiecMarch.Free(actor);
        Datum(144, free ? "ПЕРЕДВИЖЕНИЕ" : "ОСТАЛОСЬ ХОДА",
            free ? "Марш" : OsowiecMenuLanguage.Format("{0:0.0} м",actor.GetRemainingMoveMeters()), Brown);
        Datum(285,"ПАТРОНЫ",weapon?weapon.AmmoInMag+" / "+weapon.AmmoReserve:"—",Brown);
        Datum(410,mask?"ФИЛЬТР · НАДЕТ":"ФИЛЬТР",OsowiecMenuLanguage.Format("{0} с",Mathf.CeilToInt(filter)),filter<6?Red:Green);
        string keys=mask?"T   Снять маску     X   Протереть":"T   Надеть маску";
        if(weapon && weapon.IsReloading) keys="Перезарядка…";
        var held=OsowiecFieldKit.Held(actor);
        if(OsowiecFieldKit.Throwable(held))
            keys=OsowiecMenuLanguage.Format("Держите ЛКМ     {0} ×{1}",OsowiecKitWheel.Name(held),OsowiecFieldKit.Remaining(actor,held));
        else if(held==OsowiecKitItem.Bayonet) keys="ЛКМ   Удар штыком";
        Print(new Rect(15,81,500,24),OsowiecMenuLanguage.Text(keys)+"       "+OsowiecMenuLanguage.Text("G / F1   Снаряжение"),16,Dull);
        if(!browsing && !string.IsNullOrWhiteSpace(guidance))
        {
            string value=guidance;
            var ribbon=new Rect(0,-43,width,40);
            OsowiecHudPaint.Fill(ribbon,new Color(.10f,.12f,.095f,.94f));
            type.wordWrap=true;
            Print(new Rect(12,-42,width-24,38),value,17,new Color(.95f,.90f,.75f));
            type.wordWrap=false;
        }
        float suppression = OsowiecOperation.Instance.Pressure(actor);
        float statusY = string.IsNullOrWhiteSpace(guidance) ? -30 : -73;
        if (!browsing && actor.FieldTurn.Observed)
        {
            float x = suppression >= .5f ? 164 : 0;
            OsowiecHudPaint.Fill(new Rect(x,statusY,154,28),new Color(.13f,.18f,.12f,.94f));
            var ink = new Color(.88f,.90f,.65f);
            Print(new Rect(x+8,statusY,138,28),OsowiecMenuLanguage.Text("»  Наблюдение"),17,ink,true);
        }
        if (!browsing && suppression >= .5f)
        {
            bool full = suppression >= .999f;
            float intensity = full || OsowiecFieldSettings.ReducedEffects ? 1f :
                .7f + .3f * Mathf.Cos(Time.time * 5f);
            float y = statusY;
            OsowiecHudPaint.Fill(new Rect(0,y,159,28),new Color(.10f,.09f,.08f,.92f));
            var ink = full ? new Color(1f,.32f,.25f) : new Color(1f,.82f,.32f,intensity);
            Print(new Rect(10,y,145,28),full ? "!  Подавление" : "Подавление",18,ink);
        }
        GUI.matrix=previous;
        if (!free && Time.time < actor.FieldTurn.ContactNoticeUntil)
        {
            var notice = new Rect((Screen.width-298*s)*.5f, 52*s, 298*s, 36*s);
            OsowiecHudPaint.Fill(notice, new Color(.20f,.09f,.05f,.94f));
            OsowiecHudPaint.Text(notice, "КОНТАКТ  ·  БОЕВОЙ ХОД", 19, new Color(.99f,.85f,.59f), true);
        }
    }
}
