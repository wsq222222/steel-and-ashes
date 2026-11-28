using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.AI;
using UnityEngine.Serialization;

/// <summary>Runs the active mission brain and handles its camera, movement and attack requests.</summary>
public class BotController : MonoBehaviour
{
    public static BotController Instance;

    [Header("Bot Settings")]
    [SerializeField] private float turnStartDelay = 0.5f;
    [Tooltip("После камеры на выбранного врага: пауза перед броском кубика.")]
    [SerializeField] private float preDiceRollDelay = 0.2f;
    [Tooltip("Пауза после броска кубика, чтобы успела проиграться анимация кубика.")]
    [SerializeField] private float dicePresentationDelay = 1.0f;
    [Tooltip("Доп. пауза после анимации кубика перед действием выбранного врага.")]
    [SerializeField] private float postDiceRollDelay = 0.2f;
    [Tooltip("Пауза после прилёта камеры к выбранному врагу (до кубика; плавный MoveCamera ~0.5 c + запас).")]
    [SerializeField] private float botIntroCameraSettleDelay = 0.55f;
    [SerializeField] private float moveDelay = 0.3f;
    [SerializeField] private float attackDelay = 0.3f;
    [SerializeField] private float attackRange = 3.0f; // Увеличен радиус атаки
    [SerializeField] private float enemyDetectionRange = 8.0f; // Радиус обнаружения врагов (4 клетки, больше радиуса атаки)
    [SerializeField] private float approachDistance = 2.0f; // Насколько близко бот может подойти к врагу после перемещения на клетку

    [Header("Debug")]
    [FormerlySerializedAs("debugMission1Bot")]
    [SerializeField] private bool debugBot;

    [Header("Vision")]
    [SerializeField] private float visionRange = 18f;
    [SerializeField] [Range(10f, 180f)] private float visionFovDegrees = 120f;
    [SerializeField] private float visionEyeOffsetY = 1.4f;
    [SerializeField] private LayerMask visionOcclusionMask = ~0;
    [Tooltip("Если игрок очень близко, бот замечает его даже вне FOV (360°), но всё ещё требует LOS.")]
    [SerializeField] private float closeAwarenessRadius = 3.0f;

    private Unit _pickedBotActingUnitThisTurn;
    private uint? executingPhase;
    public bool CanSeeTargetForBrain(Unit observer, Unit target) => CanSeeTarget(observer, target);
    public bool TryGetBestVisibleEnemyInAttackRangeForBrain(Unit attacker, out Unit target) => TryGetBestVisibleEnemyInAttackRange(attacker, out target);

    // Ценности фигур
    private static readonly Dictionary<ChessUnitType, int> PieceValues = new Dictionary<ChessUnitType, int>
    {
        { ChessUnitType.Queen, 9 },
        { ChessUnitType.Гончая, 5 },
        { ChessUnitType.Циклоп, 3 },
        { ChessUnitType.Horse, 3 },
        { ChessUnitType.Молот, 1 },
        { ChessUnitType.King, 100 }
    };

    public bool IsExecutingTurn() => executingPhase.HasValue;

    internal void SetPickedBotActingUnitForTurn(Unit unit)
    {
        _pickedBotActingUnitThisTurn = unit;
        if (unit != null && GameManager.Instance != null && GameManager.Instance.IsBotTurn())
            FogOfWarManager.Instance?.BeginBotSpectatorVision(unit);
    }

    internal Unit PeekPickedBotActingUnitForTurn() => _pickedBotActingUnitThisTurn;

    public Unit GetPickedActingUnitForPresentation() => _pickedBotActingUnitThisTurn;

    internal Unit ConsumePickedBotActingUnitForTurn()
    {
        Unit u = _pickedBotActingUnitThisTurn;
        _pickedBotActingUnitThisTurn = null;
        return u;
    }

    internal void ClearPickedBotActingUnitForTurn() => _pickedBotActingUnitThisTurn = null;

    void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    public void ExecuteBotTurn()
    {
        var game = GameManager.Instance;
        if (!game || !game.IsBotTurn() || game.IsArmyDeploymentPhase() || game.IsGameOver()) return;
        if (executingPhase == game.TurnSequence) return;
        StartCoroutine(RunMissionTurn(game));
    }

