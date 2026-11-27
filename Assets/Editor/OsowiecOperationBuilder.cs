using System;
using System.IO;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

public static class OsowiecOperationBuilder
{
    const string Mission = "Assets/Osowiec/Osowiec.unity";
    const string Headquarters = "Assets/Osowiec/OsowiecHQ.unity";

    [MenuItem("Steel and Ashes/Build Osowiec Operation")]
    public static void Generate()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/VerdunGraybox.unity", OpenSceneMode.Single);
        Directory.CreateDirectory("Assets/Osowiec");
        foreach (var root in scene.GetRootGameObjects())
            if (root.name is "Verdun Graybox" or "Player1Units" or "Player2Units")
                root.SetActive(false);
        foreach (var legacyUnit in UnityEngine.Object.FindObjectsByType<Unit>(FindObjectsInactive.Exclude))
            legacyUnit.gameObject.SetActive(false);
        foreach (var script in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (script is Mission2BotBrain)
                UnityEngine.Object.DestroyImmediate(script);
            else if (script is Mission2.DestructibleObjective or Mission2.Mission2DefenseTerminal)
                script.gameObject.SetActive(false);
            else if (script != null && script.GetType().Namespace == "Mission2")
                script.enabled = false;
        }

        var grid = Required<ChessGrid>();
        grid.width = 106;
        grid.height = 64;
        grid.cellSize = 1;
        Set(grid, so => so.FindProperty("originPosition").vector3Value = new Vector3(-53, 0, -32));
        OsowiecFieldCast.Prepare();
        var infantry = ArmedUnit("Pawn", "Молот", OsowiecFieldCast.BuildWeapon("RamCarbine"));
        var rifleman = ArmedUnit("Bishop", "Циклоп", OsowiecFieldCast.BuildWeapon("ObserverCarbine"));
        var support = ArmedUnit("GuardianRig", "Гончая", OsowiecFieldCast.BuildWeapon("RunnerCarbine"));
        var game = Required<GameManager>();
        Set(game, so =>
        {
            so.FindProperty("testGameMode").enumValueIndex = (int)GameMode.PlayerVsBot;
            so.FindProperty("useTurnDice").boolValue = false;
            so.FindProperty("allowUnitAbilities").boolValue = false;
            so.FindProperty("fixedTurnMoveMeters").floatValue = 14;
        });
        var deployment = Required<ArmyDeploymentController>();
        deployment.gameObject.AddComponent<OsowiecDeploymentSectors>();
        Set(deployment, so =>
        {
            so.FindProperty("armyPointBudget").intValue = 12;
            var west=OsowiecDeploymentSectors.Cells[0];
            so.FindProperty("deploymentZoneMin").vector2IntValue = west.Aggregate(Vector2Int.Min);
            so.FindProperty("deploymentZoneMax").vector2IntValue = west.Aggregate(Vector2Int.Max);
            so.FindProperty("requireExactlyOneKing").boolValue = false;
            so.FindProperty("requireDeployedInfantry").boolValue = false;
            so.FindProperty("deploymentSpawnYawDegrees").floatValue = 90;
            so.FindProperty("deploymentZoneColor").colorValue = new Color(.74f, .65f, .38f, .48f);
            var offers = so.FindProperty("offers");
            offers.arraySize = 3;
            Offer(offers.GetArrayElementAtIndex(0), infantry, 5);
            Offer(offers.GetArrayElementAtIndex(1), rifleman, 4);
            Offer(offers.GetArrayElementAtIndex(2), support, 3);
        });
        OsowiecDeploymentStyle.Apply(deployment);
        var cameras = Required<CameraManager>();
        cameras.GetTacticalCamera().transform.SetPositionAndRotation(
            new Vector3(-43, 26, -14), Quaternion.Euler(90, 0, 0));
        Set(cameras, so =>
        {
            so.FindProperty("maxCameraHeight").floatValue = 110;
            so.FindProperty("player1TacticalPosition").vector3Value = new Vector3(-4, 80, -16);
            so.FindProperty("player2TacticalPosition").vector3Value = new Vector3(-4, 80, -16);
            so.FindProperty("player1TacticalRotation").quaternionValue = Quaternion.Euler(72, 0, 0);
            so.FindProperty("player2TacticalRotation").quaternionValue = Quaternion.Euler(72, 0, 0);
            so.FindProperty("minArenaX").floatValue = -53;
            so.FindProperty("maxArenaX").floatValue = 53;
            so.FindProperty("minArenaZ").floatValue = -32;
            so.FindProperty("maxArenaZ").floatValue = 32;
            so.FindProperty("deploymentCameraHeight").floatValue = 26;
            so.FindProperty("postDeploymentCameraHeight").floatValue = 70;
            so.FindProperty("postDeploymentPitchDegrees").floatValue = 78;
            so.FindProperty("enableEdgeScroll").boolValue = false;
            so.FindProperty("centerTacticalCameraOnLastPlayedUnit").boolValue = false;
        });
        var bot = Required<BotController>();
        bot.gameObject.AddComponent<OsowiecBotBrain>();

