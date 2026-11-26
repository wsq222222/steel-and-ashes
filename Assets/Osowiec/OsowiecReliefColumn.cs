using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public sealed class OsowiecReliefColumn : MonoBehaviour
{
    public GameObject cartPrefab;
    public GameObject[] reserve = System.Array.Empty<GameObject>();
    public GameObject[] alarmGuard = System.Array.Empty<GameObject>();
    int requestedReserve, arrivedReserve, alarmArrivals;
    bool alarm;
    public bool AlarmRaised => alarm;
    public int GuardsArrived => alarmArrivals;
    public void SoundAlarm(Unit sentry = null, Unit seen = null)
    {
        if (alarm) return;
        alarm = true;
        nextArrivalCheck = Time.time + 2;
        OsowiecHudPaint.Caption("Гудок караульни. Подкрепление на подходе.");
        if (sentry && seen) OsowiecEnemyFireReadout.Alert(sentry, seen);
    }
    float nextArrivalCheck;
    // The gallery positions lie inside the two roofed passages. The other marks
    // are behind the eastern and western banks. Each candidate is verified against live sight and floors.
    static readonly Vector3[] EntryPockets = {
        new(6,0,-12), new(3,0,-12), new(8,0,-12), new(-6,0,-12), new(-3,0,-12),
        new(41,0,-12), new(43,0,8), new(26,0,-16), new(19,0,17), new(-30,0,-18)
    };
    sealed class Arrival
    {
        public Unit unit;
        public Vector3 duty;
        public bool holdPost;
        public Vector3[] path;
        public int corner;
        public float nextPlan;
    }
    readonly List<Arrival> arrivals = new();
    public static readonly Vector3 Hospital = new(-45.5f,0,-15.5f);

    private enum Phase { Dormant, Loading, Moving, Help, Clearance, Complete }
    private Phase phase;
    private int delivered;
    private readonly List<Vector3> road = new();
    private readonly List<Vector3> stops = new();
    private readonly List<float> distances = new();
    private readonly List<int> stopCorners = new();
    // The axle is the pivot. The shafts project 1.76 m; a handler occupies the next .65 m.
    // This swept disc also covers the stretcher and changing orientation through corners.
    private const float ReliefEnvelope = 2.60f;
    private int corner, firstStop;
    private bool waitingForRoad, dispatchRequested;
    private float nextRoadCheck;
    private bool marsh, assisted, gateCalled, crewNearby, stocked, received;
    private float accompanied;
    private float totalMetres, metres, nextScan;
    private Vector3 position;
    private Quaternion cartHeading = Quaternion.identity;
    private bool haveHeading;
    private Vector3 plannedAidPosition;
    private Transform cart;
    private OsowiecCartMotion motion;
    private AudioSource foley;
    private float nextCreak;
    private string advisory = "";
    private float advisoryUntil;
    enum Passage { Free, Mist, Pedestrian }
    Passage passage;
    // A brief wind shift is triggered once, on the first actual north-bank approach.
    bool mistIssued;
    float mistClock;
    const float MistWarning=3f, MistDuration=17f;
    public bool MistVisible => mistIssued && mistClock<MistWarning+MistDuration;
    public bool MistActive => MistVisible && mistClock>=MistWarning;
    Unit yielding;
    Vector3 yieldingTo;
    float nextCourtesy;
    bool checkPeople=true;

    public bool Started => phase != Phase.Dormant;
    internal bool UsesNorthRoad => marsh && (phase==Phase.Moving || phase==Phase.Help || phase==Phase.Clearance);
    public bool Complete => phase == Phase.Complete;
    public int Delivered => delivered;
    public bool NeedsHelp => phase == Phase.Help || phase == Phase.Clearance;
    Unit interceptor;
    float blockedUntil, nextIntercept;
    internal bool Intercepted => interceptor && interceptor.isActiveAndEnabled && interceptor.GetHealth()>0 &&
        Time.time<blockedUntil && Near(interceptor.transform.position,position,7f);
    internal bool HoldingCart(Unit guard) => guard==interceptor && Intercepted;
    internal int ReservesArrived => arrivedReserve;
    public bool TryGetEscortTarget(out Vector3 point)
    {
        point=position;
        return phase==Phase.Moving && cart && !waitingForRoad && passage==Passage.Free;
    }

    public bool TryDelayCart(Unit guard)
    {
        if(!guard || guard.owner!=Player.Player2 || guard.GetHealth()<=0 || Time.time<nextIntercept ||
           !Near(guard.transform.position,position,6f) || Mathf.Abs(guard.transform.position.y-position.y)>1.45f ||
           !SeesCart(guard,out _) || EscortNear(4.2f)) return false;
        interceptor=guard; blockedUntil=Time.time+12f; nextIntercept=Time.time+26f;
        Announce("Санитары остановились: караул перегородил дорогу. Подойдите к повозке.");
        return true;
    }

    internal bool SeesCart(Unit sentry, out Vector3 location)
    {
        if (!TryGetEscortTarget(out location) || !sentry || (location-sentry.transform.position).sqrMagnitude>18*18) return false;
        Vector3 eye=sentry.transform.position+Vector3.up*1.45f, top=location+Vector3.up*.95f;
        if (OsowiecFieldKit.Obscured(eye,top)) return false;
        Vector3 start=eye+(top-eye).normalized*.6f;
        return !Physics.Linecast(start,top,out var hit,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore) ||
            hit.collider.transform==cart || hit.collider.transform.IsChildOf(cart);
    }

    bool EscortNear(float distance)
    {
        var op=OsowiecOperation.Instance;
        if (!op) return false;
        foreach (var u in op.FieldRoster)
            if (u && u.isActiveAndEnabled && u.owner==Player.Player1 && u.GetHealth()>0 &&
                Near(u.transform.position,position,distance)) return true;
        return false;
    }

    public bool Stocked => stocked;
    public float EscortedMetres => accompanied;
    public string StageName => phase.ToString();
    public string RoadObstruction => lastBlocker;
    string lastBlocker;
    public string RouteName => marsh ? "Низина · северная насыпь" : "Каземат";
    public Vector3 CartPosition => position;
    public float DistanceRemaining => Mathf.Max(0,totalMetres-metres);
    public Vector3 NextAidPoint => phase==Phase.Help ? position : plannedAidPosition;
    public Vector3 CurrentObjective => phase switch
    {
        Phase.Loading => Pickup,
        Phase.Help => Prepared ? position : SupplyPost,
        Phase.Clearance => OsowiecOperation.Telephone,
        Phase.Moving => assisted ? Hospital : Prepared ? plannedAidPosition : SupplyPost,
        _ => OsowiecOperation.Battery
    };
    private Vector3 Pickup => delivered switch
    {
        0 => OsowiecOperation.SouthLine+new Vector3(-4.8f,0,1.2f),
        1 => OsowiecOperation.NorthLine+new Vector3(.4f,0,-2.3f),
        _ => OsowiecOperation.Battery+new Vector3(0,0,-.35f)
    };
    private string LoadNote => delivered switch
    {
        0 => "Примите первых раненых", 1 => "Погрузите перевязочный отряд", _ => "Примите последних раненых"
    };
    private string FieldJob => delivered switch
    {
        0 => "Закрепить колесо", 1 => "Сменить промокшие повязки", _ => "Переложить носилки"
    };
    private string RadioJob => delivered switch
    {
        0 => "Выслать запасную ось", 1 => "Передать сухие бинты", _ => "Вызвать двух санитаров"
    };
    public Vector3 SupplyPost => delivered==1 ? OsowiecOperation.NorthLine : OsowiecOperation.SouthLine;
    private string Supply => delivered switch {0=>"запасную ступицу",1=>"сухие бинты",_=>"сменные носилки"};
    private string SupplyName => delivered==1 ? "северный ввод" : "южный ввод";
    private bool Prepared => stocked || accompanied>=9f;

    public void Begin()
    {
        if (Started) return;
        if (cartPrefab == null)
        {
            Debug.LogError("The Osowiec relief column is missing its cart prefab.", this);
            return;
        }
        foley = OsowiecSoundBank.Source(transform,"Orderlies and wheels","World",1f,110);
        PrepareLoading();
    }

    private void PrepareLoading()
    {
        phase=Phase.Loading;
        marsh=delivered==1;
        assisted=gateCalled=crewNearby=stocked=received=false;
        accompanied=0f; interceptor=null; nextIntercept=0; blockedUntil=0;
        road.Clear();
        stops.Clear();
        distances.Clear();
        stopCorners.Clear();
        corner=firstStop=0;
        dispatchRequested=false;
        waitingForRoad=false;
        passage=Passage.Free;
        yielding=null;
        totalMetres=metres=0;
        haveHeading=false;
        position=Sample(Pickup,out var ground)?ground:Pickup;
        cart=Instantiate(cartPrefab,position,Quaternion.identity,transform).transform;
        cart.name=$"Санитарная повозка {delivered+1}";
        motion=cart.GetComponent<OsowiecCartMotion>();
        // Face the first common waypoint even while loading at the station.
        cartHeading=Quaternion.LookRotation(Vector3.ProjectOnPlane(OsowiecOperation.Junction-position,Vector3.up));
        if(motion)motion.Place(position,cartHeading,0,0);
        Announce(delivered==0?"Лазарет ждёт первый рейс.":"Предыдущая повозка дошла. Поступили новые раненые.");
    }

    public bool Interact(Unit actor)
    {
        var game=GameManager.Instance;
        if (!Started || Complete || actor==null || actor.owner!=Player.Player1 || actor.GetHealth()<=0 ||
            game==null || game.IsPaused() || game.IsGameOver() || game.IsBotTurn()) return false;
        Vector3 p=actor.transform.position;
        bool atNorth=Near(p,OsowiecOperation.NorthLine), atSouth=Near(p,OsowiecOperation.SouthLine);
        bool atBattery=Near(p,OsowiecOperation.Battery), atPhone=Near(p,OsowiecOperation.Telephone);
        if (phase==Phase.Loading && (atNorth||atSouth||atBattery||atPhone||Near(p,position)))
        {
            bool chooseMarsh=atNorth || (!atSouth && delivered==1);
            if (!chooseMarsh && OsowiecOperation.Instance && !OsowiecOperation.Instance.GateOpen)
            {
                chooseMarsh=true;
                Announce("Каземат заперт. Повозка назначена на северную насыпь.");
            }
            if (!Dispatch(chooseMarsh))
            {
                // Opening the northern boom updates its carved NavMesh on a later frame.
                // Keep the order and wait for an actual traversable route.
                dispatchRequested=true;
                nextRoadCheck=Time.time+.35f;
                Debug.LogWarning($"[Osowiec relief] Dispatch pending: {routeFailure}",this);
                Announce("Приказ принят. Санитары ждут свободного проезда.");
            }
            return true;
        }
        if(Intercepted && Near(p,position,4.2f))
        {
            interceptor=null;
            Announce("Сопровождение на месте. Санитары продолжают рейс.");
            return true;
        }
        if((waitingForRoad || passage!=Passage.Free) && (atSouth || atNorth || atPhone || atBattery || Near(p,position)))
        {
            bool chosen=atNorth;
            if(!atNorth && !atSouth && !atPhone)chosen=!marsh;
            if(chosen!=marsh)
            {
                int priorStop=firstStop;
                SetStops(chosen);
                if(!PlanLegs())
                {
                    SetStops(!chosen);
                    firstStop=priorStop;
                    waitingForRoad=!PlanLegs();
                    Announce("С этого места нет безопасного переезда. Сохраняем прежний маршрут и раненых.");
                    return true;
                }
            }
            waitingForRoad=!PlanLegs();
            passage=Passage.Free;
            Announce(waitingForRoad ? "Проезд закрыт. Санитары ждут безопасную дорогу." : "Санитары проверили дорогу и вышли в путь.");
            return true;
        }
        if((phase==Phase.Moving || phase==Phase.Help) && !assisted && !stocked && Near(p,SupplyPost))
        {
            stocked=true;
            Announce("Комплект у отряда. Передайте его у повозки или через другой пост связи.");
            OsowiecAtmosphere.PlayOptics(p);
            return true;
        }
        if(phase==Phase.Help && (Near(p,position,3.4f) || CanRelay(p)))
        {
            if(!Prepared)
            {
                Announce($"Нужен комплект. Возьмите {Supply}, точка на карте — {SupplyName}.");
                return true;
            }
            assisted=true;
            phase=Phase.Moving;
            Announce(Near(p,position,3.4f)?"Помощь оказана на месте. Повозка снова в пути.":"Приказ постом принят. Санитары возобновили движение.");
            OsowiecAtmosphere.PlayPneumatic(position);
            return true;
        }
        if(assisted && !received && (phase==Phase.Clearance || phase==Phase.Moving) &&
            (atPhone || Near(p,Hospital,4.2f) || (phase==Phase.Clearance && (atBattery || Near(p,position,3.4f)))))
        {
            received=true;
            phase=Phase.Moving;
            Announce("Лазарет подтвердил койки. Въезд в западную траншею разрешён.");
            OsowiecAtmosphere.PlayOptics(p);
            return true;
        }
        return false;
    }

    private bool CanRelay(Vector3 p)
    {
        // The stores and the transmitter are deliberately separate interactive locations.
        if(!stocked || Near(p,SupplyPost))return false;
        return Near(p,OsowiecOperation.Battery) || Near(p,OsowiecOperation.Telephone);
    }

    private Vector3 ReliefPost => OsowiecOperation.Battery;

    private static bool Near(Vector3 a,Vector3 b,float radius=3.65f) =>
        (new Vector2(a.x-b.x,a.z-b.z)).sqrMagnitude<=radius*radius && Mathf.Abs(a.y-b.y)<3f;

    private static bool Sample(Vector3 point,out Vector3 result)
    {
        bool ok=NavMesh.SamplePosition(point,out var contact,2.2f,NavMesh.AllAreas);
        result=ok?contact.position:point;
        return ok;
    }

    private bool Dispatch(bool throughMarsh)
    {
        if (!StartRoad(throughMarsh)) return false;
        dispatchRequested=false;
        requestedReserve=Mathf.Min(delivered+1,reserve.Length);
        Announce(marsh ? $"Рейс {delivered+1} идёт по северной насыпи в обход газа." : $"Рейс {delivered+1} идёт через каземат.");
        OsowiecAtmosphere.PlayOptics(position);
        return true;
    }

    private bool StartRoad(bool throughMarsh)
    {
        SetStops(throughMarsh);
        if(!PlanLegs())return false;
        phase=Phase.Moving;
        nextRoadCheck=Time.time+1f;
        Debug.Log($"[Osowiec relief] Рейс {delivered+1}: {RouteName}, {totalMetres:F1} м, {road.Count-1} участков.",this);
        return true;
    }

    private void SetStops(bool throughMarsh)
    {
        marsh=throughMarsh;
        firstStop=0;
        stops.Clear();
        stops.Add(OsowiecOperation.Junction);
        // Both itineraries enter the same western trench from the central junction.
        if(marsh)
        {
            var gas=OsowiecOperation.Gas;
            float east=gas.max.x+3.3f, west=gas.min.x-3.6f, north=gas.max.z+5f;
            stops.Add(new Vector3(gas.max.x+7,1,gas.min.z+1));
            stops.Add(new Vector3(east,1,gas.min.z+6.4f));
            stops.Add(new Vector3(east,1,gas.max.z));
            stops.Add(new Vector3(east,1,north-.4f));
            stops.Add(new Vector3(gas.center.x+1,1,north));
            stops.Add(new Vector3(west,1,north));
            stops.Add(new Vector3(west,1,gas.center.z+1));
            stops.Add(new Vector3(west,1,gas.min.z-2.9f));
            stops.Add(new Vector3(-15.45f,0,-1.8f));
            stops.Add(new Vector3(-18.5f,0,-2.5f));
        }
        else
        {
            stops.Add(new Vector3(20,0,-4));
            stops.Add(new Vector3(16,0,-12));
            stops.Add(new Vector3(14,0,-12));
            stops.Add(OsowiecOperation.Casemate);
            stops.Add(new Vector3(-13,0,-12));
            // Pass the eastern side of the telephone shed before turning west across its open front.
            stops.Add(new Vector3(-13.25f,0,-2.7f));
            stops.Add(OsowiecOperation.Telephone);
        }
        // Follow the centre of each dogleg, giving the stretcher and rear orderly room to turn.
        stops.Add(new Vector3(-25,0,-3));
        stops.Add(new Vector3(-25,0,-9));
        stops.Add(new Vector3(-34,0,-9));
        stops.Add(new Vector3(-34,0,-16));
        stops.Add(Hospital);
    }

    // Exact distance in plan from a whole segment to the active game's gas rectangle.
    // Projecting heights keeps the cart and handler clear even on uneven ground.
    private static float GasClearance(Vector3 a,Vector3 b)
        => ClearanceFrom(OsowiecOperation.Gas,a,b);

    private static float ClearanceFrom(Bounds gas,Vector3 a,Vector3 b)
    {
        a.y=b.y=gas.center.y;
        Vector3 d=b-a;
        float length=d.magnitude;
        if(gas.Contains(a) || gas.Contains(b) || (length>.0001f && gas.IntersectRay(new Ray(a,d/length),out var hit) && hit<=length)) return 0f;
        float distance=Mathf.Min(gas.SqrDistance(a),gas.SqrDistance(b));
        for(int i=0;i<4;i++)
        {
            var corner=new Vector3((i&1)==0?gas.min.x:gas.max.x,gas.center.y,(i&2)==0?gas.min.z:gas.max.z);
            var t=length>.0001f ? Mathf.Clamp01(Vector3.Dot(corner-a,d)/(length*length)) : 0f;
            distance=Mathf.Min(distance,(corner-(a+d*t)).sqrMagnitude);
        }
        return Mathf.Sqrt(distance);
    }

    private static bool CanTravel(Vector3 from,Vector3 to)
    {
        if(GasClearance(from,to)<=ReliefEnvelope) return false;
        int count=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(from,to)/.5f));
        for(int i=0;i<=count;i++)
        {
            Vector3 point=Vector3.Lerp(from,to,(float)i/count);
            if(!NavMesh.SamplePosition(point,out _,.35f,NavMesh.AllAreas) || OsowiecGroundEvents.InCrater(point)) return false;
        }
        return true;
    }

    private string routeFailure;
    private bool RoadRejected(string cause, Vector3 location)
    {
        routeFailure = $"{cause} at {location:F2}";
        return false;
    }

    private bool PlanLegs()
    {
        List<Vector3> result=new();
        if(!Sample(position,out Vector3 previous)) return RoadRejected("pickup off navigation",position);
        // A rebake cannot teleport an occupied cart to a different bank.
        if(Vector3.Distance(position,previous)>.35f || GasClearance(position,previous)<=ReliefEnvelope) return RoadRejected("unsafe pickup",previous);
        result.Add(previous);
        var path=new NavMeshPath();
        List<int> ends=new();
        for(int index=firstStop;index<stops.Count;index++)
        {
            if(!Sample(stops[index],out var dest) ||
               !NavMesh.CalculatePath(previous,dest,NavMesh.AllAreas,path) ||
               path.status!=NavMeshPathStatus.PathComplete)return RoadRejected($"incomplete leg {index}",stops[index]);
            foreach(var point in path.corners)
                if((point-result[^1]).sqrMagnitude>.0025f)
                {
                    // The navigation funnel supplies turns in plan. On the ridge, interpolating
                    // their heights directly can put the axle over a metre below the roadway.
                    var start=result[^1];
                    if(GasClearance(start,point)<=ReliefEnvelope)return RoadRejected($"leg {index} enters gas margin",point);
                    int steps=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(start,point)/.24f));
                    for(int j=1;j<=steps;j++)
                    {
                        Vector3 along=Vector3.Lerp(start,point,(float)j/steps);
                        if(!NavMesh.SamplePosition(along,out var ground,2f,NavMesh.AllAreas))return RoadRejected("missing roadway",along);
                        Vector3 delta=ground.position-along;
                        if(delta.x*delta.x+delta.z*delta.z>.027f || !CanTravel(result[^1],ground.position))return RoadRejected($"unsafe road on leg {index}",ground.position);
                        if((ground.position-result[^1]).sqrMagnitude>.00001f)result.Add(ground.position);
                    }
                }
            previous=dest;
            ends.Add(result.Count-1);
        }
        if(result.Count<2)return RoadRejected("empty itinerary",position);
        road.Clear();
        road.AddRange(result);
        stopCorners.Clear(); stopCorners.AddRange(ends);
        corner=1;
        position=road[0];
        if(!haveHeading)
        {
            var direction=Vector3.ProjectOnPlane(road[1]-position,Vector3.up);
            if(direction.sqrMagnitude>.0001f)cartHeading=Quaternion.LookRotation(direction);
            haveHeading=true;
        }
        distances.Clear();
        distances.Add(metres);
        for(int i=1;i<road.Count;i++)
            distances.Add(distances[i-1]+Vector3.Distance(road[i-1],road[i]));
        totalMetres=distances[^1];
        float toAid=Mathf.Max(0,totalMetres*AidFraction-metres);
        plannedAidPosition=road[^1];
        for(int i=1;i<road.Count;i++)
        {
            float leg=Vector3.Distance(road[i-1],road[i]);
            if(toAid<=leg){plannedAidPosition=Vector3.MoveTowards(road[i-1],road[i],toAid);break;}
            toAid-=leg;
        }
        Vector3 trial=Vector3.MoveTowards(position,road[corner],.14f);
        Quaternion heading=Steer(road[corner]-position,Vector3.Distance(position,trial));
        if(!OsowiecCartMotion.Sweep(position,cartHeading,trial,heading,out var blocked))
            return RoadRejected($"pickup blocked by {blocked?.name}",position);
        routeFailure = null;
        return true;
    }

    private void Update()
    {
        var game=GameManager.Instance;
        if(game==null || game.IsPaused() || game.IsGameOver() || Time.timeScale<=0)
        {
            if(motion)motion.Suspend();
            return;
        }
        if(phase==Phase.Loading && dispatchRequested && Time.time>=nextRoadCheck)
        {
            nextRoadCheck=Time.time+.65f;
            Dispatch(marsh);
        }
        if(Time.time >= nextArrivalCheck)
        {
            nextArrivalCheck = Time.time + .65f;
            bool admitted=false;
            if(alarm && alarmArrivals < alarmGuard.Length && Enter(alarmGuard[alarmArrivals],false))
            {
                alarmArrivals++;
                admitted=true;
            }
            if(!admitted && arrivedReserve<requestedReserve && Enter(reserve[arrivedReserve],true))
            {
                arrivedReserve++;
                admitted=true;
            }
            if(admitted) nextArrivalCheck=Time.time+1.6f;
        }
        if(!game.IsBotTurn()) MarchReserves(Time.deltaTime);
        TickMist(Time.deltaTime);
        if(yielding)YieldStep(Time.deltaTime,game.IsBotTurn());
        if(phase!=Phase.Moving)
        {
            if(motion)motion.Place(position,cartHeading,0,Time.deltaTime);
            return;
        }
        if(!marsh && !gateCalled && Near(position,OsowiecOperation.Casemate,4.0f))
        {
            gateCalled=true;
            if(OsowiecOperation.Instance && !OsowiecOperation.Instance.GateOpen)
                Announce("Каземат заперт. Установите заряд у ворот или назначьте северный объезд.");
        }
        if(Time.time>=nextRoadCheck)
        {
            nextRoadCheck=Time.time+1f;
            bool clear=!waitingForRoad && CanTravel(position,road[corner]);
            if(clear)
                for(int i=corner+1;i<road.Count && clear;i++)clear=CanTravel(road[i-1],road[i]);
            if(!clear)
            {
                bool wasWaiting=waitingForRoad;
                waitingForRoad=!PlanLegs();
                if(waitingForRoad && !wasWaiting)Announce("Повозка остановилась. Северный пост — объезд, южный пост или телефон — каземат.");
                if(!waitingForRoad && wasWaiting)Announce("Безопасный проезд найден. Санитары продолжают путь.");
            }
        }
        if(waitingForRoad)
        {
            if(motion)motion.Place(position,cartHeading,0,Time.deltaTime);
            return;
        }
        if(Time.time>=nextScan)
        {
            crewNearby=EscortNear(5);
            nextScan=Time.time+1f;
        }
        if(Intercepted && EscortNear(4.2f))
        {
            interceptor=null;
            Announce("Сопровождение подошло. Санитары продолжают рейс.");
        }
        else if(interceptor && !Intercepted)
        {
            interceptor=null;
            Announce("Караул оставил проезд. Санитары продолжают рейс.");
        }
        if(Intercepted) {
            if(motion)motion.Place(position,cartHeading,0,Time.deltaTime);
            return;
        }
        float travel=AdvanceRoad((crewNearby ? 1.53f : 1.18f)*Time.deltaTime);
        if(motion)motion.Place(position,cartHeading,travel,Time.deltaTime);
        if(waitingForRoad)
        {
            Announce("Проезд закрыт. Освободите дорогу или смените маршрут у поста связи.");
            return;
        }
        if(passage!=Passage.Free)return;
        if(crewNearby)accompanied+=travel;
        if(Time.time>=nextCreak && cart!=null)
        {
            nextCreak=Time.time+Random.Range(3.2f,5.4f);
            foley.transform.position=position+Vector3.up*.48f;
            foley.pitch=Random.Range(.73f,.89f);
            foley.PlayOneShot(OsowiecSoundBank.Variant("CartAxle",3),OsowiecEnvironmentAudio.EffectsVolume*.32f);
        }
        if(!assisted && totalMetres>1 && metres>=totalMetres*AidFraction)
        {
            phase=Phase.Help;
            OsowiecAtmosphere.PlayOptics(position);
            Announce(delivered==0?"Сломалось колесо. Повозка остановилась. Нужна помощь у оси.":delivered==1?"Повозка остановилась: перевязочный комплект промок.":"Повозка остановилась: носилки сместились.");
            return;
        }
        if(!received && assisted && metres>=totalMetres*.81f)
        {
            phase=Phase.Clearance;
            Announce("Перед западной траншеей. Свяжитесь с лазаретом и подтвердите приём.");
            OsowiecAtmosphere.PlayOptics(position);
            return;
        }
        if(metres<totalMetres)return;
        delivered++;
        Destroy(cart.gameObject); cart=null;
        OsowiecAtmosphere.Pulse(Hospital+Vector3.up*.7f);
        if(delivered==3)
        {
            phase=Phase.Complete;
            Announce("Все три повозки прибыли в западный лазарет.");
        }
        else PrepareLoading();
    }

    private float AdvanceRoad(float amount)
    {
        // A complete path can touch a NavMesh polygon boundary. Raycasting each millimetre
        // again at that edge used to stall the cart in the western trench.
        float previousDistance=metres;
        float next=Mathf.Min(totalMetres,metres+amount);
        int at=corner;
        while(at<road.Count-1 && next>=distances[at])at++;
        float legLength=distances[at]-distances[at-1];
        var candidate=Vector3.Lerp(road[at-1],road[at],(next-distances[at-1])/legLength);
        if(!CanTravel(position,candidate)){
            lastBlocker="Проходимость грунта";
#if UNITY_EDITOR
            if(!Application.isPlaying){
                NavMesh.SamplePosition(candidate,out var nearest,3,NavMesh.AllAreas);
                lastBlocker+=$" {position:F3} -> {candidate:F3}, nearest={nearest.position:F3} clearance={GasClearance(position,candidate):F3}, delta={nearest.distance:F3}";
            }
#endif
            waitingForRoad=true;return 0;
        }
        var nextHeading=Steer(road[at]-road[at-1],amount);
        if(MistActive && ClearanceFrom(OsowiecOperation.NorthBankMist,position,candidate)<=ReliefEnvelope)
        {
            Hold(Passage.Mist);
            return 0;
        }
        if(!OsowiecCartMotion.Sweep(position,cartHeading,candidate,nextHeading,out var hit,checkPeople))
        {
            lastBlocker=hit?hit.name:"world query full";
            Unit person=hit?hit.GetComponentInParent<Unit>():null;
            if(person)
            {
                Hold(Passage.Pedestrian);
                if(!yielding && !person.IsControlled() && Time.time>=nextCourtesy)
                {
                    nextCourtesy=Time.time+.4f;
                    FindPassingPlace(person);
                }
                return 0;
            }
            waitingForRoad=true;
            return 0;
        }
        if(passage!=Passage.Free)
        {
            passage=Passage.Free;
            Announce("Проход свободен. Продолжаем тот же рейс.");
        }
        lastBlocker=null;
        cartHeading=nextHeading;
        metres=next;corner=at;position=candidate;
        while(stopCorners.Count>0 && next>=distances[stopCorners[0]]){firstStop++;stopCorners.RemoveAt(0);}
        return metres-previousDistance;
    }

    void TickMist(float elapsed)
    {
        if(mistIssued){mistClock+=elapsed;return;}
        if(phase!=Phase.Moving || !marsh || ClearanceFrom(OsowiecOperation.NorthBankMist,position,position)>6.5f)return;
        mistIssued=true;
        Announce("Северный пост: ветер несёт газ поперёк насыпи. Подождите или выберите каземат.");
        OsowiecHudPaint.Caption("[Северный пост предупреждает о газе на насыпи]");
    }

    void Hold(Passage reason)
    {
        if(passage==reason)return;
        passage=reason;
        Announce(reason==Passage.Mist ? "Газ на северной дороге. Раненые в безопасности, ждём, пока облако пройдёт."
            : "На пути человек. Пропустите санитарную повозку.");
    }

    void FindPassingPlace(Unit person)
    {
        var origin=person.transform.position;
        var side=cartHeading*Vector3.right;
        if(Vector3.Dot(origin-position,side)<0)side=-side;
        var along=cartHeading*Vector3.forward;
        float lateral=Vector3.Dot(origin-position,side);
        for(int which=0;which<2;which++)
        for(int diagonal=0;diagonal<3;diagonal++)
        {
            float targetOffset=which==0?1.14f:-1.14f;
            Vector3 desired=origin+side*(targetOffset-lateral)+along*(diagonal==1?.65f:diagonal==2?-.65f:0);
            if(!NavMesh.SamplePosition(desired,out var spot,.48f,NavMesh.AllAreas) ||
               Mathf.Abs(Vector3.Dot(spot.position-position,side))<.91f ||
               !NavMesh.SamplePosition(origin,out var start,.65f,NavMesh.AllAreas) ||
               NavMesh.Raycast(start.position,spot.position,out _,NavMesh.AllAreas) ||
               OsowiecOperation.GasOnSegment(start.position,spot.position) ||
               Physics.CheckCapsule(spot.position+Vector3.up*.38f,spot.position+Vector3.up*1.42f,.24f,
                   Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))continue;
            yielding=person;
            yieldingTo=spot.position;
            return;
        }
    }

    void YieldStep(float dt,bool botTurn)
    {
        if(yielding.IsControlled() || yielding.GetHealth()<=0){yielding=null;return;}
        // Leave the bot turn's own controller free to move and animate the same actor.
        if(botTurn && yielding.owner==Player.Player2)return;
        var cc=yielding.GetComponent<CharacterController>();
        if(!cc || !cc.enabled){yielding=null;return;}
        Vector3 delta=Vector3.ProjectOnPlane(yieldingTo-yielding.transform.position,Vector3.up);
        if(delta.sqrMagnitude<.0064f){yielding=null;return;}
        Vector3 before=yielding.transform.position;
        cc.Move(Vector3.ClampMagnitude(delta,dt*1.2f));
        yielding.RefreshGridPositionFromWorld();
        if((yielding.transform.position-before).sqrMagnitude<.00000001f)yielding=null;
    }

    Quaternion Steer(Vector3 tangent,float metresForward)
    {
        var direction=Vector3.ProjectOnPlane(tangent,Vector3.up);
        return direction.sqrMagnitude>.000001f
            ?Quaternion.RotateTowards(cartHeading,Quaternion.LookRotation(direction),metresForward*170f)
            :cartHeading;
    }

