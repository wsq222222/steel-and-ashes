using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

public sealed class OsowiecCastImport : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(OsowiecFieldCast.Folder, StringComparison.Ordinal)) return;
        var importer = (TextureImporter)assetImporter;
        if (assetPath.EndsWith("_N.png")) importer.textureType = TextureImporterType.NormalMap;
        if (assetPath.EndsWith("_MS.png")) importer.sRGBTexture = false;
        if (assetPath.EndsWith("Portrait.png"))
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
        }
    }

    void OnPreprocessModel()
    {
        if (!assetPath.StartsWith(OsowiecFieldCast.Folder, StringComparison.Ordinal)) return;
        var importer = (ModelImporter)assetImporter;
        string modelName = Path.GetFileNameWithoutExtension(assetPath);
        bool body = OsowiecFieldCast.IsUniform(modelName) || modelName == "StaffCoat";
        if (modelName=="StaffCoat") importer.isReadable=true;
        importer.animationType = body ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
        importer.importAnimation = body;
        importer.importCameras = false;
        importer.importLights = false;
        importer.optimizeGameObjects = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        if (!body) return;
        var clips = importer.defaultClipAnimations;
        foreach (var clip in clips)
        {
            clip.loopTime = OsowiecFieldCast.IsLoop(clip.name);
            clip.lockRootRotation = true;
            clip.lockRootHeightY = true;
            clip.lockRootPositionXZ = true;
        }
        if (clips.Length > 0) importer.clipAnimations = clips;
    }
}

public static class OsowiecFieldCast
{
    public const string Folder = "Assets/Osowiec/Art/Cast/";
    const string Generated = Folder + "Game/";
    static readonly Dictionary<string, Material> materials = new();
    static readonly Dictionary<string, RuntimeAnimatorController> controllers = new();
    static readonly string[] roles = { "Assault", "Scout", "Support", "Sentry" };
    static readonly string[] actions = { "Idle", "Walk", "Run", "Aim", "Fire", "Reload", "Hit", "Fall", "Mask" };
    public static bool IsUniform(string model) => Array.IndexOf(roles, model) >= 0;
    public static bool IsLoop(string clip) => clip.EndsWith("Idle") || clip.EndsWith("Walk") || clip.EndsWith("Run");

    public static T Art<T>(string file) where T : Object
    {
        var path = Folder + file;
        return AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException("Missing field cast asset " + path);
    }