        foreach (var oldSurface in UnityEngine.Object.FindObjectsByType<NavMeshSurface>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            oldSurface.enabled = false;
        foreach (var lamp in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (lamp.type == LightType.Directional) lamp.enabled = false;
        OsowiecEnvironment.Create();
        OsowiecSignalLinesAndRuin.Install();
        OsowiecCentralTrench.Install();
        OsowiecApproachClosures.Install();
        OsowiecAtmosphere.Create();
        OsowiecAtmosphere.SetGasBounds(OsowiecOperation.Gas);
        var operation = new GameObject("Osowiec operation");
        var rules = operation.AddComponent<OsowiecOperation>();
        rules.relief = operation.AddComponent<OsowiecReliefColumn>();
        operation.AddComponent<OsowiecWetTracks>();
        rules.relief.cartPrefab = OsowiecFieldCast.MercyCartPrefab();
        OsowiecWardBuilder.Install(rules.relief);
        operation.AddComponent<OsowiecFieldKit>();
        const string particlesPath = "Assets/Osowiec/Atmosphere/Contact haze.mat";
        var particles = AssetDatabase.LoadAssetAtPath<Material>(particlesPath);
        if (!particles)
        {
            particles = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            AssetDatabase.CreateAsset(particles,particlesPath);
        }
        particles.SetFloat("_Surface",1);
        particles.SetFloat("_SrcBlend",(float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        particles.SetFloat("_DstBlend",(float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        particles.SetFloat("_ZWrite",0);
        particles.SetOverrideTag("RenderType","Transparent");
        particles.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        particles.renderQueue=3000;
        EditorUtility.SetDirty(particles);
        operation.AddComponent<OsowiecShotEffects>().softParticles=particles;
        var view = operation.AddComponent<OsowiecWeaponView>();
        view.model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Osowiec/Art/Cast/Game/RamCarbineView.prefab");
        view.hands = OsowiecFieldCast.ViewPrefab("FieldHands");
        view.reloadClip = OsowiecReloadClip.Build();
        var planner = operation.AddComponent<OsowiecPlanner>();
        planner.survey = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Osowiec/Art/SurveySheet.png");
        planner.portraits = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/OsowiecJournal/portraits.png");
        rules.gate = GameObject.Find("Osowiec Door Hinge")?.transform;
        var surface = operation.AddComponent<NavMeshSurface>();
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.collectObjects = CollectObjects.Volume;
        surface.center = new Vector3(0, 1, 0);
        surface.size = new Vector3(106, 14, 64);
        surface.BuildNavMesh();
        if (surface.navMeshData == null)
            throw new InvalidOperationException("Osowiec navigation bake produced no data.");
        var navPath = "Assets/Osowiec/OsowiecNavMesh.asset";
        var oldNav = AssetDatabase.LoadAssetAtPath<NavMeshData>(navPath);
        if (oldNav == null)
            AssetDatabase.CreateAsset(surface.navMeshData, navPath);
        else
        {
            EditorUtility.CopySerialized(surface.navMeshData, oldNav);
            surface.navMeshData = oldNav;
        }
        VerifyPaths();
        OsowiecCentralTrench.VerifyRoutes();
        OsowiecBatteryFort.VerifyPaths();
        if (rules.gate != null)
        {
            var doorCollider = rules.gate.gameObject.AddComponent<BoxCollider>();
            doorCollider.center = new Vector3(0, 1.17f, 1.40f);
            doorCollider.size = new Vector3(.17f, 2.34f, 2.80f);
        }
        Physics.SyncTransforms();
        Spawn("Дозорный", rifleman, new Vector3(1, 0, 14), grid);
        Spawn("Защитник входа", infantry, new Vector3(18, 0, -10), grid);
        Spawn("Расчёт батареи", support, new Vector3(31, 0, 4), grid);
        Spawn("Защитник двора", rifleman, new Vector3(37, 0, -3), grid);
        Spawn("Смотритель ворот", infantry, new Vector3(25, 0, -3), grid);
        Spawn("Сменный дозор", rifleman, new Vector3(12, 0, 10), grid);
        rules.relief.reserve = new[] {
            Spawn("Дозор у низины", rifleman, new Vector3(12, 0, 14), grid),
            Spawn("Часовой галереи", infantry, new Vector3(16, 0, -12), grid),
            Spawn("Дозор у бастиона", rifleman, OsowiecBatteryFort.GalleryPost, grid)
        };
        foreach (var reserve in rules.relief.reserve) reserve.SetActive(false);
        OsowiecBatteryFort.PlaceObserver();
        rules.relief.alarmGuard = new[] {
            Spawn("Караул каземата", infantry, new Vector3(22, 0, -11), grid),
            Spawn("Связной караульни", rifleman, new Vector3(36, 0, 5), grid)
        };
        foreach (var guard in rules.relief.alarmGuard) guard.SetActive(false);

        var quest = Required<MissionQuestUI>();
        Set(quest, so => {
            so.FindProperty("config").objectReferenceValue = OsowiecHeadquartersBuilder.GetOrCreateObjectives();
            so.FindProperty("visibleObjectives").intValue = 1;
        });
        OsowiecHeadquartersBuilder.InstallPauseMenu(game, false);
        OsowiecMapIcons.Apply();
        OsowiecHudStyle.Apply();
        OsowiecResultStyle.Apply(game, rules);
        foreach (var button in UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            for (int index = button.onClick.GetPersistentEventCount() - 1; index >= 0; index--)
            {
                if (button.onClick.GetPersistentMethodName(index) != nameof(GameManager.LoadMainMenu)) continue;
                UnityEventTools.RemovePersistentListener(button.onClick, index);
                UnityEventTools.AddPersistentListener(button.onClick, rules.ReturnToHeadquarters);
            }
        }
        EditorSceneManager.SaveScene(scene, Mission, true);
        OsowiecHeadquartersBuilder.Generate();
        var scenes = EditorBuildSettings.scenes.Where(item => item.path != Mission && item.path != Headquarters);
        EditorBuildSettings.scenes = new[] {
            new EditorBuildSettingsScene(Headquarters, true),
            new EditorBuildSettingsScene(Mission, true)
        }.Concat(scenes).ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log("Osowiec operation and headquarters scenes generated.");
    }

    public static void BuildWindows()
        => BuildWindows("../Osowiec-Release/Osowiec.exe");

    public static void BuildWindows(string destination)
    {
        var path = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string sourceProductName = PlayerSettings.productName;
        try
        {
            PlayerSettings.productName = "Steel & Ashes - Iron Protocol";
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { Headquarters, Mission },
                target = BuildTarget.StandaloneWindows64,
                locationPathName = path
            });
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new InvalidOperationException("Osowiec Windows build did not succeed: " + report.summary.result);
            File.WriteAllLines(Path.Combine(Path.GetDirectoryName(path), "Build.txt"), new[] {
                "Steel & Ashes: Iron Protocol / Osowiec", "Windows x64 / Unity " + Application.unityVersion,
                "Build ID " + report.summary.guid, "UTC " + DateTime.UtcNow.ToString("O") });
        }
        finally
        {
            PlayerSettings.productName = sourceProductName;
        }
    }

    static T Required<T>() where T : Component =>
        UnityEngine.Object.FindFirstObjectByType<T>(FindObjectsInactive.Include)
        ?? throw new InvalidOperationException("Verdun scene missing " + typeof(T).Name);

    static void VerifyPaths()
    {
        var points = new[]
        {
            new Vector3(-45.5f, 0, -15.5f),
            new Vector3(0, 0, -12),
            new Vector3(0, 0, 14),
            OsowiecOperation.Battery
        };
        for (int route = 1; route <= 2; route++)
        {
            bool first = PathBetween(points[0], points[route], out float before);
            bool last = PathBetween(points[route], points[3], out float after);
            if (first && last)
                Debug.Log($"Osowiec NavMesh route {route}: {before + after:F1} m via waypoint {points[route]}.");
            else
                Debug.LogWarning($"Osowiec NavMesh route {route} needs review at {points[route]}.");
        }
    }

    static bool PathBetween(Vector3 from, Vector3 to, out float distance)
    {
        distance = 0;
        var result = new NavMeshPath();
        if (!NavMesh.SamplePosition(from, out var start, 1.6f, NavMesh.AllAreas) ||
            !NavMesh.SamplePosition(to, out var end, 1.6f, NavMesh.AllAreas) ||
            !NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, result) ||
            result.status != NavMeshPathStatus.PathComplete) return false;
        for (int index = 1; index < result.corners.Length; index++)
            distance += Vector3.Distance(result.corners[index - 1], result.corners[index]);
        return true;
    }

    static void Set(UnityEngine.Object target, Action<SerializedObject> update)
    {
        var data = new SerializedObject(target);
        update(data);
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    static void Offer(SerializedProperty offer, string prefab, int cost)
    {
        offer.FindPropertyRelative("unitPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(prefab);
        offer.FindPropertyRelative("costPoints").intValue = cost;
        offer.FindPropertyRelative("maxCopies").intValue = 2;
    }

    static string ArmedUnit(string baseName, string displayName, Weapon rifle) =>
        OsowiecFieldCast.BuildUnit(baseName, displayName, rifle);

    static Vector3 GroundSentryPost(Vector3 survey, string name)
    {
        // The authored height locates the intended storey. The nearest NavMesh floor
        // keeps guards in the gallery while allowing outdoor posts to follow slopes.
        if (!NavMesh.SamplePosition(survey, out var nav, 3.5f, NavMesh.AllAreas))
            throw new InvalidOperationException($"Sentry post {name} is outside navigation at {survey}.");
        var point = nav.position;
        if ((new Vector2(point.x-survey.x, point.z-survey.z)).sqrMagnitude > .8f*.8f)
            throw new InvalidOperationException($"Sentry post {name} projects away from {survey} to {point}.");

        var hits = Physics.RaycastAll(point+Vector3.up*.55f, Vector3.down, 1.2f, ~0, QueryTriggerInteraction.Ignore);
        var support = hits.Where(h => h.normal.y > .55f && !h.collider.GetComponentInParent<Unit>())
            .OrderBy(h => Mathf.Abs(h.point.y-point.y)).FirstOrDefault();
        if (!support.collider)
            throw new InvalidOperationException($"Sentry post {name} has no supporting floor at {point}.");
        return support.point + Vector3.up*.035f;
    }

    static GameObject Spawn(string name, string path, Vector3 position, ChessGrid grid)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OsowiecFieldCast.EnemyPath(path));
        if (prefab == null) throw new InvalidOperationException("Missing unit prefab " + path);
        position = GroundSentryPost(position, name);
        var actor = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        actor.name = name;
        actor.transform.SetPositionAndRotation(position, Quaternion.Euler(0, -90, 0));
        var unit = actor.GetComponent<Unit>();
        unit.owner = Player.Player2;
        unit.currentGridPosition = grid.WorldToGridCoords(position);
        Set(unit, so =>
        {
            so.FindProperty("snapToGridOnStart").boolValue = false;
            so.FindProperty("unitDisplayNameOverride").stringValue = name;
        });
        return actor;
    }
}