    private IEnumerator RunMissionTurn(GameManager game)
    {
        uint phase = game.TurnSequence;
        executingPhase = phase;
        ClearPickedBotActingUnitForTurn();

        if (OsowiecOperation.Instance && !OsowiecOperation.Instance.HasActiveDefenders())
        {
            // Retain a simulation frame so the battery hold observes both turn transitions.
            yield return null;
            EndTurnInternal(phase);
            yield break;
        }
        yield return new WaitForSeconds(turnStartDelay);

        var brain = GetComponents<MonoBehaviour>().OfType<IBotMissionBrain>().FirstOrDefault(b => b.CanRun());
        if (brain == null || !OwnsTurn(game, phase))
        {
            EndTurnInternal(phase);
            yield break;
        }

        yield return brain.PickActingUnitBeforeDice(this);
        if (!OwnsTurn(game, phase))
        {
            EndTurnInternal(phase);
            yield break;
        }
        yield return RunCameraOnPickedUnitBeforeDice();

        if (game.UsesTurnDice && OwnsTurn(game, phase))
        {
            if (preDiceRollDelay > 0f) yield return new WaitForSeconds(preDiceRollDelay);
            if (OwnsTurn(game, phase) && !game.HasRolledDiceThisTurn())
                game.RollDiceForCurrentTurn();
            if (dicePresentationDelay > 0f) yield return new WaitForSeconds(dicePresentationDelay);
            if (postDiceRollDelay > 0f) yield return new WaitForSeconds(postDiceRollDelay);
        }

        if (!OwnsTurn(game, phase) || !brain.CanRun())
        {
            EndTurnInternal(phase);
            yield break;
        }
        yield return brain.ExecuteTurn(this, game.GetCurrentTurnMoveBudgetMeters());
    }

    private static bool OwnsTurn(GameManager game, uint phase)
        => game && game.TurnSequence == phase && game.IsBotTurn() && !game.IsGameOver();

    /// <summary> Камера на уже выбранного до кубика врага (если spotted / разрешено). </summary>
    private IEnumerator RunCameraOnPickedUnitBeforeDice()
    {
        if (CameraManager.Instance == null || !CameraManager.Instance.IsBotFollowCameraEnabled())
            yield break;

        Unit u = PeekPickedBotActingUnitForTurn();
        if (u == null || u.GetHealth() <= 0)
            yield break;

        CameraManager.Instance.SwitchToBotUnitView(u);
        float wait = Mathf.Max(0f, botIntroCameraSettleDelay);
        if (wait > 0f)
            yield return new WaitForSeconds(wait);
    }

    /// <summary>
    /// Завершение хода
    /// </summary>
    internal void EndTurnInternal(uint? expectedTurn = null)
    {
        if (!executingPhase.HasValue || (expectedTurn.HasValue && executingPhase != expectedTurn)) return;
        uint phase = executingPhase.Value;
        executingPhase = null;
        ClearPickedBotActingUnitForTurn();
        var game = GameManager.Instance;
        if (!OwnsTurn(game, phase)) return;
        game.SwitchTurn();
        if (CameraManager.Instance != null)
            CameraManager.Instance.UpdateCameraForCurrentPlayer();
    }

    private bool TryGetBestVisibleEnemyInAttackRange(Unit attacker, out Unit target)
    {
        target = null;
        if (attacker == null) return false;
        Unit[] allUnits = FindObjectsByType<Unit>(FindObjectsInactive.Exclude);
        List<Unit> enemies = allUnits.Where(u => u != null && u.owner == Player.Player1 && u.GetHealth() > 0).ToList();
        float best = float.NegativeInfinity;
        foreach (Unit e in enemies)
        {
            if (!BotMayTargetUnitAsEnemy(attacker, e)) continue;
            if (!CanSeeTarget(attacker, e)) continue;
            float d = Vector3.Distance(attacker.transform.position, e.transform.position);
            if (d > attackRange) continue;
            float score = 100f - d; // ближе = лучше
            if (score > best)
            {
                best = score;
                target = e;
            }
        }
        return target != null;
    }

    /// <summary>
    /// Первое попадание луча зрения считается «видим цель», если это коллайдер самого юнита
    /// или дочерний коллайдер (важно для танка: корпус часто без Unit на промежуточных узлах).
    /// </summary>
    private static bool VisionRayHitCountsAsTarget(RaycastHit hit, Unit target)
    {
        if (target == null) return false;
        Unit hitUnit = hit.collider != null ? hit.collider.GetComponentInParent<Unit>() : null;
        if (hitUnit == target) return true;
        if (!target.IsTankUnit || hit.collider == null) return false;
        Transform t = hit.collider.transform;
        return t == target.transform || t.IsChildOf(target.transform);
    }

