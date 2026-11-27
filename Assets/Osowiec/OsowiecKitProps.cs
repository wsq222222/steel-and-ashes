using UnityEngine;

public static class OsowiecKitProps
{
    const string Models = "OsowiecKit/Models/";
    static Material finish;
    static Texture2D gasFinish;

    public static GameObject Create(OsowiecKitItem item, Transform parent, Unit owner = null)
    {
        if (item == OsowiecKitItem.Rifle) return null;
        var prefab = Resources.Load<GameObject>(Models + item);
        if (!finish) finish = Resources.Load<Material>(Models + "FieldSurface");
        if (!prefab || !finish)
        {
            Debug.LogError("Missing Osowiec equipment art for " + item);
            return null;
        }

        var prop = new GameObject(item.ToString());
        prop.transform.SetParent(parent, false);
        var visual = Object.Instantiate(prefab);
        var renderer = visual.GetComponentInChildren<MeshRenderer>();
        // OBJ unit conversion varies between importer versions. Normalize in the model's authored Y-up space.
        float height = item switch
        {
            OsowiecKitItem.Bayonet => .293f,
            OsowiecKitItem.Gas => .189f,
            OsowiecKitItem.Smoke => .154f,
            OsowiecKitItem.Grenade when owner && owner.chessType==ChessUnitType.Pawn => .17f,
            _ => .144f
        };
        float importedHeight = renderer.bounds.size.y;
        visual.transform.SetParent(prop.transform, false);
        visual.transform.localPosition = Vector3.zero;
        if (importedHeight > .00001f) visual.transform.localScale *= height / importedHeight;
        if (item == OsowiecKitItem.Bayonet) visual.transform.localRotation = Quaternion.Euler(90, 0, 0);
        renderer.sharedMaterial = finish;
        if(OsowiecFieldKit.Throwable(item) && parent)
        {
            var ringPrefab=Resources.Load<GameObject>(Models+"KitRing");
            var ring=Object.Instantiate(ringPrefab,prop.transform,false);
            ring.name="Ring";
            var ringRenderer=ring.GetComponentInChildren<MeshRenderer>();
            ringRenderer.sharedMaterial=finish;
            ring.transform.localScale=visual.transform.localScale;
            ring.transform.localPosition=new Vector3(.014f,height*.55f,-.006f);
        }
        if(item == OsowiecKitItem.Gas || item == OsowiecKitItem.Grenade && owner && owner.chessType == ChessUnitType.Pawn)
        {
            var color = item == OsowiecKitItem.Gas ? new Color(.94f,.98f,.83f) : new Color(.56f,.41f,.32f);
            var properties = new MaterialPropertyBlock();
            if(item==OsowiecKitItem.Gas)
            {
                if(!gasFinish)gasFinish=Resources.Load<Texture2D>(Models+"FieldGasFinish");
                properties.SetTexture("_BaseMap",gasFinish);
                properties.SetTexture("_MainTex",gasFinish);
            }
            properties.SetColor("_BaseColor", color);
            properties.SetColor("_Color", color);
            renderer.SetPropertyBlock(properties);
        }
        return prop;
    }
}

