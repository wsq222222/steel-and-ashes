using UnityEngine;

// A report belongs to a resolved shot. Unknown shooters never receive a map marker.
public sealed class OsowiecEnemyFireReadout : MonoBehaviour
{
    static OsowiecEnemyFireReadout active;
    float remaining;
    string notice;
    Vector3 spot;
    bool showSpot;
    float casualtyUntil;
    GUIStyle textStyle;
    void OnEnable() => active = this;
    void OnDisable() { if(active==this) active=null; }

    public static void Report(Unit shooter, Unit hit, Vector3 start, Vector3 end, float loss)
    {
        if (!active || Time.time < active.casualtyUntil || !shooter || shooter.owner!=Player.Player2) return;
        bool visible = FogWarIntelTracker.Instance &&
            FogWarIntelTracker.Instance.TryGetDisplayPosition(shooter,out _,out bool exact) && exact;
        bool friendlyHit = hit && hit.owner==Player.Player1;
        if (!visible && !friendlyHit) return;
        active.spot = friendlyHit ? end : start;
        active.showSpot = true;
        active.notice = friendlyHit ? shooter.GetLocalizedDisplayName() + " → " + hit.GetLocalizedDisplayName() + (loss>0 ? "  −"+Mathf.CeilToInt(loss)+" ОЗ" : "  ·  попадание")
                                    : shooter.GetLocalizedDisplayName()+"  ·  выстрел";
        active.remaining=1.65f;
    }

    public static void Casualty(Unit victim, string message)
    {
        if (!active) return;
        active.spot = victim.transform.position;
        active.showSpot = true;
        active.notice = message;
        active.remaining = 4;
        active.casualtyUntil = Time.time + active.remaining;
    }

    public static void Alert(Unit sentry, Unit seen)
    {
        if (!active || Time.time < active.casualtyUntil) return;
        active.notice = sentry.GetLocalizedDisplayName() + " заметил бойца «"+seen.GetLocalizedDisplayName()+"». Вызван караул";
        active.showSpot = false;
        active.remaining = 3;
    }

    public static void Intercept(Unit sentry)
    {
        if (!active || Time.time < active.casualtyUntil || !OsowiecOperation.Instance.SquadSees(sentry)) return;
        active.notice = sentry.GetLocalizedDisplayName() + " идёт к повозке";
        active.showSpot = false;
        active.remaining = 2;
    }

    void Update() { if (remaining>0 && !(GameManager.Instance && GameManager.Instance.IsPaused())) remaining-=Time.deltaTime; }
    public static void Draw()
    {
        if(active) active.DrawMessage();
    }

    void DrawMessage()
    {
        if (Event.current.type!=EventType.Repaint || !CameraManager.Instance ||
            !GameManager.Instance || GameManager.Instance.IsPaused() || GameManager.Instance.IsGameOver()) return;
        var op = OsowiecOperation.Instance;
        Unit threatened = op ? op.ThreatenedUnit : null;
        Unit source = threatened ? op.ThreatSource(threatened) : null;
        bool warning = source && Time.time >= casualtyUntil;
        if (!warning && remaining <= 0) return;
        float scale=OsowiecHudPaint.Scale;
        var color=GUI.color; GUI.color=new Color(1,1,1,warning ? 1 : Mathf.Clamp01(remaining*5));
        textStyle??=new GUIStyle(GUIStyle.none){font=OsowiecHudPaint.Body,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleCenter};
        textStyle.fontSize=Mathf.Clamp(Mathf.RoundToInt(17*scale),13,27);
        textStyle.normal.textColor=new Color(.98f,.84f,.64f);
        string message = notice;
        if (warning)
        {
            var delta = source.transform.position - threatened.transform.position;
            string side = Mathf.Abs(delta.x) > Mathf.Abs(delta.z) ? (delta.x > 0 ? "восток" : "запад") :
                (delta.z > 0 ? "север" : "юг");
            if (CameraManager.Instance.IsActionMode())
            {
                var cam = CameraManager.Instance.GetActionCamera().transform;
                var relative = cam.InverseTransformDirection(Vector3.ProjectOnPlane(delta, Vector3.up));
                side = Mathf.Abs(relative.x) > Mathf.Abs(relative.z) ? (relative.x > 0 ? "справа" : "слева") :
                    (relative.z > 0 ? "впереди" : "сзади");
            }
            message = source.GetLocalizedDisplayName()+" · "+side+" · под прицелом «"+threatened.GetLocalizedDisplayName()+
                "» · "+op.ThreatSeconds(threatened).ToString("0.0")+" с";
        }
        var content=new GUIContent(message);
        float w=textStyle.CalcSize(content).x+18*scale;
        var box=new Rect((Screen.width-w)*.5f,Screen.height*.14f,w,26*scale);
        OsowiecHudPaint.Panel(box);GUI.Label(box,content,textStyle);
        var camera=CameraManager.Instance.GetTacticalCamera();
        if(!warning && showSpot && !CameraManager.Instance.IsActionMode() && camera) {
            var p=camera.WorldToScreenPoint(spot);
            if(p.z>0) {
                float s=scale*(8+6*Mathf.Clamp01(remaining/1.65f));
                GUI.DrawTexture(new Rect(p.x-s,Screen.height-p.y-s,2*s,2*s),Texture2D.whiteTexture,
                    ScaleMode.StretchToFill,true,0,new Color(.98f,.65f,.29f),2*scale,s);
            }
        }
        GUI.color=color;
    }
}