    private bool CanSeeTarget(Unit observer, Unit target)
    {
        if (observer == null || target == null) return false;
        if (target.GetHealth() <= 0) return false;
        if (OsowiecFieldKit.Obscured(observer.transform.position + Vector3.up, target.transform.position + Vector3.up))
            return false;

        Vector3 eye = observer.transform.position + Vector3.up * visionEyeOffsetY;
        Vector3 toTarget = (target.transform.position + Vector3.up * 1.0f) - eye;
        float dist = toTarget.magnitude;
        float range = Mathf.Max(0f, visionRange);
        if (OsowiecOperation.Instance && (observer.owner == Player.Player1 || OsowiecOperation.Instance.RecentlyFired(target)) &&
            observer.TryGetRangedAttackRangeMeters(out var weaponRange))
            range = Mathf.Max(range, weaponRange);
        if (observer.owner == Player.Player1 && OsowiecFieldKit.ScopeOpen && observer.IsControlled())
            range = Mathf.Max(range, 70);
        if (dist > range) return false;

        Vector3 dir = dist > 0.001f ? (toTarget / dist) : observer.transform.forward;
        // Если игрок очень близко — замечаем его даже сзади (360°), но всё равно требуем LOS.
        float ca = Mathf.Max(0f, closeAwarenessRadius);
        if (dist > ca)
        {
            float angle = Vector3.Angle(observer.transform.forward, dir);
            if (angle > Mathf.Max(1f, visionFovDegrees) * 0.5f) return false;
        }

        if (Physics.Raycast(eye, dir, out RaycastHit hit, dist, visionOcclusionMask, QueryTriggerInteraction.Ignore))
        {
            bool ok = VisionRayHitCountsAsTarget(hit, target);
            if (debugBot)
            {
                Debug.DrawLine(eye, hit.point, ok ? Color.green : Color.red, 0.2f);
            }
            return ok;
        }

        // если ничего не задели raycast'ом, считаем что LOS чистый
        if (debugBot)
        {
            Debug.DrawLine(eye, eye + dir * dist, Color.yellow, 0.2f);
        }
        return true;
    }

    private void OnDrawGizmosSelected()
    {
        if (!debugBot) return;
        if (GameManager.Instance == null) return;
        if (GameManager.Instance.GetGameMode() != GameMode.PlayerVsBot) return;

        // Рисуем "конус зрения" вокруг бота-контроллера (по ближайшему живому юниту Player2)
        Unit[] allUnits = FindObjectsByType<Unit>(FindObjectsInactive.Exclude);
        Unit botUnit = allUnits
            .Where(u => u != null && u.owner == Player.Player2 && u.GetHealth() > 0)
            .OrderBy(u => Vector3.Distance(u.transform.position, transform.position))
            .FirstOrDefault();
        if (botUnit == null) return;

        Vector3 eye = botUnit.transform.position + Vector3.up * visionEyeOffsetY;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(eye, Mathf.Max(0f, visionRange));

        Vector3 fwd = botUnit.transform.forward;
        float half = Mathf.Max(1f, visionFovDegrees) * 0.5f;
        Vector3 left = Quaternion.Euler(0f, -half, 0f) * fwd;
        Vector3 right = Quaternion.Euler(0f, half, 0f) * fwd;
        Gizmos.DrawLine(eye, eye + left * Mathf.Min(visionRange, 6f));
        Gizmos.DrawLine(eye, eye + right * Mathf.Min(visionRange, 6f));
    }

    internal IEnumerator MoveTowardsWorldTargetByBudgetInternal(Unit unit, Vector3 targetWorldPos, float moveBudgetMeters)
    {
        if (unit == null) yield break;
        uint? phase = OsowiecOperation.Instance ? OsowiecFireTurn.Stamp(unit) : (uint?)null;
        if (!OwnsMove(unit, phase)) yield break;

        Vector3 startPosition = unit.transform.position;

        // Строим путь (NavMesh) и двигаемся по corner'ам, а не прямой линией — иначе юнит будет пытаться идти "сквозь стену" и утыкаться.
        if (!TryGetPathCornersByBudget(startPosition, targetWorldPos, moveBudgetMeters, out Vector3[] corners, out float travelledMeters))
        {
            if (debugBot)
            {
                Debug.LogWarning($"[BotNavMesh] NavMesh path не построен. unit={unit.name}, from={startPosition}, target={targetWorldPos}, budget={moveBudgetMeters:0.00}m. " +
                                 "Проверь, что юнит и цель стоят на NavMesh и NavMesh запечён.");
            }
            yield break;
        }

        if (travelledMeters <= 0.01f)
        {
            // Уже на месте / слишком близко: это не ошибка, просто нечего двигать.
            if (debugBot)
            {
                Debug.Log($"[BotNavMesh] Движение не требуется (travelledMeters≈0). unit={unit.name}, pos={startPosition}, target={targetWorldPos}");
            }
            yield break;
        }

        bool showBotTurn =
            GameManager.Instance != null && GameManager.Instance.GetGameMode() == GameMode.PlayerVsBot &&
            CameraManager.Instance != null && !CameraManager.Instance.IsActionMode() &&
            CameraManager.Instance.IsBotFollowCameraEnabled();

        if (showBotTurn && BotTurnTacticalOverlay.Instance != null)
        {
            var pts = BuildPointsTrimmedByDistance(corners, travelledMeters);
            if (pts != null && pts.Count >= 2)
                BotTurnTacticalOverlay.Instance.ShowMovePath(unit, pts, pts[pts.Count - 1]);
        }

        Vector3 firstLook = corners != null && corners.Length > 0 ? corners[Mathf.Min(1, corners.Length - 1)] : targetWorldPos;
        RotateTowardsTarget(unit, firstLook);
        yield return new WaitForSeconds(moveDelay);
        if (OwnsMove(unit, phase))
            yield return StartCoroutine(MoveUnitAlongCorners(unit, corners, travelledMeters, phase));

        if (showBotTurn && BotTurnTacticalOverlay.Instance != null)
            BotTurnTacticalOverlay.Instance.Clear(unit);
    }

