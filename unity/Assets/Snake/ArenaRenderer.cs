using System.Collections.Generic;
using SnakeClub.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace SnakeClub
{
    // Uses instanced spheres instead of hundreds of GameObjects, keeping the
    // large arena practical on mobile WebGL while retaining fully 3D geometry.
    public sealed class ArenaRenderer : MonoBehaviour
    {
        public static readonly Color[] Palette =
        {
            new Color(.16f, .94f, .74f), new Color(1f, .39f, .48f), new Color(.46f, .57f, 1f),
            new Color(1f, .72f, .23f), new Color(.75f, .43f, 1f), new Color(.20f, .77f, 1f),
            new Color(1f, .45f, .79f), new Color(.70f, .91f, .30f),
            new Color(.93f, .96f, 1f), new Color(.91f, .20f, .24f)
        };
        private static readonly Color[] FruitColors =
        {
            new Color(1f, .66f, .26f), new Color(.98f, .32f, .47f),
            new Color(.35f, .92f, .70f), new Color(.62f, .57f, 1f)
        };
        private static readonly int[] SkinPalette = { 0, 3, 4, 5, 8, 9, 3, 8, 3, 7, 2, 6, 3, 3, 7, 5, 3, 4, 7, 3, 3, 2, 2, 2 };
        private readonly List<Matrix4x4>[] bodies = new List<Matrix4x4>[Palette.Length];
        private readonly List<Matrix4x4>[] links = new List<Matrix4x4>[Palette.Length];
        private readonly List<Matrix4x4>[] horns = new List<Matrix4x4>[Palette.Length];
        private readonly List<Matrix4x4>[] fruits = new List<Matrix4x4>[4];
        private readonly List<Matrix4x4> eyes = new List<Matrix4x4>();
        private readonly List<Matrix4x4> pupils = new List<Matrix4x4>();
        private readonly List<Matrix4x4> scales = new List<Matrix4x4>();
        private readonly List<Matrix4x4> weakPoints = new List<Matrix4x4>();
        private readonly List<Matrix4x4> threatMarks = new List<Matrix4x4>();
        private readonly List<Matrix4x4> trail = new List<Matrix4x4>();
        private readonly List<Matrix4x4> sparks = new List<Matrix4x4>();
        private readonly List<Matrix4x4> trailPulses = new List<Matrix4x4>();
        private readonly Dictionary<string, List<Vector3>> smoothed = new Dictionary<string, List<Vector3>>();
        private readonly Dictionary<string, SpriteRenderer> characters = new Dictionary<string, SpriteRenderer>();
        private readonly Dictionary<string, CharacterMotion> characterMotions = new Dictionary<string, CharacterMotion>();
        private readonly EliminationTransition eliminations = new EliminationTransition();
        private readonly Dictionary<string, ExitPose> exitPoses = new Dictionary<string, ExitPose>();
        private int presentationTick = -1;
        private readonly ArenaVisualClock visualClock = new ArenaVisualClock();
        private int visualFrame = -1;
        private struct ExitPose { public Vector3 position, scale; }
        private readonly Dictionary<string, LineRenderer[]> characterTrails = new Dictionary<string, LineRenderer[]>();
        private readonly Dictionary<string, LineRenderer> opponentMarkers = new Dictionary<string, LineRenderer>();
        private readonly Dictionary<string, LineRenderer> protectionMarkers = new Dictionary<string, LineRenderer>();
        private readonly List<Transform> crystals = new List<Transform>();
        private Material[] bodyMaterials;
        private Material[] fruitMaterials;
        private Material eyeMaterial;
        private Material pupilMaterial;
        private Material scaleMaterial;
        private Material weakPointMaterial;
        private Material threatMaterial;
        private Material trailLineMaterial;
        private Material trailPulseMaterial;
        private Material outerBackdropMaterial;
        private Mesh sphere;
        private Mesh cylinder;
        private Mesh cone;
        private Mesh captureSweepMesh;
        private Color32 captureSweepColor = new Color32(255, 239, 146, 0);
        private MeshRenderer captureSweepRenderer;
        private readonly List<float> sweepIntersections = new List<float>();
        private readonly List<Vector3> sweepVertices = new List<Vector3>();
        private readonly List<int> sweepTriangles = new List<int>();
        private readonly List<Color32> sweepColors = new List<Color32>();
        private readonly List<float> sweepRows = new List<float>();
        private Camera view;
        private RenderTexture previewTexture;
        private bool previewAttempted;
        public RenderTexture PreviewTexture => previewTexture != null && previewTexture.IsCreated() ? previewTexture : null;
        private LineRenderer marker, eventRing, captureOutline, captureFlare, reboundRipple, directionArrow, bossWarning, dangerArrow;
        private Transform runtimeRoot;
        private GameObject starObject;
        private bool cameraReady;
        private int lastTick = -1, lastKills;
        private float burstStarted = -10f;
        private Vector3 burstPosition;
        private int shownCaptureTick = -1;
        private float captureStarted = -10f;
        private float reboundStarted = -10f, previousRebound;
        private LineRenderer strengthAttackRing, strengthDamageRing, strengthPickupPendingRing, strengthLevelRing;
        private ArenaState strengthImpactSource;
        private int seenStrengthAttackTick = -1, seenStrengthDamageTick = -1, seenStrengthLevelTick = -1;
        private float strengthAttackStarted = -10f, strengthDamageStarted = -10f, strengthLevelStarted = -10f;
        private Point strengthAttackPoint, strengthDamagePoint;
        private Vector3 reboundPosition;
        public int SelectedSkin { get; set; }
        public bool ReducedEffects { get; set; }
        public bool LegacyDangerWarning { get; set; } = true;
        public bool StrengthBodyVisual { get; set; }
        public bool MotionPaused { get; set; }
        public bool VisualFallback { get; private set; }
        public bool Visible
        {
            get => view != null && view.enabled;
            set
            {
                if (view == null || view.enabled == value) return;
                view.enabled = value;
                if (!value) { cameraReady = false; eliminations.Reset(); exitPoses.Clear(); characterMotions.Clear(); presentationTick = -1; }
            }
        }

        // The wardrobe uses the same camera and renderer as a match, directed
        // to a bounded off-screen texture only while the player requests it.
        public void SetPreviewMode(bool enabled)
        {
            if (view == null) return;
            if (enabled && previewTexture != null && !previewTexture.IsCreated())
            {
                if (view.targetTexture == previewTexture) view.targetTexture = null;
                Destroy(previewTexture); previewTexture = null; previewAttempted = false;
            }
            if (enabled && previewTexture == null && !previewAttempted)
            {
                previewAttempted = true;
                try
                {
                    previewTexture = new RenderTexture(288, 512, 16, RenderTextureFormat.ARGB32) { name = "Wardrobe arena preview" };
                    if (!previewTexture.Create()) { Destroy(previewTexture); previewTexture = null; }
                }
                catch (System.Exception) { if (previewTexture != null) Destroy(previewTexture); previewTexture = null; }
            }
            if (!enabled)
            {
                previewAttempted = false;
                if (view.targetTexture != null) { view.targetTexture = null; cameraReady = false; }
                if (previewTexture != null) { previewTexture.Release(); Destroy(previewTexture); previewTexture = null; }
                return;
            }
            RenderTexture target = PreviewTexture;
            if (view.targetTexture != target) { view.targetTexture = target; cameraReady = false; }
        }

        private void Awake() => Initialize();

        private void OnEnable()
        {
            // Unity reconstructs managed collections on a script reload in Play mode.
            if (Application.isPlaying && (view == null || bodies[0] == null)) Initialize();
        }

        private void Initialize()
        {
            for (int i = 0; i < bodies.Length; i++) { bodies[i] = new List<Matrix4x4>(); links[i] = new List<Matrix4x4>(); horns[i] = new List<Matrix4x4>(); }
            for (int i = 0; i < fruits.Length; i++) fruits[i] = new List<Matrix4x4>();
            var sample = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere = sample.GetComponent<MeshFilter>().sharedMesh;
            Destroy(sample);
            var cylinderSample = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinder = cylinderSample.GetComponent<MeshFilter>().sharedMesh;
            Destroy(cylinderSample);
            cone = CreateCone();
            bodyMaterials = new Material[Palette.Length];
            fruitMaterials = new Material[FruitColors.Length];
            for (int i = 0; i < Palette.Length; i++) bodyMaterials[i] = MakeMaterial(Palette[i], .28f);
            for (int i = 0; i < FruitColors.Length; i++) fruitMaterials[i] = MakeMaterial(FruitColors[i], .15f);
            eyeMaterial = MakeMaterial(new Color(.96f, .99f, 1f), .15f);
            pupilMaterial = MakeMaterial(new Color(.025f, .035f, .075f), .1f);
            scaleMaterial = MakeMaterial(new Color(.70f, 1f, .9f), .2f);
            scaleMaterial.EnableKeyword("_EMISSION");
            weakPointMaterial = MakeMaterial(new Color(1f, .83f, .20f), .05f);
            weakPointMaterial.EnableKeyword("_EMISSION");
            weakPointMaterial.SetColor("_EmissionColor", new Color(.25f, .16f, .02f));
            threatMaterial = MakeMaterial(new Color(1f, .25f, .15f), .05f);
            threatMaterial.EnableKeyword("_EMISSION");
            threatMaterial.SetColor("_EmissionColor", new Color(.28f, .035f, .01f));
            trailPulseMaterial = MakeMaterial(new Color(.95f, 1f, .98f), .1f);
            trailPulseMaterial.EnableKeyword("_EMISSION");
            trailPulseMaterial.SetColor("_EmissionColor", new Color(.18f, .22f, .20f));
            var lineShader = Shader.Find("Sprites/Default");
            trailLineMaterial = lineShader != null ? new Material(lineShader) : MakeMaterial(Color.white, 0);
            SetupScene();
        }

        private Material MakeMaterial(Color color, float metallic)
        {
            Material template = Resources.Load<Material>("ArenaMaterial");
            Material material = template != null ? new Material(template) : new Material(Shader.Find("Standard"));
            material.color = color;
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Glossiness", .55f);
            material.enableInstancing = true;
            return material;
        }

        private void SetupScene()
        {
            var previous = transform.Find("Arena Runtime");
            if (previous != null) { previous.gameObject.SetActive(false); Destroy(previous.gameObject); }
            var runtime = new GameObject("Arena Runtime");
            runtime.transform.SetParent(transform, false); runtimeRoot = runtime.transform; characters.Clear(); characterTrails.Clear(); opponentMarkers.Clear(); protectionMarkers.Clear();
            RenderSettings.ambientLight = new Color(.44f, .48f, .61f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(.025f, .045f, .09f);
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 95;
            RenderSettings.fogEndDistance = 180;

            var cameraObject = new GameObject("Arena Camera");
            cameraObject.transform.SetParent(runtimeRoot, false);
            view = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = RenderSettings.fogColor;
            view.orthographic = true;
            view.orthographicSize = 21;
            view.nearClipPlane = .3f;
            view.farClipPlane = 250;
            view.cullingMask &= ~(1 << 31);
            view.transform.rotation = Quaternion.Euler(70, 0, 0);
            cameraObject.tag = "MainCamera";

            var lightObject = new GameObject("Sun");
            lightObject.transform.SetParent(runtimeRoot, false);
            var sun = lightObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.15f;
            sun.color = new Color(.83f, .91f, 1f);
            sun.shadows = LightShadows.None;
            lightObject.transform.rotation = Quaternion.Euler(50, -30, 0);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.SetParent(runtimeRoot, false);
            floor.name = "Arena Floor";
            floor.transform.localScale = Vector3.one * (Arena.WorldHalfSize * 2 / 10f);
            Destroy(floor.GetComponent<Collider>());
            var ground = Resources.Load<Texture2D>("ArenaStage");
            if (ground == null) ground = Resources.Load<Texture2D>("ArenaFloor");
            var floorMat = MakeMaterial(ground != null ? new Color(.82f, .88f, .94f) : new Color(.17f, .26f, .36f), .05f);
            if (ground != null)
            {
                floorMat.mainTexture = ground;
                floorMat.mainTextureScale = Vector2.one * 7;
            }
            else
            {
                var grid = new Texture2D(64, 64, TextureFormat.RGBA32, false);
                grid.wrapMode = TextureWrapMode.Repeat;
                for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
                {
                    float line = (x < 2 || y < 2) ? .22f : 0f;
                    float pattern = ((x / 32 + y / 32) % 2 == 0) ? .035f : 0f;
                    grid.SetPixel(x, y, new Color(.20f + line + pattern, .30f + line + pattern, .40f + line + pattern, 1));
                }
                grid.Apply(); floorMat.mainTexture = grid; floorMat.mainTextureScale = Vector2.one * 44;
            }
            floor.GetComponent<Renderer>().sharedMaterial = floorMat;

            // The playable plane ends at the reflecting rails. Continue the
            // scenery below it so an edge-following camera never reveals a
            // hard black void; this backdrop has no collider or gameplay role.
            var outerFloor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            outerFloor.transform.SetParent(runtimeRoot, false);
            outerFloor.name = "Arena edge backdrop";
            Destroy(outerFloor.GetComponent<Collider>());
            outerFloor.transform.position = new Vector3(0, -.12f, 0);
            outerFloor.transform.localScale = Vector3.one * (Arena.WorldHalfSize * 4 / 10f);
            outerBackdropMaterial = new Material(floorMat) { color = new Color(.30f, .48f, .52f) };
            outerBackdropMaterial.mainTextureScale = floorMat.mainTextureScale * 2;
            outerFloor.GetComponent<Renderer>().sharedMaterial = outerBackdropMaterial;

            var railMat = MakeMaterial(new Color(.20f, .89f, .86f), .1f);
            railMat.EnableKeyword("_EMISSION");
            railMat.SetColor("_EmissionColor", new Color(.025f, .20f, .18f));
            float edge = Arena.WorldHalfSize;
            for (int i = 0; i < 4; i++)
            {
                var rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rail.transform.SetParent(runtimeRoot, false);
                rail.name = "Reflecting Boundary";
                Destroy(rail.GetComponent<Collider>());
                rail.GetComponent<Renderer>().sharedMaterial = railMat;
                rail.transform.position = i < 2 ? new Vector3(i == 0 ? -edge : edge, .45f, 0) : new Vector3(0, .45f, i == 2 ? -edge : edge);
                rail.transform.localScale = i < 2 ? new Vector3(.65f, .9f, edge * 2) : new Vector3(edge * 2, .9f, .65f);
            }

            var crystalMat = MakeMaterial(new Color(.12f, .58f, .62f), .25f);
            crystalMat.EnableKeyword("_EMISSION"); crystalMat.SetColor("_EmissionColor", new Color(.015f, .09f, .095f));
            var decorationRandom = new System.Random(20261116);
            for (int i = 0; i < 32; i++)
            {
                float x = (float)(decorationRandom.NextDouble() * 194 - 97);
                float z = (float)(decorationRandom.NextDouble() * 194 - 97);
                if (Mathf.Abs(x) < 18 && Mathf.Abs(z) < 18) x += x < 0 ? -25 : 25;
                var crystal = GameObject.CreatePrimitive(PrimitiveType.Cube);
                crystal.transform.SetParent(runtimeRoot, false);
                crystal.name = "Cave crystal"; Destroy(crystal.GetComponent<Collider>());
                float height = 1.3f + (float)decorationRandom.NextDouble() * 1.8f;
                crystal.transform.position = new Vector3(x, height * .45f, z);
                crystal.transform.rotation = Quaternion.Euler(0, decorationRandom.Next(45), 45);
                crystal.transform.localScale = new Vector3(.5f, height, .5f);
                crystal.GetComponent<Renderer>().sharedMaterial = crystalMat;
                crystals.Add(crystal.transform);
            }

            starObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            starObject.transform.SetParent(runtimeRoot, false);
            starObject.name = "Star core"; Destroy(starObject.GetComponent<Collider>());
            var starMaterial = MakeMaterial(new Color(1f, .78f, .30f), .35f);
            starMaterial.EnableKeyword("_EMISSION"); starMaterial.SetColor("_EmissionColor", new Color(.5f, .24f, .025f));
            starObject.GetComponent<Renderer>().sharedMaterial = starMaterial; starObject.SetActive(false);

            var ring = new GameObject("Player Marker");
            ring.transform.SetParent(runtimeRoot, false);
            marker = ring.AddComponent<LineRenderer>();
            marker.useWorldSpace = true;
            marker.loop = true;
            marker.positionCount = 48;
            marker.widthMultiplier = .08f;
            marker.sharedMaterial = trailLineMaterial; marker.sortingOrder = 4;
            marker.startColor = marker.endColor = new Color(.7f, 1f, .9f, .9f);
            marker.shadowCastingMode = ShadowCastingMode.Off;
            var arrowObject = new GameObject("Movement direction"); arrowObject.transform.SetParent(runtimeRoot, false);
            directionArrow = arrowObject.AddComponent<LineRenderer>(); directionArrow.useWorldSpace = true;
            directionArrow.positionCount = 3; directionArrow.widthMultiplier = .14f;
            directionArrow.sharedMaterial = trailLineMaterial; directionArrow.sortingOrder = 4;
            directionArrow.startColor = directionArrow.endColor = new Color(.9f, 1f, .95f, 1f);
            directionArrow.shadowCastingMode = ShadowCastingMode.Off;
            var dangerObject = new GameObject("Nearby danger direction"); dangerObject.transform.SetParent(runtimeRoot, false);
            dangerArrow = dangerObject.AddComponent<LineRenderer>(); dangerArrow.useWorldSpace = true;
            dangerArrow.positionCount = 3; dangerArrow.widthMultiplier = .18f;
            dangerArrow.sharedMaterial = trailLineMaterial; dangerArrow.sortingOrder = 5;
            dangerArrow.startColor = dangerArrow.endColor = new Color(1f, .68f, .28f, 1f);
            dangerArrow.shadowCastingMode = ShadowCastingMode.Off;
            var bossObject = new GameObject("Boss attack warning"); bossObject.transform.SetParent(runtimeRoot, false);
            bossWarning = bossObject.AddComponent<LineRenderer>(); bossWarning.useWorldSpace = true;
            bossWarning.positionCount = 2; bossWarning.widthMultiplier = .20f; bossWarning.sortingOrder = 3;
            bossWarning.sharedMaterial = trailLineMaterial; bossWarning.startColor = bossWarning.endColor = new Color(1f, .55f, .18f, 1f);
            bossWarning.shadowCastingMode = ShadowCastingMode.Off;
            var eventObject = new GameObject("Event boundary");
            eventObject.transform.SetParent(runtimeRoot, false);
            eventRing = eventObject.AddComponent<LineRenderer>();
            eventRing.useWorldSpace = true; eventRing.loop = true; eventRing.positionCount = 64;
            eventRing.widthMultiplier = .12f; eventRing.material = MakeMaterial(new Color(1f, .73f, .29f), 0);
            eventRing.startColor = eventRing.endColor = new Color(1f, .78f, .35f, .65f);
            eventRing.shadowCastingMode = ShadowCastingMode.Off;
            var captureObject = new GameObject("Capture outline"); captureObject.transform.SetParent(runtimeRoot, false);
            captureOutline = captureObject.AddComponent<LineRenderer>();
            captureOutline.useWorldSpace = true; captureOutline.loop = true; captureOutline.enabled = false;
            captureOutline.widthMultiplier = .22f; captureOutline.sharedMaterial = trailLineMaterial;
            captureOutline.startColor = captureOutline.endColor = new Color(1f, .94f, .60f, 1f);
            captureOutline.shadowCastingMode = ShadowCastingMode.Off;
            var sweepObject = new GameObject("Captured area sweep"); sweepObject.transform.SetParent(runtimeRoot, false);
            captureSweepMesh = new Mesh { name = "Capture sweep mesh" };
            captureSweepMesh.MarkDynamic();
            sweepObject.AddComponent<MeshFilter>().sharedMesh = captureSweepMesh;
            captureSweepRenderer = sweepObject.AddComponent<MeshRenderer>();
            captureSweepRenderer.sharedMaterial = trailLineMaterial;
            captureSweepRenderer.shadowCastingMode = ShadowCastingMode.Off;
            captureSweepRenderer.receiveShadows = false; captureSweepRenderer.enabled = false;
            captureFlare = MakeEffectRing("Capture closing point", new Color(1f, .94f, .60f, 1f));
            reboundRipple = MakeEffectRing("Rebound ripple", new Color(.55f, 1f, .91f, 1f));
            strengthAttackRing = MakeEffectRing("Strength attack impact", new Color(1f, .86f, .30f, 1f));
            strengthDamageRing = MakeEffectRing("Strength damage impact", new Color(1f, .32f, .25f, 1f));
            strengthPickupPendingRing = MakeEffectRing("Strength pickup warning", new Color(1f, .78f, .30f, 1f));
            strengthLevelRing = MakeEffectRing("Strength level up", new Color(.65f, 1f, .60f, 1f));
        }

        private LineRenderer MakeEffectRing(string name, Color color)
        {
            var item = new GameObject(name); item.transform.SetParent(runtimeRoot, false);
            var line = item.AddComponent<LineRenderer>(); line.useWorldSpace = true; line.loop = true;
            line.positionCount = 32; line.widthMultiplier = .09f; line.sharedMaterial = trailLineMaterial;
            line.startColor = line.endColor = color; line.sortingOrder = 5;
            line.shadowCastingMode = ShadowCastingMode.Off; line.enabled = false;
            return line;
        }

        private static void PlaceRing(LineRenderer line, Vector3 center, float radius)
        {
            for (int i = 0; i < line.positionCount; i++)
            {
                float angle = i * Mathf.PI * 2f / line.positionCount;
                line.SetPosition(i, center + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius));
            }
        }

        public void Draw(ArenaState state, string playerId)
        {
            if (state == null) return;
            if (visualFrame != Time.frameCount)
            {
                visualFrame = Time.frameCount;
                visualClock.Advance(Time.deltaTime, MotionPaused);
            }
            if (state.tick < presentationTick) { eliminations.Reset(); exitPoses.Clear(); characterMotions.Clear(); }
            presentationTick = state.tick;
            foreach (var list in bodies) list.Clear();
            foreach (var list in links) list.Clear();
            foreach (var list in horns) list.Clear();
            foreach (var list in fruits) list.Clear();
            eyes.Clear(); pupils.Clear(); scales.Clear(); weakPoints.Clear(); threatMarks.Clear(); trail.Clear(); sparks.Clear(); trailPulses.Clear();
            foreach (var character in characters.Values) if (character != null) character.enabled = false;
            foreach (var pair in characterTrails.Values) foreach (var line in pair) if (line != null) line.enabled = false;
            foreach (var ring in opponentMarkers.Values) if (ring != null) ring.enabled = false;
            foreach (var ring in protectionMarkers.Values) if (ring != null) ring.enabled = false;
            int playerVisual = SkinVisual.ResolveIndex(SelectedSkin);
            VisualFallback = playerVisual != SelectedSkin;
            bool characterMode = playerVisual >= 0;
            if (state.tick < lastTick) { shownCaptureTick = -1; captureStarted = -10f; reboundStarted = -10f; previousRebound = 0; }
            if (state.captureTick >= 0 && state.captureTick != shownCaptureTick && state.capturePolygon != null && state.capturePolygon.Count > 2)
            {
                shownCaptureTick = state.captureTick; captureStarted = visualClock.Time;
                var captureOwner = state.snakes.Find(s => s.id == state.captureOwner);
                Color captureColor = captureOwner == null ? new Color(1f, .94f, .60f) : Color.Lerp(TrailColor(captureOwner, playerId), Color.white, .35f);
                captureOutline.startColor = captureOutline.endColor = captureColor;
                captureFlare.startColor = captureFlare.endColor = captureColor;
                captureSweepColor = (Color32)captureColor;
                BuildCaptureSweep(state.capturePolygon, state.captureCount > 0);
                captureOutline.positionCount = state.capturePolygon.Count;
                for (int i = 0; i < state.capturePolygon.Count; i++)
                    captureOutline.SetPosition(i, new Vector3(state.capturePolygon[i].x, .20f, state.capturePolygon[i].y));
            }
            captureOutline.enabled = visualClock.Time - captureStarted < .55f;
            if (captureOutline.enabled) captureOutline.widthMultiplier = ReducedEffects ? .24f : .24f * (1f - (visualClock.Time - captureStarted) / .55f);
            float captureAge = visualClock.Time - captureStarted;
            captureSweepRenderer.enabled = !ReducedEffects && captureAge >= 0f && captureAge < .25f && sweepRows.Count > 0;
            if (captureSweepRenderer.enabled) UpdateCaptureSweep(captureAge / .25f);
            captureFlare.enabled = !ReducedEffects && captureAge >= 0f && captureAge < .4f && state.capturePolygon != null && state.capturePolygon.Count > 0;
            if (captureFlare.enabled)
            {
                var hit = state.capturePolygon[0];
                PlaceRing(captureFlare, new Vector3(hit.x, .29f, hit.y), Mathf.Lerp(.15f, .9f, captureAge / .4f));
                captureFlare.widthMultiplier = .12f * (1f - captureAge / .4f);
            }
            SnakeState focus = state.snakes.Find(s => s.id == playerId && s.alive);
            if (focus == null) focus = state.snakes.Find(s => s.alive);
            if (focus != null && focus.segments.Count > 0)
            {
                Point p = focus.segments[0];
                Vector3 target = new Vector3(p.x, 29, p.y - 10.5f);
                view.transform.position = cameraReady ? Vector3.Lerp(view.transform.position, target, 1f - Mathf.Exp(-5f * (MotionPaused ? 0f : Time.deltaTime))) : target;
                view.orthographicSize = Mathf.Lerp(view.orthographicSize, focus.boosting ? 22f : 20f, 1f - Mathf.Exp(-2.5f * (MotionPaused ? 0f : Time.deltaTime)));
                cameraReady = true;
            }
            for (int i = 0; i < crystals.Count; i++)
                if (!ReducedEffects && !MotionPaused && crystals[i] != null) crystals[i].Rotate(Vector3.up, Time.deltaTime * (5f + i % 5), Space.World);
            marker.enabled = false;
            directionArrow.enabled = false;
            dangerArrow.enabled = false;
            bossWarning.enabled = false;
            eventRing.enabled = state.eventStatus == "active" && state.eventKind == "fruit" && state.eventRadius > 0;
            if (eventRing.enabled)
                for (int i = 0; i < eventRing.positionCount; i++)
                {
                    float angle = i * Mathf.PI * 2 / eventRing.positionCount;
                    eventRing.SetPosition(i, new Vector3(state.eventPosition.x + Mathf.Cos(angle) * state.eventRadius, .1f,
                        state.eventPosition.y + Mathf.Sin(angle) * state.eventRadius));
                }
            foreach (var snake in state.snakes)
            {
                if (!snake.alive)
                {
                    if (eliminations.ObserveDead(snake.id, visualClock.Time, out float progress, out bool started) &&
                        characters.TryGetValue(snake.id, out var departing) && departing != null)
                    {
                        if (started) exitPoses[snake.id] = new ExitPose { position = departing.transform.position, scale = departing.transform.localScale };
                        if (!ReducedEffects && exitPoses.TryGetValue(snake.id, out var pose))
                        {
                            departing.enabled = true;
                            departing.transform.position = pose.position + view.transform.up * (.35f * progress);
                            departing.transform.localScale = pose.scale * (1f - .16f * progress);
                            departing.color = new Color(1f, 1f, 1f, 1f - progress);
                        }
                    }
                    else exitPoses.Remove(snake.id);
                    continue;
                }
                if (snake.segments.Count == 0) continue;
                int visualSkin = ResolveVisualSkin(snake, playerId, playerVisual);
                Sprite characterSprite = characterMode && visualSkin >= 0 ? SkinVisual.Sprite(visualSkin) : null;
                int color = PaletteIndex(snake, playerId, visualSkin, characterSprite != null);
                var list = bodies[color];
                List<Vector3> positions;
                if (!smoothed.TryGetValue(snake.id, out positions))
                {
                    positions = new List<Vector3>();
                    smoothed[snake.id] = positions;
                }
                while (positions.Count > snake.segments.Count) positions.RemoveAt(positions.Count - 1);
                while (positions.Count < snake.segments.Count)
                {
                    Point initial = snake.segments[positions.Count];
                    positions.Add(new Vector3(initial.x, .72f, initial.y));
                }
                for (int i = snake.segments.Count - 1; i >= 0; i--)
                {
                    Point p = snake.segments[i];
                    Vector3 target = new Vector3(p.x, .72f + (ReducedEffects ? 0f : .045f * Mathf.Sin(visualClock.Time * 7f - i * .55f)), p.y);
                    positions[i] = characterMode || Vector3.Distance(positions[i], target) > 8f ? target :
                        Vector3.Lerp(positions[i], target, 1f - Mathf.Exp(-16f * (MotionPaused ? 0f : Time.deltaTime)));
                    float taper = characterSprite != null ? .80f : i == 0 ? 1.65f : Mathf.Lerp(.72f, 1.40f, Mathf.Clamp01((snake.segments.Count - i) / 6f));
                    if (characterSprite == null) list.Add(Matrix4x4.TRS(positions[i], Quaternion.identity, Vector3.one * taper));
                    else if (StrengthBodyVisual && i > 0)
                    {
                        // Mark actual body-segment centres; taper makes the
                        // weaker tail distinguishable without adding collision.
                        float progress = (float)i / Mathf.Max(1, snake.segments.Count - 1);
                        float bead = Mathf.Lerp(.36f, .18f, progress);
                        Vector3 point = positions[i]; point.y = .38f;
                        scales.Add(Matrix4x4.TRS(point, Quaternion.identity, Vector3.one * bead));
                    }
                    if (StrengthBodyVisual && i < snake.strengthWeakSegments.Count && snake.strengthWeakSegments[i])
                    {
                        Vector3 point = positions[i];
                        point.y = characterSprite != null ? .45f : positions[i].y + taper * .5f + .12f;
                        weakPoints.Add(Matrix4x4.TRS(point, Quaternion.identity, Vector3.one * .48f));
                    }
                    if (StrengthBodyVisual && i < snake.strengthThreatSegments.Count && snake.strengthThreatSegments[i])
                    {
                        // A cross gives the warning a distinct shape from the
                        // round edible-body markers, including in reduced mode.
                        Vector3 point = positions[i];
                        point.y = characterSprite != null ? .58f : positions[i].y + taper * .5f + .20f;
                        Vector3 size = new Vector3(1.25f, .18f, .28f);
                        threatMarks.Add(Matrix4x4.TRS(point, Quaternion.Euler(0, 45, 0), size));
                        threatMarks.Add(Matrix4x4.TRS(point, Quaternion.Euler(0, -45, 0), size));
                    }
                    if (characterSprite == null && snake.id == playerId && i > 0 && i % (SelectedSkin == 1 ? 3 : 2) == 0)
                    {
                        float scale = SelectedSkin == 2 ? .45f : .35f;
                        scales.Add(Matrix4x4.TRS(positions[i] + Vector3.up * .53f, Quaternion.identity, new Vector3(scale, .17f, scale)));
                    }
                }
                for (int i = 1; i < positions.Count; i++)
                {
                    Vector3 delta = positions[i - 1] - positions[i];
                    if (delta.sqrMagnitude < .0001f) continue;
                    float diameter = characterSprite != null ? .80f : Mathf.Lerp(.70f, 1.27f, Mathf.Clamp01((positions.Count - i) / 6f));
                    if (characterSprite == null)
                        links[color].Add(Matrix4x4.TRS((positions[i - 1] + positions[i]) * .5f, Quaternion.FromToRotation(Vector3.up, delta), new Vector3(diameter, delta.magnitude * .5f, diameter)));
                }
                Vector3 forward = new Vector3(Mathf.Cos(snake.angle), 0, Mathf.Sin(snake.angle));
                Vector3 side = new Vector3(-forward.z, 0, forward.x);
                Vector3 center = positions[0];
                Point actualHead = snake.segments[0];
                Vector3 collisionCenter = new Vector3(actualHead.x, .20f, actualHead.y);
                if (snake.id != playerId) DrawOpponentMarker(snake.id, collisionCenter, Palette[color]);
                if (snake.shield > 0) DrawProtectionMarker(snake.id, collisionCenter, snake.shield);
                if (snake.boss && snake.bossWindup > 0)
                {
                    Vector3 aim = new Vector3(snake.bossAim.x, 0, snake.bossAim.y);
                    bossWarning.enabled = true; bossWarning.SetPosition(0, center + Vector3.down * .42f);
                    bossWarning.SetPosition(1, center + aim * 10f + Vector3.down * .42f);
                    bossWarning.widthMultiplier = ReducedEffects ? .18f : .14f + .08f * (.5f + .5f * Mathf.Sin(visualClock.Time * 12f));
                }
                if (characterSprite != null)
                {
                    DrawCharacterTrail(snake.id, positions, Palette[color], StrengthBodyVisual ? 1 : 2);
                    if (!characters.TryGetValue(snake.id, out var character) || character == null)
                    {
                        var item = new GameObject("Full character " + snake.id); item.transform.SetParent(runtimeRoot, false);
                        character = item.AddComponent<SpriteRenderer>(); character.sortingOrder = 10;
                        characters[snake.id] = character;
                    }
                    eliminations.ObserveAlive(snake.id); exitPoses.Remove(snake.id);
                    // Keep the controlled character recognizable when sprites overlap.
                    // Re-evaluate every frame because online player identity can change.
                    character.sortingOrder = snake.id == playerId ? 20 : 10;
                    character.sprite = characterSprite; character.color = Color.white; character.enabled = true;
                    if (!characterMotions.TryGetValue(snake.id, out var motion))
                        characterMotions[snake.id] = motion = new CharacterMotion();
                    motion.Update(state.tick, snake.angle, snake.boosting, Time.deltaTime, ReducedEffects,
                        !MotionPaused && state.phase == "running");
                    float characterScale = 1.35f * motion.Scale;
                    float bob = ReducedEffects ? 0 : .06f * Mathf.Sin(visualClock.Time * 4f + snake.color);
                    // Anchor the bottom of the camera-facing sprite at the real
                    // head position, leaving the collision marker visible.
                    character.transform.position = center + view.transform.up * (2f * characterScale + bob);
                    character.transform.rotation = view.transform.rotation * Quaternion.Euler(0, 0, -forward.x * 4f + motion.LeanDegrees);
                    character.transform.localScale = Vector3.one * characterScale;
                }
                if (snake.id == playerId)
                {
                    int shape = Mathf.Clamp(SelectedSkin, 0, SkinPalette.Length - 1);
                    if (characterSprite != null) { }
                    else if (shape == 5)
                    {
                        for (int i = -2; i <= 2; i++)
                            horns[color].Add(Matrix4x4.TRS(center + side * (i * .18f) + Vector3.up * .62f,
                                Quaternion.Euler(0, 0, -i * 10f), new Vector3(.27f, .48f + (2 - Mathf.Abs(i)) * .08f, .27f)));
                    }
                    else
                        for (int sign = -1; sign <= 1; sign += 2)
                            horns[color].Add(Matrix4x4.TRS(center + side * (.48f * sign) + Vector3.up * .54f,
                                Quaternion.Euler(0, 0, sign * 15f), new Vector3(.31f, shape == 4 ? .85f : .55f, .28f)));
                    if (state.tick < lastTick) { lastKills = snake.kills; burstStarted = -10f; }
                    if (lastTick >= 0 && snake.kills > lastKills) { burstStarted = visualClock.Time; burstPosition = center; }
                    if (snake.rebound > 0 && previousRebound <= 0) { reboundStarted = visualClock.Time; reboundPosition = center; }
                    previousRebound = snake.rebound;
                    lastKills = snake.kills; lastTick = state.tick;
                }
                for (int sign = -1; characterSprite == null && sign <= 1; sign += 2)
                {
                    Vector3 eye = center + forward * .52f + side * (.37f * sign) + Vector3.up * .37f;
                    eyes.Add(Matrix4x4.TRS(eye, Quaternion.identity, Vector3.one * .38f));
                    pupils.Add(Matrix4x4.TRS(eye + forward * .14f + Vector3.up * .06f, Quaternion.identity, Vector3.one * .18f));
                }
                if (snake.id == playerId)
                {
                    if (!ReducedEffects && positions.Count >= 2)
                    {
                        Vector3 tail = positions[positions.Count - 1];
                        Vector3 back = (tail - positions[positions.Count - 2]).normalized;
                        for (int i = 1; i <= 5; i++)
                        {
                            float size = .29f * (1f - i / 6f) * (1f + .12f * Mathf.Sin(visualClock.Time * 4f + i));
                            Vector3 glow = tail + back * (i * .35f) + Vector3.up * (.12f + .04f * Mathf.Sin(visualClock.Time * 3f + i));
                            trail.Add(Matrix4x4.TRS(glow, Quaternion.identity, Vector3.one * size));
                        }
                    }
                    if (LegacyDangerWarning && (DangerProbe.Find(state, playerId, out var danger) || DangerProbe.FindBeforeProtectionEnds(state, playerId, out danger)))
                    {
                        Point offset = danger - snake.segments[0];
                        if (offset.Length > .01f)
                        {
                            Vector3 toward = new Vector3(offset.x, 0, offset.y).normalized;
                            Vector3 across = new Vector3(-toward.z, 0, toward.x);
                            Vector3 tip = center + toward * 2.3f + Vector3.down * .43f;
                            dangerArrow.enabled = true;
                            dangerArrow.SetPosition(0, tip - toward * .55f + across * .38f);
                            dangerArrow.SetPosition(1, tip);
                            dangerArrow.SetPosition(2, tip - toward * .55f - across * .38f);
                        }
                    }
                    marker.enabled = true;
                    directionArrow.enabled = true;
                    Vector3 arrowTip = center + forward * 1.8f + Vector3.down * .45f;
                    directionArrow.SetPosition(0, arrowTip - forward * .65f + side * .45f);
                    directionArrow.SetPosition(1, arrowTip);
                    directionArrow.SetPosition(2, arrowTip - forward * .65f - side * .45f);
                    marker.widthMultiplier = snake.rebound > 0 ? .18f : .08f;
                    for (int i = 0; i < marker.positionCount; i++)
                    {
                        float angle = i * Mathf.PI * 2 / marker.positionCount;
                        marker.SetPosition(i, collisionCenter + new Vector3(Mathf.Cos(angle) * Arena.CollisionRadius, 0, Mathf.Sin(angle) * Arena.CollisionRadius));
                    }
                }
            }
            if (!ReferenceEquals(strengthImpactSource, state))
            {
                strengthImpactSource = state; seenStrengthAttackTick = seenStrengthDamageTick = seenStrengthLevelTick = -1;
                strengthAttackStarted = strengthDamageStarted = strengthLevelStarted = -10f;
            }
            if (StrengthBodyVisual && !MotionPaused)
            {
                if (state.strengthLevelUpTick >= 0 && state.strengthLevelUpTick != seenStrengthLevelTick &&
                    state.tick >= state.strengthLevelUpTick && state.tick - state.strengthLevelUpTick <= 3)
                { seenStrengthLevelTick = state.strengthLevelUpTick; strengthLevelStarted = visualClock.Time; }
                if (state.strengthAttackTick >= 0 && state.strengthAttackTick != seenStrengthAttackTick &&
                    state.tick >= state.strengthAttackTick && state.tick - state.strengthAttackTick <= 3)
                {
                    seenStrengthAttackTick = state.strengthAttackTick; strengthAttackStarted = visualClock.Time;
                    strengthAttackPoint = state.strengthAttackPoint;
                }
                if (state.strengthDamageTick >= 0 && state.strengthDamageTick != seenStrengthDamageTick &&
                    state.tick >= state.strengthDamageTick && state.tick - state.strengthDamageTick <= 3)
                {
                    seenStrengthDamageTick = state.strengthDamageTick; strengthDamageStarted = visualClock.Time;
                    strengthDamagePoint = state.strengthDamagePoint;
                }
            }
            else if (StrengthBodyVisual && MotionPaused)
            {
                // Do not replay a newly observed event after suspension.
                if (state.strengthLevelUpTick >= 0) seenStrengthLevelTick = state.strengthLevelUpTick;
                if (state.strengthAttackTick >= 0) seenStrengthAttackTick = state.strengthAttackTick;
                if (state.strengthDamageTick >= 0) seenStrengthDamageTick = state.strengthDamageTick;
            }
            var levelPlayer = state.snakes.Find(item => item.id == playerId && item.alive && item.segments.Count > 0);
            float levelAge = visualClock.Time - strengthLevelStarted;
            strengthLevelRing.enabled = StrengthBodyVisual && state.phase == "running" && levelPlayer != null && levelAge >= 0f && levelAge < .8f;
            if (strengthLevelRing.enabled)
            {
                Point head = levelPlayer.segments[0];
                float progress = levelAge / .8f;
                PlaceRing(strengthLevelRing, new Vector3(head.x, .34f, head.y), ReducedEffects ? 1.3f : Mathf.Lerp(1.1f, 2.1f, progress));
                strengthLevelRing.widthMultiplier = ReducedEffects ? .16f : .20f * (1f - progress);
            }
            DrawStrengthImpact(strengthAttackRing, strengthAttackPoint, strengthAttackStarted);
            DrawStrengthImpact(strengthDamageRing, strengthDamagePoint, strengthDamageStarted);
            float reboundAge = visualClock.Time - reboundStarted;
            reboundRipple.enabled = !ReducedEffects && reboundAge >= 0f && reboundAge < .5f;
            if (reboundRipple.enabled)
            {
                PlaceRing(reboundRipple, new Vector3(reboundPosition.x, .27f, reboundPosition.z), Mathf.Lerp(.45f, 1.7f, reboundAge / .5f));
                reboundRipple.widthMultiplier = .12f * (1f - reboundAge / .5f);
            }
            float burstAge = visualClock.Time - burstStarted;
            if (!ReducedEffects && burstAge >= 0 && burstAge < .8f)
            {
                float progress = burstAge / .8f;
                for (int i = 0; i < 16; i++)
                {
                    float angle = i * Mathf.PI * 2 / 16f + progress;
                    Vector3 point = burstPosition + new Vector3(Mathf.Cos(angle) * progress * 3f, .5f + Mathf.Sin(progress * Mathf.PI) * 1.3f, Mathf.Sin(angle) * progress * 3f);
                    sparks.Add(Matrix4x4.TRS(point, Quaternion.identity, Vector3.one * (.28f * (1f - progress))));
                }
            }
            strengthPickupPendingRing.enabled = state.strengthPickupPending && !state.strengthPickupVisible;
            if (strengthPickupPendingRing.enabled)
            {
                Point point = state.strengthPickupPosition;
                float pulse = ReducedEffects ? 1f : .90f + .12f * Mathf.Sin(visualClock.Time * 5f);
                PlaceRing(strengthPickupPendingRing, new Vector3(point.x, .32f, point.y), pulse);
                strengthPickupPendingRing.widthMultiplier = .14f;
            }
            if (starObject != null)
            {
                starObject.SetActive(state.starVisible || state.strengthPickupVisible);
                if (state.starVisible || state.strengthPickupVisible)
                {
                    Point item = state.strengthPickupVisible ? state.strengthPickupPosition : state.starPosition;
                    starObject.transform.position = new Vector3(item.x, 1.5f + (ReducedEffects ? 0f : .2f * Mathf.Sin(visualClock.Time * 3)), item.y);
                    starObject.transform.localScale = Vector3.one * (1.2f + (ReducedEffects ? 0f : .15f * Mathf.Sin(visualClock.Time * 4)));
                }
            }
            foreach (var fruit in state.fruits)
            {
                Point p = fruit.position;
                float pulse = ReducedEffects ? .65f : .65f + .08f * Mathf.Sin(visualClock.Time * 2.4f + p.x + p.y);
                fruits[Mathf.Abs(fruit.color % fruits.Length)].Add(Matrix4x4.TRS(new Vector3(p.x, .52f, p.y), Quaternion.identity, Vector3.one * pulse));
            }
            for (int i = 0; i < bodies.Length; i++) { DrawBatch(links[i], bodyMaterials[i], cylinder); DrawBatch(bodies[i], bodyMaterials[i], sphere); DrawBatch(horns[i], bodyMaterials[i], cone); }
            for (int i = 0; i < fruits.Length; i++) DrawBatch(fruits[i], fruitMaterials[i]);
            scaleMaterial.color = Color.Lerp(Palette[SkinPalette[Mathf.Clamp(SelectedSkin, 0, SkinPalette.Length - 1)]], Color.white, .65f);
            scaleMaterial.SetColor("_EmissionColor", scaleMaterial.color * .18f);
            DrawBatch(scales, scaleMaterial); DrawBatch(weakPoints, weakPointMaterial); DrawBatch(threatMarks, threatMaterial); DrawBatch(trail, scaleMaterial); DrawBatch(trailPulses, trailPulseMaterial); DrawBatch(sparks, fruitMaterials[0]); DrawBatch(eyes, eyeMaterial); DrawBatch(pupils, pupilMaterial);
        }

        private void DrawStrengthImpact(LineRenderer ring, Point point, float started)
        {
            float age = visualClock.Time - started;
            ring.enabled = StrengthBodyVisual && age >= 0f && age < .45f;
            if (!ring.enabled) return;
            float progress = age / .45f;
            PlaceRing(ring, new Vector3(point.x, .32f, point.y), ReducedEffects ? .85f : Mathf.Lerp(.35f, 1.5f, progress));
            ring.widthMultiplier = ReducedEffects ? .16f : .20f * (1f - progress);
        }

        // Thin scanline bands leave the original path and characters readable.
        // They are derived from the resolved polygon and never affect rules.
        private void BuildCaptureSweep(List<Point> polygon, bool captured)
        {
            sweepVertices.Clear(); sweepTriangles.Clear(); sweepColors.Clear(); sweepRows.Clear();
            captureSweepMesh.Clear();
            if (!captured || polygon == null || polygon.Count < 3) return;
            float bottom = polygon[0].y, top = bottom;
            foreach (var point in polygon) { bottom = Mathf.Min(bottom, point.y); top = Mathf.Max(top, point.y); }
            if (top - bottom < .001f) return;
            const int bands = 32;
            float step = (top - bottom) / bands;
            float halfWidth = Mathf.Min(.035f, step * .2f);
            for (int row = 0; row < bands; row++)
            {
                float z = bottom + (row + .5f) * step;
                CaptureGeometry.Scanline(polygon, z, sweepIntersections);
                for (int i = 0; i + 1 < sweepIntersections.Count; i += 2)
                {
                    float left = sweepIntersections[i], right = sweepIntersections[i + 1];
                    if (right - left < .001f) continue;
                    int start = sweepVertices.Count;
                    sweepVertices.Add(new Vector3(left, .025f, z - halfWidth));
                    sweepVertices.Add(new Vector3(left, .025f, z + halfWidth));
                    sweepVertices.Add(new Vector3(right, .025f, z + halfWidth));
                    sweepVertices.Add(new Vector3(right, .025f, z - halfWidth));
                    sweepTriangles.Add(start); sweepTriangles.Add(start + 1); sweepTriangles.Add(start + 2);
                    sweepTriangles.Add(start); sweepTriangles.Add(start + 2); sweepTriangles.Add(start + 3);
                    for (int v = 0; v < 4; v++) { sweepRows.Add((row + .5f) / bands); sweepColors.Add(new Color32(captureSweepColor.r, captureSweepColor.g, captureSweepColor.b, 0)); }
                }
            }
            captureSweepMesh.SetVertices(sweepVertices); captureSweepMesh.SetTriangles(sweepTriangles, 0);
            captureSweepMesh.SetColors(sweepColors); captureSweepMesh.RecalculateBounds();
        }

        private void UpdateCaptureSweep(float progress)
        {
            float wave = Mathf.Lerp(-.22f, 1.22f, progress);
            for (int i = 0; i < sweepRows.Count; i++)
            {
                float intensity = Mathf.Clamp01(1f - Mathf.Abs(sweepRows[i] - wave) / .24f);
                sweepColors[i] = new Color32(captureSweepColor.r, captureSweepColor.g, captureSweepColor.b, (byte)(intensity * 105f));
            }
            captureSweepMesh.SetColors(sweepColors);
        }

        private void OnDestroy()
        {
            if (view != null) view.targetTexture = null;
            if (previewTexture != null) { previewTexture.Release(); Destroy(previewTexture); }
            if (captureSweepMesh != null) Destroy(captureSweepMesh);
            if (trailPulseMaterial != null) Destroy(trailPulseMaterial);
            if (weakPointMaterial != null) Destroy(weakPointMaterial);
            if (threatMaterial != null) Destroy(threatMaterial);
            if (outerBackdropMaterial != null) Destroy(outerBackdropMaterial);
        }

        private int ResolveVisualSkin(SnakeState snake, string playerId, int playerVisual)
        {
            int visualSkin = playerVisual >= 0 ? playerVisual : SelectedSkin;
            if (snake.id != playerId)
            {
                bool referenceMode = playerVisual >= SkinCatalog.OwnedSkinCount;
                int first = referenceMode ? SkinCatalog.OwnedSkinCount : 0;
                int count = referenceMode ? SkinCatalog.All.Length - first : SkinCatalog.OwnedSkinCount;
                int choice = Mathf.Abs(snake.color % (count - 1));
                int playerChoice = Mathf.Clamp(playerVisual - first, 0, count - 1);
                if (choice >= playerChoice) choice++;
                visualSkin = first + choice;
                if (playerVisual >= 0) visualSkin = SkinVisual.ResolveIndex(visualSkin);
            }
            return visualSkin;
        }

        private int PaletteIndex(SnakeState snake, string playerId, int visualSkin, bool hasSprite)
            => hasSprite || snake.id == playerId ? SkinPalette[Mathf.Clamp(visualSkin, 0, SkinPalette.Length - 1)] : Mathf.Abs(snake.color % Palette.Length);

        // Map dots must use the exact same skin fallback and palette as trails.
        public Color TrailColor(SnakeState snake, string playerId)
        {
            int playerVisual = SkinVisual.ResolveIndex(SelectedSkin);
            int visualSkin = ResolveVisualSkin(snake, playerId, playerVisual);
            bool hasSprite = playerVisual >= 0 && visualSkin >= 0 && SkinVisual.Sprite(visualSkin) != null;
            return Palette[PaletteIndex(snake, playerId, visualSkin, hasSprite)];
        }

        private void DrawOpponentMarker(string id, Vector3 center, Color color)
        {
            if (!opponentMarkers.TryGetValue(id, out var ring) || ring == null)
            {
                ring = MakeEffectRing("Opponent collision " + id, color);
                opponentMarkers[id] = ring;
            }
            ring.enabled = true; ring.widthMultiplier = .075f;
            ring.startColor = ring.endColor = Color.Lerp(color, Color.white, .55f);
            PlaceRing(ring, new Vector3(center.x, .20f, center.z), Arena.CollisionRadius);
        }

        private void DrawProtectionMarker(string id, Vector3 center, float remaining)
        {
            if (!protectionMarkers.TryGetValue(id, out var ring) || ring == null)
            {
                ring = MakeEffectRing("Protection " + id, new Color(.40f, .88f, 1f, 1f));
                protectionMarkers[id] = ring;
            }
            bool expiring = remaining <= 1f;
            ring.enabled = true;
            ring.startColor = ring.endColor = expiring ? new Color(1f, .72f, .32f, 1f) : new Color(.40f, .88f, 1f, 1f);
            ring.widthMultiplier = expiring && !ReducedEffects ? .14f + .06f * (.5f + .5f * Mathf.Sin(visualClock.Time * 9f)) : .13f;
            PlaceRing(ring, center + Vector3.up * .03f, 1.12f);
        }

        private void DrawCharacterTrail(string id, List<Vector3> positions, Color color, int firstSegment)
        {
            // Old rules start body collision at segment 2; strength practice
            // includes segment 1. Segment 0 remains the character's head.
            if (positions.Count < firstSegment + 2) return;
            if (!characterTrails.TryGetValue(id, out var lines) || lines[0] == null)
            {
                lines = new LineRenderer[2];
                for (int layer = 0; layer < lines.Length; layer++)
                {
                    var item = new GameObject("Character trail " + id + " " + layer); item.transform.SetParent(runtimeRoot, false);
                    var line = item.AddComponent<LineRenderer>(); line.sharedMaterial = trailLineMaterial;
                    line.useWorldSpace = true; line.numCapVertices = 4; line.numCornerVertices = 3;
                    line.widthMultiplier = layer == 0 ? .80f : .25f; line.sortingOrder = layer;
                    line.shadowCastingMode = ShadowCastingMode.Off; lines[layer] = line;
                }
                characterTrails[id] = lines;
            }
            for (int layer = 0; layer < lines.Length; layer++)
            {
                var line = lines[layer]; line.enabled = true; line.positionCount = positions.Count - firstSegment;
                Color shade = layer == 0 ? color : Color.Lerp(color, Color.white, .78f);
                float pulse = ReducedEffects ? 1f : .88f + .12f * Mathf.Sin(visualClock.Time * 4f);
                line.startColor = line.endColor = new Color(shade.r * pulse, shade.g * pulse, shade.b * pulse, 1f);
                for (int i = firstSegment; i < positions.Count; i++) line.SetPosition(i - firstSegment, new Vector3(positions[i].x, .23f + layer * .02f, positions[i].z));
            }
            if (!ReducedEffects)
            {
                // Moving highlights remain inside the real collidable path.
                float phase = Mathf.Repeat(visualClock.Time * 3f, 5f);
                for (float sample = firstSegment + phase; sample < positions.Count - 1; sample += 5f)
                {
                    int i = Mathf.FloorToInt(sample);
                    Vector3 point = Vector3.Lerp(positions[i], positions[i + 1], sample - i);
                    point.y = .34f;
                    trailPulses.Add(Matrix4x4.TRS(point, Quaternion.identity, Vector3.one * .22f));
                }
            }
        }

        private static Mesh CreateCone()
        {
            const int sides = 12;
            var mesh = new Mesh { name = "Arena character ornaments" };
            var points = new Vector3[sides + 2]; var triangles = new int[sides * 6];
            points[0] = Vector3.up; points[1] = Vector3.zero;
            for (int i = 0; i < sides; i++)
            {
                float angle = i * Mathf.PI * 2 / sides;
                points[i + 2] = new Vector3(Mathf.Cos(angle) * .5f, 0, Mathf.Sin(angle) * .5f);
                int next = (i + 1) % sides + 2, k = i * 6;
                triangles[k] = 0; triangles[k + 1] = next; triangles[k + 2] = i + 2;
                triangles[k + 3] = 1; triangles[k + 4] = i + 2; triangles[k + 5] = next;
            }
            mesh.vertices = points; mesh.triangles = triangles; mesh.RecalculateNormals();
            return mesh;
        }

        public Vector3 WorldToScreen(Point point) => view.WorldToScreenPoint(new Vector3(point.x, .72f, point.y) + view.transform.up * 5.7f);
        public Vector3 HeadToScreen(Point point) => view.WorldToScreenPoint(new Vector3(point.x, .72f, point.y));

        private void DrawBatch(List<Matrix4x4> matrices, Material material, Mesh mesh = null)
        {
            if (matrices.Count == 0) return;
            if (mesh == null) mesh = sphere;
            if (SystemInfo.supportsInstancing && matrices.Count <= 1023)
                Graphics.DrawMeshInstanced(mesh, 0, material, matrices, null, ShadowCastingMode.Off, false);
            else
                foreach (var matrix in matrices) Graphics.DrawMesh(mesh, matrix, material, 0, null, 0, null, false, false);
        }
    }
}