#if UNITY_EDITOR
    public static bool VerifyEditorRoute(int trip,bool throughMarsh,out string report)
    {
        if(trip<1 || trip>3) { report="Trip must be 1, 2 or 3."; return false; }
        var host=new GameObject("Relief navigation probe"){hideFlags=HideFlags.HideAndDontSave};
        Transform testGate=GameObject.Find("Osowiec Door Hinge")?.transform;
        var savedGate=testGate?testGate.localRotation:Quaternion.identity;
        var northGate=FindAnyObjectByType<OsowiecBankPassage>();
        try
        {
            var probe=host.AddComponent<OsowiecReliefColumn>();
            probe.checkPeople=false; // Baseline scene actors do not take live turns in this editor matrix.
            probe.delivered=trip-1;
            probe.position=probe.Pickup;
            if(!probe.StartRoad(throughMarsh))
            {report=$"Trip {trip}, marsh={throughMarsh}: {probe.routeFailure}.";return false;}
            float minimum=float.MaxValue;
            for(int i=1;i<probe.road.Count;i++)minimum=Mathf.Min(minimum,GasClearance(probe.road[i-1],probe.road[i]));
            if(minimum<=ReliefEnvelope || GasClearance(OsowiecOperation.Lowland,OsowiecOperation.Lowland)>0)
            {report="Gas test failed for the original pedestrian crossing or the cart envelope.";return false;}
            // A newly unsafe leg must stop an occupied cart; replanning starts at the same axle.
            var savedPosition=probe.position;
            probe.road.Clear();probe.road.Add(savedPosition);probe.road.Add(OsowiecOperation.Lowland);
            probe.distances.Clear();probe.distances.Add(0);probe.totalMetres=Vector3.Distance(savedPosition,OsowiecOperation.Lowland);
            probe.distances.Add(probe.totalMetres);probe.corner=1;
            if(probe.AdvanceRoad(probe.totalMetres)!=0 || !probe.waitingForRoad || probe.position!=savedPosition || !probe.PlanLegs())
            {report="Loaded cart entered a closed leg, teleported, or failed to replan.";return false;}
            probe.waitingForRoad=false;
            var obstruction=new GameObject("Relief test obstructed shaft"){hideFlags=HideFlags.HideAndDontSave};
            try
            {
                obstruction.transform.position=probe.position+probe.cartHeading*new Vector3(.27f,.71f,1.43f);
                obstruction.AddComponent<BoxCollider>().size=new Vector3(.12f,.23f,.11f);
                Physics.SyncTransforms();
                if(probe.AdvanceRoad(.02f)!=0 || !probe.waitingForRoad || probe.position!=savedPosition)
                {report="Cart passed a solid obstruction at its shaft.";return false;}
            }
            finally { DestroyImmediate(obstruction);Physics.SyncTransforms(); }
            probe.waitingForRoad=false;
            Vector2 oldEdge=new(-22.07934f,-5.22646f);
            float nearest=float.MaxValue;
            int steps=0;
            bool testedCrossing=false,sawMist=false,sawResume=false;
            float mistHold=0;
            while(probe.DistanceRemaining>0 && steps++<100000)
            {
                Vector3 before=probe.position;
                if(northGate)northGate.SetInspectionPose(throughMarsh && Mathf.Abs(before.x-northGate.transform.position.x)<6 &&
                    Mathf.Abs(before.z-northGate.transform.position.z)<3.25f);
                if(!throughMarsh && testGate!=null && !probe.gateCalled && Near(before,OsowiecOperation.Casemate,4.0f))
                {
                    testGate.localRotation=savedGate*Quaternion.Euler(0,92,0);
                    Physics.SyncTransforms();
                    probe.gateCalled=true;
                }
                float edgeDistance=Vector2.Distance(oldEdge,new Vector2(before.x,before.z));
                nearest=Mathf.Min(nearest,edgeDistance);
                float step=edgeDistance<1.5f ? .001f : .09f;
                probe.TickMist(step/.46f);
                if(!testedCrossing && throughMarsh && trip==1 && probe.metres>probe.totalMetres*.85f)
                {
                    if(!probe.VerifyCrossing(out report))return false;
                    testedCrossing=true;
                    before=probe.position;
                }
                float advanced=probe.AdvanceRoad(step);
                if(probe.passage==Passage.Mist)
                {
                    if(advanced!=0 || before!=probe.position || !probe.MistActive ||
                       ClearanceFrom(OsowiecOperation.NorthBankMist,before,before)<ReliefEnvelope-.001f)
                    {report="Northern squall failed to stop outside the occupied envelope.";return false;}
                    mistHold+=step/.46f;
                    sawMist=true;
                    continue;
                }
                if(sawMist && advanced>0)sawResume=true;
                if(advanced<=0 || Vector3.Distance(before,probe.position)>step+.003f ||
                   !NavMesh.SamplePosition(probe.position,out _,.65f,NavMesh.AllAreas))
                {
                    report=$"Trip {trip}, {probe.RouteName}: invalid advance at {probe.position}, {probe.DistanceRemaining:F3} m left; collider {probe.lastBlocker}.";
                    return false;
                }
            }
            bool arrived=probe.DistanceRemaining==0 && Vector3.Distance(probe.position,Hospital)<2.25f &&
                (!throughMarsh || (sawResume && mistHold>1f)) && (!throughMarsh || trip!=1 || testedCrossing);
            report=$"Trip {trip}, {probe.RouteName}: {probe.totalMetres:F2} m; {steps} verified advances; closest to old stall {nearest:F3} m; gas centerline clearance {minimum:F2} m, loaded cart and handler margin {minimum-ReliefEnvelope:F2} m; gust stop {mistHold:F2}s; crossing checked {testedCrossing}; endpoint {probe.position}; arrived={arrived}.";
            return arrived;
        }
        finally
        {
            if(testGate!=null){testGate.localRotation=savedGate;Physics.SyncTransforms();}
            if(northGate)northGate.SetInspectionPose(false);
            DestroyImmediate(host);
        }
    }

    bool VerifyCrossing(out string report)
    {
        var person=new GameObject("Crossing orderly's path"){hideFlags=HideFlags.HideAndDontSave};
        var start=position;
        try
        {
            var location=position+cartHeading*Vector3.forward*1.68f;
            if(Sample(location,out var ground))location=ground;
            person.transform.position=location;
            var cc=person.AddComponent<CharacterController>();cc.center=Vector3.up*.90f;cc.height=1.78f;cc.radius=.29f;
            var u=person.AddComponent<Unit>();u.owner=Player.Player2;
            Physics.SyncTransforms();
            checkPeople=true;
            nextCourtesy=-1;
            if(AdvanceRoad(.04f)!=0 || position!=start || passage!=Passage.Pedestrian)
            {report="Occupied shafts crossed the live combatant.";return false;}
            if(yielding!=u)
            {report=$"Blocked pedestrian has no passing place at {location}.";return false;}
            for(int i=0;i<160 && passage==Passage.Pedestrian;i++)
            {
                if(yielding)YieldStep(.025f,false);
                Physics.SyncTransforms();
                nextCourtesy=-1;
                AdvanceRoad(.012f);
            }
            if((person.transform.position-location).sqrMagnitude<.20f*.20f || passage!=Passage.Free)
            {report=$"Blocked cart or pedestrian failed to resume at {location}; person now at {person.transform.position}, physics {cc.bounds}, axle {position}, yaw {cartHeading.eulerAngles.y}, yielding {yieldingTo}; {passage}, {lastBlocker}, worldHold={waitingForRoad}.";return false;}
            report="The convoy stopped for a pedestrian and resumed from the same axle.";return true;
        }
        finally{checkPeople=false;yielding=null;DestroyImmediate(person);Physics.SyncTransforms();}
    }
