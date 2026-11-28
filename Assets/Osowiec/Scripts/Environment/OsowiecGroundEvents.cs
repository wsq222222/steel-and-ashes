using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using System.Collections.Generic;

/// <summary>Two authored collapses in the wet neutral ground and short removable rolls of field wire.</summary>
public sealed class OsowiecGroundEvents : MonoBehaviour
{
    public GameObject[] intact;
    public GameObject[] hollows;
    public GameObject[] wire;
    public Material warningMaterial;
    public Material debrisMaterial;
    public Material plumeMaterial;
    public Mesh chipMesh;
    public MeshFilter water;

    static readonly Vector3[] Sites = { new(-4, 0, 13), new(11, 0, 11) };
    static readonly Vector3[] Snags = { new(-12.8f, 0, 8.8f), new(15, 0, 7.7f) };
    static OsowiecGroundEvents live;
    readonly bool[] collapsed = new bool[2];
    readonly bool[] removed = new bool[2];
    readonly float[] warningTime = new float[2];
    readonly LineRenderer[] outline = new LineRenderer[2];
    float activeSeconds, quietUntil, flashLeft;
    bool distantFired;
    Light impactFlash;
    LineRenderer distantStreak;
    float untilDistantFlight=2.7f, flightTime;
    int flightNumber;
    Vector3 flightStart, flightEnd;
    static readonly Vector3[] FarBanks={new(-20,0,23),new(24,0,23),new(18,0,-26),new(-36,0,27)};

    NavMeshSurface surface;
    NavMeshData initialNavigation;
    NavMeshData sessionNavigation;
    AsyncOperation rebuilding;
    bool needsRebuild;
    readonly Dictionary<Vector2Int,List<int>> waterGrid=new();
    Vector3[] waterPoints;
    int[] waterIndices;

    static Vector2Int Cell(Vector3 p) => new(Mathf.FloorToInt(p.x*.5f),Mathf.FloorToInt(p.z*.5f));

    void CacheWater()
    {
        if (!water || !water.sharedMesh) return;
        var mesh=water.sharedMesh;
        waterPoints=mesh.vertices;
        waterIndices=mesh.triangles;
        for(int i=0;i<waterPoints.Length;i++) waterPoints[i]=water.transform.TransformPoint(waterPoints[i]);
        for(int i=0;i<waterIndices.Length;i+=3)
        {
            var a=waterPoints[waterIndices[i]];
            var b=waterPoints[waterIndices[i+1]];
            var c=waterPoints[waterIndices[i+2]];
            var lo=Cell(Vector3.Min(a,Vector3.Min(b,c)));
            var hi=Cell(Vector3.Max(a,Vector3.Max(b,c)));
            for(int x=lo.x;x<=hi.x;x++)
            for(int y=lo.y;y<=hi.y;y++)
            {
                var key=new Vector2Int(x,y);
                if(!waterGrid.TryGetValue(key,out var ids)) waterGrid.Add(key,ids=new List<int>());
                ids.Add(i);
            }
        }
    }

    public static bool TryWaterAt(Vector3 hit,out Vector3 surfacePoint)
    {
        surfacePoint=default;
        if(!live || !live.waterGrid.TryGetValue(Cell(hit),out var triangles)) return false;
        foreach(int j in triangles)
        {
            var a=live.waterPoints[live.waterIndices[j]];
            if(hit.y>a.y+.075f || hit.y<a.y-.8f) continue;
            var b=live.waterPoints[live.waterIndices[j+1]];
            var c=live.waterPoints[live.waterIndices[j+2]];
            float e0=(b.x-a.x)*(hit.z-a.z)-(b.z-a.z)*(hit.x-a.x);
            float e1=(c.x-b.x)*(hit.z-b.z)-(c.z-b.z)*(hit.x-b.x);
            float e2=(a.x-c.x)*(hit.z-c.z)-(a.z-c.z)*(hit.x-c.x);
            if((e0<-.00001f||e1<-.00001f||e2<-.00001f) && (e0>.00001f||e1>.00001f||e2>.00001f)) continue;
            surfacePoint=new Vector3(hit.x,a.y,hit.z);
            return true;
        }
        return false;
    }

    void Awake() => live = this;

