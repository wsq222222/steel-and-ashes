using System;
using UnityEngine;
using UnityEngine.Rendering;

// Sparse world-space height field. Only two-metre squares actually walked on occupy
// atlas space. Each page has a one-texel apron painted in both neighbouring pages.
internal sealed class OsowiecMudField : IDisposable
{
    const int Texels = 96, Page = Texels + 2, Columns = 16, Slots = 128, MapSize = 256;
    const float Metres = 2f, Pixel = Metres / Texels, Lifetime = 145f;
    static readonly int AtlasId = Shader.PropertyToID("_OsowiecMudAtlas");
    static readonly int PagesId = Shader.PropertyToID("_OsowiecMudPages");
    static readonly int ClockId = Shader.PropertyToID("_OsowiecMudClock");
    static readonly int EnabledId = Shader.PropertyToID("_OsowiecMudEnabled");

    sealed class Tile
    {
        public int mapIndex = -1;
        public float last;
        public bool dirty;
        public Color32[] samples;
    }

    readonly Tile[] tiles = new Tile[Slots];
    readonly Color32[] mapping = new Color32[MapSize * MapSize];
    readonly Texture2D atlas, pages, staging;
    bool mappingDirty;
    int epoch;
    public bool Visible { get; private set; } = true;

    static Texture2D Texture(string label, int w, int h, FilterMode filter) =>
        new Texture2D(w,h,TextureFormat.RGBA32,false,true) { name=label, wrapMode=TextureWrapMode.Clamp, filterMode=filter };

    public OsowiecMudField()
    {
        for (int i=0; i<Slots; i++) tiles[i]=new Tile();
        if (SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null) return;
        atlas=Texture("Mud height pages",Page*Columns,Page*(Slots/Columns),FilterMode.Bilinear);
        atlas.Apply(false,true);
        pages=Texture("Mud page addresses",MapSize,MapSize,FilterMode.Point);
        staging=Texture("Mud upload",Page,Page,FilterMode.Point);
        pages.SetPixels32(mapping);
        pages.Apply(false);
        Shader.SetGlobalTexture(AtlasId,atlas);
        Shader.SetGlobalTexture(PagesId,pages);
        SetVisible(true);
    }

    public void SetVisible(bool show)
    {
        Visible=show;
        Shader.SetGlobalFloat(EnabledId,show && atlas ? 1 : 0);
    }

    static int MapIndex(int x,int z)
    {
        x+=MapSize/2; z+=MapSize/2;
        return x>=0 && x<MapSize && z>=0 && z<MapSize ? x+z*MapSize : -1;
    }

    // The gallery earthwork overlaps the survey in XZ. It has a separate page
    // address (green) so walking above a passage cannot dent the soil under it.
    int SlotAt(int index) => (index<mapping.Length ? mapping[index].r : mapping[index-mapping.Length].g)-1;

    void SetSlot(int index,int slot)
    {
        byte address=(byte)(slot+1);
        if(index<mapping.Length) mapping[index].r=address;
        else mapping[index-mapping.Length].g=address;
        mappingDirty=true;
    }

    Tile Acquire(int index,float now)
    {
        int existing=SlotAt(index);
        if (existing>=0) return tiles[existing];
        int slot=0;
        for (int i=0;i<Slots;i++)
        {
            if (tiles[i].mapIndex<0) {slot=i; break;}
            if (tiles[i].last<tiles[slot].last) slot=i;
        }
        Tile tile=tiles[slot];
        if (tile.mapIndex>=0) SetSlot(tile.mapIndex,-1);
        if (tile.samples==null) tile.samples=new Color32[Page*Page];
        else Array.Clear(tile.samples,0,tile.samples.Length);
        tile.mapIndex=index;
        tile.last=now;
        SetSlot(index,slot);
        return tile;
    }

    static float Ease(float low,float high,float v)
    {
        float t=Mathf.Clamp01((v-low)/(high-low));
        return t*t*(3-2*t);
    }

    // Signed metres. The heel and ball compress most; the outside arch only drags.
    // The lip is displaced soil. Tread interrupts pressure locally without drawing a sole.
    static float BootProfile(float x,float z)
    {
        float heel=1f-Mathf.Sqrt(x*x/(.054f*.054f)+(z+.105f)*(z+.105f)/(.060f*.060f));
        float bx=x-.007f;
        float ball=1f-Mathf.Sqrt(bx*bx/(.072f*.072f)+(z-.070f)*(z-.070f)/(.102f*.102f));
        float ax=x+.024f;
        float arch=1f-Mathf.Sqrt(ax*ax/(.042f*.042f)+z*z/(.098f*.098f));
        float edge=Mathf.Max(heel,Mathf.Max(ball,arch));
        if (edge<-.42f) return 0;
        float rim=.014f*Ease(-.42f,-.16f,edge)*(1-Ease(-.16f,.06f,edge));
        float pressure=Mathf.Max(.057f*Ease(-.02f,.62f,heel),.046f*Ease(-.04f,.56f,ball));
        pressure=Mathf.Max(pressure,.022f*Ease(-.05f,.58f,arch));
        float tread=.83f+.17f*Mathf.Cos(127f*(z+.31f*Mathf.Abs(x)));
        return rim-pressure*tread;
    }

