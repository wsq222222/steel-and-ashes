using System;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class OsowiecFieldCastReview
{
    const string Folder = OsowiecFieldCast.Folder;
    const string Mission = OsowiecProjectPaths.OperationScene;

    static void Require(bool ok, string details)
    {
        if (!ok) throw new InvalidOperationException("Field cast check: " + details);
    }

    static string AssetGuid(Object asset) => asset != null ?
        AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset)) : "";
    static string FileGuid(string file)
    {
        string guid = AssetDatabase.AssetPathToGUID(file);
        Require(!string.IsNullOrEmpty(guid),"missing " + file);
        return guid;
    }

    static T ComponentIn<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<T>(true))
            .First(c => c.gameObject.activeInHierarchy);

    static void Meshes(GameObject visual, params string[] files)
    {
        var allowed = new HashSet<string>(files.Select(file => FileGuid(Folder + file)));
        var found = new HashSet<string>();
        var renderers = new HashSet<Renderer>(visual.GetComponentsInChildren<Renderer>(true));
        foreach (var group in visual.GetComponentsInChildren<LODGroup>(true))
        foreach (var level in group.GetLODs())
        foreach (var renderer in level.renderers)
        {
            Require(renderer != null && renderer.transform.IsChildOf(visual.transform),"missing or external LOD on " + visual.name);
            renderers.Add(renderer);
        }
        foreach (var renderer in renderers)
        {
            Mesh mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
            string guid=AssetGuid(mesh);
            Require(allowed.Contains(guid), visual.name + "/" + renderer.name + " unexpected mesh GUID " + guid);
            found.Add(guid);
            foreach (var mat in renderer.sharedMaterials)
                Require(mat != null && AssetDatabase.GetAssetPath(mat).StartsWith(Folder+"Game/",StringComparison.Ordinal),
                    visual.name + "/" + renderer.name + " unexpected material");
        }
        Require(found.SetEquals(allowed),visual.name + " missing expected mesh");
    }

    static string IssuedModel(Unit unit) => unit.chessType switch
    {
        ChessUnitType.Hammer => "RamCarbine",
        ChessUnitType.Cyclops => "ObserverCarbine",
        ChessUnitType.Hound => "RunnerCarbine",
        _ => throw new InvalidOperationException("No Osowiec presentation assigned to " + unit.chessType)
    };

    static void Actor(GameObject model,string role)
    {
        Require(model, "missing actor for " + role);
        var unit=model.GetComponent<Unit>();
        Require(unit && unit.GetEquippedWeapon() && unit.GetEquippedWeapon().Muzzle,
            model.name+" missing weapon or muzzle binding");
        string expectedWeapon=IssuedModel(unit);
        var shells = model.GetComponentsInChildren<OsowiecHitPart>(true)
            .Where(part => part.area == OsowiecHitArea.Helmet).ToArray();
        if (role == "Support")
        {
            Require(shells.Length == 0, model.name + " has an unexpected issued helmet");
            Meshes(model,role+".fbx",expectedWeapon+".fbx");
        }
        else
        {
            string helmet = "Game/" + role + "-Helmet.asset";
            Require(shells.Length == 1, model.name + " requires one issued helmet");
            var shell = shells[0];
            var filter = shell.GetComponent<MeshFilter>();
            Require(filter && filter.sharedMesh == shell.helmetMesh && AssetGuid(shell.helmetMesh) == FileGuid(Folder + helmet),
                model.name + " helmet renderer and hit part must use the issued " + role + " mesh");
            Meshes(model,role+".fbx",expectedWeapon+".fbx",helmet);
        }
        var appearance=unit.GetEquippedWeapon().Config.firstPersonPrefab;
        Require(appearance,model.name+" missing first-person variant");
        Meshes(appearance,expectedWeapon+".fbx");
        Require(AssetGuid(unit.GetTacticalPortrait())==FileGuid(Folder+role+"Portrait.png"),model.name+" wrong portrait");
        var animator=model.GetComponentInChildren<Animator>(true);
        Require(animator && animator.runtimeAnimatorController,model.name+" missing Animator");
        var clips=animator.runtimeAnimatorController.animationClips;
        foreach (var take in new[] {"Idle","Walk","Aim","Fire","Reload","Hit","Fall","Mask"})
            Require(clips.Any(c=> c.name.EndsWith(take,StringComparison.Ordinal) && AssetGuid(c)==FileGuid(Folder+role+".fbx")),
                model.name+" missing "+take);
    }

    [MenuItem("Steel and Ashes/Verify issued ammunition")]
    public static void VerifyAmmunition()
    {
        foreach(var role in new[] {ChessUnitType.Hammer, ChessUnitType.Cyclops, ChessUnitType.Hound})
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(OsowiecProjectPaths.Prefab(role.ToString()));
            Require(prefab, "missing unit prefab for " + role);
            var issued=prefab.GetComponent<Unit>().GetEquippedWeapon();
            string expected=IssuedModel(prefab.GetComponent<Unit>());
            Require(issued && AssetGuid(issued.Config)==FileGuid(Folder+"Game/"+expected+".asset") &&
                issued.Config.isMarksmanRifle==(expected=="ObserverCarbine") && issued.AmmoInMag==5 &&
                issued.MagazineSize==5 && issued.AmmoReserve==20,"incorrect issued ammunition on "+role);
        }
        string weaponPath=OsowiecProjectPaths.Prefab("RamCarbine");
        var preview=PrefabUtility.LoadPrefabContents(weaponPath);
        RangedWeaponConfig instant=null;
        try
        {
            var weapon=preview.GetComponent<Weapon>();
            instant=Object.Instantiate(weapon.Config);
            instant.reloadTimeSeconds=0;
            var data=new SerializedObject(weapon);
            data.FindProperty("config").objectReferenceValue=instant;
            data.FindProperty("ammoInMag").intValue=2;
            data.ApplyModifiedPropertiesWithoutUndo();
            Require(weapon.TryStartReload(false),"could not start initial reload");
            Require(!weapon.TryStartReload(false),"duplicate request restarted the animation");
            weapon.TickReload();
            Require(weapon.AmmoInMag==5 && weapon.AmmoReserve==17 && !weapon.IsReloading &&
                !weapon.TryStartReload(false),"incorrect transfer or reload on a full magazine");
            data.Update();
            data.FindProperty("ammoInMag").intValue=0;
            data.FindProperty("ammoReserve").intValue=4;
            data.ApplyModifiedPropertiesWithoutUndo();
            Require(weapon.TryStartReload(false),"could not start on empty with a partial reserve");
            weapon.TickReload();
            weapon.TickReload();
            Require(weapon.AmmoInMag==4 && weapon.AmmoReserve==0 && !weapon.TryStartReload(false),
                "last four rounds were lost, duplicated or an empty reserve accepted reload");
            var fps=OsowiecFieldCast.Art<AnimationClip>("Game/CarbineReload-FPS.anim");
            var tracks=AnimationUtility.GetCurveBindings(fps);
            Require(fps.length>=.98f && new[]{"Presentation","Presentation/Support","Presentation/Brace","Presentation/Cassette"}
                .All(path=>tracks.Any(t=>t.path==path && AnimationUtility.GetEditorCurve(fps,t).keys.Length>=5)),
                "incomplete first person reload clip");
            var model=AssetDatabase.LoadAssetAtPath<GameObject>(weaponPath);
            Require(model.GetComponentInChildren<LODGroup>(true)?.lodCount==2,"missing distance mesh on issued prop");
            Require(OsowiecFieldCast.Art<Texture2D>("Textures/WorkshopFinish_N.png").width>=1024,
                "missing workshop tangent-space bake");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(preview);
            if (instant) Object.DestroyImmediate(instant);
        }
        Debug.Log("PASS Osowiec issued ammunition: each role 5+20, partial reload 2+20 to 5+17, full magazine rejects reload, repeated input and low reserve verified, FPS clip and depth LOD checked.");
    }

    static void Starts(Scene scene, ArmyDeploymentController deployment)
    {
        var grid=ComponentIn<ChessGrid>(scene);
        var surface=ComponentIn<OsowiecOperation>(scene).GetComponent<NavMeshSurface>();
        Require(surface && surface.isActiveAndEnabled && surface.navMeshData &&
            AssetDatabase.GetAssetPath(surface.navMeshData)==OsowiecProjectPaths.OperationNavMesh,
            "operation is not bound to the baked Osowiec navigation");
        var sectors=deployment.GetComponent<OsowiecDeploymentSectors>();
        Require(sectors && OsowiecDeploymentSectors.Cells.Length>=2,"missing separated deployment sectors");
        // Isolate this surface from navigation data of other scenes open in the editor.
        var offset=new Vector3(12000,180,12000);
        var copy=NavMesh.AddNavMeshData(surface.navMeshData,surface.transform.position+offset,surface.transform.rotation);
        try
        {
            Require(copy.valid,"failed to register temporary isolated NavMesh");
            Require(NavMesh.SamplePosition(OsowiecOperation.Telephone+offset,out var fork,3.75f,NavMesh.AllAreas),
                "field telephone at first split has no walkable approach");
            var lengths=new List<string>();
            foreach (var sector in OsowiecDeploymentSectors.Cells)
            foreach (var position in sector)
            {
                int x=position.x,y=position.y;
                var cell=grid.GridToWorldPosition(x,y)+offset;
                Require(NavMesh.SamplePosition(cell,out var start,1.05f,NavMesh.AllAreas),
                    $"start {x},{y} off the baked surface");
                float horizontal = Vector2.Distance(new Vector2(cell.x,cell.z),new Vector2(start.position.x,start.position.z));
                Require(horizontal<=grid.cellSize*.49f,$"start {x},{y} snapped outside its own cell");
                var path=new NavMeshPath();
                Require(NavMesh.CalculatePath(start.position,fork.position,NavMesh.AllAreas,path) &&
                    path.status==NavMeshPathStatus.PathComplete,$"start {x},{y} cannot reach the first split");
                float metres=0;
                for(int i=1;i<path.corners.Length;i++) metres+=Vector3.Distance(path.corners[i-1],path.corners[i]);
                Require(metres>2,$"trivial path for start {x},{y}");
                lengths.Add($"({x},{y}) {metres:F1} m");
            }
            Debug.Log("Six starts to field telephone: "+string.Join(", ",lengths));
        }
        finally { if(copy.valid) copy.Remove(); }
    }

    [MenuItem("Steel and Ashes/Verify field cast and six deployment routes")]
    public static void Verify()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run the field cast check in Edit Mode.");
        var scene=SceneManager.GetSceneByPath(Mission);
        bool opened=!scene.IsValid() || !scene.isLoaded;
        if(opened) scene=EditorSceneManager.OpenScene(Mission,OpenSceneMode.Additive);
        try
        {
            string[] roles={"Assault","Scout","Support"};
            ChessUnitType[] soldiers={ChessUnitType.Hammer, ChessUnitType.Cyclops, ChessUnitType.Hound};
            string[] displayKeys={"Молот","Циклоп","Гончая"};
            int[] costs={5,4,3};
            var deploy=ComponentIn<ArmyDeploymentController>(scene);
            var data=new SerializedObject(deploy);
            var offers=data.FindProperty("offers");
            Require(offers.arraySize==roles.Length,"wrong number of roster entries");
            for(int i=0;i<roles.Length;i++)
            {
                string name=soldiers[i].ToString();
                string path=OsowiecProjectPaths.Prefab(name);
                var offer=offers.GetArrayElementAtIndex(i);
                var obj=offer.FindPropertyRelative("unitPrefab").objectReferenceValue as GameObject;
                Require(obj && AssetGuid(obj)==FileGuid(path),"deployment uses wrong prefab "+roles[i]);
                Require(offer.FindPropertyRelative("costPoints").intValue==costs[i],"incorrect price "+roles[i]);
                Require(obj.name==name && obj.GetComponent<Unit>().chessType==soldiers[i] &&
                    obj.GetComponent<Unit>().GetUnitDisplayName()==displayKeys[i],"incorrect deployment identity "+name);
                Actor(obj,roles[i]);
                var enemy=AssetDatabase.LoadAssetAtPath<GameObject>(OsowiecFieldCast.EnemyPath(path));
                Require(enemy && enemy.GetComponent<Unit>().chessType==soldiers[i],"incorrect sentry identity "+name);
                Actor(enemy,"Sentry");
            }
            var actors=scene.GetRootGameObjects().SelectMany(go=>go.GetComponentsInChildren<Unit>(true))
                .Where(u=>u.gameObject.activeInHierarchy && u.owner==Player.Player2).ToArray();
            Require(actors.Length>0,"scene has no active opponents");
            foreach(var actor in actors) Actor(actor.gameObject,"Sentry");
            var fps=ComponentIn<OsowiecWeaponView>(scene);
            Require(fps.model && fps.hands,"missing first-person presentation");
            Meshes(fps.model,"RamCarbine.fbx"); Meshes(fps.hands,"FieldHands.fbx");
            foreach(string marker in new[]{"FieldRifleMuzzle","FieldRifleGrip","FieldFeed"})
                Require(fps.model.GetComponentsInChildren<Transform>(true).Any(t=>t.name==marker),"FPS missing "+marker);
            Require(fps.hands.GetComponentsInChildren<Transform>(true).Any(t=>t.name=="FieldGloveL"),"missing wipe/reload hand");
            string atlas=FileGuid(Folder+"FieldPortraitAtlas.png");
            Require(AssetGuid(ComponentIn<OsowiecPlanner>(scene).portraits)==FileGuid("Assets/Resources/OsowiecJournal/portraits.png"),
                "wrong journal portraits on the field map");
            var icons=new SerializedObject(ComponentIn<TacticalWorldIconsController>(scene));
            Require(AssetGuid(icons.FindProperty("fieldPortraitAtlas").objectReferenceValue)==atlas,"wrong world icons atlas");
            var card=data.FindProperty("cardPrefab").objectReferenceValue as DeploymentCardUI;
            Require(card && card.GetComponentsInChildren<RawImage>(true).Any(p=>AssetGuid(p.texture)==atlas),
                "wrong deployment card atlas");
            Starts(scene,deploy);
            Debug.Log($"PASS Osowiec field cast: 3 ally + 3 enemy prefabs, {actors.Length} scene opponents, 8 takes, world/FPS, portraits, 6 routes.");
        }
        finally { if(opened) EditorSceneManager.CloseScene(scene,true); }
    }
}