#endif

    private float AidFraction => delivered switch {0=>.34f,1=>.53f,_=>.69f};

    private void OnGUI()
    {
        if(!Started || Complete)return;
        var cameraManager=CameraManager.Instance;
        var game=GameManager.Instance;
        if(game==null || cameraManager==null || game.IsPaused() || game.IsGameOver())return;
        bool fps=cameraManager.IsActionMode();
        var cam=fps?cameraManager.GetActionCamera():cameraManager.GetTacticalCamera();
        if(cam==null)return;
        Unit unit=fps?cameraManager.GetCurrentControlledUnit():null;
        bool local=unit!=null && unit.owner==Player.Player1 && unit.GetHealth()>0;
        Vector3 actor=local?unit.transform.position:new Vector3(1000,1000,1000);
        string action,where;
        if(phase==Phase.Loading)
        {
            bool node=Near(actor,OsowiecOperation.NorthLine) || Near(actor,OsowiecOperation.SouthLine) ||
                Near(actor,OsowiecOperation.Battery) || Near(actor,OsowiecOperation.Telephone);
            action=node ? "[E] Принять носилки и отправить" : LoadNote;
            where="Северный пост — насыпь в обход газа. Южный — через каземат.";
        }
        else if(Intercepted)
        {
            action="Караул остановил повозку";
            where=Near(actor,position,4.2f)?"[E] Поддержать санитаров":"Вернитесь к отметке санитарного обоза.";
        }
        else if(passage!=Passage.Free)
        {
            action=passage==Passage.Mist ? "Газ перекрыл северную насыпь" : "Пропустите санитаров";
            where=passage==Passage.Mist ? "Колонна продолжит путь после порыва ветра. У южного поста можно выбрать каземат."
                : "Оставьте проезд по центру. Бойцы вне управления сами отойдут в сторону.";
        }
        else if(waitingForRoad)
        {
            action="Санитары ждут безопасную дорогу";
            where="[E] у южного поста или телефона — каземат. У северного поста — объезд по насыпи.";
        }
        else if(phase==Phase.Clearance)
        {
            bool node=Near(actor,position,3.4f)||Near(actor,OsowiecOperation.Battery)||Near(actor,OsowiecOperation.Telephone)||Near(actor,Hospital,4.2f);
            action=node?"[E] Подтвердить приём в лазарете":"Лазарет запрашивает приём";
            where="Подойдите к повозке, западному телефону или передатчику батареи.";
        }
        else if(phase==Phase.Help)
        {
            if(!Prepared)
            {
                action=Near(actor,SupplyPost)?"[E] Взять "+Supply:delivered==0?"Сломано колесо":FieldJob;
                where="Получите "+Supply+". Место выдачи — "+SupplyName+".";
            }
            else
            {
                action=Near(actor,position,3.4f)?"[E] "+FieldJob:CanRelay(actor)?"[E] "+RadioJob:FieldJob;
                where=stocked?"Помогите лично либо передайте комплект с батареи или западного телефона.":
                    "Вы шли рядом с санитарами. Подойдите к повозке, чтобы помочь.";
            }
        }
        else if(!assisted)
        {
            action=!stocked && Near(actor,SupplyPost)?"[E] Взять "+Supply:
                stocked?"Комплект у отряда":$"Соберите {Supply}";
            where=stocked?"Встречайте санитаров у отметки привала или дежурьте у передатчика.":
                "Склад — "+SupplyName+". Можно сопровождать повозку хотя бы 9 м.";
        }
        else
        {
            action=!received && (Near(actor,OsowiecOperation.Telephone)||Near(actor,Hospital))?"[E] Предупредить лазарет":
                received?"Лазарет готов принять раненых":"Связаться с лазаретом";
            where=received?"Колонна идёт к западным носильным путям.":
                "Передайте заявку с западного телефона заранее или дождитесь входной отметки.";
        }
        const int layer=-5;
        int oldDepth=GUI.depth;
        GUI.depth=layer;
        float s=OsowiecHudPaint.Scale;
        var box=new Rect(Screen.width-388*s, Screen.height*(fps?.245f:.21f),366*s,116*s);
        OsowiecHudPaint.Panel(box);
        float x=box.x+12*s;
        OsowiecHudPaint.Text(new Rect(x,box.y+5*s,344*s,19*s),
            $"САНИТАРНЫЙ ОБОЗ    {delivered+1}/3"+(phase==Phase.Loading?"":$"    ·    {RouteName}"),15,
            new Color(.82f,.69f,.45f),true);
        OsowiecHudPaint.Text(new Rect(x,box.y+25*s,344*s,24*s),action,19,new Color(.96f,.91f,.77f),true);
        string small=Time.time<advisoryUntil && phase!=Phase.Help && phase!=Phase.Clearance ? advisory:where;
        var style=new GUIStyle(GUI.skin.label){fontSize=Mathf.RoundToInt(15*s),wordWrap=true};
        style.normal.textColor=new Color(.85f,.84f,.74f);
        GUI.Label(new Rect(x,box.y+50*s,344*s,42*s),small,style);
        string progress=phase==Phase.Loading?"Выберите место погрузки":
            $"До лазарета  {DistanceRemaining:F0} м"+(crewNearby?"   ·   Рядом боец":"");
        OsowiecHudPaint.Text(new Rect(x,box.y+91*s,344*s,18*s),progress,14,new Color(.77f,.82f,.70f));
        OsowiecHudPaint.Meter(new Rect(x,box.y+110*s,342*s,3*s),totalMetres>.01f?metres/totalMetres:0,
            new Color(.65f,.67f,.44f));
        if(fps && (NeedsHelp || Intercepted) && local)
        {
            Vector3 screen=cam.WorldToScreenPoint(position+Vector3.up*1.2f);
            Vector2 direction=new(screen.x-Screen.width*.5f,Screen.height*.5f-screen.y);
            if(screen.z<0)direction=-direction;
            float edge=Mathf.Max(1,Mathf.Max(Mathf.Abs(direction.x)/(Screen.width*.41f),Mathf.Abs(direction.y)/(Screen.height*.33f)));
            Vector2 pixel=new Vector2(Screen.width*.5f,Screen.height*.5f)+direction/edge;
            OsowiecHudPaint.Text(new Rect(pixel.x-88*s,pixel.y-11*s,176*s,25*s),
                $"!  Обоз · {Vector3.Distance(position,actor):F0} м",17,new Color(1f,.79f,.36f),true);
        }
        if(!fps && !(OsowiecPlanner.Instance && OsowiecPlanner.Instance.SurveyOpen) &&
           phase!=Phase.Loading && !waitingForRoad && Event.current.type==EventType.Repaint)
            DrawRoad(cam);
        GUI.depth=oldDepth;
    }

    internal void AddMapMarkers(OsowiecMapMarkers pins)
    {
        if(!Started || Complete) return;
        if(MistVisible) pins.Add(OsowiecMapSymbol.Gas,OsowiecOperation.NorthBankMist.center,
            MistActive?"Газ на насыпи":"Порыв ветра");
        pins.Add(NeedsHelp||Intercepted?OsowiecMapSymbol.Aid:OsowiecMapSymbol.Wagon,position, NeedsHelp ? FieldJob : Intercepted ? "Караул у повозки" : $"Санитарная повозка · рейс {delivered+1}",advisory);
        if(phase==Phase.Loading)
        {
            pins.Add(OsowiecMapSymbol.JunctionNorth,OsowiecOperation.NorthLine,"Северный пост","Обход низины по насыпи.");
            pins.Add(OsowiecMapSymbol.JunctionSouth,OsowiecOperation.SouthLine,"Южный пост","Путь через каземат.");
        }
        else if(!assisted)
        {
            pins.Add(Prepared?OsowiecMapSymbol.Aid:OsowiecMapSymbol.Supplies,Prepared?plannedAidPosition:SupplyPost,
                Prepared?"Привал":"Склад",Prepared?"Здесь повозке понадобится помощь.":Supply);
            if(phase==Phase.Help && stocked) pins.Add(OsowiecMapSymbol.Phone,ReliefPost,"Пост связи");
        }
        else if(received) pins.Add(OsowiecMapSymbol.Aid,Hospital,"Лазарет","Здесь ждут повозку.");
    }

    private void DrawRoad(Camera camera)
    {
        var previousColor=GUI.color; var previousMatrix=GUI.matrix;
        GUI.color=new Color(.72f,.80f,.59f,.85f);
        var a=camera.WorldToScreenPoint(position+Vector3.up*.4f);
        for(int i=corner;i<road.Count;i++)
        {
            var b=camera.WorldToScreenPoint(road[i]+Vector3.up*.4f);
            if(a.z>0 && b.z>0)
            {
                Vector2 p=new(a.x,Screen.height-a.y),delta=new(b.x-a.x,a.y-b.y);
                GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg,p);
                GUI.DrawTexture(new Rect(p.x,p.y,delta.magnitude,Mathf.Max(1,1.5f*OsowiecHudPaint.Scale)),Texture2D.whiteTexture);
                GUI.matrix=previousMatrix;
            }
            a=b;
        }
        GUI.color=previousColor;
    }

    bool Enter(GameObject actor, bool convoy)
    {
        if (!actor) return true;
        var unit=actor.GetComponent<Unit>();
        if (!unit) return false;
        bool fixedPost=convoy && reserve.Length>2 && actor==reserve[2];
        Vector3 duty=convoy && !fixedPost ? OsowiecOperation.Junction + Vector3.forward*3.6f : actor.transform.position;
        for(int i=fixedPost?-1:0;i<EntryPockets.Length;i++)
        {
            Vector3 entrance=i<0 ? duty : EntryPockets[i];
            if (!NavMesh.SamplePosition(entrance,out var nav,2.8f,NavMesh.AllAreas) ||
                !OsowiecHitBody.TryFloor(nav.position,out var floor) || !CanEnterUnseen(floor)) continue;
            var path=new NavMeshPath();
            if (!NavMesh.CalculatePath(nav.position,duty,NavMesh.AllAreas,path) || path.status!=NavMeshPathStatus.PathComplete) continue;
            if (Physics.CheckCapsule(floor+Vector3.up*.42f,floor+Vector3.up*1.45f,.28f,
                    Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)) continue;
            actor.transform.position=floor+Vector3.up*.035f;
            actor.SetActive(true);
            unit.RefreshGridPositionFromWorld();
            arrivals.Add(new Arrival {unit=unit,duty=duty,holdPost=fixedPost});
            if(BotController.Instance) BotController.Instance.GetComponent<OsowiecBotBrain>()?.AssignPost(unit,duty);
            Announce(fixedPost ? "Бастион выставляет наблюдателя на галерею." :
                convoy ? "Из укрытия выходит резерв. Его приказ — задержать обоз." : "Караульная смена выходит из укрытия к батарее.");
            return true;
        }
        return false;
    }

    void MarchReserves(float dt)
    {
        foreach(var order in arrivals)
        {
            var unit=order.unit;
            if(!unit || !unit.isActiveAndEnabled || unit.GetHealth()<=0 || unit==interceptor && Intercepted ||
                OsowiecOperation.InGas(unit) || OsowiecOperation.Instance.Aiming(unit)) continue;
            Vector3 cartAt=default;
            bool sees=!order.holdPost && SeesCart(unit,out cartAt);
            Vector3 here=unit.transform.position;
            Vector3 goal=sees ? cartAt : order.duty;
            if(sees && Near(here,cartAt,5.7f))
            {
                if(TryDelayCart(unit))continue;
                Vector3 side=cartHeading*Vector3.right;
                if(Vector3.Dot(here-cartAt,side)<0) side=-side;
                goal=cartAt+side*5.8f;
            }
            if(Near(here,goal,1.2f) || !unit.TryGetComponent<CharacterController>(out var motor) || !motor.enabled) continue;
            if(Time.time>=order.nextPlan)
            {
                order.nextPlan=Time.time+1.25f;
                var path=new NavMeshPath();
                order.path=NavMesh.SamplePosition(goal,out var to,2f,NavMesh.AllAreas) &&
                    NavMesh.CalculatePath(here,to.position,NavMesh.AllAreas,path) && path.status==NavMeshPathStatus.PathComplete ?
                    path.corners : null;
                order.corner=1;
            }
            if(order.path==null)continue;
            while(order.corner<order.path.Length)
            {
                Vector3 delta=Vector3.ProjectOnPlane(order.path[order.corner]-here,Vector3.up);
                if(delta.sqrMagnitude<.04f){order.corner++;continue;}
                unit.transform.rotation=Quaternion.RotateTowards(unit.transform.rotation,Quaternion.LookRotation(delta),dt*220);
                motor.Move(Vector3.ClampMagnitude(delta,dt*1.95f)+Vector3.down*(dt*.35f));
                unit.RefreshGridPositionFromWorld();
                break;
            }
        }
    }

    static bool CanEnterUnseen(Vector3 point)
    {
        foreach(var ally in OsowiecOperation.Instance.FieldRoster)
        {
            if(!ally || !ally.isActiveAndEnabled || ally.owner!=Player.Player1 || ally.GetHealth()<=0) continue;
            Vector3 delta=point-ally.transform.position;
            if(delta.sqrMagnitude<36) return false;
            Vector3 eye=ally.transform.position+Vector3.up*1.4f+delta.normalized*.6f;
            Vector3 line=point+Vector3.up*1.2f-eye;
            bool covered=false;
            foreach(var hit in Physics.RaycastAll(eye,line.normalized,line.magnitude,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
                if(!hit.collider.GetComponentInParent<Unit>() && !hit.collider.GetComponentInParent<OsowiecCartMotion>())
                {covered=true;break;}
            if(!covered)
                return false;
        }
        return true;
    }

    private void Announce(string value)
    {
        OsowiecHudPaint.Caption(value);
        advisory=value;
        advisoryUntil=Time.time+5f;
        Debug.Log($"[Osowiec relief] {StageName}, рейс {Mathf.Min(delivered+1,3)}. {value}",this);
    }
}
