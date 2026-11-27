using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class OsowiecAtmosphere : MonoBehaviour
{
    private const string AssetFolder = "Assets/Osowiec/Atmosphere/";
    private static readonly Color FogTint = new Color(.53f, .54f, .415f);

    [SerializeField] private Material fogMaterial;
    [SerializeField] private Material rainMaterial;
    [SerializeField] private Bounds gasBounds = new Bounds(
        new Vector3(-2f, .8f, 5.4f), new Vector3(16f, 3f, 5f));
    [SerializeField] private Bounds casemateCover = new Bounds(
        new Vector3(.45f, 2.2f, -12f), new Vector3(20.1f, 6.4f, 4.4f));
    [SerializeField, Range(0f, 1f)] private float rainIntensity = .78f;
    [SerializeField, Range(0f, 1f)] private float fogIntensity = .67f;

    private ParticleSystem rain;
    private OsowiecMistBody lowlandBody, causewayBody;
    private Texture2D cloudCookie;
    private Mesh hazePlane;
    private Material tacticalHaze;
    internal bool DrawingTacticalHaze { get; private set; }
    private ParticleSystem gas;
    private ParticleSystem bank;
    private ParticleSystem drips;
    private ParticleSystem steam;
    private OsowiecEnvironmentAudio environmentAudio;
    private static OsowiecEnvironmentAudio activeAudio;
    private float nextCameraLookup;
    private Camera observer;

    public static void Create()
    {
        if (FindFirstObjectByType<OsowiecAtmosphere>() != null)
            return;

        var world = new GameObject("Osowiec Atmosphere");
        var atmosphere = world.AddComponent<OsowiecAtmosphere>();
        var activeScene = SceneManager.GetActiveScene();
        foreach (var light in FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (light.gameObject.scene == activeScene && light.type == LightType.Directional)
                light.enabled = false;
        }

        var skyLight = new GameObject("Overcast dawn");
        skyLight.transform.SetParent(world.transform, false);
        skyLight.transform.rotation = Quaternion.Euler(36f, 64f, 0f);
        var sun = skyLight.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(.88f, .92f, .97f);
        sun.intensity = .97f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = .64f;
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.51f, .54f, .58f);
        RenderSettings.ambientEquatorColor = new Color(.35f, .36f, .32f);
        RenderSettings.ambientGroundColor = new Color(.21f, .19f, .16f);
        RenderSettings.ambientIntensity = .85f;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = FogTint;
        RenderSettings.fogDensity = .0128f;

#if UNITY_EDITOR
        atmosphere.rainMaterial = ParticleMaterial("Rain", new Color(.77f, .83f, .87f, 1f), true);
        atmosphere.fogMaterial = ParticleMaterial("Low Fog", new Color(.75f, .79f, .51f, 1f), false);
        var skyShader = Shader.Find("Skybox/Panoramic");
        if (skyShader != null)
        {
            const string panoramaPath = "Assets/Osowiec/Art/Fort1916/Surfaces/OvercastSoil.hdr";
            var importer = UnityEditor.AssetImporter.GetAtPath(panoramaPath) as UnityEditor.TextureImporter;
            if (importer != null && importer.textureShape != UnityEditor.TextureImporterShape.Texture2D)
            {
                importer.textureShape = UnityEditor.TextureImporterShape.Texture2D;
                importer.SaveAndReimport();
            }
            var panorama = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(panoramaPath);
            if (panorama == null) throw new System.InvalidOperationException("Missing overcast panorama " + panoramaPath);
            var sky = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(AssetFolder + "Biebrza Sky.mat");
            if (sky == null)
            {
                sky = new Material(skyShader);
                UnityEditor.AssetDatabase.CreateAsset(sky, AssetFolder + "Biebrza Sky.mat");
            }
            sky.SetTexture("_MainTex", panorama);
            sky.SetColor("_Tint", new Color(.55f, .54f, .45f));
            sky.SetFloat("_Exposure", .76f);
            sky.SetFloat("_Rotation", 144f);
            UnityEditor.EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;
        }

        var profile = UnityEditor.AssetDatabase.LoadAssetAtPath<VolumeProfile>(AssetFolder + "Dawn.asset");
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            UnityEditor.AssetDatabase.CreateAsset(profile, AssetFolder + "Dawn.asset");
            var tonemapping = profile.Add<Tonemapping>();
            tonemapping.mode.Override(TonemappingMode.ACES);
            var grading = profile.Add<ColorAdjustments>();
            grading.postExposure.Override(.08f);
            grading.contrast.Override(6f);
            grading.saturation.Override(-11f);
            var bloom = profile.Add<Bloom>();
            bloom.threshold.Override(1.13f);
            bloom.intensity.Override(.25f);
            var vignette = profile.Add<Vignette>();
            vignette.intensity.Override(.13f);
            foreach (var effect in profile.components)
                UnityEditor.AssetDatabase.AddObjectToAsset(effect, profile);
            UnityEditor.EditorUtility.SetDirty(profile);
        }
        var colorGrade = world.AddComponent<Volume>();
        colorGrade.isGlobal = true;
        colorGrade.priority = 5f;
        colorGrade.sharedProfile = profile;
        UnityEditor.EditorUtility.SetDirty(atmosphere);
        UnityEditor.AssetDatabase.SaveAssets();