    public static void Prepare()
    {
        materials.Clear();
        controllers.Clear();
        OsowiecProjectPaths.EnsureFolder(OsowiecProjectPaths.PrefabFolder);
        if (!AssetDatabase.IsValidFolder(Generated.TrimEnd('/')))
            AssetDatabase.CreateFolder(Folder.TrimEnd('/'), "Game");
        foreach (string role in roles)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(Folder + role + ".fbx");
            if (importer == null) throw new InvalidOperationException("Missing Blender export " + role);
            if (importer.animationType != ModelImporterAnimationType.Generic ||
                importer.materialImportMode != ModelImporterMaterialImportMode.ImportStandard ||
                !importer.clipAnimations.Any(c => c.name.EndsWith("Run") && c.loopTime))
            {
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                var clips = importer.defaultClipAnimations;
                foreach (var clip in clips)
                {
                    clip.loopTime = IsLoop(clip.name);
                    clip.lockRootRotation = true;
                    clip.lockRootHeightY = true;
                    clip.lockRootPositionXZ = true;
                }
                if (clips.Length != 0) importer.clipAnimations = clips;
                importer.SaveAndReimport();
            }
        }
    }

    static Material Surface(Material imported)
    {
        if (imported == null) throw new InvalidOperationException("Unassigned Blender surface");
        string key = imported.name.Split('.')[0].Replace(" (Instance)", "").Trim();
        if (materials.TryGetValue(key, out var existing)) return existing;
        bool uncoveredFace = key is "FaceAssault" or "FaceScout";
        var diffuse = Art<Texture2D>("Textures/" + key + (uncoveredFace ? "Uncovered.png" : ".png"));
        string path = Generated + key + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Lit is unavailable for " + key);
            material = new Material(shader) {name = key};
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", Color.white);
        material.SetTexture("_BaseMap", diffuse);
        if (uncoveredFace)
        {
            // The extra eighth of texture height is the bare forehead above the old photo crop.
            var coverage = new Vector2(1f, 8f / 9f);
            material.SetTextureScale("_BaseMap", coverage);
            material.SetTexture("_MainTex", diffuse);
            material.SetTextureScale("_MainTex", coverage);
        }
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "Textures/" + key + "_N.png");
        var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "Textures/" + key + "_MS.png");
        if (normal != null)
        {
            if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(normal)) is TextureImporter ni &&
                ni.textureType != TextureImporterType.NormalMap) ni.SaveAndReimport();
            material.SetTexture("_BumpMap", normal); material.SetFloat("_BumpScale", key == "WorkshopFinish" ? .78f : .55f);
            material.EnableKeyword("_NORMALMAP");
        }
        if (mask != null)
        {
            if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(mask)) is TextureImporter mi && mi.sRGBTexture) mi.SaveAndReimport();
            material.SetTexture("_MetallicGlossMap", mask);
            material.SetFloat("_Smoothness", 1);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
        }
        else material.SetFloat("_Smoothness", .28f);
        if (key == "AmberPane")
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", new Color(1.24f, .48f, .075f));
        }
        EditorUtility.SetDirty(material);
        materials[key] = material;
        return material;
    }

    internal static GameObject Visual(string name, Transform parent)
    {
        var asset = Art<GameObject>(name + ".fbx");
        var model = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        model.transform.SetParent(parent, false);
        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            renderer.sharedMaterials = renderer.sharedMaterials.Select(Surface).ToArray();
        return model;
    }

    static Transform Marker(Transform root, string name) => root.GetComponentsInChildren<Transform>(true)
        .FirstOrDefault(t => t.name == name) ?? throw new InvalidOperationException(root.name + " missing " + name);

    internal static RuntimeAnimatorController Controller(string role)
    {
        if (controllers.TryGetValue(role, out var old)) return old;
        string path = Generated + role + ".controller";
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        if (controller.layers.Length == 0) controller.AddLayer("Base Layer");
        var machine = controller.layers[0].stateMachine;
        var clips = AssetDatabase.LoadAllAssetsAtPath(Folder + role + ".fbx").OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
        if (!controller.parameters.Any(p => p.name == "Die"))
            controller.AddParameter("Die", AnimatorControllerParameterType.Trigger);
        foreach (string action in actions)
        {
            var clip = clips.FirstOrDefault(c => c.name.Equals(action, StringComparison.OrdinalIgnoreCase) ||
                c.name.EndsWith("|" + action, StringComparison.OrdinalIgnoreCase));
            if (clip == null)
                throw new InvalidOperationException(role + " missing " + action + " in " + string.Join(", ", clips.Select(c => c.name)));
            var state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == action) ?? machine.AddState(action);
            state.motion = clip;
            if (action == "Idle") machine.defaultState = state;
            if (action == "Fall" && !machine.anyStateTransitions.Any(t => t.destinationState == state))
            {
                var transition = machine.AddAnyStateTransition(state);
                transition.AddCondition(AnimatorConditionMode.If, 0, "Die"); transition.duration = .06f;
                transition.hasExitTime = false; transition.canTransitionToSelf = false;
            }
        }
        EditorUtility.SetDirty(controller);
        controllers[role] = controller;
        return controller;
    }

    static void Assign(SerializedObject data, string field, Object value) =>
        data.FindProperty(field).objectReferenceValue = value;

    static RangedWeaponConfig IssuedCarbineConfig(RangedWeaponConfig original, string modelName)
    {
        string path=Generated+modelName+".asset";
        var config=AssetDatabase.LoadAssetAtPath<RangedWeaponConfig>(path);
        if (!config)
        {
            config=ScriptableObject.CreateInstance<RangedWeaponConfig>();
            AssetDatabase.CreateAsset(config,path);
        }
        EditorUtility.CopySerialized(original,config);
        config.name=modelName;
        config.isMarksmanRifle=modelName=="ObserverCarbine";
        config.firstPersonPrefab=ViewPrefab(modelName);
        config.magazineSize = 5;
        config.startingAmmoInMag = config.magazineSize;
        config.startingReserveAmmo = 4 * config.magazineSize;
        config.damagePerHit = modelName == "RamCarbine" ? 51 : modelName == "ObserverCarbine" ? 57 : 46;
        EditorUtility.SetDirty(config);
        return config;
    }

    public static Weapon BuildWeapon(string modelName)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Rifle.prefab");
        var root = new GameObject(modelName);
        try
        {
            var weapon = root.AddComponent<Weapon>();
            EditorUtility.CopySerialized(source.GetComponent<Weapon>(), weapon);
            var model = Visual(modelName, root.transform);
            var near = Marker(model.transform, "Field_carbine").GetComponent<Renderer>();
            var distance = Marker(model.transform, "Distant_carbine").GetComponent<Renderer>();
            var lod = model.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(.09f, new[] { near }), new LOD(.003f, new[] { distance }) });
            lod.RecalculateBounds();
            var muzzle = Marker(model.transform, "FieldRifleMuzzle");
            var grip = Marker(model.transform, "FieldRifleGrip");
            model.transform.rotation = Quaternion.FromToRotation(
                Vector3.ProjectOnPlane(muzzle.position - grip.position, Vector3.up), Vector3.forward) * model.transform.rotation;
            model.transform.position -= grip.position;
            var firePoint = new GameObject("Camera aligned muzzle").transform;
            firePoint.SetParent(root.transform, false); firePoint.position = muzzle.position;
            var data = new SerializedObject(weapon);
            Assign(data, "muzzle", firePoint);
            var issue = IssuedCarbineConfig(weapon.Config, modelName);
            Assign(data,"config",issue);
            data.FindProperty("ammoInMag").intValue=issue.startingAmmoInMag;
            data.FindProperty("ammoReserve").intValue=issue.startingReserveAmmo;
            data.FindProperty("isReloading").boolValue=false;
            data.ApplyModifiedPropertiesWithoutUndo();
            return PrefabUtility.SaveAsPrefabAsset(root, OsowiecProjectPaths.Prefab(modelName)).GetComponent<Weapon>();
        }
        finally { Object.DestroyImmediate(root); }
    }

    public static GameObject ViewPrefab(string modelName)
    {
        var root = new GameObject(modelName + " presentation");
        try
        {
            var model = Visual(modelName, root.transform);
            if (modelName.EndsWith("Carbine", StringComparison.Ordinal))
                Marker(model.transform, "Distant_carbine").GetComponent<Renderer>().enabled = false;
            return PrefabUtility.SaveAsPrefabAsset(root, OsowiecProjectPaths.Prefab(modelName + "View"));
        }
        finally { Object.DestroyImmediate(root); }
    }

    public static GameObject MercyCartPrefab()
    {
        OsowiecProjectPaths.EnsureFolder(OsowiecProjectPaths.PrefabFolder);
        if (!AssetDatabase.IsValidFolder(Generated.TrimEnd('/')))
            AssetDatabase.CreateFolder(Folder.TrimEnd('/'), "Game");
        var root = new GameObject("Mercy cart");
        try
        {
            // Its own moving geometry must not become part of the road during a crater rebake.
            root.AddComponent<Unity.AI.Navigation.NavMeshModifier>().ignoreFromBuild = true;
            var body = Visual("MercyCart",root.transform);
            PrefabUtility.UnpackPrefabInstance(body,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            var front = Marker(body.transform,"CartFront");
            var back = Marker(body.transform,"CartBack");
            var direction = Vector3.ProjectOnPlane(front.position-back.position,Vector3.up);
            body.transform.rotation=Quaternion.AngleAxis(Vector3.SignedAngle(direction,Vector3.forward,Vector3.up),Vector3.up)*
                body.transform.rotation;
            var left=Marker(body.transform,"WheelLeftGeometry");
            var right=Marker(body.transform,"WheelRightGeometry");
            // Different FBX axis import conventions may swap lateral marker names.
            if (left.position.x > right.position.x) (left,right)=(right,left);
            float width=Vector3.Distance(left.position,right.position);
            if (width < .001f) throw new InvalidOperationException("Missing cart axle span");
            body.transform.localScale *= .982f / width;
            var axle = (left.position + right.position) * .5f;
            body.transform.position += new Vector3(-axle.x, .488f-axle.y, -axle.z);
            Transform Pivot(string name, Transform geometry)
            {
                var pivot = new GameObject(name).transform;
                pivot.SetParent(root.transform,false);
                pivot.position=geometry.position;
                geometry.SetParent(pivot,true);
                return pivot;
            }
            var mechanism=root.AddComponent<OsowiecCartMotion>();
            mechanism.leftWheel=Pivot("MercyWheelLeft",left);
            mechanism.rightWheel=Pivot("MercyWheelRight",right);
            mechanism.flywheel=Pivot("Air drive · flywheel",Marker(body.transform,"DriveFlywheelGeometry"));
            mechanism.rod=Pivot("Air drive · conrod",Marker(body.transform,"DriveRodGeometry"));
            var crewAnchor=new GameObject("Rear tiller · orderly").transform;
            crewAnchor.SetParent(root.transform,false);
            crewAnchor.localPosition=new Vector3(0,0,-1.34f);
            mechanism.orderly=OsowiecWardBuilder.MakeAttendant(crewAnchor,"Санитар обоза");
            return PrefabUtility.SaveAsPrefabAsset(root, OsowiecProjectPaths.Prefab("MercyCart"));
        }
        finally { Object.DestroyImmediate(root); }
    }

    public static string EnemyPath(string ally) => ally.Replace(".prefab", "-Противник.prefab");

    public static string BuildUnit(ChessUnitType soldier, string uniform, Weapon rifle)
    {
        string name = soldier.ToString();
        var template = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Units/" + name + ".prefab");
        if (!template || !template.TryGetComponent<Unit>(out var unit) || unit.chessType != soldier || !rifle)
            throw new InvalidOperationException("Missing or mismatched unit template or weapon for " + name);
        string path = OsowiecProjectPaths.Prefab(name);
        UnitPrefab(template, name, rifle, uniform, path);
        UnitPrefab(template, name, rifle, "Sentry", EnemyPath(path));
        return path;
    }

    static void UnitPrefab(GameObject template, string display, Weapon rifle, string role, string path)
    {
        var root = (GameObject)PrefabUtility.InstantiatePrefab(template);
        try
        {
            // Preserve persistent InputAction callbacks to the Unit on this root while dropping the entire old rig.
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var component in root.GetComponents<Component>())
            {
                if (component == null || component is Transform or CharacterController or Unit or PlayerInput or AudioSource) continue;
                Object.DestroyImmediate(component);
            }
            while (root.transform.childCount > 0) Object.DestroyImmediate(root.transform.GetChild(0).gameObject);
            root.name = Path.GetFileNameWithoutExtension(path);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;
            var capsule = root.GetComponent<CharacterController>();
            capsule.height = 1.78f; capsule.radius = .29f; capsule.center = new Vector3(0,.9f,0);
            capsule.stepOffset = .29f; capsule.skinWidth = .035f; capsule.minMoveDistance = 0;
            var unit = root.GetComponent<Unit>();
            var eyes = new GameObject("CameraAttachPoint").transform;
            eyes.SetParent(root.transform, false); eyes.localPosition = new Vector3(0,1.65f,.025f);
            var model = Visual(role, root.transform);
            var left = Marker(model.transform,"LHand");
            var right = Marker(model.transform,"RHand");
            var pelvis = Marker(model.transform,"Pelvis");
            var leftHip = Marker(model.transform,"LThigh");
            var rightHip = Marker(model.transform,"RThigh");
            var anatomicalRight = Vector3.ProjectOnPlane(rightHip.position-leftHip.position,Vector3.up);
            Vector3 forward = Vector3.Cross(anatomicalRight, Vector3.up);
            if (Vector3.Dot(forward, left.position - pelvis.position) < 0) forward = -forward;
            model.transform.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(forward,Vector3.forward,Vector3.up),Vector3.up)
                * model.transform.rotation;
            var animator = model.GetComponent<Animator>();
            if (!animator) animator = model.AddComponent<Animator>();
            if (!animator) throw new InvalidOperationException("Cannot attach field-cast Animator to " + role);
            animator.runtimeAnimatorController = Controller(role);
            animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            OsowiecHitModelBuilder.Attach(root, model, role);
            var socket = new GameObject("Carbine grip socket").transform;
            socket.SetParent(right, true); socket.position = right.position; socket.rotation = Quaternion.identity;
            var gun = (GameObject)PrefabUtility.InstantiatePrefab(rifle.gameObject);
            gun.transform.SetParent(socket, false); gun.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            // FBX skeletal joints can carry an inherited centimetre conversion.
            var inherited = socket.lossyScale;
            gun.transform.localScale = new Vector3(1f/inherited.x, 1f/inherited.y, 1f/inherited.z);
            var motion = root.AddComponent<OsowiecCastMotion>(); motion.body = animator;
            root.AddComponent<OsowiecBootContact>();
            var info = new SerializedObject(unit);
            if (role != "Sentry")
            {
                var (health, speed) = role switch
                {
                    "Assault" => (125, 3.6f),
                    "Support" => (85, 5.3f),
                    _ => (100, 4f)
                };
                info.FindProperty("health").intValue = health;
                info.FindProperty("maxHealth").intValue = health;
                info.FindProperty("moveSpeed").floatValue = speed;
            }
            info.FindProperty("damage").intValue = rifle.Config.damagePerHit;
            info.FindProperty("rangedShootStateName").stringValue = "Fire";
            Assign(info,"unitMoveSound",null);
            Assign(info,"takeDamageSound",AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Osowiec/Atmosphere/Resources/OsowiecSound/FieldInjury.wav"));
            Assign(info,"unitDeathSound",AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Osowiec/Atmosphere/Resources/OsowiecSound/FieldFall.wav"));
            Assign(info,"observerFireSound",null);
            Assign(info,"observerReloadSound",null);
            Assign(info, "cameraAttachPoint", eyes); Assign(info,"weaponSocket",socket);
            Assign(info,"startingWeaponPrefab",null); Assign(info,"equippedWeapon",gun.GetComponent<Weapon>());
            var portraitPath = Folder + role + "Portrait.png";
            var portrait = AssetDatabase.LoadAssetAtPath<Sprite>(portraitPath);
            if (portrait == null && AssetImporter.GetAtPath(portraitPath) is TextureImporter pi)
            {
                pi.textureType = TextureImporterType.Sprite; pi.spriteImportMode = SpriteImportMode.Single; pi.SaveAndReimport();
                portrait = Art<Sprite>(role + "Portrait.png");
            }
            Assign(info,"tacticalPortrait",portrait);
            info.FindProperty("unitDisplayNameOverride").stringValue = display;
            info.FindProperty("snapToGridOnStart").boolValue = false;
            info.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root,path);
            Validate(root);
        }
        finally { Object.DestroyImmediate(root); }
    }

    static void Validate(GameObject unit)
    {
        foreach (var renderer in unit.GetComponentsInChildren<Renderer>(true))
        {
            Mesh mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null || !AssetDatabase.GetAssetPath(mesh).StartsWith(Folder, StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected mesh in " + unit.name + ": " + renderer.name);
            foreach (var mat in renderer.sharedMaterials)
                if (mat == null || !AssetDatabase.GetAssetPath(mat).StartsWith(Generated, StringComparison.Ordinal))
                    throw new InvalidOperationException("Unexpected material on " + renderer.name);
        }
    }
}