    static float WheelProfile(float x,float z,float halfLength)
    {
        float end=Mathf.Max(0,Mathf.Abs(z)-halfLength);
        float edge=Mathf.Sqrt(x*x+end*end);
        return -.033f*(1-Ease(.017f,.052f,edge))+.008f*Ease(.034f,.054f,edge)*(1-Ease(.054f,.077f,edge));
    }

    public bool Stamp(Vector3 centre,Vector3 forward,bool left,float now,bool gallery,float wheelLength=0)
    {
        Vector2 axis=new Vector2(forward.x,forward.z).normalized;
        if (axis.sqrMagnitude<.9f) return false;
        Vector2 side=new(axis.y,-axis.x);
        if (!left) side=-side;
        bool wheel=wheelLength>0;
        float half=wheelLength*.5f;
        float radius=(wheel?half+.080f:.235f)+Pixel*1.5f;
        int tick=Mathf.FloorToInt(now)%65536;
        int minX=Mathf.FloorToInt((centre.x-radius)/Metres),maxX=Mathf.FloorToInt((centre.x+radius)/Metres);
        int minZ=Mathf.FloorToInt((centre.z-radius)/Metres),maxZ=Mathf.FloorToInt((centre.z+radius)/Metres);
        bool planted=false;
        for (int z=minZ;z<=maxZ;z++)
        for (int x=minX;x<=maxX;x++)
        {
            int index=MapIndex(x,z);
            if (index<0) continue;
            if (gallery) index+=mapping.Length;
            float originX=x*Metres-Pixel*.5f,originZ=z*Metres-Pixel*.5f;
            int loX=Mathf.Max(0,Mathf.CeilToInt((centre.x-radius-originX)/Pixel));
            int hiX=Mathf.Min(Page-1,Mathf.FloorToInt((centre.x+radius-originX)/Pixel));
            int loZ=Mathf.Max(0,Mathf.CeilToInt((centre.z-radius-originZ)/Pixel));
            int hiZ=Mathf.Min(Page-1,Mathf.FloorToInt((centre.z+radius-originZ)/Pixel));
            Tile tile=null;
            for (int iz=loZ;iz<=hiZ;iz++)
            for (int ix=loX;ix<=hiX;ix++)
            {
                Vector2 rel=new(originX+ix*Pixel-centre.x,originZ+iz*Pixel-centre.z);
                float across=Vector2.Dot(rel,side),along=Vector2.Dot(rel,axis);
                float h=wheel?WheelProfile(across,along,half):BootProfile(across,along);
                if (Mathf.Abs(h)<.0003f) continue;
                tile??=Acquire(index,now);
                int pixel=iz*Page+ix;
                Color32 old=tile.samples[pixel];
                float prior=DecodeHeight(old,tick);
                // A second pass of the wheel or boot presses down a previously raised rim.
                if (h<0) h=Mathf.Min(h,prior);
                else if (prior<-.004f) continue;
                else h=Mathf.Max(prior,h);
                tile.samples[pixel]=new Color32((byte)Mathf.Clamp(Mathf.RoundToInt(-h*3600),0,255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(h*3600),0,255),(byte)(tick>>8),(byte)(tick&255));
                tile.last=now; tile.dirty=true; planted=true;
            }
        }
        return planted;
    }

    static float DecodeHeight(Color32 sample,int tick)
        => (sample.g-sample.r)*(1f/3600)*(1-Ease(38,Lifetime,tick-(sample.b*256+sample.a)));

    // The test samples the same height texels that feed the domain shader.
    public float ReadHeight(Vector3 point,float now,bool gallery)
    {
        int x=Mathf.FloorToInt(point.x/Metres),z=Mathf.FloorToInt(point.z/Metres),index=MapIndex(x,z);
        if (index<0) return 0;
        int slot=SlotAt(gallery ? index+mapping.Length : index);
        if (slot<0) return 0;
        Tile tile=tiles[slot];
        int ix=Mathf.Clamp(1+Mathf.FloorToInt((point.x-x*Metres)/Pixel),0,Page-1);
        int iz=Mathf.Clamp(1+Mathf.FloorToInt((point.z-z*Metres)/Pixel),0,Page-1);
        return DecodeHeight(tile.samples[iz*Page+ix],Mathf.FloorToInt(now)%65536);
    }

    public void Flush(float now)
    {
        int currentEpoch=Mathf.FloorToInt(now/65536f);
        for (int i=0;i<Slots;i++)
        {
            Tile t=tiles[i];
            if (t.mapIndex<0) continue;
            if (currentEpoch!=epoch || now-t.last>=Lifetime)
            {
                SetSlot(t.mapIndex,-1); t.mapIndex=-1; t.dirty=false; continue;
            }
            if (!t.dirty) continue;
            if (atlas)
            {
                staging.SetPixelData(t.samples,0);
                staging.Apply(false);
                Graphics.CopyTexture(staging,0,0,0,0,Page,Page,atlas,0,0,(i%Columns)*Page,(i/Columns)*Page);
            }
            t.dirty=false;
        }
        epoch=currentEpoch;
        if (mappingDirty && pages) { pages.SetPixels32(mapping); pages.Apply(false); }
        mappingDirty=false;
        Shader.SetGlobalFloat(ClockId,now-currentEpoch*65536f);
    }

    public void Dispose()
    {
        SetVisible(false);
        if (atlas) UnityEngine.Object.Destroy(atlas);
        if (pages) UnityEngine.Object.Destroy(pages);
        if (staging) UnityEngine.Object.Destroy(staging);
    }
}