    internal IEnumerator PerformAttackInternal(Unit attacker, Unit target)
    {
        bool showBotTurn =
            GameManager.Instance != null && GameManager.Instance.GetGameMode() == GameMode.PlayerVsBot &&
            CameraManager.Instance != null && !CameraManager.Instance.IsActionMode() &&
            CameraManager.Instance.IsBotFollowCameraEnabled();

        if (showBotTurn && BotTurnTacticalOverlay.Instance != null && target != null)
        {
            BotTurnTacticalOverlay.Instance.Ping(target.transform.position);
        }

        yield return PerformAttack(attacker, target);
    }

    private static List<Vector3> BuildPointsTrimmedByDistance(Vector3[] corners, float maxDistance)
    {
        if (corners == null || corners.Length < 2) return null;
        float remaining = Mathf.Max(0f, maxDistance);

        List<Vector3> pts = new List<Vector3>(corners.Length);
        pts.Add(corners[0]);

        for (int i = 1; i < corners.Length && remaining > 0.001f; i++)
        {
            Vector3 a = pts[pts.Count - 1];
            Vector3 b = corners[i];
            Vector3 delta = b - a;
            delta.y = 0f;
            float seg = delta.magnitude;
            if (seg <= 0.001f) continue;

            if (seg <= remaining)
            {
                pts.Add(b);
                remaining -= seg;
            }
            else
            {
                Vector3 dir = delta / seg;
                Vector3 end = a + dir * remaining;
                end.y = b.y; // оставим высоту как у следующего corner
                pts.Add(end);
                remaining = 0f;
                break;
            }
        }

        return pts;
    }

    private bool TryGetPathCornersByBudget(Vector3 from, Vector3 target, float budgetMeters, out Vector3[] corners, out float travelledMeters)
    {
        corners = null;
        travelledMeters = 0f;
        if (budgetMeters <= 0f) return false;

        // Приводим старт/цель к ближайшим точкам на NavMesh.
        // В некоторых сценах pivot у юнитов/целей может быть высоко над полом (Y-offset).
        // Поэтому берём больший радиус снэпа, чтобы стабильно находить ближайшую точку на NavMesh.
        float sampleRadius = Mathf.Clamp(Mathf.Abs(from.y) + 2.0f, 2.0f, 12.0f);
        float targetSampleRadius = Mathf.Clamp(Mathf.Abs(target.y) + 2.0f, 2.0f, 12.0f);

        if (NavMesh.SamplePosition(from, out NavMeshHit fromHit, sampleRadius, NavMesh.AllAreas))
            from = OffsetFromNavMeshEdge(fromHit.position, 0.35f);
        if (NavMesh.SamplePosition(target, out NavMeshHit targetHit, targetSampleRadius, NavMesh.AllAreas))
            target = OffsetFromNavMeshEdge(targetHit.position, 0.35f);

        if (Vector3.Distance(from, target) <= 0.25f)
        {
            corners = new[] { from, target };
            travelledMeters = 0f;
            return true;
        }

        NavMeshPath path = new NavMeshPath();
        if (!NavMesh.CalculatePath(from, target, NavMesh.AllAreas, path) ||
            path.corners == null || path.corners.Length == 0)
        {
            return false;
        }

        if (path.corners.Length < 2)
        {
            corners = path.corners;
            travelledMeters = 0f;
            return true;
        }

        float totalLength = 0f;
        for (int i = 1; i < path.corners.Length; i++)
            totalLength += Vector3.Distance(path.corners[i - 1], path.corners[i]);

        if (totalLength <= 0.01f) return false;

        travelledMeters = Mathf.Min(totalLength, budgetMeters);
        corners = path.corners;
        return true;
    }

