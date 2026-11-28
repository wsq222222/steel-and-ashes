using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public sealed class OsowiecOperation : MonoBehaviour
{
    public Transform gate;
    public OsowiecReliefColumn relief;
    public TMPro.TMP_Text resultNote;
    public TMPro.TMP_Text resultRoster;
    public static readonly Bounds Gas = new(new Vector3(1, .8f, 12), new Vector3(26, 3, 10));
    public static readonly Bounds NorthBankMist = new(new Vector3(8.4f,1.75f,22f),new Vector3(5,7,4.5f));
    public static bool BankClosed => Instance && Instance.relief && Instance.relief.MistActive;
    public static bool GasAt(Vector3 p) => Gas.Contains(p) || (BankClosed && NorthBankMist.Contains(p)) || OsowiecFieldKit.TryGasCloud(p,out _);
    public static bool InGas(Unit unit) => unit && GasAt(unit.transform.position + Vector3.up * .5f);

    // Count the actual length of the sight line inside each visible cloud, in either direction.
    internal static float GasSightDepth(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        float length = delta.magnitude;
        if (length < .001f) return 0;
        Vector3 direction = delta / length;
        Bounds lowlandMist = OsowiecMistBody.VisibleVolume(Gas, false);
        return Mathf.Max(OsowiecFieldKit.GasCloudSightDepth(from,to), Depth(lowlandMist) + (BankClosed ? Depth(NorthBankMist) : 0));

        float Depth(Bounds cloud)
        {
            float before = 0, after = 0;
            if (!cloud.Contains(from) && !cloud.IntersectRay(new Ray(from, direction), out before)) return 0;
            if (!cloud.Contains(to) && !cloud.IntersectRay(new Ray(to, -direction), out after)) return 0;
            return Mathf.Max(0, length - before - after);
        }
    }
    public static bool GasOnSegment(Vector3 from,Vector3 to)
    {
        Vector3 d=to-from;
        float length=d.magnitude;
        if(GasAt(from))return true;
        if(length<.0001f)return false;
        Ray ray=new(from,d/length);
        return (Gas.IntersectRay(ray,out float hit) && hit<=length) ||
               (BankClosed && NorthBankMist.IntersectRay(ray,out hit) && hit<=length) ||
               OsowiecFieldKit.GasCloudSightDepth(from,to)>.001f;
    }
    public static readonly Vector3 Battery = new(33, 0, 0);
    public static readonly Vector3 Telephone = new(-15, 0, -3);
    public static readonly Vector3 Lowland = new(0, 0, 14);
    public static readonly Vector3 Casemate = new(0, 0, -12);
    public static readonly Vector3 Junction = new(24, 0, 0);
    public static readonly Vector3 NorthLine = new(30, 0, 6);
    public static readonly Vector3 SouthLine = new(37, 0, -5);
    public bool NorthOnline { get; private set; }
    public bool SouthOnline { get; private set; }
    public static OsowiecOperation Instance { get; private set; }
    public int CompletedOrders => reportedObjectives;
    public int TurnsHeld => heldTurns;
    internal bool BatteryHeldAfterEvacuation => batteryConnected && relief && relief.Complete && heldTurns>=2;
    public bool MaskOn(Unit unit) => unit != null && protection.TryGetValue(unit, out var state) && state.mask;
    public float RemainingFilter(Unit unit) => unit != null && protection.TryGetValue(unit,out var state) ? state.filter : 22f;
    public float Pressure(Unit unit) => unit!=null && protection.TryGetValue(unit,out var state) ? state.pressure : 0;
    public float LensFog(Unit unit) => unit!=null && protection.TryGetValue(unit,out var state) ? state.visor.Fog : 0;
    public string LastLoss { get; private set; }
    public Unit ThreatSource(Unit target) => target && target == threatened ? aiming : null;
    public Unit ThreatenedUnit => threatened;
    public bool SquadSees(Unit enemy) => enemy && enemy.isActiveAndEnabled && enemy.GetHealth() > 0 &&
        protection.TryGetValue(enemy,out var state) && state.seenBySquad;
    public float ThreatSeconds(Unit target) => ThreatSource(target) ? Mathf.Max(0, shotAt - Time.time) : 0;
    internal bool SentryEngaged(Unit unit) => unit && (unit==aiming ||
        (protection.TryGetValue(unit,out var state) && (state.spotted || (state.lastSeen && Time.time-state.lastSeenAt<2f))));
    internal bool Aiming(Unit unit) => unit && unit == aiming;
    internal bool QuietMarch(Unit sentry)
    {
        if (SentryEngaged(sentry) || (brain && brain.TryGetHeardSector(sentry, out _))) return false;
        foreach (var unit in roster)
            if (unit && unit.owner == Player.Player1 && unit.isActiveAndEnabled && unit.GetHealth() > 0)
                return OsowiecMarch.Free(unit);
        return false;
    }
    public Vector3 NextObjective => !approached ? Telephone :
        !securedEntry ? (gateOpen || usedLowland ? Junction : Lowland) :
        !NorthOnline ? NorthLine : !SouthOnline ? SouthLine :
        relief != null && relief.Started && !relief.Complete ? relief.CurrentObjective : Battery;

    enum EntryRoute { None, Casemate, Lowland }

    sealed class Protection
    {
        public EntryRoute approach;
        public bool mask;
        public float filter = 22;
        public float exposure;
        public float pressure;
        public float nextInterception;
        public long lastShotTurn = -2;
        public readonly OsowiecVisorState visor = new();
        public float impactPressure;
        public int lastHealth = -1;
        public Vector3 lastPosition;
        public int lastFrame = -2;
        public float nextSightCheck;
        public Unit defender;
        public Unit spotted;
        public float seenFor;
        public Unit lastSeen;
        public Vector3 lastSight;
        public float lastSeenAt;
        public bool answeredCloud;
        public bool seenBySquad;
        public float firingUntil;
        public Vector3[] exitPath;
        public int exitCorner;
        public float nextExitPlan;
    }

    readonly Dictionary<Unit, Protection> protection = new();
    bool gateOpen;
    float chargeTime = -1;
    GameObject seal;
    public bool GateOpen => gateOpen;
    public bool ChargeSet => chargeTime >= 0;
    public const float ChargeDelay = 3.5f;
    bool leftTrench;
    bool approached;
    bool securedEntry;
    EntryRoute firstEntry;
    bool batteryConnected;
    bool usedLowland;
    bool usingTelephone;
    Unit phoneOperator;
    int turns;
    float playedSeconds;
    uint rosterRevision = uint.MaxValue;
    Unit[] roster = System.Array.Empty<Unit>();
    internal Unit[] FieldRoster => roster;
    internal bool HasActiveDefenders()
    {
        foreach (var unit in roster)
            if (unit && unit.isActiveAndEnabled && unit.owner==Player.Player2 && unit.GetHealth()>0) return true;
        return false;
    }
    int heldTurns;
    Player previousTurn;
    string message = "";
    Texture2D maskOverlay;
    OsowiecStressEffects stress;
    Renderer[] roof;
    internal OsowiecMapMarkers MapMarkers { get; private set; }
    bool? lastActionMode;
    int reportedObjectives;
    int reportedCarts = -1;
    Unit threatened, aiming, lastControlled;
    OsowiecBotBrain brain;
    Vector3 lockedAim;
    float shotAt, nextContactCheck;
    uint aimTurn;
    uint reactionTurn;
    int reactionShots;
    float nextReactionAt;
    const int MaxReactionShots = 3;
    bool reaction;
    bool aimedThroughCloud;
    internal const float AimSeconds = 1.6f;
    Unit fallenView;
    float lossReviewUntil;
    internal bool ReviewingLossOf(Unit unit) => unit && unit == fallenView && Time.time < lossReviewUntil;

    void Awake()
    {
        Instance = this;
        MapMarkers = new OsowiecMapMarkers();
        gameObject.AddComponent<OsowiecEnemyFireReadout>();
        roof = new[] {
            GameObject.Find("Barrel-vault brick interior")?.GetComponent<Renderer>(),
            GameObject.Find("Low earth over the concrete roof")?.GetComponent<Renderer>()
        };
        maskOverlay = BuildMask();
        stress = gameObject.AddComponent<OsowiecStressEffects>();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (maskOverlay != null) Destroy(maskOverlay);
    }

    void Update()
    {
        var game = GameManager.Instance;
        var cameras = CameraManager.Instance;
        bool actionMode = cameras != null && cameras.IsActionMode();
        if (lastActionMode != actionMode)
        {
            foreach (var panel in roof)
                if (panel != null) panel.enabled = actionMode;
            lastActionMode = actionMode;
        }
        if (game == null || game.IsPaused() || game.IsGameOver() || game.IsArmyDeploymentPhase())
            return;

        AnimateGate();
        playedSeconds += Time.deltaTime;
        if (rosterRevision != Unit.RosterRevision)
        {
            roster = FindObjectsByType<Unit>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            rosterRevision = Unit.RosterRevision;
        }
        var units = roster;
        if (lossReviewUntil > 0 && Time.time >= lossReviewUntil)
        {
            lossReviewUntil = 0;
            if (cameras && cameras.IsActionMode() && cameras.GetCurrentControlledUnit() == fallenView)
                cameras.ForceSwitchToTacticalMode();
            fallenView = null;
        }
        var controlled = LocalViewUnit();
        if (aiming && (GameManager.Instance.TurnSequence != aimTurn || !CanContinueAim(controlled)))
            ClearAim();
        if (controlled != lastControlled)
        {
            if (controlled) GetProtection(controlled).nextInterception = Time.time + .85f;
            lastControlled = controlled;
        }
        if (controlled)
            UpdateControlled(controlled, units);
        else
            OsowiecAtmosphere.SetMaskAudio(false);
        foreach (var unit in units)
            if (unit && unit.isActiveAndEnabled && unit.GetHealth() > 0)
                UpdateGas(unit, GetProtection(unit), Time.deltaTime);
        CheckSight(units);
        UpdateObjectives(units, game);
    }

    void AnimateGate()
    {
        if (ChargeSet)
        {
            chargeTime -= Time.deltaTime;
            if (chargeTime <= 0)
            {
                chargeTime = -1;
                gateOpen = true;
                if (seal) Destroy(seal);
                Vector3 center = gate.TransformPoint(new Vector3(0, 1.25f, 1.4f));
                OsowiecGateBreak.Release(gate);
                OsowiecAtmosphere.DoorBurst(center);
                relief?.SoundAlarm();
                message = "Проход открыт. Из караульни идёт подкрепление.";
            }
        }

    }

    void UpdateControlled(Unit unit, Unit[] units)
    {
        var state = GetProtection(unit);
        var keys = Keyboard.current;
        bool freeHands = !usingTelephone && (MissionSelectUI.Instance == null || !MissionSelectUI.Instance.IsOpen);
        if (freeHands && keys != null && keys.tKey.wasPressedThisFrame)
            state.mask = !state.mask;
        int frame = Time.frameCount;
        float travel = state.lastFrame == frame-1 ? Vector2.Distance(
            new Vector2(unit.transform.position.x,unit.transform.position.z),
            new Vector2(state.lastPosition.x,state.lastPosition.z)) : 0;
        bool moving = travel > .003f && travel < 1.2f;
        state.lastFrame = frame;
        state.lastPosition = unit.transform.position;
        bool wipe = freeHands && keys != null && keys.xKey.wasPressedThisFrame;
        if (state.visor.Step(state.mask, moving, wipe, Time.deltaTime)) stress.Wipe();
        OsowiecAtmosphere.SetMaskAudio(state.mask);
        int hp = unit.GetHealth();
        if (state.lastHealth >= 0 && hp < state.lastHealth)
            OnNearbyImpact(unit, Mathf.Clamp((state.lastHealth-hp)/80f,.09f,.38f));
        state.lastHealth = hp;
        ApplySuppression(unit, state, units, moving);

        if (usingTelephone && (unit != phoneOperator ||
            !Near(unit, approached ? Battery : Telephone))) usingTelephone = false;
        if (keys == null) return;
        if (keys.eKey.wasPressedThisFrame)
        {
            if (usingTelephone) usingTelephone = false;
            else if (securedEntry && !NorthOnline && Near(unit, NorthLine))
            {
                NorthOnline = true;
                message = "Северное плечо подключено.";
                OsowiecAtmosphere.Pulse(NorthLine + Vector3.up);
            }
            else if (securedEntry && !SouthOnline && Near(unit, SouthLine))
            {
                SouthOnline = true;
                message = "Южное плечо подключено.";
                OsowiecAtmosphere.Pulse(SouthLine + Vector3.up);
            }
            else if ((!approached && Near(unit, Telephone)) ||
                (NorthOnline && SouthOnline && !batteryConnected && Near(unit, Battery)))
            {
                usingTelephone = true;
                phoneOperator = unit;
                message = "";
            }
            else if (!TrySetCharge(unit)) relief?.Interact(unit);
        }
        if (usingTelephone)
        {
            if (keys.digit1Key.wasPressedThisFrame) Answer(1);
            if (keys.digit2Key.wasPressedThisFrame) Answer(2);
            if (keys.digit3Key.wasPressedThisFrame) Answer(3);
        }
    }

    static bool Near(Unit unit, Vector3 position) =>
        (unit.transform.position - position).sqrMagnitude <= 15;

    void UpdateGas(Unit unit, Protection state, float seconds)
    {
        if (!InGas(unit))
        {
            state.exposure = 0;
            state.exitPath = null;
            if (unit.owner == Player.Player2) state.mask = false;
            return;
        }
        if (unit.owner == Player.Player2)
        {
            if (state.filter > 0) state.mask = true;
            if (state.filter <= 10 && QuietMarch(unit))
                StepOutOfCloud(unit, state, seconds);
        }
        if (state.mask)
        {
            float protectedSeconds = Mathf.Min(state.filter, seconds);
            state.filter -= protectedSeconds;
            seconds -= protectedSeconds;
        }
        state.exposure += seconds;
        // First contact leaves enough time to read the warning and put on the mask.
        const float pulse = 1.25f;
        if (state.exposure < pulse) return;
        int pulses = Mathf.FloorToInt(state.exposure / pulse);
        state.exposure -= pulses * pulse;
        unit.TakeDamage(10 * pulses, null, false, "газ");
        state.lastHealth = unit.GetHealth();
    }

    // Waiting on the map can last longer than a filter. A critical NPC may walk to air
    // on the player's clock. Its ordinary bot turn owns the same controller exclusively.
    static void StepOutOfCloud(Unit unit, Protection state, float dt)
    {
        if (dt <= 0 || !unit.TryGetComponent<CharacterController>(out var motor) || !motor.enabled) return;
        Vector3 here = unit.transform.position;
        if (state.exitPath == null || Time.time >= state.nextExitPlan)
        {
            if (Time.time < state.nextExitPlan && state.exitPath == null) return;
            state.nextExitPlan = Time.time + 1.25f;
            var path = new NavMeshPath();
            Vector3 edge = OsowiecBotBrain.ExitCloud(unit);
            if (Vector3.SqrMagnitude(edge-here) < .16f ||
                !NavMesh.CalculatePath(here,edge,NavMesh.AllAreas,path) || path.status != NavMeshPathStatus.PathComplete)
            { state.exitPath = null; return; }
            state.exitPath = path.corners;
            state.exitCorner = 1;
        }
        while (state.exitCorner < state.exitPath.Length)
        {
            Vector3 next = Vector3.ProjectOnPlane(state.exitPath[state.exitCorner] - here,Vector3.up);
            if (next.sqrMagnitude < .04f) { state.exitCorner++; continue; }
            unit.transform.rotation = Quaternion.RotateTowards(unit.transform.rotation,Quaternion.LookRotation(next),dt * 240);
            motor.Move(Vector3.ClampMagnitude(next,1.8f * dt) + Vector3.down * (dt * .35f));
            unit.RefreshGridPositionFromWorld();
            return;
        }
        state.exitPath = null;
    }

    void CheckSight(Unit[] units)
    {
        if (Time.time < nextContactCheck) return;
        const float interval = .18f;
        nextContactCheck = Time.time + interval;
        var bot = BotController.Instance;
        if (!bot) return;
        foreach (var sentry in units)
        {
            if (!sentry || !sentry.isActiveAndEnabled || sentry.owner != Player.Player2 || sentry.GetHealth() <= 0) continue;
            var state = GetProtection(sentry);
            Unit seen = null;
            state.seenBySquad = false;
            foreach (var ally in units)
            {
                if (!ally || !ally.isActiveAndEnabled || ally.owner != Player.Player1 || ally.GetHealth() <= 0) continue;
                if (!state.seenBySquad && bot.CanSeeTargetForBrain(ally, sentry)) state.seenBySquad = true;
                if (!bot.CanSeeTargetForBrain(sentry, ally)) continue;
                if (!seen || ally == state.spotted) seen = ally;
            }
            state.seenFor = seen && seen == state.spotted ? state.seenFor + interval : 0;
            state.spotted = seen;
            if (state.seenBySquad) FogWarIntelTracker.Instance?.MarkVisible(sentry,sentry.transform.position);
            if (!seen) continue;
            RememberSight(sentry, seen);
            if (state.seenFor >= 1.25f && relief && !relief.AlarmRaised) relief.SoundAlarm(sentry, seen);
        }
    }

    internal void RememberSight(Unit observer, Unit seen)
    {
        var state = GetProtection(observer);
        state.lastSeen = seen;
        state.lastSeenAt = Time.time;
        state.lastSight = seen.transform.position + Vector3.up;
        state.answeredCloud = false;
    }

    internal Unit CloudContact(Unit shooter)
    {
        var state = GetProtection(shooter);
        var target = state.lastSeen;
        if (!target || target.GetHealth() <= 0 || !target.isActiveAndEnabled || state.answeredCloud ||
            Time.time - state.lastSeenAt > 4.5f) return null;
        Vector3 eye = shooter.transform.position + Vector3.up;
        return OsowiecFieldKit.Obscured(eye, target.transform.position + Vector3.up) ? target : null;
    }
    internal Vector3 LastSightOf(Unit shooter) => GetProtection(shooter).lastSight;

    public void HearShot(Vector3 sound)
    {
        Unit actor = LocalViewUnit();
        if (actor) GetProtection(actor).firingUntil = Time.time + 8;
        if (!brain && BotController.Instance) brain = BotController.Instance.GetComponent<OsowiecBotBrain>();
        if (brain) brain.HearShot(sound, roster);
    }

    internal bool RecentlyFired(Unit actor) => actor && protection.TryGetValue(actor, out var state) && Time.time < state.firingUntil;

    internal void HearImpact(Vector3 impact, Vector3 shooterPosition)
    {
        if (!brain && BotController.Instance) brain = BotController.Instance.GetComponent<OsowiecBotBrain>();
        if (brain) brain.HearImpact(impact, shooterPosition, roster);
    }

    internal void ReportLoss(Unit victim, Unit source, string cause)
    {
        if (!victim || victim.owner != Player.Player1) return;
        string reason = cause ?? (source ? "выстрел" : "ранение");
        if (source) reason += " · " + source.GetLocalizedDisplayName();
        LastLoss = victim.GetLocalizedDisplayName() + " погиб · " + reason;
        OsowiecEnemyFireReadout.Casualty(victim, LastLoss);
        var cameras = CameraManager.Instance;
        if (cameras && cameras.IsActionMode() && cameras.GetCurrentControlledUnit() == victim)
        {
            fallenView = victim;
            lossReviewUntil = Time.time + 1.35f;
            cameras.GetActionCamera().transform.SetParent(null, true);
        }
    }

    void Answer(int socket)
    {
        if (socket != (approached ? 3 : 2))
        {
            message = "Линия занята. Сверьте адрес на полоске приказа.";
            return;
        }
        usingTelephone = false;
        if (!approached)
        {
            approached = true;
            message = "Позывной — «Ива». В низине нужен фильтр, подрыв поднимет тревогу.";
            OsowiecAtmosphere.Pulse(Telephone + Vector3.up);
        }
        else
        {
            batteryConnected = true;
            message = "«Ива» на связи. Разрешите выезд.";
            OsowiecAtmosphere.Pulse(Battery + Vector3.up);
            relief?.Begin();
        }
    }

    public bool TrySetCharge(Unit unit)
    {
        if (!IsAtClosedGate(unit) || unit.GetHealth() <= 0) return false;
        if (ChargeSet) return true;
        var prefab = Resources.Load<GameObject>("OsowiecGate/FieldCase");
        if (!prefab) { Debug.LogError("Missing OsowiecGate/FieldCase prefab."); return false; }
        float face = gate.InverseTransformPoint(unit.transform.position).x <= 0 ? -1f : 1f;
        seal = Instantiate(prefab, gate, false);
        seal.transform.localPosition = new Vector3(face * .14f, 1.18f, 1.55f);
        seal.transform.localRotation = Quaternion.Euler(0, face < 0 ? 180 : 0, 0);
        chargeTime = ChargeDelay;
        OsowiecAtmosphere.PlayOptics(gate.position);
        message = "Заряд установлен.";
        return true;
    }

    bool IsAtClosedGate(Unit unit) => !gateOpen && gate != null && unit != null &&
        unit.owner == Player.Player1 && (unit.transform.position - gate.position).sqrMagnitude < 12f;

    public Unit LocalViewUnit()
    {
        var game = GameManager.Instance;
        var camera = CameraManager.Instance;
        if (game == null || camera == null || game.IsArmyDeploymentPhase() || game.IsPaused() ||
            game.IsGameOver() || game.IsBotTurn() || !camera.IsActionMode()) return null;
        var actor = camera.GetCurrentControlledUnit();
        return actor != null && actor.owner == Player.Player1 && actor.GetHealth()>0 && actor.IsControlled() ? actor : null;
    }

    public void OnNearbyImpact(Unit actor, float amount)
    {
        if (actor == null || amount <= 0 || actor != LocalViewUnit()) return;
        var state = GetProtection(actor);
        state.impactPressure = Mathf.Clamp01(state.impactPressure+amount);
        state.pressure = Mathf.Clamp01(state.pressure+amount*.55f);
        stress.Impact(actor,amount);
    }

    void ApplySuppression(Unit unit, Protection state, Unit[] units, bool moving)
    {
        Vector3 position = unit.transform.position;
        bool openGround = (!OsowiecGroundEvents.InCrater(position) && !OsowiecAtmosphere.IsInAudioVault(position)) ||
            Time.time < state.firingUntil;
        if (!openGround) state.defender=null;
        else if (Time.time >= state.nextSightCheck)
        {
            var bot = BotController.Instance;
            state.nextSightCheck = Time.time+.17f;
            state.defender = null;
            float closest = float.PositiveInfinity;
            if (bot != null)
            foreach (var candidate in units)
            {
                if (candidate == null || !candidate.isActiveAndEnabled || candidate.owner != Player.Player2 || candidate.GetHealth() <= 0 ||
                    !CanShootThisPhase(candidate) || !candidate.GetEquippedWeapon() || !candidate.GetEquippedWeapon().CanFire() ||
                    !candidate.TryGetRangedAttackRangeMeters(out float range)) continue;
                float distance = (candidate.transform.position - unit.transform.position).sqrMagnitude;
                if (distance > range * range || distance >= closest ||
                    (!bot.CanSeeTargetForBrain(candidate, unit) && CloudContact(candidate) != unit))
                    continue;
                state.defender = candidate;
                closest = distance;
            }
        }
        Unit defender = state.defender;
        if (defender && (defender.GetHealth()<=0 || !CanShootThisPhase(defender) || !ReactionAvailable()))
            defender = state.defender = null;
        state.impactPressure = Mathf.MoveTowards(state.impactPressure,0,Time.deltaTime*(openGround ? .40f : .77f));
        float target = defender ? (moving ? .55f : 1f) : state.impactPressure;
        float rate = target > state.pressure ? .50f : .85f;
        state.pressure = Mathf.MoveTowards(state.pressure,target,Time.deltaTime*rate);
        if (aiming && reaction && threatened == unit)
        {
            if (!openGround || !CanContinueAim(unit)) { ClearAim(); return; }
            if (Time.time < shotAt) return;
            ReleaseAim(aiming, unit);
            ClearAim();
            return;
        }
        if (!aiming && defender && state.pressure >= .5f && Time.time >= state.nextInterception &&
            defender.GetEquippedWeapon() && defender.GetEquippedWeapon().CanFire())
            BeginAim(defender, unit, true);
    }

    internal bool CanShootThisPhase(Unit shooter) => shooter && GameManager.Instance &&
        (long)GameManager.Instance.TurnSequence - GetProtection(shooter).lastShotTurn > 1;

    bool ReactionAvailable() => reactionTurn != GameManager.Instance.TurnSequence ||
        (reactionShots < MaxReactionShots && Time.time >= nextReactionAt);

    internal void BeginAim(Unit shooter, Unit target, bool isReaction)
    {
        if (aiming || !target || !BotController.Instance || !CanShootThisPhase(shooter) ||
            (isReaction ? !ReactionAvailable() || !OsowiecFireTurn.Allows(target) : !OsowiecFireTurn.Allows(shooter))) return;
        bool visible = BotController.Instance.CanSeeTargetForBrain(shooter, target);
        if (!visible && CloudContact(shooter) != target) return;
        aiming = shooter;
        threatened = target;
        reaction = isReaction;
        aimTurn = GameManager.Instance.TurnSequence;
        shotAt = Time.time + AimSeconds;
        aimedThroughCloud = !visible;
        if (!aimedThroughCloud) RememberSight(shooter, target);
        lockedAim = GetProtection(shooter).lastSight;
        Vector3 towards = Vector3.ProjectOnPlane(lockedAim - shooter.transform.position, Vector3.up);
        if (towards.sqrMagnitude > .01f) shooter.transform.rotation = Quaternion.LookRotation(towards);
    }

    bool CanContinueAim(Unit controlled)
    {
        if (!aiming || !aiming.isActiveAndEnabled || aiming.GetHealth() <= 0 || !threatened ||
            !threatened.isActiveAndEnabled || threatened.GetHealth() <= 0 ||
            (reaction && (controlled != threatened || !OsowiecFireTurn.Allows(controlled)))) return false;
        if (BotController.Instance && BotController.Instance.CanSeeTargetForBrain(aiming, threatened)) return true;
        if (CloudContact(aiming) != threatened) return false;
        aimedThroughCloud = true;
        return true;
    }

    internal bool CanReleaseAim(Unit shooter, Unit target) =>
        shooter == aiming && target == threatened && CanShootThisPhase(shooter) && aimTurn == GameManager.Instance.TurnSequence &&
        (reaction || OsowiecFireTurn.Allows(shooter)) &&
        Time.time >= shotAt && CanContinueAim(LocalViewUnit());

    internal bool ReleaseAim(Unit shooter, Unit target)
    {
        if (!CanReleaseAim(shooter, target)) return false;
        var weapon = shooter.GetEquippedWeapon();
        if (!weapon || !weapon.CanFire()) return false;
        // The last observed point remains fixed. Cover and movement can still defeat a good roll.
        float accuracy = (reaction ? Pressure(target) : 1f) * (aimedThroughCloud ? .5f : 1f);
        bool scattered = accuracy < 1f && Random.value >= accuracy;
        Vector3 aim = scattered ? lockedAim + Vector3.up * 3.5f : lockedAim;
        bool fired = weapon.TryFirePrepared(shooter, target, aim, scattered);
        if (fired)
        {
            var state = GetProtection(shooter);
            state.lastShotTurn = aimTurn;
            if (aimedThroughCloud) state.answeredCloud = true;
            if (reaction)
            {
                if (reactionTurn != aimTurn) { reactionTurn = aimTurn; reactionShots = 0; }
                reactionShots++;
                nextReactionAt = Time.time + 3.5f;
            }
        }
        return fired;
    }

    internal void ClearAim() { threatened = null; aiming = null; reaction = false; }

    void UpdateObjectives(Unit[] units, GameManager game)
    {
        bool friendlyAtBattery = false;
        bool contested = false;
        int friendlyAlive = 0;
        foreach (var unit in units)
        {
            if (unit == null || !unit.isActiveAndEnabled || unit.GetHealth() <= 0) continue;
            Vector3 position = unit.transform.position;
            bool atBattery = (position - Battery).sqrMagnitude <= 25;
            if (unit.owner == Player.Player2)
            {
                contested |= atBattery;
                continue;
            }

            friendlyAlive++;
            friendlyAtBattery |= atBattery;
            leftTrench |= position.x >= -33 || position.z > -11;
            ObserveApproach(unit, position);
        }
        if (friendlyAlive == 0)
        {
            if (Time.time < lossReviewUntil) return;
            if (resultNote != null) resultNote.text = "Отряд потерян. " + LastLoss;
            if (resultRoster != null) resultRoster.text = Report(0);
            game.EndGame(Player.Player1);
            return;
        }
        var hud = MissionQuestUI.Instance;
        bool evacuated = batteryConnected && relief != null && relief.Complete;
        int achieved = evacuated ? 6 : batteryConnected ? 5 : NorthOnline && SouthOnline ? 4 : securedEntry ? 3 : approached ? 2 : leftTrench ? 1 : 0;
        while (reportedObjectives < achieved)
        {
            hud?.Complete(reportedObjectives);
            reportedObjectives++;
        }
        if (batteryConnected && relief != null && reportedCarts != relief.Delivered)
        {
            reportedCarts = relief.Delivered;
            hud?.SetProgress(5, reportedCarts, 3);
        }

        if (game.currentPlayer == Player.Player2 && previousTurn == Player.Player1)
        {
            turns++;
            if (!evacuated || !friendlyAtBattery || contested) heldTurns = 0;
            previousHold = evacuated && friendlyAtBattery && !contested;
        }
        if (game.currentPlayer == Player.Player1 && previousTurn == Player.Player2)
        {
            heldTurns = previousHold && friendlyAtBattery && !contested ? heldTurns + 1 : 0;
            if (evacuated) hud?.SetProgress(6, heldTurns, 2);
            if (heldTurns >= 2)
            {
                hud?.Complete(6);
                reportedObjectives = 7;
                if (resultNote != null) resultNote.text = "Все три повозки в лазарете. С батареи передают отбой.";
                if (resultRoster != null) resultRoster.text = Report(friendlyAlive);
                game.EndGame(Player.Player2);
            }
        }
        previousTurn = game.currentPlayer;
    }

    bool previousHold;

    void ObserveApproach(Unit unit, Vector3 position)
    {
        if (!approached || securedEntry) return;

        if ((position - Lowland).sqrMagnitude <= 15)
        {
            GetProtection(unit).approach = EntryRoute.Lowland;
            usedLowland = true;
        }
        else if (gateOpen && (position - Casemate).sqrMagnitude <= 15)
            GetProtection(unit).approach = EntryRoute.Casemate;

        // At the eastern exits the two branches are still physically separate.
        if (position.x >= 12 && position.x <= 17 && Mathf.Abs(position.z) >= 4)
            GetProtection(unit).approach = position.z > 0 ? EntryRoute.Lowland : EntryRoute.Casemate;

        if (position.x <= 19 || !(gateOpen || usedLowland)) return;
        firstEntry = GetProtection(unit).approach;
        if (firstEntry == EntryRoute.None)
            firstEntry = position.z > 0 || (position.z == 0 && !gateOpen) ? EntryRoute.Lowland : EntryRoute.Casemate;
        securedEntry = true;
    }

    string Report(int alive) =>
        $"В строю   {alive}     Ходов   {turns}\n" +
        $"Время   {(int)playedSeconds / 60:00}:{(int)playedSeconds % 60:00}     " +
        (firstEntry == EntryRoute.Lowland ? "Через низину" : firstEntry == EntryRoute.Casemate ? "Через каземат" : "У траншеи") +
        $"\nВ лазарете   {(relief != null ? relief.Delivered : 0)} / 3" +
        (relief && relief.Delivered>0 ? "\n"+OsowiecWesternWard.Ledger(relief.Delivered) : "");

    Protection GetProtection(Unit unit)
    {
        if (!protection.TryGetValue(unit, out var state))
            protection.Add(unit, state = new Protection());
        return state;
    }

    public void ReturnToHeadquarters()
    {
        Time.timeScale = 1;
        SceneManager.LoadScene("OsowiecHQ");
    }

    static Texture2D BuildMask()
    {
        const int width = 320, height = 150;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float u = (x + .5f) / width;
            float v = (y + .5f) / height;
            float horizontal = Mathf.Min(Mathf.Abs(u - .28f), Mathf.Abs(u - .72f)) / .24f;
            float vertical = (v - .51f) / .37f;
            float radius = Mathf.Sqrt(horizontal * horizontal + vertical * vertical);
            float edge = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.87f, 1.02f, radius));
            float ring = 1 - Mathf.Abs(Mathf.InverseLerp(.9f, 1.11f, radius) * 2 - 1);
            pixels[y * width + x] = new Color(.08f + ring * .09f, .09f + ring * .07f,
                .065f + ring * .035f, .98f * edge);
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    void OnGUI()
    {
        var game = GameManager.Instance;
        var cam = CameraManager.Instance;
        if (game == null || cam == null || game.IsGameOver() || game.IsPaused() || game.IsArmyDeploymentPhase()) return;
        var unit = cam.GetCurrentControlledUnit();
        if (cam.IsActionMode() && unit != null && unit.owner == Player.Player1)
        {
            var state = GetProtection(unit);
            if (state.mask)
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), maskOverlay);
            stress.Draw(unit,state.mask,state.pressure,state.visor.Fog,state.visor.WipeProgress);
            string note = OsowiecFieldKit.HoldPosition(unit) && !OsowiecFieldKit.ScopeOpen ? "Действие завершено   ·   Ход по Tab" :
                OsowiecGroundEvents.InWire(unit.transform.position) ? (unit.chessType == ChessUnitType.Hammer ? "Проволока   ·   Q расчистить" : "Проволока замедляет движение") :
                !approached && Near(unit, Telephone) ? "E   ·   Полевой телефон" :
                securedEntry && !NorthOnline && Near(unit, NorthLine) ? "E   ·   Северный ввод" :
                securedEntry && !SouthOnline && Near(unit, SouthLine) ? "E   ·   Южный ввод" :
                NorthOnline && SouthOnline && !batteryConnected && Near(unit, Battery) ? "E   ·   Передатчик батареи" :
                ChargeSet ? $"Заряд установлен   ·   {Mathf.CeilToInt(chargeTime)} с" :
                IsAtClosedGate(unit) ? "E   ·   Установить заряд" :
                GasAt(unit.transform.position + Vector3.up * .5f) ? "В газовой зоне" : message;
            note=OsowiecFieldKit.Feedback(unit) ?? note;
            if(string.IsNullOrEmpty(note)) note=OsowiecFieldKit.Status(unit);
            OsowiecActionReadout.Draw(unit,state.filter,state.mask,note);
            if (usingTelephone && phoneOperator == unit) DrawSwitchboard(OsowiecHudPaint.Scale);
        }
        else if (!cam.IsActionMode() && !(OsowiecPlanner.Instance && OsowiecPlanner.Instance.SurveyOpen) && Event.current.type==EventType.Repaint)
        {
            var pins=MapMarkers;
            pins.Begin(cam.GetTacticalCamera());
            pins.Add(OsowiecMapSymbol.Phone,Telephone,approached?"Узел связи":"Полевой телефон",
                approached?"Связь со штабом установлена.":"Здесь можно вызвать штаб.");
            pins.Add(OsowiecMapSymbol.Gas,Gas.center,"Газовая низина","Для прохода нужен фильтр.");
            pins.Add(OsowiecMapSymbol.Gate,Casemate,"Каземат",gateOpen?"Проход открыт.":ChargeSet?"Заряд установлен.":"Дверь заперта. Взрыв поднимет тревогу.");
            if (securedEntry && !(NorthOnline && SouthOnline))
            {
                if (!NorthOnline) pins.Add(OsowiecMapSymbol.JunctionNorth,NorthLine,"Северный ввод","Восстановите связь на посту.");
                if (!SouthOnline) pins.Add(OsowiecMapSymbol.JunctionSouth,SouthLine,"Южный ввод","Восстановите связь на посту.");
            }
            else pins.Add(OsowiecMapSymbol.Battery,Battery,"Батарея",relief && relief.Complete?$"Двор удержан ходов: {heldTurns} из 2.":"Артиллерийский двор.");
            if(relief)relief.AddMapMarkers(pins);
            pins.Paint();
        }
        OsowiecHudPaint.DrawCaption();
        OsowiecEnemyFireReadout.Draw();
    }

    void DrawSwitchboard(float s)
    {
        var box = new Rect((Screen.width - 610*s)/2, Screen.height*.54f, 610*s, 142*s);
        var paper = new Color(.93f,.87f,.71f);
        var brass = new Color(.79f,.65f,.39f);
        OsowiecHudPaint.Panel(box);
        float x = box.x + 16*s, y = box.y + 8*s;
        OsowiecHudPaint.Text(new Rect(x,y,575*s,23*s), approached ? "ПЕРЕДАТЧИК  •  АДРЕС БАТАРЕИ" : "ПОЛЕВОЙ КОММУТАТОР",16,brass,true);
        OsowiecHudPaint.Text(new Rect(x,y+26*s,575*s,26*s), approached ? "Введите позывной, полученный на развилке." : "По приказу соединитесь со штабом и получите позывной.",19,paper);
        OsowiecHudPaint.Text(new Rect(x,y+59*s,575*s,27*s), approached ? "[1]  Дуб       [2]  Клён       [3]  Ива" : "[1]  Лазарет       [2]  Штаб       [3]  Склад",21,paper,true);
        OsowiecHudPaint.Text(new Rect(x,y+96*s,575*s,23*s), string.IsNullOrEmpty(message) ? "E   Положить трубку" : message,16,brass);
    }

}
