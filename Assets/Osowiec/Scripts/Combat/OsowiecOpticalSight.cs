using System.Collections.Generic;
using UnityEngine;

internal enum OsowiecSightState { Empty, Visible, Cover, Gas, Smoke }
internal enum OsowiecObservation { None, Marked, Cooling, Empty, Cover, Gas, Smoke }

public sealed partial class OsowiecFieldKit
{
    internal Unit SightedEnemy { get; private set; }
    internal OsowiecSightState SightState { get; private set; }
    internal OsowiecObservation LastObservation { get; private set; }
    internal int MarkTurns => IsIdentified(tagged) ? taggedUntil-round : 0;
    internal bool MarkVisible { get; private set; }
    internal int ConfirmedDamage { get; private set; }
    internal int ConfirmedRemaining { get; private set; }
    internal int ConfirmedHits { get; private set; }
    readonly Dictionary<Unit,Collider[]> sightShapes=new();
    bool markRequested;
    float observationUntil, hitUntil;
    string hitName;
    Vector3 markerAnchor;
    string MarkLabel => OsowiecMenuLanguage.Format("МЕТКА · {0} х.",MarkTurns);
    string SightMessage => OsowiecMenuLanguage.Text(SightState switch {
        OsowiecSightState.Gas => "Газ закрывает обзор",
        OsowiecSightState.Smoke => "Дым закрывает обзор",
        OsowiecSightState.Cover => "Линия обзора перекрыта",
        _ => "Наведите перекрестие на силуэт"
    });

    internal static OsowiecSightState PlayerSightAir(Vector3 eye,Vector3 point)
    {
        // 14% transmitted light still shows a silhouette against a bright bank.
        if(OsowiecMistBody.OpticalThickness(eye,point)>1.97f || GasCloudSightDepth(eye,point)>=2.25f)
            return OsowiecSightState.Gas;
        if(CloudSightDepth(eye,point,false)>=2.0f)return OsowiecSightState.Smoke;
        return OsowiecSightState.Empty;
    }

    Collider[] Silhouette(Unit u)
    {
        if(sightShapes.TryGetValue(u,out var shapes))return shapes;
        var parts=u.GetComponentsInChildren<OsowiecHitPart>(true);
        var list=new List<Collider>(4);
        foreach(var part in parts)
            if(part.area is OsowiecHitArea.Head or OsowiecHitArea.Torso or OsowiecHitArea.Helmet)
            {
                var shape=part.GetComponent<Collider>();
                if(shape)list.Add(shape);
            }
        return sightShapes[u]=list.ToArray();
    }

    void RefreshSight(Camera camera)
    {
        SightedEnemy=null;SightState=OsowiecSightState.Empty;MarkVisible=false;
        var operation=OsowiecOperation.Instance;
        if(!camera || !operation || !actor)return;
        Vector3 eye=camera.transform.position,forward=camera.transform.forward;
        var ray=new Ray(eye,forward);
        const float range=70f;
        float best=.046f,closest=float.PositiveInfinity,blockedOffset=float.PositiveInfinity;
        bool obstructed=false;
        float rayEnd=range;
        if(OsowiecHitBody.Trace(actor,ray,range,~0,out var hit,out _))
        {
            rayEnd=hit.distance;
            var u=hit.collider.GetComponentInParent<Unit>();
            if(u && u.owner!=actor.owner && u.GetHealth()>0)Consider(u,hit.point,true);
        }
        foreach(var u in operation.FieldRoster)
        {
            if(!u || !u.isActiveAndEnabled || u.owner==actor.owner || u.GetHealth()<=0)continue;
            var root=camera.WorldToViewportPoint(u.transform.position+Vector3.up*1.05f);
            // Allow the entire near silhouette to extend outside the centre, including an animated helmet.
            if(root.z<=0 || root.z>range+1)continue;
            float window=.075f+2f/(root.z*Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*.5f));
            if(Mathf.Abs((root.x-.5f)*camera.aspect)>window || Mathf.Abs(root.y-.5f)>window)continue;
            var shapes=Silhouette(u);
            foreach(var shape in shapes)
            {
                if(!shape || !shape.enabled || !shape.gameObject.activeInHierarchy)continue;
                var b=shape.bounds;
                Consider(u,b.center,false);
                Consider(u,b.center+Vector3.up*(b.extents.y*.75f),false);
            }
            if(shapes.Length==0)Consider(u,u.transform.position+Vector3.up*1.22f,false);
        }
        if(!SightedEnemy && !obstructed)SightState=PlayerSightAir(eye,ray.GetPoint(rayEnd));
        if(MarkTurns>0)
        {
            var head=tagged.cameraAttachPoint ? tagged.cameraAttachPoint.position : tagged.transform.position+Vector3.up*1.65f;
            var v=camera.WorldToViewportPoint(head);
            if(v.z>0 && v.z<=range && v.x>=0 && v.x<=1 && v.y>=0 && v.y<=1)
            {
                var delta=head-eye;
                MarkVisible=tagged==SightedEnemy || (SightClearance(eye,delta.normalized,delta.magnitude,0,tagged)>=delta.magnitude-.015f &&
                    PlayerSightAir(eye,head)==OsowiecSightState.Empty);
                if(MarkVisible){taggedPosition=tagged.transform.position;markerAnchor=head+Vector3.up*.24f;}
            }
        }

