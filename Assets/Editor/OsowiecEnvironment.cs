using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Places the editable Fort I survey mesh and wires its playable surfaces.</summary>
public static class OsowiecEnvironment
{
    public const string GateHingeName = "Osowiec Door Hinge";
    const string Directory = "Assets/Osowiec/Art/Fort1916/";
    const string SurfaceDirectory = Directory + "Surfaces/";
    const string ModelPath = Directory + "MarshFort.fbx";
    const string DressingPath = Directory + "TrenchDressing.fbx";
    const string DressingName = "Trench repairs and west portal";

    public static void Create()
    {
        // The two collapsible ground sections must also be bakeable in a standalone player.
        if (AssetImporter.GetAtPath(ModelPath) is ModelImporter importer &&
            (!importer.isReadable || !(importer.userData ?? string.Empty).Contains(OsowiecSurveyNormals.Revision)))
        {
            importer.isReadable = true;
            if(!(importer.userData ?? string.Empty).Contains(OsowiecSurveyNormals.Revision))
                importer.userData=(importer.userData+"\n"+OsowiecSurveyNormals.Revision).Trim();
            importer.SaveAndReimport();
        }
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null) throw new InvalidOperationException("Missing Fort I landscape export: " + ModelPath);
        var root = new GameObject("Osowiec · Fort I");
        var handedness = new GameObject("FBX handedness");
        handedness.transform.SetParent(root.transform, false);
        var basis = new GameObject("Survey to Unity");
        basis.transform.SetParent(handedness.transform, false);
        var fort = (GameObject)PrefabUtility.InstantiatePrefab(model, basis.transform);
        PrefabUtility.UnpackPrefabInstance(fort, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        OrientSurvey(handedness.transform, basis.transform, fort.transform);

        var gate = new GameObject(GateHingeName);
        gate.transform.SetParent(root.transform, false);
        gate.transform.position = new Vector3(0, 0, -13.5f);
        gate.AddComponent<Unity.AI.Navigation.NavMeshModifier>().ignoreFromBuild = true;
        var createdMaterials = new Dictionary<string, Material>();
        var roofs = new List<Renderer>();
        InstallDressing(fort.transform, createdMaterials, roofs);
        var events = root.AddComponent<OsowiecGroundEvents>();
        events.intact = new GameObject[2];
        events.hollows = new GameObject[2];
        events.wire = new GameObject[2];
        var meshes = fort.GetComponentsInChildren<MeshFilter>();
        var weatheredSurfaces = new HashSet<string>(meshes.Select(m => m.name)
            .Where(name => name.StartsWith("Weathered_", StringComparison.Ordinal))
            .Select(name => name.Substring("Weathered_".Length)));
        foreach (var mesh in meshes)
        {
            string label = mesh.name;
            var renderer = mesh.GetComponent<MeshRenderer>();
            if(label=="Runoff_pools") events.water=mesh;
            bool eventSite = label.StartsWith("Event_", StringComparison.Ordinal);
            bool terrain = label.StartsWith("Ground_", StringComparison.Ordinal)
                || label.StartsWith("Flood_meadow_", StringComparison.Ordinal) || eventSite;
            renderer.sharedMaterials = renderer.sharedMaterials.Select(m =>
                Resolve(terrain ? "Bank growth" : (m == null ? "Concrete" : m.name), createdMaterials)).ToArray();
            GameObject item = mesh.gameObject;
            if (label == "Doorleaf")
            {
                // Model vertices are in survey coordinates, so retain world space on the hinge.
                item.transform.SetParent(gate.transform, true);
                continue;
            }
            if (label.StartsWith("Roof_", StringComparison.Ordinal)) roofs.Add(renderer);
            if (label.StartsWith("WireSnag_", StringComparison.Ordinal))
            {
                events.wire[int.Parse(label.Substring("WireSnag_".Length))] = item;
                // A soldier can wade through this short roll. Shafts and wheels cannot.
                var roll = item.AddComponent<BoxCollider>();
                roll.center = mesh.sharedMesh.bounds.center;
                roll.size = mesh.sharedMesh.bounds.size;
                roll.isTrigger = true;
            }
            if (label.StartsWith("Marsh_cover_", StringComparison.Ordinal))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
            bool board = label.StartsWith("Walkway_", StringComparison.Ordinal);
            bool dressedDeck = label.StartsWith("Weathered_Walkway_", StringComparison.Ordinal);
            bool hiddenProxy = weatheredSurfaces.Contains(label);
            if (hiddenProxy) renderer.enabled = false;
            if(label == "Solid_Casemate_gate_head") roofs.Add(renderer);
            bool stone = label == "Brickfloor";
            bool solid = label.StartsWith("Solid_", StringComparison.Ordinal) || IsSolidFieldProp(label);
            if (terrain || (board && !hiddenProxy) || dressedDeck || stone || solid)
            {
                var collider = item.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh.sharedMesh;
                if(label.StartsWith("Trench_revetment_",StringComparison.Ordinal))
                {
                    var wall=item.AddComponent<Unity.AI.Navigation.NavMeshModifier>();
                    wall.overrideArea=true;
                    wall.area=UnityEngine.AI.NavMesh.GetAreaFromName("Not Walkable");
                }
                if (board || dressedDeck || stone)
                {
                    item.name = stone ? "Casemate brick floor" : "Duckboard deck " + label;
                    var ground = item.AddComponent<OsowiecAcousticSurface>();
                    ground.surface = stone ? OsowiecGroundSurface.Brick : OsowiecGroundSurface.Timber;
                }
                else if (terrain) item.name = "Biebrza ground " + label;
            }
            if (eventSite)
            {
                string[] parts = label.Split('_');
                int slot = int.Parse(parts[2]);
                if (parts[1] == "Plate") events.intact[slot] = item;
                else
                {
                    events.hollows[slot] = item;
                    item.SetActive(false);
                }
            }
        }
        if (events.intact.Any(p => p == null) || events.hollows.Any(p => p == null) || events.wire.Any(p => p == null))
            throw new InvalidOperationException("MarshFort.fbx is missing one of the surveyed ground or wire sections.");
        OsowiecFieldTelephoneArt.Install(fort.transform);
        events.warningMaterial = Resolve("Slip warning", createdMaterials);
        events.debrisMaterial = Resolve("Earth", createdMaterials);
        OsowiecCutBankDetails.Bake(root.transform,events.debrisMaterial,Resolve("Bark",createdMaterials));
        roofs.Add(OsowiecArchitectureDetails.BakeFort(root.transform));
        var batteryRoof=OsowiecBatteryFort.Install(root.transform);
        roofs.Add(batteryRoof);
        events.plumeMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Osowiec/Atmosphere/Low Fog.mat");
        var chip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        events.chipMesh = chip.GetComponent<MeshFilter>().sharedMesh;
        UnityEngine.Object.DestroyImmediate(chip);
        var cutaway=root.AddComponent<OsowiecFortCutaway>();
        cutaway.surfaces=roofs.ToArray();
        cutaway.batteryRoof=batteryRoof.GetComponent<MeshCollider>();
        OsowiecCeilings.Bake(cutaway);
        OsowiecFloodplain.Create(root.transform);
        OsowiecRoofTurf.Install(root.transform);
        foreach (var pos in LampSites)
        {
            var point = new GameObject("Storm lamp · warm practical").AddComponent<Light>();
            point.transform.SetParent(root.transform, false);
            bool indoor = pos.x >= -9.8f && pos.x < 10 && Mathf.Abs(pos.y + 12) < 2;
            float wallInset = indoor ? Mathf.Sign(-12 - pos.y) * .34f : 0f;
            bool bridge = pos.x < -36.8f && pos.x > -38.6f && (pos.y > -14 || pos.y < -18);
            point.transform.position = new Vector3(pos.x, bridge ? 2.43f : 1.69f, pos.y + wallInset);
            point.type = LightType.Point;
            bool byGate = indoor && Mathf.Approximately(pos.x, -3f);
            point.color = indoor ? new Color(1f,.66f,.37f) : new Color(1f,.60f,.27f);
            point.intensity = indoor ? (byGate ? 4.0f : 3.35f) : 3.2f;
            point.range = indoor ? 7.0f : 6.5f;
            // The lamp immediately beside the door throws the moving leaf's shadow.
            point.shadows = byGate ? LightShadows.Soft : LightShadows.None;
            if (byGate)
            {
                point.shadowStrength = .72f;
                point.shadowBias = .025f;
                point.shadowNormalBias = .12f;
                point.shadowResolution = UnityEngine.Rendering.LightShadowResolution.Low;
            }
        }
        AssetDatabase.SaveAssets();
    }