    private static Vector3 OffsetFromNavMeshEdge(Vector3 pos, float insetMeters)
    {
        float inset = Mathf.Clamp(insetMeters, 0f, 2f);
        if (inset <= 0f) return pos;
        if (NavMesh.FindClosestEdge(pos, out NavMeshHit hit, NavMesh.AllAreas))
        {
            // Move inside the navmesh, away from the closest edge.
            Vector3 n = hit.normal;
            n.y = 0f;
            if (n.sqrMagnitude > 0.0001f)
            {
                n.Normalize();
                return pos + n * inset;
            }
        }
        return pos;
    }

    private static bool OwnsMove(Unit unit, uint? phase)
    {
        if (!unit) return false;
        if (!phase.HasValue) return true;
        var game = GameManager.Instance;
        return unit.isActiveAndEnabled && unit.GetHealth() > 0 && game && !game.IsGameOver() &&
            game.currentPlayer == unit.owner && game.TurnSequence == phase.Value;
    }

    private IEnumerator MoveUnitAlongCorners(Unit unit, Vector3[] corners, float maxDistanceMeters, uint? phase)
    {
        if (unit == null) yield break;
        if (corners == null || corners.Length < 2) yield break;

        CharacterController controller = unit.GetComponent<CharacterController>();
        float remaining = Mathf.Max(0f, maxDistanceMeters);

        // Анимация и звук хода (юнит не isControlled, поэтому Unit.Update() их не крутит).
        float moveSpeed = Mathf.Max(0.1f, unit.MoveSpeed);
        Mission2.TankController tankCtrl = unit.GetComponent<Mission2.TankController>();
        unit.SetExternalMoveAnimation(true, moveSpeed);
        if (tankCtrl != null && tankCtrl.enabled)
            tankCtrl.SetBotPathMoveAudio(true, moveSpeed);

        // Начинаем с текущей позиции (не обязательно совпадает с corners[0]).
        Vector3 current = unit.transform.position;
        for (int i = 1; i < corners.Length && remaining > 0.001f; i++)
        {
            Vector3 target = corners[i];
            target.y = current.y;

            Vector3 to = target - current;
            to.y = 0f;
            float seg = to.magnitude;
            if (seg <= 0.01f)
            {
                current = target;
                continue;
            }

            float take = Mathf.Min(seg, remaining);
            Vector3 dir = to / seg;
            Vector3 end = current + dir * take;

            // Двигаемся к end небольшими шагами, чтобы CC корректно сталкивался со стенами.
            float speed = Mathf.Max(0.1f, unit.MoveSpeed);
            // Длительность строго по скорости юнита (без верхнего clamp — иначе длинные сегменты «ускоряются»).
            float duration = Mathf.Max(0.05f, take / speed);
            float elapsed = 0f;
            Vector3 start = unit.transform.position;
            RotateTowardsTarget(unit, end);

            while (elapsed < duration && OwnsMove(unit, phase))
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                Vector3 nextPos = Vector3.Lerp(start, end, t);
                Vector3 move = nextPos - unit.transform.position;
                move.y = 0f;

                if (controller != null && controller.enabled) controller.Move(move);
                else unit.transform.position = nextPos;

                unit.RefreshGridPositionFromWorld();
                yield return null;
            }

            if (!OwnsMove(unit, phase)) break;
            current = unit.transform.position;
            remaining -= take;
        }