        void Consider(Unit u,Vector3 p,bool direct)
        {
            var v=camera.WorldToViewportPoint(p);
            if(v.z<=0)return;
            float offset=direct?0:new Vector2((v.x-.5f)*camera.aspect,v.y-.5f).magnitude;
            Vector3 delta=p-eye;float distance=delta.magnitude;
            if(offset>best || distance>range || (Mathf.Abs(offset-best)<.0001f && distance>=closest))return;
            var reason=OsowiecSightState.Empty;
            if(!direct && SightClearance(eye,delta.normalized,distance,0,u)<distance-.015f)reason=OsowiecSightState.Cover;
            else reason=PlayerSightAir(eye,p);
            if(reason!=OsowiecSightState.Empty)
            {
                if(!SightedEnemy && offset<blockedOffset){SightState=reason;blockedOffset=offset;obstructed=true;}
                return;
            }
            best=offset;closest=distance;SightedEnemy=u;SightState=OsowiecSightState.Visible;
        }
    }

    void ApplyObservation()
    {
        observationUntil=Time.time+2.1f;
        if(Cooling){LastObservation=OsowiecObservation.Cooling;return;}
        if(!SightedEnemy)
        {
            LastObservation=SightState switch{
                OsowiecSightState.Gas=>OsowiecObservation.Gas,OsowiecSightState.Smoke=>OsowiecObservation.Smoke,
                OsowiecSightState.Cover=>OsowiecObservation.Cover,_=>OsowiecObservation.Empty};
            return;
        }
        tagged=SightedEnemy;taggedUntil=round+2;taggedPosition=tagged.transform.position;
        markerAnchor=(tagged.cameraAttachPoint ? tagged.cameraAttachPoint.position : taggedPosition+Vector3.up*1.65f)+Vector3.up*.24f;
        MarkVisible=true;LastObservation=OsowiecObservation.Marked;
        FogWarIntelTracker.Instance?.MarkVisible(tagged,taggedPosition);
        OsowiecAtmosphere.PlayOptics(actor.transform.position);
        FinishSkill();
    }

    internal static void ReportPlayerShot(Unit shooter,Unit victim,int damage)
    {
        var kit=Instance;
        if(!kit || !shooter || kit.actor!=shooter)return;
        kit.hitUntil=0;kit.ConfirmedDamage=0;
        if(!victim || victim.owner==shooter.owner || damage<=0)return;
        kit.ConfirmedDamage=damage;kit.ConfirmedRemaining=Mathf.Max(0,victim.GetHealth());kit.ConfirmedHits++;
        kit.hitName=victim.GetUnitDisplayName();kit.hitUntil=Time.time+1.65f;
    }

    void DrawSightReports(Camera camera)
    {
        float s=OsowiecHudPaint.Scale;
        var center=new Vector2(Screen.width*.5f,Screen.height*.5f);
        bool attached=false;
        if(MarkTurns>0 && MarkVisible && camera)
        {
            var v=camera.WorldToScreenPoint(markerAnchor);
            var anchor=new Vector2(v.x,Screen.height-v.y);
            if(v.z>0 && (!scope || Vector2.Distance(anchor,center)<Mathf.Min(Screen.width,Screen.height)*.39f))
            {
                OsowiecHudPaint.FittedPlate(anchor-Vector2.up*40*s,MarkLabel,270*s);
                attached=true;
            }
        }
        if(MarkTurns>0 && !attached)
            OsowiecHudPaint.FittedPlate(new Vector2(170*s,Screen.height*.56f),
                OsowiecMenuLanguage.Text(tagged.GetUnitDisplayName())+"   ·   "+MarkLabel,440*s);
        if(scope && observationUntil>Time.time)
        {
            string message=LastObservation switch {
                OsowiecObservation.Marked=>OsowiecMenuLanguage.Format("Цель отмечена · {0}",tagged ? OsowiecMenuLanguage.Text(tagged.GetUnitDisplayName()) : ""),
                OsowiecObservation.Cooling=>"Новая метка будет доступна в следующий ход",
                OsowiecObservation.Cover=>"Метка не поставлена   ·   укрытие",
                OsowiecObservation.Smoke=>"Метка не поставлена   ·   дым",
                OsowiecObservation.Gas=>"Метка не поставлена   ·   газ",
                _=>"Метка не поставлена   ·   цель не найдена"};
            OsowiecHudPaint.FittedPlate(center+Vector2.up*73*s,OsowiecMenuLanguage.Text(message),620*s);
        }
        if(hitUntil<=Time.time || ConfirmedDamage<=0)return;
        var ink=new Color(1,.83f,.53f,Mathf.Clamp01((hitUntil-Time.time)*5));
        string feedback=OsowiecMenuLanguage.Format("{0}   −{1} ОЗ   ·   Осталось {2} ОЗ",
            OsowiecMenuLanguage.Text(hitName),ConfirmedDamage,ConfirmedRemaining);
        OsowiecHudPaint.FittedPlate(center+Vector2.up*51*s,feedback,680*s);
        if(hitUntil-Time.time<1.15f)return;
        var matrix=GUI.matrix;
        GUIUtility.RotateAroundPivot(45,center);
        for(int side=-1;side<=1;side+=2)
        {
            OsowiecHudPaint.Fill(new Rect(center.x+(side<0?-22:9)*s,center.y-s,13*s,2*s),ink);
            OsowiecHudPaint.Fill(new Rect(center.x-s,center.y+(side<0?-22:9)*s,2*s,13*s),ink);
        }
        GUI.matrix=matrix;
    }
}
