using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

public sealed class OsowiecBotBrain : MonoBehaviour, IBotMissionBrain
{
    sealed class Watch
    {
        public Vector3 post;
        public Vector3 lastSeen;
        public int sightTurn = -10;
        public int lastActed = -1;
        public bool visiting;
        public Unit visible;
        public Vector3 heardSector;
        public int heardTurn = -100;
    }

    readonly Dictionary<Unit, Watch> watches = new();
    [SerializeField, Min(3)] float assaultReach = 9;
    [SerializeField, Min(3)] float scoutReach = 13;
    [SerializeField, Min(3)] float supportReach = 15;
    int turn;
    Unit[] allies = System.Array.Empty<Unit>();
    public bool CanRun() => OsowiecOperation.Instance != null;

    void Start()
    {
        // Move the two original lowland posts to different rims before deployment is confirmed.
        foreach (var unit in FindObjectsByType<Unit>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (unit.owner != Player.Player2) continue;
            // Includes the upper gallery relief guard when the alarm later activates him.
            Vector3 at=unit.transform.position;
            if(at.x>=29f && at.x<=50f && Mathf.Abs(at.z)<12f && !unit.TryGetComponent<OsowiecBatteryPatrol>(out _))
                unit.gameObject.AddComponent<OsowiecBatteryPatrol>();
            if(!unit.isActiveAndEnabled)continue;
            bool first = unit.name == "Дозорный";
            if (!first && unit.name != "Сменный дозор") continue;
            Vector3 survey = first ? new Vector3(1, 0, 18.8f) : new Vector3(15.8f, 0, 9.2f);
            if (!NavMesh.SamplePosition(survey, out var rim, 3.5f, NavMesh.AllAreas) ||
                OsowiecOperation.GasAt(rim.position + Vector3.up * .5f)) continue;
            Vector3 floor = rim.position;
            if (OsowiecHitBody.TryFloor(floor, out var ground)) floor = ground + Vector3.up * .035f;
            var motor = unit.GetComponent<CharacterController>();
            bool active = motor && motor.enabled;
            if (active) motor.enabled = false;
            unit.transform.SetPositionAndRotation(floor, Quaternion.Euler(0, first ? -65 : 130, 0));
            if (active) motor.enabled = true;
            if (ChessGrid.Instance) unit.currentGridPosition = ChessGrid.Instance.WorldToGridCoords(floor);
        }
    }

    Watch Station(Unit unit)
    {
        if (!watches.TryGetValue(unit, out var watch))
            watches.Add(unit, watch = new Watch { post = unit.transform.position });
        return watch;
    }
    internal void AssignPost(Unit unit, Vector3 post) => Station(unit).post = post;

    internal void HearShot(Vector3 sound, Unit[] roster) => AlertNearby(sound, sound, 60f, roster);
    internal void HearImpact(Vector3 impact, Vector3 shooter, Unit[] roster) => AlertNearby(impact, shooter, 18f, roster);

    void AlertNearby(Vector3 eventPosition, Vector3 sound, float radius, Unit[] roster)
    {
        foreach (var sentry in roster)
        {
            if (!sentry || !sentry.isActiveAndEnabled || sentry.owner != Player.Player2 || sentry.GetHealth() <= 0 ||
                (sentry.transform.position - eventPosition).sqrMagnitude > radius * radius) continue;
            var watch = Station(sentry);
            // Keep only the sector. Repeated reports from the same place never triangulate the shooter.
            watch.heardSector = new Vector3((Mathf.Floor(sound.x / 8) + .5f) * 8, 0,
                (Mathf.Floor(sound.z / 8) + .5f) * 8);
            watch.heardTurn = turn;
            if (!watch.visible && !OsowiecOperation.Instance.Aiming(sentry))
            {
                Vector3 facing = Vector3.ProjectOnPlane(watch.heardSector - sentry.transform.position, Vector3.up);
                if (facing.sqrMagnitude > 1) sentry.transform.rotation = Quaternion.LookRotation(facing);
            }
        }
    }

    internal bool TryGetHeardSector(Unit unit, out Vector3 approximate)
    {
        approximate = default;
        if (!watches.TryGetValue(unit, out var watch) || turn - watch.heardTurn > 8) return false;
        approximate = watch.heardSector;
        return true;
    }