        if (!unit) yield break;
        if (OwnsMove(unit, phase)) unit.ConsumeMoveMeters(maxDistanceMeters - Mathf.Max(0f, remaining));
        unit.RefreshGridPositionFromWorld();
        if (tankCtrl != null && tankCtrl.enabled)
            tankCtrl.SetBotPathMoveAudio(false, 0f);
        unit.SetExternalMoveAnimation(false);
    }

    /// <summary>
    /// Подходит ближе к врагу, если враг в радиусе обнаружения, но не в радиусе атаки
    /// </summary>
    private IEnumerator ApproachEnemyIfNeeded(Unit unit)
    {
        if (unit == null) yield break;

        // Ищем ближайшего врага
        Unit nearestEnemy = FindNearestEnemy(unit);
        if (nearestEnemy == null) yield break;

        float distanceToEnemy = GetDistance(unit, nearestEnemy);

        // Если враг в радиусе обнаружения, но не в радиусе атаки - подходим ближе
        // Также подходим, если враг очень близко к радиусу атаки (с небольшим запасом)
        if (distanceToEnemy > attackRange * 0.95f && distanceToEnemy <= enemyDetectionRange)
        {
            Vector3 directionToEnemy = (nearestEnemy.transform.position - unit.transform.position).normalized;
            directionToEnemy.y = 0;

            // Вычисляем целевую позицию - ближе к врагу, на оптимальном расстоянии для атаки
            float desiredDistance = attackRange * 0.85f; // 85% от радиуса атаки для оптимальной позиции
            Vector3 targetPosition = nearestEnemy.transform.position - directionToEnemy * desiredDistance;
            targetPosition.y = unit.transform.position.y; // Сохраняем высоту

            Vector3 currentPos = unit.transform.position;
            Vector3 approachVector = targetPosition - currentPos;
            float approachDistanceNeeded = approachVector.magnitude;

            // Вычисляем, сколько нужно пройти, чтобы попасть в радиус атаки
            float distanceToReachAttackRange = distanceToEnemy - attackRange * 0.9f;

            // Используем большее из двух: нужное расстояние или максимальное разрешенное
            float maxApproachDistance = Mathf.Max(approachDistance, distanceToReachAttackRange + 0.3f);

            // Ограничиваем максимальное расстояние приближения
            if (approachDistanceNeeded > maxApproachDistance)
            {
                approachVector = approachVector.normalized * maxApproachDistance;
                targetPosition = currentPos + approachVector;
            }

            // Проверяем, что не выходим за пределы разумного (не слишком далеко от клетки)
            if (ChessGrid.Instance != null)
            {
                Vector2Int currentGridPos = ChessGrid.Instance.WorldToGridCoords(currentPos);
                Vector3 cellCenter = ChessGrid.Instance.GridToWorldPosition(currentGridPos.x, currentGridPos.y);
                float distanceFromCell = Vector3.Distance(targetPosition, cellCenter);

                // Увеличиваем максимальное расстояние от центра клетки, чтобы бот мог подойти ближе
                float maxDistanceFromCell = 2.5f; // Увеличено для возможности подойти ближе к врагу

                // Если нужно подойти ближе к врагу, разрешаем выйти дальше от клетки
                if (distanceToEnemy > attackRange * 1.1f)
                {
                    maxDistanceFromCell = 3.0f; // Еще больше, если враг далеко
                }

                if (distanceFromCell > maxDistanceFromCell)
                {
                    Vector3 directionFromCell = (targetPosition - cellCenter).normalized;
                    targetPosition = cellCenter + directionFromCell * maxDistanceFromCell;
                }
            }

            // Подходим к врагу
            float actualApproachDistance = Vector3.Distance(currentPos, targetPosition);
            if (actualApproachDistance > 0.1f)
            {
                CharacterController controller = unit.GetComponent<CharacterController>();
                if (controller != null && controller.enabled)
                {
                    float approachDuration = actualApproachDistance / unit.MoveSpeed;
                    approachDuration = Mathf.Clamp(approachDuration, 0.1f, 0.5f);

                    float elapsed = 0f;
                    Vector3 startPos = unit.transform.position;

                    // Разворачиваемся к цели
                    RotateTowardsTarget(unit, targetPosition);

                    while (elapsed < approachDuration)
                    {
                        elapsed += Time.deltaTime;
                        float t = elapsed / approachDuration;
                        Vector3 newPos = Vector3.Lerp(startPos, targetPosition, t);
                        Vector3 moveVector = newPos - unit.transform.position;
                        moveVector.y = 0;

                        controller.Move(moveVector);
                        yield return null;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Разворот юнита к цели
    /// </summary>
    private void RotateTowardsTarget(Unit unit, Vector3 targetPosition)
    {
        if (unit == null) return;

        Vector3 direction = (targetPosition - unit.transform.position).normalized;
        direction.y = 0;

        if (direction != Vector3.zero)
        {
            unit.transform.rotation = Quaternion.LookRotation(direction);
        }
    }

    /// <summary>
    /// PvBot: танк игрока не выбирается целью, кроме бойца с противотанковым оружием.
    /// </summary>
    private static bool BotMayTargetUnitAsEnemy(Unit attacker, Unit potentialEnemy)
    {
        if (potentialEnemy == null || !potentialEnemy.IsTankUnit) return true;
        if (potentialEnemy.owner != Player.Player1) return true;
        if (attacker == null || attacker.owner != Player.Player2) return true;
        return attacker.chessType == ChessUnitType.Гончая;
    }

    /// <summary>
    /// Выполнение атаки
    /// </summary>
    private IEnumerator PerformAttack(Unit attacker, Unit target)
    {
        if (attacker == null || target == null)
        {
            yield break;
        }

        if (attacker.owner == Player.Player2 && target.owner == Player.Player1 && target.IsTankUnit &&
            attacker.chessType != ChessUnitType.Гончая)
            yield break;

        // Если у атакующего есть экипированное оружие — атакуем огнестрелом (hitscan) без рывка/WeaponCollider.
        Weapon ranged = attacker.GetEquippedWeapon();
        if (ranged != null && ranged.Config != null)
        {
            var operation = OsowiecOperation.Instance;
            if (!CanSeeTarget(attacker, target) && (!operation || operation.CloudContact(attacker) != target))
                yield break;

            uint shotTurn = OsowiecFireTurn.Stamp(attacker);
            if (!OsowiecFireTurn.Allows(attacker)) yield break;
            if (operation) operation.BeginAim(attacker, target, false);
            else RotateTowardsTarget(attacker, target.transform.position);
            yield return new WaitForSeconds(operation ? OsowiecOperation.AimSeconds : attackDelay);
            if (operation)
            {
                if (operation.ThreatSource(target) != attacker) yield break;
                if (!operation.ReleaseAim(attacker, target)) { operation.ClearAim(); yield break; }
                operation.ClearAim();
            }
            else
            {
                if (!OsowiecFireTurn.StillActive(attacker, shotTurn) || !target || target.GetHealth() <= 0) yield break;
                attacker.Attack(target);
            }
            if (debugBot)
            {
                Weapon w = attacker.GetEquippedWeapon();
                Debug.Log($"[BotController] RangedFire attacker={attacker.name} target={target.name} ammo={w?.AmmoInMag}/{w?.AmmoReserve}");
            }
            yield return new WaitForSeconds(0.55f);
            yield break;
        }

        // Мили-урон в проекте часто завязан на WeaponCollider + IsAttacking() (анимации).
        // Для бота делаем безопасный фоллбек: если после Attack() HP цели не изменился,
        // наносим урон напрямую (без зависимости от анимаций), чтобы бот не "махал впустую".
        bool hasWeaponCollider = attacker.GetComponentInChildren<WeaponCollider>(true) != null;

        float distance = GetDistance(attacker, target);

        // Если враг немного дальше радиуса атаки, но в радиусе обнаружения - подходим ближе
        if (distance > attackRange && distance <= enemyDetectionRange)
        {
            yield return StartCoroutine(ApproachEnemyIfNeeded(attacker));
            distance = GetDistance(attacker, target); // Обновляем расстояние после приближения
        }

        // Проверяем расстояние после попытки приближения
        if (distance > attackRange * 1.1f) // Небольшой запас для погрешности
        {
            yield break;
        }

        // Разворот к врагу
        RotateTowardsTarget(attacker, target.transform.position);
        yield return new WaitForSeconds(attackDelay);

        // Рывок к врагу
        Vector3 direction = (target.transform.position - attacker.transform.position).normalized;
        float lungeDistance = Mathf.Min(0.5f, distance - 1.0f);

        if (lungeDistance > 0.1f)
        {
            Vector3 lungeTarget = attacker.transform.position + direction * lungeDistance;
            CharacterController controller = attacker.GetComponent<CharacterController>();

            float elapsed = 0f;
            float lungeTime = 0.2f;
            Vector3 startPos = attacker.transform.position;

            while (elapsed < lungeTime)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / lungeTime;

                Vector3 newPos = Vector3.Lerp(startPos, lungeTarget, t);
                if (controller != null && controller.enabled)
                {
                    controller.Move(newPos - attacker.transform.position);
                }
                else
                {
                    attacker.transform.position = newPos;
                }

                yield return null;
            }
        }

        // АТАКА!
        // Если цель - юнит игрока, пытаемся запустить QTE
        bool qteWasTriggered = false;
        if (target.owner == Player.Player1 && QTESystem.Instance != null)
        {
            // Пытаемся запустить QTE (с шансом)
            // QTE система сама запустит анимацию атаки, остановит время и заморозит её
            qteWasTriggered = QTESystem.Instance.StartQTE(target, attacker);

            if (qteWasTriggered)
            {
                // QTE запущен - ждем его завершения
                // Время остановлено, анимация атаки застыла на начальных кадрах
                // Ждем, пока QTE не завершится
                while (QTESystem.Instance.IsQTEActive())
                {
                    yield return null;
                }

                // После завершения QTE проверяем, была ли атака заблокирована
                // Если атака была заблокирована, анимация уже отменена в QTESystem
                // Если атака не была заблокирована, анимация продолжается и урон будет нанесен через WeaponCollider
                yield return new WaitForSeconds(0.5f); // Небольшая задержка для завершения анимации (если она не была отменена)
            }
            else
            {
                // QTE не запустился - атакуем как обычно
                int hpBefore = target.GetHealth();
                attacker.Attack();

                // Даем шанс коллайдеру/анимации нанести урон
                yield return new WaitForSeconds(0.35f);

                int hpAfter = target.GetHealth();
                bool colliderProbablyHit = hpAfter < hpBefore;
                if (!colliderProbablyHit)
                {
                    if (GetDistance(attacker, target) <= attackRange * 1.15f)
                    {
                        int finalDamage = Mathf.RoundToInt(attacker.Damage * attacker.GetDamageMultiplier());
                        target.TakeDamage(finalDamage, attacker);
                        if (debugBot) Debug.Log($"[BotController] MeleeFallbackDamage attacker={attacker.name} target={target.name} dmg={finalDamage} (hasCollider={hasWeaponCollider})");
                    }
                }

                yield return new WaitForSeconds(0.15f);
            }
        }
        else
        {
            // Цель не юнит игрока или QTE система недоступна - атакуем как обычно
            int hpBefore = target.GetHealth();
            attacker.Attack();

            // Даем шанс коллайдеру/анимации нанести урон
            yield return new WaitForSeconds(0.35f);

            int hpAfter = target.GetHealth();
            bool colliderProbablyHit = hpAfter < hpBefore;
            if (!colliderProbablyHit)
            {
                if (GetDistance(attacker, target) <= attackRange * 1.15f)
                {
                    int finalDamage = Mathf.RoundToInt(attacker.Damage * attacker.GetDamageMultiplier());
                    target.TakeDamage(finalDamage, attacker);
                    if (debugBot) Debug.Log($"[BotController] MeleeFallbackDamage attacker={attacker.name} target={target.name} dmg={finalDamage} (hasCollider={hasWeaponCollider})");
                }
            }

            yield return new WaitForSeconds(0.15f);
        }
    }

    private float GetDistance(Unit a, Unit b)
    {
        if (a == null || b == null) return float.MaxValue;
        return Vector3.Distance(a.transform.position, b.transform.position);
    }

    private Unit FindNearestEnemy(Unit unit)
    {
        if (unit == null) return null;

        Unit[] allUnits = FindObjectsByType<Unit>(FindObjectsInactive.Exclude);
        var enemies = allUnits
            .Where(u => u != null && u.owner != unit.owner && u.GetHealth() > 0)
            .Where(u => BotMayTargetUnitAsEnemy(unit, u))
            .Select(u => new { Unit = u, Distance = GetDistance(unit, u) })
            .ToList();

        if (enemies.Count == 0) return null;

        // Находим минимальное расстояние
        float minDistance = enemies.Min(e => e.Distance);

        // Если несколько врагов на одинаковом расстоянии, выбираем лучшего по приоритету
        var enemiesAtSameDistance = enemies
            .Where(e => Mathf.Abs(e.Distance - minDistance) < 0.1f) // Учитываем погрешность
            .Select(e => {
                float priority = GetPieceValue(e.Unit.chessType) * 10f;

                // Приоритет королю
                if (e.Unit.chessType == ChessUnitType.King)
                {
                    priority += 500f;
                }

                // Приоритет гарантированному убийству
                int damage = Mathf.RoundToInt(unit.Damage * unit.GetDamageMultiplier());
                if (e.Unit.GetHealth() <= damage)
                {
                    priority += 200f;
                }

                // Приоритет низкому HP
                priority += (100f - e.Unit.GetHealth()) * 0.5f;

                return new { e.Unit, e.Distance, Priority = priority };
            })
            .OrderByDescending(e => e.Priority)
            .ToList();

        // Возвращаем лучшего по приоритету из врагов на одинаковом расстоянии
        if (enemiesAtSameDistance.Count > 0)
        {
            return enemiesAtSameDistance[0].Unit;
        }

        // Если нет врагов на одинаковом расстоянии, возвращаем ближайшего
        return enemies
            .OrderBy(e => e.Distance)
            .FirstOrDefault()?.Unit;
    }

    private int GetPieceValue(ChessUnitType type)
    {
        return PieceValues.ContainsKey(type) ? PieceValues[type] : 1;
    }
}