    internal static bool IsSolidFieldProp(string name) => name.StartsWith("Trench_revetment_",StringComparison.Ordinal)
        || name == "Field_wire" || name == "Service_lines" || name == "Telegraph" || name == "Signal_party_workbench"
        || name == "Supply_crates" || name == "Battery_line_boxes" || name == "Field_station"
        || name == "Battery_statuary" || name == "Fort_end_walls";

    static void InstallDressing(Transform fort, Dictionary<string, Material> materials, List<Renderer> roofs)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(DressingPath);
        if (model == null) throw new InvalidOperationException("Missing trench repairs export: " + DressingPath);
        var dressing = (GameObject)PrefabUtility.InstantiatePrefab(model, fort.parent);
        dressing.name = DressingName;
        dressing.transform.SetLocalPositionAndRotation(fort.localPosition, fort.localRotation);
        dressing.transform.localScale = fort.localScale;
        dressing.AddComponent<Unity.AI.Navigation.NavMeshModifier>().ignoreFromBuild = true;
        foreach (var renderer in dressing.GetComponentsInChildren<MeshRenderer>())
        {
            renderer.sharedMaterials = renderer.sharedMaterials.Select(source =>
                Resolve(source.name, materials)).ToArray();
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            if (renderer.name == "Turf_section_over_vault") roofs.Add(renderer);
        }
        PrefabUtility.RecordPrefabInstancePropertyModifications(dressing);
        PrefabUtility.RecordPrefabInstancePropertyModifications(dressing.transform);
        if (dressing.GetComponentsInChildren<Collider>(true).Length > 0)
            throw new InvalidOperationException("Trench repairs unexpectedly affect player collisions.");
        var meshes = dressing.GetComponentsInChildren<MeshRenderer>();
        var station = meshes.Single(r => r.name == "Signal_party_workbench").bounds;
        var masonry = meshes.Single(r => r.name == "Engineers_portal_masonry").bounds;
        if (meshes.Length != 4 || !meshes.Any(r => r.name == "Turf_section_over_vault") ||
            !station.Contains(new Vector3(-17.65f, 1f, -5f)) ||
            !masonry.Contains(new Vector3(-10.52f, 1.1f, -14.3f)))
            throw new InvalidOperationException("Trench repairs do not align with the surveyed geometry: " + station + " / " + masonry);
        var bench = meshes.Single(r => r.name == "Signal_party_workbench").GetComponent<MeshFilter>();
        bench.gameObject.AddComponent<MeshCollider>().sharedMesh = bench.sharedMesh;
    }

    static readonly Vector2[] LampSites = {
        new(-38.50f,-13.65f),new(-36.96f,-18.45f),
        new(-44,-18.8f), new(-34.7f,-12.3f), new(-25.8f,-6.2f), new(-15.5f,-5.8f),
        new(-9.7f,-13.6f), new(-3,-13.7f), new(3,-10.28f), new(9.1f,-13.7f),
        new(21.4f,-5.3f), new(31.7f,1.2f)
    };

    static void OrientSurvey(Transform root, Transform basis, Transform fort)
    {
        var marks = fort.GetComponentsInChildren<Transform>()
            .Where(t => t.name.StartsWith("Survey_", StringComparison.Ordinal)).ToDictionary(t => t.name);
        if (marks.Count < 4) throw new InvalidOperationException("Missing FBX survey frame in " + ModelPath);
        Vector3 origin = marks["Survey_origin"].position;
        Vector3 north = marks["Survey_north"].position - origin;
        Vector3 up = marks["Survey_up"].position - origin;
        basis.rotation = Quaternion.Inverse(Quaternion.LookRotation(north.normalized, up.normalized));
        basis.localScale = Vector3.one / north.magnitude;
        if (marks["Survey_east"].position.x < marks["Survey_origin"].position.x)
            root.localScale = new Vector3(-1, 1, 1);
        basis.position -= marks["Survey_origin"].position;
        if (Vector3.Distance(marks["Survey_north"].position, Vector3.forward) > .005f ||
            Vector3.Distance(marks["Survey_east"].position, Vector3.right) > .005f)
            throw new InvalidOperationException("The Fort I mesh is not calibrated to game metres.");
    }

    internal static Material Resolve(string key, Dictionary<string, Material> cache)
    {
        if (cache.TryGetValue(key, out var value)) return value;
        Shader shader = Shader.Find(key switch {
            "Water" => "Steel and Ashes/Marsh Runoff",
            "River" => "Steel and Ashes/Biebrza River",
            "Flag" => "Steel and Ashes/Battery signal cloth",
            "Brick" or "Brick core" or "Concrete" => "Steel and Ashes/Seeping masonry",
            "Grass" or "Reed" or "Sedge" => "Steel and Ashes/Floodplain grasses",
            "Bank growth" => "Steel and Ashes/Biebrza Ground",
            _ => "Universal Render Pipeline/Lit"
        });
        if (shader == null) throw new InvalidOperationException("Missing Fort I shader for " + key);
        string path = Directory + key + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader) { name = key };
            AssetDatabase.CreateAsset(material, path);
        }
        else material.shader = shader;
        string diffuse = SurfaceDirectory + key + "_colour.jpg";
        if (key == "Bank growth")
        {
            foreach (string layer in new[] { "Earth", "Sod", "Till" })
            {
                material.SetTexture("_" + layer, AssetDatabase.LoadAssetAtPath<Texture2D>(SurfaceDirectory + layer + "_colour.jpg"));
                material.SetTexture("_" + layer + "Normal", ImportedMap(SurfaceDirectory + layer + "_normal.jpg", true));
                material.SetTexture("_" + layer + "Mask", ImportedMap(SurfaceDirectory + layer + "_mask.png", false));
            }
        }
        else if (AssetDatabase.LoadAssetAtPath<Texture2D>(diffuse) != null)
        {
            var normal = ImportedMap(SurfaceDirectory + key + "_normal.jpg", true);
            var mask = ImportedMap(SurfaceDirectory + key + "_mask.png", false);
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(diffuse));
            material.SetTexture("_BumpMap", normal);
            material.SetTexture("_MetallicGlossMap", mask);
            material.SetFloat("_BumpScale", key == "Timber" ? .73f : .92f);
            material.SetFloat("_Smoothness", key == "Sod" ? .68f : .92f);
            material.SetColor("_BaseColor", key switch {
                "Brick" => new Color(.92f,.79f,.69f),
                "Earth" => new Color(.78f,.78f,.70f),
                "Timber" => new Color(.73f,.65f,.54f),
                "Concrete" => new Color(.85f,.83f,.73f),
                "Oxide" => new Color(.56f,.62f,.62f),
                "Sod" => new Color(.58f,.65f,.47f),
                _ => new Color(.85f,.84f,.74f)
            });
            if (material.HasProperty("_ReliefUV"))
            {
                material.SetFloat("_ReliefUV",key=="Brick" ? .0105f : key=="Concrete" ? .0059f : 0f);
                material.SetFloat("_Finish",key=="Concrete" ? 1f : 0f);
            }
            if (key == "Canvas" || key == "Repair flax") {
                material.SetTexture("_OcclusionMap",mask);
                material.SetFloat("_OcclusionStrength",.45f);
                material.EnableKeyword("_OCCLUSIONMAP");
            }
            material.EnableKeyword("_NORMALMAP");
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
        }
        else if (key != "Water" && key != "River")
        {
            var colour = key switch {
                "Brass" => new Color(.53f,.40f,.20f),
                "Tar" => new Color(.14f,.135f,.12f),
                "Grass" => new Color(.34f,.36f,.21f),
                "Sedge" => new Color(.35f,.38f,.21f),
                "Seed" => new Color(.37f,.31f,.19f),
                "Reed" => new Color(.49f,.46f,.30f),
                "Canvas" => new Color(.42f,.39f,.28f),
                "Glass" => new Color(.98f,.66f,.26f),
                "Flag" => new Color(.59f,.28f,.17f),
                "Slip warning" => new Color(1f,.64f,.20f),
                _ => throw new InvalidOperationException("Unresolved Fort I model material: " + key)
            };
            material.SetColor("_BaseColor", colour);
            if (key=="Sedge") material.SetFloat("_FoldedLeaf",1);
            if (key == "Grass" || key == "Reed")
            {
                const string atlasPath = SurfaceDirectory + "Floodplain_sedges.png";
                var atlasImport = (TextureImporter)AssetImporter.GetAtPath(atlasPath);
                if (!atlasImport.alphaIsTransparency || atlasImport.wrapMode != TextureWrapMode.Clamp)
                {
                    atlasImport.alphaIsTransparency = true;
                    atlasImport.mipMapsPreserveCoverage = true;
                    atlasImport.alphaTestReferenceValue = .32f;
                    atlasImport.wrapMode = TextureWrapMode.Clamp;
                    atlasImport.anisoLevel = 4;
                    atlasImport.SaveAndReimport();
                }
                material.SetTexture("_BladeAtlas", AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath));
            }
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", key == "Brass" ? .56f : .21f);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", key == "Brass" ? .64f : 0f);
            if (key == "Flag")
            {
                material.SetFloat("_Cull", 0f);
                material.doubleSidedGI = true;
            }
            if (key == "Glass")
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", new Color(3.22f,1.38f,.39f));
            }
            if (key == "Slip warning")
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", new Color(.74f,.39f,.08f));
            }
        }
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        cache.Add(key, material);
        return material;
    }

    static Texture2D ImportedMap(string path, bool normal)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new InvalidOperationException("Missing Fort I source map: " + path);
        var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        if (importer.textureType != type || importer.sRGBTexture)
        {
            importer.textureType = type;
            importer.sRGBTexture = false;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}