    public IEnumerator PickActingUnitBeforeDice(BotController controller)
    {
        turn++;
        var units = FindObjectsByType<Unit>(FindObjectsSortMode.InstanceID);
        allies = units.Where(u => u.owner == Player.Player1 && u.GetHealth() > 0).ToArray();
        Unit selected = null;
        float best = float.NegativeInfinity;
        foreach (var unit in units)
        {
            if (unit.owner != Player.Player2 || unit.GetHealth() <= 0) continue;
            if (OsowiecOperation.Instance.relief && OsowiecOperation.Instance.relief.HoldingCart(unit)) continue;
            var watch = Station(unit);
            Observe(controller, unit, watch);
            var weapon = unit.GetEquippedWeapon();
            bool ready = !weapon || (weapon.CanFire() && OsowiecOperation.Instance.CanShootThisPhase(unit));
            int priority = OsowiecOperation.InGas(unit) ? 7 : TargetInReach(controller, unit, watch, out _) && ready ? 4 :
                watch.visible ? 3 : TryGetHeardSector(unit,out _) || turn-watch.sightTurn <= 2 ? 2 : 0;
            var column = OsowiecOperation.Instance.relief;
            float cartDistance = column && column.SeesCart(unit,out var cart) ?
                Vector3.Distance(unit.transform.position, cart) : float.PositiveInfinity;
            if (cartDistance <= 18) priority = Mathf.Max(priority, 1);
            int waitingTurns = turn - Mathf.Max(0,watch.lastActed);
            float waitBonus = priority >= 2 ? Mathf.Min(215, waitingTurns*32) : Mathf.Min(35,waitingTurns);
            float score = 100 * priority + waitBonus +
                (priority == 1 ? 18-cartDistance : priority == 2 ? Mathf.Max(0, 30 - Vector3.Distance(unit.transform.position, watch.heardSector)) : 0);
            if (score <= best) continue;
            best = score;
            selected = unit;
        }
        controller.SetPickedBotActingUnitForTurn(selected);
        yield break;
    }