    void Start()
    {
        CacheWater();
        impactFlash=new GameObject("Distant ordnance light").AddComponent<Light>();
        impactFlash.transform.SetParent(transform,false);
        impactFlash.type=LightType.Point;
        impactFlash.color=new Color(1,.68f,.32f);
        impactFlash.range=13f;
        impactFlash.shadows=LightShadows.None;
        impactFlash.enabled=false;
        distantStreak=new GameObject("Distant passing shell · horizon trail").AddComponent<LineRenderer>();
        distantStreak.transform.SetParent(transform,false);
        distantStreak.useWorldSpace=true;
        distantStreak.positionCount=5;
        distantStreak.widthCurve=new AnimationCurve(new Keyframe(0,0),new Keyframe(.74f,1),new Keyframe(1,.38f));
        distantStreak.widthMultiplier=.072f;
        distantStreak.numCapVertices=2;
        distantStreak.shadowCastingMode=ShadowCastingMode.Off;
        distantStreak.receiveShadows=false;
        distantStreak.sharedMaterial=warningMaterial;
        distantStreak.enabled=false;
        surface = FindFirstObjectByType<NavMeshSurface>();
        if (surface == null || !surface.enabled)
        {
            foreach (var candidate in FindObjectsByType<NavMeshSurface>(FindObjectsSortMode.None))
                if (candidate.isActiveAndEnabled && candidate.navMeshData != null) { surface = candidate; break; }
        }
        if (surface != null && surface.isActiveAndEnabled && surface.navMeshData != null)
        {
            initialNavigation = surface.navMeshData;
            sessionNavigation = Instantiate(initialNavigation);
            surface.RemoveData();
            surface.navMeshData = sessionNavigation;
            surface.AddData();
        }
        for (int i = 0; i < Sites.Length; i++)
        {
            var line = new GameObject("Slipping peat warning").AddComponent<LineRenderer>();
            line.transform.SetParent(transform, false);
            line.loop = true;
            line.useWorldSpace = true;
            line.positionCount = 48;
            line.sharedMaterial = warningMaterial;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.startWidth = line.endWidth = .045f;
            for (int j = 0; j < 48; j++)
            {
                float angle = j * Mathf.PI * 2 / 48;
                float r = 1.43f + .10f * Mathf.Sin(angle * 5 + i);
                line.SetPosition(j, Sites[i] + new Vector3(r*Mathf.Cos(angle),.095f,r*Mathf.Sin(angle)));
            }
            line.enabled = false;
            outline[i] = line;
        }
    }