#endif

        foreach (var camera in FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (camera.gameObject.scene != activeScene || camera.targetTexture != null)
                continue;

            EnableEffects(camera);
        }
    }

#if UNITY_EDITOR
    private static Material ParticleMaterial(string name, Color tint, bool rain)
    {
        string path = AssetFolder + name + ".mat";
        var material = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            var shader = Shader.Find("Steel and Ashes/Osowiec Weather Particle");
            if (shader == null)
                throw new System.InvalidOperationException("Osowiec weather shader was not imported.");
            material = new Material(shader);
            UnityEditor.AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_Tint", tint);
        material.SetFloat("_Rain", rain ? 1f : 0f);
        material.SetFloat("_FadeDistance", rain ? .34f : 1.55f);
        UnityEditor.EditorUtility.SetDirty(material);
        return material;
    }
#endif

    public static void SetGasBounds(Bounds worldBounds)
    {
        var atmosphere = FindFirstObjectByType<OsowiecAtmosphere>();
        if (atmosphere != null)
            atmosphere.gasBounds = worldBounds;
    }

    public static void Pulse(Vector3 position)
    {
        var atmosphere = FindFirstObjectByType<OsowiecAtmosphere>();
        if (atmosphere != null && atmosphere.fogMaterial != null)
            atmosphere.EmitCaptureBurst(position);
        if (activeAudio != null)
            activeAudio.PlayCapture();
    }

    public static bool HasSurfaceFootsteps => (activeAudio != null && activeAudio.isActiveAndEnabled) ||
        OsowiecHeadquartersAudio.ProvidesFootsteps;
    public static bool IsInAudioVault(Vector3 position) => activeAudio != null && activeAudio.InVault(position);
    internal static void PlayContact(Vector3 position) => activeAudio?.PlayWorldCue("SignalCall", position, .62f);
    public static void PlayPneumatic(Vector3 position) => activeAudio?.PlayWorldCue("Pneumatic", position, .76f);
    internal static void PlayReinforcement() => activeAudio?.PlayReinforcement();
    public static void PlayOptics(Vector3 position) => activeAudio?.PlayWorldCue("Optics", position, .70f);
    internal static void PlayKitImpact(Vector3 position,bool smoke)
    {
        activeAudio?.PlayWorldCue(smoke?"SmokeBloom":"GrenadeBurst",position,smoke?.82f:1f);
        if(!smoke) activeAudio?.CloseBlast(position);
        OsowiecHudPaint.Caption(smoke?"[Шипение]":"[Взрыв гранаты]");
    }
    public static void PlayEarthfall(Vector3 position)
    {
        activeAudio?.PlayWorldCue("Earthfall", position, .92f);
        OsowiecHudPaint.Caption("[Осыпается мокрая земля]");
    }
    public static void PlayShellWarning(Vector3 position)
    {
        activeAudio?.PlayWarning(position);
        OsowiecHudPaint.Caption("[Нарастает свист снаряда]");
    }
    public static void ReportEnemyContact() => activeAudio?.ReportEnemyContact();
    public static void PlayImpact(Collider collider, Vector3 point)
    {
        if (activeAudio != null) activeAudio.PlayImpact(collider, point);
    }

    public static void DoorBurst(Vector3 position)
    {
        var air=FindFirstObjectByType<OsowiecAtmosphere>();
        if(air && air.fogMaterial) air.EmitCaptureBurst(position,true);
        activeAudio?.PlayWorldCue("Earthfall",position,.93f);
        activeAudio?.PlayDoor(position);
    }

    public static void PlayDoor(Vector3 position)
    {
        if (activeAudio != null) activeAudio.PlayDoor(position);
        OsowiecHudPaint.Caption("[Скрип ворот]");
    }

    public static void SetMaskAudio(bool active)
    {
        if (activeAudio != null)
            activeAudio.SetMask(active);
    }

    private static void EnableEffects(Camera camera)
    {
        var cameraData = camera.GetUniversalAdditionalCameraData();
        if (cameraData.renderType == CameraRenderType.Base)
        {
            cameraData.renderPostProcessing = true;
            cameraData.requiresDepthTexture = true;
            cameraData.requiresColorTexture = true;
            cameraData.volumeLayerMask |= 1;
        }
    }

    private void Start()
    {
        var sky = RenderSettings.skybox;
        if (sky && sky.HasProperty("_MainTex"))
        {
            Shader.SetGlobalTexture("_OsowiecWaterSky", sky.GetTexture("_MainTex"));
            Shader.SetGlobalFloat("_OsowiecWaterSkyTurn", sky.GetFloat("_Rotation") / 360f);
        }
        lowlandBody=OsowiecMistBody.Install(transform,gasBounds,false);
        causewayBody=OsowiecMistBody.Install(transform,OsowiecOperation.NorthBankMist,true);
        InstallCloudShade();
        environmentAudio = gameObject.AddComponent<OsowiecEnvironmentAudio>();
        activeAudio = environmentAudio;
        if (rainMaterial != null)
        {
            rain = CreateRain();
            drips = CreateDrips();
        }
        if (fogMaterial != null)
        {
            gas = CreateGas(gasBounds,false);
            bank = CreateGas(OsowiecOperation.NorthBankMist,true);
            steam = CreateSteam();
        }
    }

    private void LateUpdate()
    {
        var controller = CameraManager.Instance;
        var gameCamera = controller == null ? null :
            controller.IsActionMode() ? controller.GetActionCamera() : controller.GetTacticalCamera();
        if (gameCamera != null && gameCamera.isActiveAndEnabled && gameCamera != observer)
        {
            observer = gameCamera;
            EnableEffects(observer);
        }
        else if ((gameCamera == null || !gameCamera.isActiveAndEnabled) &&
            (observer == null || !observer.isActiveAndEnabled || Time.unscaledTime >= nextCameraLookup))
        {
            observer = FindObserver();
            nextCameraLookup = Time.unscaledTime + .3f;
            if (observer != null)
                EnableEffects(observer);
        }
        if (observer == null)
            return;

        Vector3 position = observer.transform.position;
        bool overhead = observer.orthographic || position.y > 7.5f;
        bool underCover = casemateCover.Contains(position) || OsowiecFortCutaway.Sheltered(position);
        bool reducedEffects = OsowiecFieldSettings.ReducedEffects || QualitySettings.GetQualityLevel() == 0;
        Shader.SetGlobalFloat("_OsowiecLandscapeLow",reducedEffects ? 1f : 0f);
        bool tactical = controller && !controller.IsActionMode() && !controller.IsHubExploreMode() &&
            observer == controller.GetTacticalCamera();
        DrawingTacticalHaze = tactical && overhead && tacticalHaze;
        if (DrawingTacticalHaze)
        {
            var draw = new RenderParams(tacticalHaze) {
                camera = observer, worldBounds = hazePlane.bounds, receiveShadows = false,
                shadowCastingMode = ShadowCastingMode.Off, lightProbeUsage = LightProbeUsage.Off
            };
            Graphics.RenderMesh(draw,hazePlane,0,Matrix4x4.identity);
        }

        var column=OsowiecOperation.Instance ? OsowiecOperation.Instance.relief : null;
        lowlandBody.Apply(column,reducedEffects);
        causewayBody.Apply(column,reducedEffects);
        if (rain != null)
        {
            rain.transform.position = position + Vector3.up * 4.7f;
            var emission = rain.emission;
            emission.rateOverTime = overhead || underCover ? 0f : rainIntensity * (reducedEffects ? 650f : 1550f);
            if ((overhead || underCover) && rain.particleCount > 0)
                rain.Clear();
        }
        if (gas != null)
            gas.GetComponent<ParticleSystemRenderer>().enabled = !overhead;
        if(bank!=null)
        {
            var relief=OsowiecOperation.Instance ? OsowiecOperation.Instance.relief : null;
            var emission=bank.emission;
            emission.rateOverTime=relief && relief.MistVisible ? (relief.MistActive?18:9) : 0;
            bank.GetComponent<ParticleSystemRenderer>().enabled=!overhead;
        }
        if (steam != null)
            steam.GetComponent<ParticleSystemRenderer>().enabled = !overhead && !reducedEffects;
        if (drips != null)
            drips.GetComponent<ParticleSystemRenderer>().enabled = !overhead;

        RenderSettings.fogColor = tactical ? new Color(.61f,.64f,.65f) : FogTint;
        float desiredDensity = overhead ? .0026f : underCover ? .011f : Mathf.Lerp(.021f, .041f, fogIntensity);
        float blend = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 7f);
        RenderSettings.fogDensity = overhead ? desiredDensity : Mathf.Lerp(RenderSettings.fogDensity, desiredDensity, blend);

        environmentAudio.MixSpace(position, overhead, underCover, casemateCover);
    }

    private void InstallCloudShade()
    {
        var sun=RenderSettings.sun;
        if(!sun || sun.type!=LightType.Directional) return;
        const int side=128;
        cloudCookie=new Texture2D(side,side,TextureFormat.RGBA32,true,true)
            {name="Broken overcast",wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear};
        var pixels=new Color32[side*side];
        for(int y=0;y<side;y++)
        for(int x=0;x<side;x++)
        {
            float u=x*(2*Mathf.PI/side),v=y*(2*Mathf.PI/side);
            float a=Mathf.Cos(u)+Mathf.Sin(v)*2.1f, b=Mathf.Sin(u)*1.85f+Mathf.Cos(v);
            float value=Mathf.PerlinNoise(a+9,b+17)*.72f + Mathf.PerlinNoise(a*2.8f+4,b*2.8f+6)*.28f;
            byte shade=(byte)(255*Mathf.Lerp(.55f,1,Mathf.SmoothStep(0,1,Mathf.InverseLerp(.20f,.78f,value))));
            pixels[y*side+x]=new Color32(shade,shade,shade,shade);
        }
        cloudCookie.SetPixels32(pixels); cloudCookie.Apply(true,true);
        sun.cookie=cloudCookie;
        sun.GetUniversalAdditionalLightData().lightCookieSize=new Vector2(82,82);
        var hazeShader=Resources.Load<Shader>("OsowiecTacticalHaze");
        if (!hazeShader) return;
        tacticalHaze=new Material(hazeShader) {name="Grey mist above the valley"};
        tacticalHaze.SetTexture("_CloudMap",cloudCookie);
        hazePlane=new Mesh {name="Valley mist at treetop level"};
        hazePlane.vertices=new[] {new Vector3(-140,5.1f,-96),new Vector3(-140,5.1f,96),
            new Vector3(140,5.1f,96),new Vector3(140,5.1f,-96)};
        hazePlane.triangles=new[] {0,1,2,0,2,3};
        hazePlane.RecalculateBounds();
    }

    private Camera FindObserver()
    {
        Camera main = Camera.main;
        if (main != null && main.isActiveAndEnabled && main.gameObject.scene == gameObject.scene)
            return main;

        foreach (var camera in Camera.allCameras)
        {
            if (camera.gameObject.scene == gameObject.scene && camera.targetTexture == null
                && camera.cameraType == CameraType.Game
                && (!camera.TryGetComponent<UniversalAdditionalCameraData>(out var data)
                    || data.renderType == CameraRenderType.Base))
                return camera;
        }
        return null;
    }

    private ParticleSystem CreateRain()
    {
        var particles = NewEmitter("Wind-driven rain");
        var main = particles.main;
        main.maxParticles = 1300;
        main.startLifetime = new ParticleSystem.MinMaxCurve(.38f, .72f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(.009f, .018f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(.77f, .81f, .86f, .075f), new Color(.96f, .98f, 1f, .23f));
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(12f, 2f, 12f);
        var velocity = particles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = -2.1f;
        velocity.y = -15f;
        velocity.z = 1.3f;
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = rainMaterial;
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = .029f;
        renderer.lengthScale = 1.58f;
        renderer.maxParticleSize = .12f;
        particles.Play();
        return particles;
    }

    private ParticleSystem CreateGas(Bounds bounds,bool temporary)
    {
        var particles = NewEmitter(temporary?"Wind across the northern causeway":"Ground fog in gameplay zone");
        Vector3 centre=bounds.center;
        centre.y*=.58f;
        if(temporary && UnityEngine.AI.NavMesh.SamplePosition(bounds.center,out var floor,6,UnityEngine.AI.NavMesh.AllAreas))
            centre.y=floor.position.y+.68f;
        particles.transform.position=centre;
        var main = particles.main;
        main.maxParticles = 116;
        main.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 6.5f);
        main.startSize3D = true;
        main.startSizeX = new ParticleSystem.MinMaxCurve(1.8f, 3.35f);
        main.startSizeY = new ParticleSystem.MinMaxCurve(.75f, 1.65f);
        main.startSizeZ = 1f;
        main.startSpeed = 0f;
        main.startRotation = new ParticleSystem.MinMaxCurve(-.19f, .19f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(.67f, .72f, .48f, .12f * fogIntensity),
            new Color(.90f, .94f, .67f, .31f * fogIntensity));
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(bounds.size.x * .78f, .55f, bounds.size.z * .66f);
        var emission = particles.emission;
        emission.rateOverTime = temporary?0f:19f;
        var velocity = particles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = .34f;
        velocity.y = .11f;
        velocity.z = .13f;
        var lifetime = particles.colorOverLifetime;
        lifetime.enabled = true;
        var envelope = new Gradient();
        envelope.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .22f),
                new GradientAlphaKey(.9f, .60f), new GradientAlphaKey(0f, 1f) });
        lifetime.color = envelope;
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = fogMaterial;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.maxParticleSize = .44f;
        if(!temporary)particles.Simulate(5f, false, true);
        particles.Play();
        return particles;
    }

    private ParticleSystem CreateDrips()
    {
        var particles = NewEmitter("Seepage at casemate ceiling");
        particles.transform.position = new Vector3(casemateCover.center.x, casemateCover.max.y - .39f, casemateCover.center.z);
        var main = particles.main;
        main.maxParticles = 65;
        main.startLifetime = new ParticleSystem.MinMaxCurve(.53f, .71f);
        main.startSize = new ParticleSystem.MinMaxCurve(.014f, .024f);
        main.startColor = new Color(.82f, .79f, .67f, .36f);
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(casemateCover.size.x * .90f, .13f, casemateCover.size.z * .84f);
        var emission = particles.emission;
        emission.rateOverTime = 39f;
        var velocity = particles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.y = -4.8f;
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = rainMaterial;
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = .047f;
        renderer.lengthScale = 2f;
        particles.Play();
        return particles;
    }

    private ParticleSystem CreateSteam()
    {
        var particles = NewEmitter("Warm vapour near battery entrance");
        particles.transform.position = new Vector3(casemateCover.max.x + .75f, .13f, casemateCover.center.z - 1.25f);
        var main = particles.main;
        main.maxParticles = 24;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 3.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(.23f, 1.05f);
        main.startColor = new Color(.84f, .84f, .72f, .16f);
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = .36f;
        var emission = particles.emission;
        emission.rateOverTime = 7f;
        var velocity = particles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.x = .35f;
        velocity.y = .54f;
        var scale = particles.sizeOverLifetime;
        scale.enabled = true;
        scale.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, .60f, 1f, 1.6f));
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = fogMaterial;
        particles.Play();
        return particles;
    }

    private void EmitCaptureBurst(Vector3 position,bool masonry=false)
    {
        var particles = NewEmitter(masonry ? "Door dust" : "Battery signal burst");
        particles.transform.position = position;
        var main = particles.main;
        main.loop = false;
        main.duration = .12f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(.6f, 1.15f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(.9f, 2.1f);
        main.startSize = new ParticleSystem.MinMaxCurve(.1f, .31f);
        main.startColor = new Color(1f, .77f, .28f, .63f);
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = masonry ? .45f : .1f;
        if(masonry)
        {
            main.startSize=new ParticleSystem.MinMaxCurve(.24f,.68f);
            main.startColor=new ParticleSystem.MinMaxGradient(new Color(.41f,.31f,.23f,.7f),new Color(.68f,.58f,.47f,.6f));
        }
        var emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 24) });
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = fogMaterial;
        particles.Play();
        Destroy(particles.gameObject, 1.4f);
    }

    private ParticleSystem NewEmitter(string name)
    {
        var root = new GameObject(name);
        root.transform.SetParent(transform, false);
        var particles = root.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.playOnAwake = false;
        main.loop = true;
        main.duration = 8f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var renderer = root.GetComponent<ParticleSystemRenderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return particles;
    }

    private void OnDestroy()
    {
        if(cloudCookie) Destroy(cloudCookie);
        if(tacticalHaze) Destroy(tacticalHaze);
        if(hazePlane) Destroy(hazePlane);
        Shader.SetGlobalFloat("_OsowiecLandscapeLow",0);
        if (activeAudio == environmentAudio)
            activeAudio = null;
    }
}