    public IEnumerator ExecuteTurn(BotController controller, float moveBudgetMeters)
    {
        uint phase = GameManager.Instance.TurnSequence;
        var unit = controller.ConsumePickedBotActingUnitForTurn();
        if (unit != null && unit.GetHealth() > 0 && watches.TryGetValue(unit, out var watch))
        {
            watch.lastActed = turn;
            unit.FieldTurn.Activated = true;
            bool escaping = OsowiecOperation.InGas(unit);
            bool heard = TryGetHeardSector(unit,out _);
            bool scout = unit.chessType == ChessUnitType.Guardian;
            float roleBudget = moveBudgetMeters;
            if (scout && (escaping || heard || watch.visible) && OsowiecOperation.Instance.Pressure(unit) <= .65f)
                roleBudget += OsowiecFieldTurn.SprintBonus;
            unit.SetRemainingMoveMeters(roleBudget);
            var column = OsowiecOperation.Instance.relief;
            Vector3 cart = default;
            bool intercept = !watch.visible && !heard && column && column.SeesCart(unit,out cart);
            if (!escaping && intercept && column.TryDelayCart(unit))
            {
                controller.EndTurnInternal(phase);
                yield break;
            }
            if (escaping || !TargetInReach(controller, unit, watch, out _))
            {
                Vector3 goal;
                if (escaping)
                    goal = ExitCloud(unit);
                else if (intercept && !watch.visible)
                {
                    goal = cart;
                    OsowiecEnemyFireReadout.Intercept(unit);
                }
                else if (turn - watch.sightTurn <= 2 && (watch.visible || watch.sightTurn >= watch.heardTurn))
                    goal = ObservationPoint(unit, watch);
                else if (heard)
                    goal = ApproachCover(unit, watch.heardSector);
                else
                {
                    // Each sentry keeps its own sector. A quiet post alternates between two marks.
                    Vector3 offset = watch.post.z < -4 ? new Vector3(-3,0,0) :
                        watch.post.z > 6 ? new Vector3(-2.5f,0,-1) : new Vector3(2,0,1.5f);
                    goal = watch.post + (watch.visiting ? offset : Vector3.zero);
                    if (Vector3.SqrMagnitude(goal-unit.transform.position)<1.5f)
                    {
                        watch.visiting = !watch.visiting;
                        goal = watch.post + (watch.visiting ? offset : Vector3.zero);
                    }
                    if (OsowiecOperation.GasAt(goal + Vector3.up * .5f))
                        goal = watch.post - offset;
                }
                float budget = Mathf.Min(roleBudget, escaping || heard || intercept || turn-watch.sightTurn<=2
                    ? (scout ? roleBudget : 6f) : 3f);
                if (OsowiecGroundEvents.IsWarned(goal)) goal = watch.post;
                if ((goal-unit.transform.position).sqrMagnitude>1.2f)
                    yield return controller.MoveTowardsWorldTargetByBudgetInternal(unit,goal,budget);
            }
            if (GameManager.Instance.TurnSequence != phase || !GameManager.Instance.IsBotTurn() || GameManager.Instance.IsGameOver())
            {
                controller.EndTurnInternal(phase);
                yield break;
            }
            if (unit && unit.GetHealth()>0)
            {
                Observe(controller, unit, watch);
                if (!watch.visible && heard && Vector3.ProjectOnPlane(unit.transform.position-watch.heardSector,Vector3.up).sqrMagnitude < 16f)
                    watch.heardTurn = -100;
            }
            if (unit && unit.GetHealth()>0 && TargetInReach(controller,unit,watch,out var target))
            {
                var tool=unit.GetEquippedWeapon();
                if(tool && tool.AmmoInMag==0) tool.TryAutoReloadIfEmpty();
                else if(!tool || (tool.CanFire() && OsowiecOperation.Instance.CanShootThisPhase(unit)))
                {
                    int rounds = tool ? tool.AmmoInMag : 0;
                    Vector3 lastTarget = target.transform.position;
                    yield return controller.PerformAttackInternal(unit,target);
                    if (scout && unit && tool && tool.AmmoInMag < rounds && unit.GetHealth() > 0 &&
                        GameManager.Instance.TurnSequence == phase && unit.GetRemainingMoveMeters() > .1f)
                    {
                        Vector3 away = Vector3.ProjectOnPlane(unit.transform.position-lastTarget,Vector3.up).normalized;
                        Vector3 goal = unit.transform.position + away * Mathf.Min(3f,unit.GetRemainingMoveMeters());
                        if (!OsowiecGroundEvents.IsWarned(goal) && !OsowiecOperation.GasAt(goal+Vector3.up*.5f))
                            yield return controller.MoveTowardsWorldTargetByBudgetInternal(unit,goal,unit.GetRemainingMoveMeters());
                    }
                }
            }
            else if (unit && unit.GetHealth() > 0 && intercept) column.TryDelayCart(unit);
        }
        controller.EndTurnInternal(phase);
    }

    void Observe(BotController controller, Unit unit, Watch watch)
    {
        float best = float.PositiveInfinity;
        watch.visible = null;
        foreach (var ally in allies)
        {
            if (!ally || !ally.isActiveAndEnabled || ally.GetHealth()<=0) continue;
            float distance = (ally.transform.position-unit.transform.position).sqrMagnitude;
            if (distance>=best || !controller.CanSeeTargetForBrain(unit,ally)) continue;
            watch.visible = ally;
            best = distance;
        }
        if (!watch.visible) return;
        watch.lastSeen=watch.visible.transform.position;
        watch.sightTurn=turn;
        OsowiecOperation.Instance.RememberSight(unit, watch.visible);
        OsowiecAtmosphere.ReportEnemyContact();
    }

    float Reach(Unit unit)
    {
        var tool=unit.GetEquippedWeapon();
        if(!tool || !tool.Config) return 0;
        float preferred=unit.chessType switch {
            ChessUnitType.Pawn => assaultReach, ChessUnitType.Guardian => scoutReach, _ => supportReach
        };
        return Mathf.Min(preferred,tool.Config.maxHitRangeMeters);
    }

    bool TargetInReach(BotController controller,Unit unit,Watch watch,out Unit target)
    {
        float reach=Reach(unit);
        if(reach==0) return controller.TryGetBestVisibleEnemyInAttackRangeForBrain(unit,out target);
        target=watch.visible ? watch.visible : OsowiecOperation.Instance.CloudContact(unit);
        return target && target.GetHealth()>0 &&
               Vector3.SqrMagnitude((watch.visible ? target.transform.position : OsowiecOperation.Instance.LastSightOf(unit))-unit.transform.position)<=reach*reach;
    }