    void Update()
    {
        if (needsRebuild && sessionNavigation != null && (rebuilding == null || rebuilding.isDone))
        {
            needsRebuild = false;
            rebuilding = surface.UpdateNavMesh(sessionNavigation);
        }
        var game = GameManager.Instance;
        if (game == null || game.IsPaused() || game.IsArmyDeploymentPhase() || game.IsGameOver()) return;
        AnimateDistantFlight(Time.deltaTime);
        var cam = CameraManager.Instance;
        var unit = cam != null && cam.IsActionMode() ? cam.GetCurrentControlledUnit() : null;
        if(flashLeft>0f)
        {
            flashLeft=Mathf.Max(0f,flashLeft-Time.deltaTime);
            impactFlash.intensity=flashLeft*75f;
            impactFlash.enabled=flashLeft>0;
        }
        if (unit == null || unit.owner != Player.Player1 || game.IsBotTurn()) return;
        activeSeconds+=Time.deltaTime;
        if(!distantFired && activeSeconds>4.5f)
        {
            distantFired=true;
            var distant=new Vector3(-23,3,21);
            if(Physics.Raycast(distant+Vector3.up*20,Vector3.down,out var ground,45,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
                distant.y=ground.point.y;
            ShellBurst(distant,true);
            quietUntil=activeSeconds+6.5f;
        }
        if(!distantFired || activeSeconds<quietUntil) return;
        var mission=OsowiecOperation.Instance;
        for (int i = 0; i < Sites.Length; i++)
        {
            if (collapsed[i]) continue;
            if(warningTime[i]==0f)
            {
                bool ready=i==0 ? mission && mission.CompletedOrders>=1 && unit.transform.position.x>-26 :
                    mission && mission.CompletedOrders>=3;
                if(!ready) continue;
                OsowiecAtmosphere.PlayShellWarning(Sites[i]);
            }
            warningTime[i] += Time.deltaTime;
            outline[i].enabled = true;
            outline[i].startWidth = outline[i].endWidth = .035f + .027f * Mathf.Abs(Mathf.Sin(warningTime[i] * 6));
            if (warningTime[i] < 4.0f) break;
            outline[i].enabled = false;
            collapsed[i] = true;
            intact[i].SetActive(false);
            hollows[i].SetActive(true);
            Physics.SyncTransforms();
            needsRebuild = true;
            if(i==1) ClearWire(Snags[1],1.5f);
            ShellBurst(Sites[i],false);
            quietUntil=activeSeconds+16;
            break;
        }
    }

    void AnimateDistantFlight(float dt)
    {
        if(!distantStreak.enabled)
        {
            untilDistantFlight-=dt;
            if(untilDistantFlight>0)return;
            flightEnd=FarBanks[flightNumber%FarBanks.Length];
            if(Physics.Raycast(flightEnd+Vector3.up*28,Vector3.down,out var ground,45,
                                Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
                flightEnd.y=ground.point.y;
            flightStart=flightEnd+new Vector3(flightNumber%2==0?-22:24,13,9);
            flightTime=0;
            distantStreak.enabled=true;
            flightNumber++;
        }
        flightTime=Mathf.Min(1,flightTime+dt/.90f);
        for(int i=0;i<5;i++)
        {
            float age=Mathf.Max(0,flightTime-(4-i)*.035f);
            Vector3 p=Vector3.Lerp(flightStart,flightEnd,age);
            p.y+=2.4f*Mathf.Sin(age*Mathf.PI);
            distantStreak.SetPosition(i,p);
        }
        if(flightTime<1)return;
        distantStreak.enabled=false;
        if(plumeMaterial)
        {
            var gameRandom=Random.state;
            try { Random.InitState(1753+flightNumber);ThrowDust(flightEnd,true); }
            finally { Random.state=gameRandom; }
        }
        untilDistantFlight=OsowiecFieldSettings.ReducedEffects ? 18f : (flightNumber%3==1 ? 5.8f : 9.7f);
    }

    public static bool IsWarned(Vector3 point)
    {
        if(!live) return false;
        for(int i=0;i<Sites.Length;i++)
            if(!live.collapsed[i] && live.warningTime[i]>0 && FlatDistanceSquared(point,Sites[i])<12.25f) return true;
        return false;
    }

    void ShellBurst(Vector3 center,bool distant)
    {
        if(!OsowiecFieldSettings.ReducedEffects)
        {
            flashLeft=.14f;
            impactFlash.transform.position=center+Vector3.up;
            impactFlash.intensity=10;
            impactFlash.enabled=true;
        }
        ThrowPeat(center);
        if(plumeMaterial) ThrowDust(center,distant);
        OsowiecAtmosphere.PlayEarthfall(center);
        OsowiecStressEffects.Earthfall(center);
    }

    void ThrowDust(Vector3 center,bool distant)
    {
        var particles=new GameObject("Displaced silt and settling dust").AddComponent<ParticleSystem>();
        particles.transform.position=center+Vector3.up*.15f;
        particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=particles.main;
        main.loop=false;
        main.duration=.3f;
        main.startLifetime=new ParticleSystem.MinMaxCurve(1.6f,3.6f);
        main.simulationSpace=ParticleSystemSimulationSpace.World;
        main.startRotation=new ParticleSystem.MinMaxCurve(0,6.283f);
        var emission=particles.emission;emission.enabled=false;
        var size=particles.sizeOverLifetime;size.enabled=true;
        size.size=new ParticleSystem.MinMaxCurve(1f,AnimationCurve.EaseInOut(0,.3f,1,1.7f));
        var tint=particles.colorOverLifetime;tint.enabled=true;
        var gradient=new Gradient();
        gradient.SetKeys(new[]{new GradientColorKey(new Color(.21f,.16f,.105f),0),
            new GradientColorKey(new Color(.53f,.50f,.40f),1)},
            new[]{new GradientAlphaKey(.84f,0),new GradientAlphaKey(.47f,.38f),new GradientAlphaKey(0,1)});
        tint.color=gradient;
        var renderer=particles.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial=plumeMaterial;
        renderer.shadowCastingMode=ShadowCastingMode.Off;
        particles.Play();
        int count=OsowiecFieldSettings.ReducedEffects ? 12 : 33;
        for(int k=0;k<count;k++)
        {
            Vector2 spread=Random.insideUnitCircle;
            bool column=k%3==0;
            var shot=new ParticleSystem.EmitParams {
                position=particles.transform.position+new Vector3(spread.x,0,spread.y)*.69f,
                velocity=new Vector3(.65f+spread.x*(column?.46f:1.6f),column?Random.Range(1.5f,3.1f):Random.Range(.10f,.55f),spread.y),
                startSize=Random.Range(1f,1.9f)*(distant?1.5f:1f)
            };
            particles.Emit(shot,1);
        }
        Destroy(particles.gameObject,4f);
    }

    void OnGUI()
    {
        var game=GameManager.Instance;
        if(!game || game.IsPaused() || game.IsArmyDeploymentPhase() || game.IsGameOver()) return;
        for(int i=0;i<Sites.Length;i++)
            if(!collapsed[i] && warningTime[i]>0 && CameraManager.Instance && CameraManager.Instance.IsActionMode())
            {
                float scale=OsowiecHudPaint.Scale;
                var area=new Rect(Screen.width*.5f-150*scale,Screen.height*.71f,300*scale,29*scale);
                OsowiecHudPaint.Plate(area,i==0?"Обстрел у развилки":"Разрывы у восточной насыпи",true);
            }
    }
    void ThrowPeat(Vector3 center)
    {
        var particles = new GameObject("Peat and spray").AddComponent<ParticleSystem>();
        particles.transform.position = center + Vector3.up * .09f;
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.loop = false;
        main.duration = .95f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(.35f,.82f);
        main.startSize = new ParticleSystem.MinMaxCurve(.03f,.13f);
        main.gravityModifier = 1.0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = particles.emission;
        emission.enabled = false;
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = chipMesh;
        renderer.sharedMaterial = debrisMaterial;
        particles.Play();
        for (int i = 0; i < 32; i++)
        {
            Vector2 near = Random.insideUnitCircle;
            var shot = new ParticleSystem.EmitParams {
                position = particles.transform.position + new Vector3(near.x,0,near.y),
                velocity = new Vector3(near.x*1.8f, Random.Range(1.6f,3.7f), near.y*1.8f)
            };
            particles.Emit(shot,1);
        }
        Destroy(particles.gameObject, 1.5f);
    }

    static float FlatDistanceSquared(Vector3 a, Vector3 b)
    {
        float x = a.x-b.x, z = a.z-b.z;
        return x*x + z*z;
    }

    public static bool InCrater(Vector3 position)
    {
        if (live == null) return false;
        for (int i = 0; i < Sites.Length; i++)
            if (live.collapsed[i] && FlatDistanceSquared(position, Sites[i]) < 1.43f * 1.43f) return true;
        return false;
    }

    public static bool InWire(Vector3 position)
    {
        if (live == null) return false;
        for (int i = 0; i < Snags.Length; i++)
            if (!live.removed[i] && Mathf.Abs(position.x - Snags[i].x) < .7f &&
                Mathf.Abs(position.z - Snags[i].z) < 1.25f) return true;
        return false;
    }

    public static bool ClearWire(Vector3 position, float radius)
    {
        if (live == null || radius < 0) return false;
        bool changed = false;
        for (int i = 0; i < Snags.Length; i++)
        {
            if (live.removed[i] || FlatDistanceSquared(position, Snags[i]) > radius * radius) continue;
            live.removed[i] = true;
            live.wire[i].SetActive(false);
            changed = true;
        }
        return changed;
    }

    void OnDestroy()
    {
        if (live == this) live = null;
        if (sessionNavigation == null) return;
        if (rebuilding != null && !rebuilding.isDone) NavMeshBuilder.Cancel(sessionNavigation);
        if (surface != null && surface.navMeshData == sessionNavigation)
        {
            surface.RemoveData();
            surface.navMeshData = initialNavigation;
        }
        Destroy(sessionNavigation);
    }
}
