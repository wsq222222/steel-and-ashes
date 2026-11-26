using UnityEngine;
using UnityEngine.AI;

// Short rounds between duties at the battery. The turn brain retains ownership
// of pursuit, attack, reserves and interception of the relief column.
[DisallowMultipleComponent]
public sealed class OsowiecBatteryPatrol : MonoBehaviour
{
    Unit sentry;
    CharacterController motor;
    Vector3 station;
    NavMeshPath path;
    readonly Vector3[] route=new Vector3[12];
    int nextCorner,count,round,seed;
    uint turn=uint.MaxValue;
    float departAt,blockedFor;
    internal float DistanceWalked { get; private set; }

    void Start()
    {
        path=new NavMeshPath();
        sentry=GetComponent<Unit>();
        motor=GetComponent<CharacterController>();
        station=transform.position;
        seed=Mathf.Abs(Mathf.RoundToInt(station.x*13f+station.z*17f));
        departAt=Time.time+.15f+(seed%5)*.16f;
    }

    void Update()
    {
        var game=GameManager.Instance;
        var op=OsowiecOperation.Instance;
        if (!game || !op || !motor || !motor.enabled || !sentry.isActiveAndEnabled ||
            sentry.GetHealth()<=0 || game.IsPaused() || game.IsGameOver() || game.IsArmyDeploymentPhase() ||
            game.IsBotTurn() || Time.deltaTime<=0) return;
        if (turn!=game.TurnSequence)
        {
            turn=game.TurnSequence;
            count=0;
            blockedFor=0;
        }
        if (!op.QuietMarch(sentry) || OsowiecOperation.InGas(sentry) ||
            (op.relief && op.relief.HoldingCart(sentry)) || (transform.position-station).sqrMagnitude>36f)
        {
            count=0;
            return;
        }
        if (Time.time<departAt)return;
        if (count==0 && !PlanRound())
        {
            departAt=Time.time+.8f;
            return;
        }

        Vector3 from=transform.position;
        var delta=Vector3.ProjectOnPlane(route[nextCorner]-from,Vector3.up);
        if (delta.sqrMagnitude<.09f*.09f)
        {
            if (++nextCorner>=count) Rest();
            return;
        }
        float dt=Mathf.Min(Time.deltaTime,.08f);
        var heading=delta.normalized;
        transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(heading),180f*dt);
        if (Vector3.Dot(transform.forward,heading)<.62f)return;
        float stride=Mathf.Min((.83f+(seed%5)*.07f)*dt,delta.magnitude);
        motor.Move(heading*stride+Vector3.down*(dt*.7f));
        float travel=Vector3.ProjectOnPlane(transform.position-from,Vector3.up).magnitude;
        DistanceWalked+=travel;
        if(travel>.001f) sentry.RefreshGridPositionFromWorld();
        blockedFor=travel<stride*.19f ? blockedFor+dt : 0;
        if(blockedFor>.4f) Rest();
    }

    bool PlanRound()
    {
        var here=transform.position;
        if(!NavMesh.SamplePosition(here,out var start,.55f,NavMesh.AllAreas))return false;
        for(int attempt=0;attempt<8;attempt++)
        {
            int mark=round++;
            float angle=(seed+mark*119f)*Mathf.Deg2Rad;
            var survey=station+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*(1.55f+(mark%3)*.50f);
            if(!NavMesh.SamplePosition(survey,out var stop,.80f,NavMesh.AllAreas) ||
                Mathf.Abs(stop.position.y-station.y)>.75f ||
                (stop.position-here).sqrMagnitude<.80f ||
                !NavMesh.CalculatePath(start.position,stop.position,NavMesh.AllAreas,path) ||
                path.status!=NavMeshPathStatus.PathComplete)continue;
            int n=path.GetCornersNonAlloc(route);
            if(n<2 || n==route.Length)continue;
            float metres=0;
            bool unsafeRoute=false;
            for(int i=1;i<n;i++)
            {
                metres+=Vector3.Distance(route[i-1],route[i]);
                unsafeRoute |= OsowiecGroundEvents.IsWarned(route[i]) ||
                    OsowiecOperation.GasAt(route[i]+Vector3.up*.6f);
            }
            if(unsafeRoute || metres>5.8f)continue;
            count=n;nextCorner=1;blockedFor=0;
            return true;
        }
        return false;
    }

    void Rest()
    {
        count=0;
        blockedFor=0;
        departAt=Time.time+1.0f+((round+seed)%4)*.49f;
    }
}