    Vector3 ObservationPoint(Unit unit,Watch watch)
    {
        var position=unit.transform.position;
        float reach=Reach(unit);
        if(reach<=0) return watch.lastSeen;
        var away=Vector3.ProjectOnPlane(position-watch.lastSeen,Vector3.up);
        float standOff=reach*.74f;
        if(away.sqrMagnitude<standOff*standOff)
            return watch.visible ? position : ApproachCover(unit,watch.lastSeen);
        var waypoint=watch.lastSeen+away.normalized*standOff;
        if(!NavMesh.SamplePosition(waypoint,out var hit,1.4f,NavMesh.AllAreas) ||
           Vector3.Distance(hit.position,watch.lastSeen)<standOff-1.4f) return ApproachCover(unit,watch.lastSeen);
        foreach(var other in watches.Keys)
            if(other && other!=unit && other.GetHealth()>0 && (other.transform.position-hit.position).sqrMagnitude<1.45f)
                return ApproachCover(unit,watch.lastSeen);
        return ApproachCover(unit,hit.position);
    }

    static Vector3 ApproachCover(Unit unit, Vector3 sector)
    {
        Vector3 here = unit.transform.position;
        Vector3 delta = Vector3.ProjectOnPlane(sector - here, Vector3.up);
        Vector3 ahead = here + Vector3.ClampMagnitude(delta, 6);
        Vector3 side = Vector3.Cross(Vector3.up, delta.normalized);
        Vector3 best = here;
        float score = float.NegativeInfinity;
        for (int lane = -1; lane <= 1; lane++)
        {
            if (!NavMesh.SamplePosition(ahead + side * (lane * 2.5f), out var nav, 3.5f, NavMesh.AllAreas) ||
                OsowiecOperation.GasAt(nav.position + Vector3.up * .5f) || OsowiecGroundEvents.IsWarned(nav.position)) continue;
            if (NavMesh.Raycast(here, nav.position, out _, NavMesh.AllAreas)) continue;
            float rank = -Vector3.Distance(nav.position, sector);
            if (OsowiecGroundEvents.InCrater(nav.position) ||
                (Physics.Linecast(nav.position + Vector3.up, sector + Vector3.up, out var block, ~0, QueryTriggerInteraction.Ignore) &&
                !block.collider.GetComponentInParent<Unit>())) rank += 3;
            if (rank > score) { best = nav.position; score = rank; }
        }
        // Let the ordinary path planner round obstacles if all nearby lanes were blocked.
        return float.IsNegativeInfinity(score) ? sector : best;
    }

    internal static Vector3 ExitCloud(Unit unit)
    {
        Vector3 start = unit.transform.position;
        Vector3 sample = start + Vector3.up * .5f;
        Bounds cloud = OsowiecOperation.Gas.Contains(sample) ? OsowiecOperation.Gas : OsowiecOperation.NorthBankMist;
        if (!cloud.Contains(sample) && OsowiecFieldKit.TryGasCloud(sample, out var center))
            cloud = new Bounds(center, Vector3.one * (2f * OsowiecFieldKit.CloudRadius));
        var rim = new[] { new Vector3(start.x, start.y, cloud.max.z + 1.6f), new Vector3(start.x, start.y, cloud.min.z - 1.6f),
            new Vector3(cloud.min.x - 1.6f, start.y, start.z), new Vector3(cloud.max.x + 1.6f, start.y, start.z) };
        Vector3 closest = start;
        float length = float.PositiveInfinity;
        var path = new NavMeshPath();
        foreach (var edge in rim)
        {
            if (!NavMesh.SamplePosition(edge,out var nav,3,NavMesh.AllAreas) || OsowiecOperation.GasAt(nav.position + Vector3.up * .5f) ||
                !NavMesh.CalculatePath(start, nav.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
            var corners = path.corners;
            float walk = 0;
            for (int i=1; i<corners.Length; i++) walk += Vector3.Distance(corners[i-1], corners[i]);
            if (walk < length) { closest=nav.position; length=walk; }
        }
        return closest;
    }
}
