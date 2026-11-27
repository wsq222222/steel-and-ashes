using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class OsowiecDugoutArt
{
    private const string Art = "Assets/Osowiec/HQ/Art/";
    private static readonly Dictionary<string, Color> Colors = new()
    {
        ["QuarterSawn"] = new(.97f, .92f, .84f), ["DarkWalnut"] = new(.67f, .56f, .47f),
        ["Steel"] = new(.21f,.236f,.221f), ["Oxide"] = new(.165f,.155f,.123f),
        ["Brass"] = new(.62f,.46f,.22f), ["Bakelite"] = new(.10f,.088f,.074f),
        ["Paper"] = new(.79f,.685f,.49f), ["Dispatch"] = Color.white, ["Profiles"] = Color.white,
        ["Map"] = Color.white, ["Canvas"] = new(.217f,.237f,.145f), ["Leather"] = new(.275f,.13f,.071f),
        ["Glass"] = new(.80f,.73f,.59f,.40f), ["Flame"] = new(1f,.77f,.36f), ["Graphite"] = new(.15f,.16f,.153f),
        ["Wax"] = new(.41f,.155f,.112f), ["Silt"] = new(.245f,.174f,.105f),
        ["Water"] = new(.21f,.245f,.217f,.58f), ["Linen"] = new(.53f,.47f,.33f), ["Enamel"] = new(.33f,.41f,.37f), ["LedgerCover"] = Color.white
    };

    public static void Install(Transform room, Texture map)
    {
        var materials = new Dictionary<string, Material>();
        Material Resolve(string slot)
        {
            if (materials.TryGetValue(slot, out var cached)) return cached;
            if (!Colors.TryGetValue(slot, out var color))
                throw new InvalidOperationException("Unexpected command post surface " + slot);
            string path = Art + "HQ_" + slot + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            bool metal = slot is "Brass" or "Steel" or "Oxide";
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metal ? .65f : 0f);
            material.SetFloat("_Smoothness", slot == "Water" ? .94f : slot == "Silt" ? .46f : slot == "Enamel" ? .73f : metal ? .51f : slot is "Glass" or "Bakelite" ? .61f : .24f);
            string texture = slot switch
            {
                "Dispatch" or "Profiles" or "LedgerCover" => slot,
                _ => null
            };
            if (slot == "Map") material.SetTexture("_BaseMap", map);
            else if (texture != null)
                material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "Textures/" + texture + ".png"));
            if (slot is "QuarterSawn" or "DarkWalnut")
            {
                material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "Textures/DeskOak_colour.jpg"));
                material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "Textures/DeskOak_normal.jpg"));
                material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "Textures/DeskOak_mask.png"));
                material.SetFloat("_BumpScale", slot == "DarkWalnut" ? .38f : .67f);
                material.SetFloat("_Smoothness", .82f);
                material.EnableKeyword("_NORMALMAP"); material.EnableKeyword("_METALLICSPECGLOSSMAP");
            }
            if (metal)
            {
                const string oxidation = "Assets/Osowiec/Art/Fort1916/Surfaces/Oxide_";
                material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(oxidation+"normal.jpg"));
                material.SetFloat("_BumpScale", slot == "Brass" ? .13f : .26f);
                material.EnableKeyword("_NORMALMAP");
                material.SetFloat("_Smoothness", slot == "Oxide" ? .27f : .41f);
            }
            if (slot is "Glass" or "Flame")
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", (slot == "Flame" ? new Color(1f,.46f,.105f)*3.1f :
                    new Color(.67f,.25f,.052f)*.39f));
            }
            if (slot is "Water" or "Glass")
            {
                material.SetOverrideTag("RenderType", "Transparent");
                material.SetFloat("_Surface",1); material.SetFloat("_ZWrite",0);
                material.SetInt("_SrcBlend",(int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend",(int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetShaderPassEnabled("ShadowCaster",false);
                material.renderQueue=(int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            EditorUtility.SetDirty(material);
            materials.Add(slot,material);
            return material;
        }

        GameObject InstallProp(string name, Vector3 position, float yaw = 0, float scale = 1)
        {
            var model = PlaceFbx(room, name, position, yaw, scale);
            foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                string slot = renderer.name;
                if (slot.StartsWith("HQ_", StringComparison.Ordinal))
                    slot = slot.Substring(3);
                else if (name == "FieldGramophone" && slot.StartsWith("Record_", StringComparison.Ordinal))
                    slot = slot.Substring(7);
                else
                    throw new InvalidOperationException("Unmapped command post prop: " + slot);
                renderer.sharedMaterial = Resolve(slot);
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            return model;
        }

        var shell = PlaceFbx(room, "CommandBunker", Vector3.zero);
        var gramLampBase = new Vector3(1.95f, 1.80f, 3.46f);
        foreach (var renderer in shell.GetComponentsInChildren<MeshRenderer>(true))
        {
            var slots = renderer.sharedMaterials;
            for (int i = 0; i < slots.Length; i++)
            {
                string slot = slots[i] ? slots[i].name : "";
                slots[i] = AssetDatabase.LoadAssetAtPath<Material>(Art + "Bunker/" + slot + ".mat");
                if (!slots[i]) throw new InvalidOperationException("Missing bunker material for " + renderer.name + ": " + slot);
            }
            renderer.sharedMaterials = slots;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            if (renderer.name.StartsWith("Solid_", StringComparison.Ordinal))
                renderer.gameObject.AddComponent<MeshCollider>().sharedMesh = renderer.GetComponent<MeshFilter>().sharedMesh;
        }
        InstallWalkSurfaces(room);

        var table = InstallProp("CommandTable",new Vector3(0,0,1.10f));
        var drip = new GameObject("Rainwater at the map edge");
        drip.transform.SetParent(room,false);
        drip.transform.position = table.GetComponentsInChildren<Transform>().First(t=>t.name=="SurveyMapDrip").position;
        drip.AddComponent<MeshFilter>();
        var sheen = drip.AddComponent<MeshRenderer>();
        sheen.sharedMaterial = Resolve("Water");
        sheen.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        drip.AddComponent<OsowiecDeskRain>();
        Box(room, "Table collision", new Vector3(0,.56f,1.10f), new Vector3(2.84f,1.12f,1.54f));

        void Lantern(string name, Vector3 basePosition, float scale, Vector3 emitterOffset)
        {
            InstallProp("StormLantern", basePosition, 0, scale);
            var light = new GameObject(name).AddComponent<Light>();
            light.transform.SetParent(room, false);
            light.transform.position = basePosition + emitterOffset;
            light.type = LightType.Point;
            light.renderMode = LightRenderMode.ForcePixel;
        }

        void WallLamp(string name, Vector3 basePosition, Vector3 intoWall)
        {
            var shelf = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shelf.name = "Forged bracket";
            shelf.transform.SetParent(room, false);
            shelf.transform.SetPositionAndRotation(basePosition + intoWall*.070f - Vector3.up*.014f, Quaternion.LookRotation(intoWall));
            shelf.transform.localScale = new Vector3(.23f,.028f,.32f);
            shelf.GetComponent<MeshRenderer>().sharedMaterial = Resolve("Oxide");
            Object.DestroyImmediate(shelf.GetComponent<Collider>());
            Lantern(name, basePosition, .84f, Vector3.up*.20f - intoWall*.145f);
        }

        Lantern("Map lantern", new Vector3(.98f, 1.123f, 1.60f), 1.08f, new Vector3(-.16f,.28f,-.03f));
        WallLamp("Gramophone lantern", gramLampBase, Vector3.forward);
        WallLamp("Passage lantern", new Vector3(-3.25f,1.81f,-.92f), Vector3.left);
        InstallProp("SurveyBoard", new Vector3(-.45f,1.77f,3.51f), 0, 1.10f);
        var horn = InstallProp("FieldGramophone", new Vector3(1.95f,0,2.82f), -25f);
        var platter = horn.transform.Find("RecordPlatter");
        if (!platter || !platter.Find("RecordWitness") || !platter.GetComponentInChildren<MeshRenderer>())
            throw new InvalidOperationException("Gramophone export is missing its rotating record or label.");
        var stand = Box(room, "Phonograph stand collision", horn.transform.position + Vector3.up*.52f, new Vector3(.76f,1.04f,.52f));
        stand.transform.rotation = Quaternion.Euler(0,-25f,0);

        // Only four real-time local lights can ever affect the same surface on the base forward profile.
        var day = new GameObject("Daylight at entry").AddComponent<Light>();
        day.transform.SetParent(room, false);
        day.transform.SetPositionAndRotation(new Vector3(.54f,2.36f,-8.42f), Quaternion.LookRotation(new Vector3(-.12f,-.19f,1)));
        day.type = LightType.Spot;
        day.spotAngle = 100f;
        day.innerSpotAngle = 56f;
        day.renderMode = LightRenderMode.ForcePixel;
        LightDugout(room);
        BakeLampBounce(shell, gramLampBase + new Vector3(0,.21f,-.10f));
    }

    static GameObject PlaceFbx(Transform room, string name, Vector3 position, float yaw = 0, float scale = 1)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Art + name + ".fbx");
        if (!asset) throw new InvalidOperationException("Missing Blender export " + name);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(asset, room);
        model.name = name;
        var marks = model.GetComponentsInChildren<Transform>(true)
            .Where(t => t.name.StartsWith("Survey", StringComparison.Ordinal))
            .ToDictionary(t => t.name);
        var origin = marks["SurveyOrigin"];
        var north = marks["SurveyNorth"].position - origin.position;
        var up = marks["SurveyUp"].position - origin.position;
        model.transform.localScale *= scale / up.magnitude;
        model.transform.rotation = Quaternion.Euler(0,yaw,0) *
            Quaternion.Inverse(Quaternion.LookRotation(north,up)) * model.transform.rotation;
        model.transform.position += position - origin.position;
        PrefabUtility.RecordPrefabInstancePropertyModifications(model.transform);
        return model;
    }

    static BoxCollider Box(Transform room, string name, Vector3 center, Vector3 size)
    {
        var collider = new GameObject(name).AddComponent<BoxCollider>();
        collider.transform.SetParent(room, false);
        collider.transform.localPosition = center;
        collider.size = size;
        return collider;
    }

    static void InstallWalkSurfaces(Transform room)
    {
        void Floor(string name, float xMin, float xMax, float zMin, float zMax, OsowiecGroundSurface type, float height = 0)
        {
            const float depth = .20f;
            var slab = Box(room, name, new Vector3((xMin+xMax)/2, height-depth/2, (zMin+zMax)/2),
                new Vector3(xMax-xMin, depth, zMax-zMin));
            slab.gameObject.AddComponent<OsowiecAcousticSurface>().surface = type;
        }
        Floor("Room decking", -3.50f,3.50f,-2.62f,3.68f,OsowiecGroundSurface.Timber);
        Floor("Signal recess decking", -4.45f,-3.40f,.21f,2.29f,OsowiecGroundSurface.Timber);
        Floor("Passage decking", -1.25f,1.25f,-5.51f,-2.59f,OsowiecGroundSurface.Timber);
        Floor("Wet upper landing", -1.27f,1.27f,-8.78f,-7.78f,OsowiecGroundSurface.Brick,.90f);
        // The controller walks a continuous envelope through the six visible 15 cm stone risers.
        const float run = 2.28f, rise = .90f, depth = .16f;
        float pitch = Mathf.Atan2(rise,run) * Mathf.Rad2Deg;
        var rotation = Quaternion.Euler(pitch,0,0);
        var slope = Box(room, "Wet stone steps", new Vector3(0,.45f,-6.64f) - rotation * Vector3.up*(depth*.5f),
            new Vector3(2.50f,depth,Mathf.Sqrt(run*run+rise*rise)));
        slope.transform.localRotation = rotation;
        slope.gameObject.AddComponent<OsowiecAcousticSurface>().surface = OsowiecGroundSurface.Brick;
    }

    public static void LightDugout(Transform room)
    {
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.36f,.38f,.40f);
        RenderSettings.ambientEquatorColor = new Color(.29f,.265f,.235f);
        RenderSettings.ambientGroundColor = new Color(.18f,.155f,.14f);
        foreach (var light in room.GetComponentsInChildren<Light>(true))
        {
            bool daylight = light.name == "Daylight at entry";
            bool table = light.name == "Map lantern";
            bool gram = light.name == "Gramophone lantern";
            light.color = daylight ? new Color(.69f,.82f,1f) : new Color(1f,.73f,.46f);
            light.intensity = daylight ? 17f : table ? 2.15f : gram ? 3.5f : 2.6f;
            light.range = daylight ? 8.6f : table ? 5.6f : 4.6f;
            light.shadows = daylight || table ? LightShadows.Soft : LightShadows.None;
            light.shadowStrength = .68f; light.shadowNearPlane = .08f;
            light.shadowBias = .035f; light.shadowNormalBias = .12f;
        }
    }

    // Store the stationary wall bounce on the actual shell geometry. It remains attached
    // to the brickwork even when a renderer's real-time light budget changes with viewpoint.
    static void BakeLampBounce(GameObject shell, Vector3 source)
    {
        const string path = Art + "Gramophone wall bounce.mat";
        var bounce = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!bounce)
        {
            var shader = Shader.Find("Steel and Ashes/Static kerosene bounce");
            if (!shader) throw new InvalidOperationException("Missing lamp bounce shader");
            bounce = new Material(shader);
            AssetDatabase.CreateAsset(bounce, path);
        }
        bounce.SetVector("_LampWorld", new Vector4(source.x,source.y,source.z,0));
        EditorUtility.SetDirty(bounce);
        foreach (var original in shell.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (original.bounds.SqrDistance(source) > 1.2f * 1.2f) continue;
            var mesh = original.GetComponent<MeshFilter>().sharedMesh;
            var layer = new GameObject("Lamp bounce on " + original.name, typeof(MeshFilter), typeof(MeshRenderer));
            layer.transform.SetParent(original.transform, false);
            layer.GetComponent<MeshFilter>().sharedMesh = mesh;
            var overlay = layer.GetComponent<MeshRenderer>();
            overlay.sharedMaterials = Enumerable.Repeat(bounce, mesh.subMeshCount).ToArray();
            overlay.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            overlay.receiveShadows = false;
            overlay.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            overlay.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }
    }
}
