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
        ["QuarterSawn"] = new(.75f, .72f, .68f), ["DarkWalnut"] = new(.39f, .335f, .30f),
        ["Steel"] = new(.21f,.236f,.221f), ["Oxide"] = new(.165f,.155f,.123f),
        ["Brass"] = new(.62f,.46f,.22f), ["Bakelite"] = new(.10f,.088f,.074f),
        ["Paper"] = new(.79f,.685f,.49f), ["Dispatch"] = Color.white, ["Profiles"] = Color.white,
        ["Map"] = Color.white, ["Canvas"] = new(.217f,.237f,.145f), ["Leather"] = new(.275f,.13f,.071f),
        ["Glass"] = new(.93f,.65f,.29f), ["Flame"] = new(1f,.77f,.36f), ["Graphite"] = new(.15f,.16f,.153f),
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
                "DarkWalnut" or "QuarterSawn" => "QuarterSawn",
                "Dispatch" or "Profiles" or "LedgerCover" => slot,
                _ => null
            };
            if (slot == "Map") material.SetTexture("_BaseMap", map);
            else if (texture != null)
                material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "Textures/" + texture + ".png"));
            if (texture == "QuarterSawn")
            {
                material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "Textures/QuarterSawn_N.png"));
                material.SetFloat("_BumpScale", .38f);
                material.EnableKeyword("_NORMALMAP");
            }
            if (slot is "Glass" or "Flame")
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", (slot == "Flame" ? new Color(1f,.48f,.12f)*4.8f :
                    new Color(.78f,.32f,.075f)*1.12f));
            }
            if (slot == "Water")
            {
                material.SetOverrideTag("RenderType", "Transparent");
                material.SetFloat("_Surface",1); material.SetFloat("_ZWrite",0);
                material.SetInt("_SrcBlend",(int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend",(int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue=(int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            EditorUtility.SetDirty(material);
            materials.Add(slot,material);
            return material;
        }

        GameObject InstallModel(string name, Vector3 position, float yaw = 0, float scale = 1)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Art + name + ".fbx");
            if (asset == null) throw new InvalidOperationException("Missing authored HQ mesh " + name);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(asset, room);
            model.name = name;
            var anchor = new Dictionary<string, Transform>();
            foreach (var item in model.GetComponentsInChildren<Transform>(true))
                if (item.name.StartsWith("Survey", StringComparison.Ordinal)) anchor[item.name] = item;
            var origin = anchor["SurveyOrigin"].position;
            Vector3 north = anchor["SurveyNorth"].position - origin;
            Vector3 up = anchor["SurveyUp"].position - origin;
            // Align Blender's metre and its design axes with the two marked Unity axes.
            model.transform.localScale *= scale / up.magnitude;
            model.transform.rotation = Quaternion.Euler(0,yaw,0) *
                Quaternion.Inverse(Quaternion.LookRotation(north,up)) * model.transform.rotation;
            model.transform.position += position - anchor["SurveyOrigin"].position;
            foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                string slot = renderer.name;
                if (!slot.StartsWith("HQ_",StringComparison.Ordinal))
                    throw new InvalidOperationException("Unmapped mesh in HQ export: "+slot);
                renderer.sharedMaterial = Resolve(slot.Substring(3));
            }
            return model;
        }

        var table = InstallModel("CommandTable",new Vector3(0,0,1.10f));
        var drip = new GameObject("Rainwater at the map edge");
        drip.transform.SetParent(room,false);
        drip.transform.position = table.GetComponentsInChildren<Transform>().First(t=>t.name=="SurveyMapDrip").position;
        drip.AddComponent<MeshFilter>();
        var sheen = drip.AddComponent<MeshRenderer>();
        sheen.sharedMaterial = Resolve("Water");
        sheen.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        drip.AddComponent<OsowiecDeskRain>();
        InstallModel("DugoutStores",Vector3.zero);
        var blocker = new GameObject("Table collision").AddComponent<BoxCollider>();
        blocker.transform.SetParent(room,false);
        blocker.center = new(0,.56f,1.10f);
        blocker.size = new(2.84f,1.12f,1.54f);

        void Lamp(Vector3 basePosition, float scale)
        {
            InstallModel("StormLantern",basePosition,0,scale);
            var light = new GameObject("Kerosene light").AddComponent<Light>();
            light.transform.SetParent(room,false);
            light.transform.position = basePosition + Vector3.up*(.24f*scale);
            light.type = LightType.Point;
            light.color = new Color(1f,.69f,.419f);
        }

        Lamp(new Vector3(.98f,1.123f,1.60f),1.08f);
        // Lamps sit on small steel brackets. Only the table contributes a navigation obstacle.
        foreach (float side in new[]{-1f,1f})
        foreach (float z in new[]{-1.4f,3.75f})
        {
            var shelf = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shelf.name="Forged lamp bracket";
            shelf.transform.SetParent(room, false);
            shelf.transform.localPosition = new(side*4.185f,2.02f,z);
            shelf.transform.localScale = new(.40f,.028f,.23f);
            shelf.GetComponent<MeshRenderer>().sharedMaterial = Resolve("Oxide");
            Object.DestroyImmediate(shelf.GetComponent<Collider>());
            Lamp(new Vector3(side*4.092f,2.04f,z),.84f);
        }
        InstallModel("SurveyBoard",new Vector3(-.10f,1.90f,5.73f),0,1.12f);
        InstallModel("FieldGramophone",new Vector3(3.18f,0,2.12f),-33f,1f);
        var stand = new GameObject("Phonograph stand collision").AddComponent<BoxCollider>();
        stand.transform.SetParent(room,false);
        stand.transform.SetPositionAndRotation(new Vector3(3.18f,.52f,2.12f),Quaternion.Euler(0,-33f,0));
        stand.size = new(.76f,1.04f,.52f);
        var boardLight = new GameObject("Board wall wash").AddComponent<Light>();
        boardLight.transform.SetParent(room,false);
        boardLight.type = LightType.Spot;
        boardLight.transform.SetPositionAndRotation(new Vector3(-1.68f,2.70f,4.35f),
            Quaternion.LookRotation(new Vector3(1.5f,-.58f,1.38f)));
        boardLight.color = new(.96f,.746f,.49f);
        boardLight.intensity = 5f;
        boardLight.spotAngle = 74f;
        boardLight.range = 4.5f;
        LightDugout(room);
    }

    public static void LightDugout(Transform room)
    {
        RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor=new Color(.20f,.22f,.23f);
        RenderSettings.ambientEquatorColor=new Color(.135f,.122f,.105f);
        RenderSettings.ambientGroundColor=new Color(.082f,.072f,.063f);
        foreach(var light in room.GetComponentsInChildren<Light>(true))
        {
            if(light.name=="Board wall wash")
            {
                light.intensity=2.0f;
                light.color=new Color(1f,.80f,.62f);
                continue;
            }
            if(light.name!="Kerosene light")continue;
            var pos=light.transform.localPosition;
            bool table=Mathf.Abs(pos.x)<2f;
            bool near=pos.z<0;
            // Move the emitter just past its globe; the iron cage and chimney would otherwise fill its shadow map.
            pos.x=table ? .82f : Mathf.Sign(pos.x)*3.88f;
            light.transform.localPosition=pos;
            light.shadows=table || (near && pos.x<0) ? LightShadows.Soft : LightShadows.None;
            light.shadowStrength=.74f;light.shadowNearPlane=.085f;light.shadowBias=.035f;light.shadowNormalBias=.16f;
            light.color=table ? new Color(1f,.80f,.59f) : new Color(1f,.73f,.50f);
            light.intensity=table ? 3.45f : near ? (pos.x<0 ? 2.9f : 1.65f) : 2.0f;
            light.range=table ? 5.6f : 5.0f;
        }
    }

    public static void LayFloor(Transform room, Material iron)
    {
        var random = new System.Random(1915);
        var vertices = new List<Vector3>();
        var coordinates = new List<Vector2>();
        var woodIndices = new List<int>();
        var nailIndices = new List<int>();
        float Range(float min, float max) => Mathf.Lerp(min,max,(float)random.NextDouble());
        const float width = .258f;
        for (int row=0; row<35; row++)
        {
            float x = (row-17)*.26f;
            float start = -6.03f;
            while (start<6.02f)
            {
                float end = Mathf.Min(6.03f,start+Range(1.55f,3.2f));
                if (6.03f-end<.65f) end=6.03f;
                float centerY = Range(.025f,.033f);
                float left = x-width*.5f+Range(.001f,.004f);
                float right = x+width*.5f-Range(.001f,.005f);
                float u = Range(0,.80f), v = Range(0,1);
                float chip=Range(.009f,.031f);
                var rim = new Vector3[] {
                    new(left+chip,centerY,start+.009f), new(left,centerY,start+Range(.025f,.062f)),
                    new(left+.001f,centerY+Range(-.003f,.002f),end-.023f),
                    new(left+.014f,centerY,end-.007f), new(right-.013f,centerY,end-.005f),
                    new(right,centerY,end-Range(.019f,.04f)), new(right,centerY-.001f,start+.017f),
                    new(right-.014f,centerY,start+.005f)
                };
                int first=vertices.Count;
                foreach (var pos in rim)
                {
                    vertices.Add(pos);
                    coordinates.Add(new Vector2(u+(pos.x-left)*.85f,v+(pos.z-start)*.57f));
                }
                for (int i=1;i<rim.Length-1;i++)
                    woodIndices.AddRange(new[]{first,first+i,first+i+1});
                for (int i=0;i<rim.Length;i++)
                {
                    var top=rim[i]; var next=rim[(i+1)%rim.Length];
                    int offset=vertices.Count;
                    vertices.AddRange(new[]{top,top-Vector3.up*.018f,next-Vector3.up*.018f,next});
                    coordinates.AddRange(new[]{new Vector2(u,v),new Vector2(u,v+.022f),
                        new Vector2(u+(next-top).magnitude*.65f,v+.022f),new Vector2(u+(next-top).magnitude*.65f,v)});
                    woodIndices.AddRange(new[]{offset,offset+1,offset+2,offset,offset+2,offset+3});
                }
                foreach (float nailZ in new[]{start+.065f,end-.065f})
                {
                    int center=vertices.Count;
                    float nailX=x+Range(-.065f,.065f);
                    vertices.Add(new Vector3(nailX,centerY+.002f,nailZ)); coordinates.Add(Vector2.zero);
                    for(int j=0;j<6;j++)
                    {
                        float angle=j*Mathf.PI/3;
                        vertices.Add(new Vector3(nailX+Mathf.Cos(angle)*.0075f,centerY+.0014f,nailZ+Mathf.Sin(angle)*.0075f));
                        coordinates.Add(Vector2.zero);
                    }
                    for(int j=0;j<6;j++)
                        nailIndices.AddRange(new[]{center,center+1+(j+1)%6,center+1+j});
                }
                start=end;
            }
        }
        const string meshPath = Art+"Signal floorboards.asset";
        var surface = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (!surface) { surface=new Mesh(); AssetDatabase.CreateAsset(surface,meshPath); }
        surface.Clear(); surface.name="Cut larch and nail heads"; surface.SetVertices(vertices);
        surface.SetUVs(0,coordinates); surface.subMeshCount=2;
        surface.SetTriangles(woodIndices,0); surface.SetTriangles(nailIndices,1);
        surface.RecalculateNormals(); surface.RecalculateTangents(); surface.RecalculateBounds();
        EditorUtility.SetDirty(surface);
        var material=AssetDatabase.LoadAssetAtPath<Material>(Art+"Signal floor.mat");
        if (!material)
        {
            material=new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material,Art+"Signal floor.mat");
        }
        const string timber="Assets/Osowiec/Art/Fort1916/Surfaces/Timber_";
        material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(timber+"colour.jpg"));
        material.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(timber+"normal.jpg"));
        material.EnableKeyword("_NORMALMAP"); material.SetFloat("_BumpScale",.68f);
        material.SetColor("_BaseColor",new Color(.82f,.81f,.76f));
        material.SetFloat("_Smoothness",.24f); EditorUtility.SetDirty(material);
        var floor=new GameObject("Fitted dugout floorboards",typeof(MeshFilter),typeof(MeshRenderer));
        floor.transform.SetParent(room,false); floor.GetComponent<MeshFilter>().sharedMesh=surface;
        floor.GetComponent<MeshRenderer>().sharedMaterials=new[]{material,iron};
    }

    public static void Grain(Material wood, bool alongLongitudinalWall = false)
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "Textures/QuarterSawn.png");
        wood.SetTexture("_BaseMap",tex);
        wood.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Art+"Textures/QuarterSawn_N.png"));
        wood.SetFloat("_BumpScale",.62f);
        wood.EnableKeyword("_NORMALMAP");
        wood.SetTextureScale("_BaseMap",alongLongitudinalWall ? new Vector2(3f,.62f) : new Vector2(1,1));
        // Pigmented wood changes colour at the surface as it wears.
        Color dry = wood.GetColor("_BaseColor");
        wood.SetColor("_BaseColor",Color.Lerp(dry,Color.white,.22f));
        EditorUtility.SetDirty(wood);
    }
}
