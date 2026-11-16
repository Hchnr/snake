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
        private static readonly int[] SkinPalette = { 0, 3, 4, 5, 8, 9 };
        private readonly List<Matrix4x4>[] bodies = new List<Matrix4x4>[Palette.Length];
        private readonly List<Matrix4x4>[] links = new List<Matrix4x4>[Palette.Length];
        private readonly List<Matrix4x4>[] horns = new List<Matrix4x4>[Palette.Length];
        private readonly List<Matrix4x4>[] fruits = new List<Matrix4x4>[4];
        private readonly List<Matrix4x4> eyes = new List<Matrix4x4>();
        private readonly List<Matrix4x4> pupils = new List<Matrix4x4>();
        private readonly List<Matrix4x4> scales = new List<Matrix4x4>();
        private readonly List<Matrix4x4> trail = new List<Matrix4x4>();
        private readonly List<Matrix4x4> sparks = new List<Matrix4x4>();
        private readonly Dictionary<string, List<Vector3>> smoothed = new Dictionary<string, List<Vector3>>();
        private readonly List<Transform> crystals = new List<Transform>();
        private Material[] bodyMaterials;
        private Material[] fruitMaterials;
        private Material eyeMaterial;
        private Material pupilMaterial;
        private Material scaleMaterial;
        private Mesh sphere;
        private Mesh cylinder;
        private Mesh cone;
        private Camera view;
        private LineRenderer marker, eventRing;
        private GameObject starObject;
        private bool cameraReady;
        private int lastTick = -1, lastKills;
        private float burstStarted = -10f;
        private Vector3 burstPosition;
        public int SelectedSkin { get; set; }
        public bool ReducedEffects { get; set; }
        public bool Visible
        {
            get => view != null && view.enabled;
            set
            {
                if (view == null || view.enabled == value) return;
                view.enabled = value;
                if (!value) cameraReady = false;
            }
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
            var runtimeRoot = new GameObject("Arena Runtime");
            runtimeRoot.transform.SetParent(transform, false);
            RenderSettings.ambientLight = new Color(.44f, .48f, .61f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(.025f, .045f, .09f);
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 95;
            RenderSettings.fogEndDistance = 180;

            var cameraObject = new GameObject("Arena Camera");
            cameraObject.transform.SetParent(runtimeRoot.transform, false);
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
            lightObject.transform.SetParent(runtimeRoot.transform, false);
            var sun = lightObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.15f;
            sun.color = new Color(.83f, .91f, 1f);
            sun.shadows = LightShadows.None;
            lightObject.transform.rotation = Quaternion.Euler(50, -30, 0);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.SetParent(runtimeRoot.transform, false);
            floor.name = "Arena Floor";
            floor.transform.localScale = Vector3.one * (Arena.WorldHalfSize * 2 / 10f);
            Destroy(floor.GetComponent<Collider>());
            var ground = Resources.Load<Texture2D>("ArenaFloor");
            var floorMat = MakeMaterial(ground != null ? new Color(.66f, .76f, .78f) : new Color(.17f, .26f, .36f), .05f);
            if (ground != null)
            {
                floorMat.mainTexture = ground;
                floorMat.mainTextureScale = Vector2.one * 11;
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

            var railMat = MakeMaterial(new Color(.20f, .89f, .86f), .1f);
            railMat.EnableKeyword("_EMISSION");
            railMat.SetColor("_EmissionColor", new Color(.025f, .20f, .18f));
            float edge = Arena.WorldHalfSize;
            for (int i = 0; i < 4; i++)
            {
                var rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rail.transform.SetParent(runtimeRoot.transform, false);
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
                crystal.transform.SetParent(runtimeRoot.transform, false);
                crystal.name = "Cave crystal"; Destroy(crystal.GetComponent<Collider>());
                float height = 1.3f + (float)decorationRandom.NextDouble() * 1.8f;
                crystal.transform.position = new Vector3(x, height * .45f, z);
                crystal.transform.rotation = Quaternion.Euler(0, decorationRandom.Next(45), 45);
                crystal.transform.localScale = new Vector3(.5f, height, .5f);
                crystal.GetComponent<Renderer>().sharedMaterial = crystalMat;
                crystals.Add(crystal.transform);
            }

            starObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            starObject.transform.SetParent(runtimeRoot.transform, false);
            starObject.name = "Star core"; Destroy(starObject.GetComponent<Collider>());
            var starMaterial = MakeMaterial(new Color(1f, .78f, .30f), .35f);
            starMaterial.EnableKeyword("_EMISSION"); starMaterial.SetColor("_EmissionColor", new Color(.5f, .24f, .025f));
            starObject.GetComponent<Renderer>().sharedMaterial = starMaterial; starObject.SetActive(false);

            var ring = new GameObject("Player Marker");
            ring.transform.SetParent(runtimeRoot.transform, false);
            marker = ring.AddComponent<LineRenderer>();
            marker.useWorldSpace = true;
            marker.loop = true;
            marker.positionCount = 48;
            marker.widthMultiplier = .08f;
            marker.material = MakeMaterial(new Color(.6f, 1f, .87f), 0);
            marker.startColor = marker.endColor = new Color(.7f, 1f, .9f, .9f);
            marker.shadowCastingMode = ShadowCastingMode.Off;
            var eventObject = new GameObject("Event boundary");
            eventObject.transform.SetParent(runtimeRoot.transform, false);
            eventRing = eventObject.AddComponent<LineRenderer>();
            eventRing.useWorldSpace = true; eventRing.loop = true; eventRing.positionCount = 64;
            eventRing.widthMultiplier = .12f; eventRing.material = MakeMaterial(new Color(1f, .73f, .29f), 0);
            eventRing.startColor = eventRing.endColor = new Color(1f, .78f, .35f, .65f);
            eventRing.shadowCastingMode = ShadowCastingMode.Off;
        }

        public void Draw(ArenaState state, string playerId)
        {
            if (state == null) return;
            foreach (var list in bodies) list.Clear();
            foreach (var list in links) list.Clear();
            foreach (var list in horns) list.Clear();
            foreach (var list in fruits) list.Clear();
            eyes.Clear(); pupils.Clear(); scales.Clear(); trail.Clear(); sparks.Clear();
            SnakeState focus = state.snakes.Find(s => s.id == playerId && s.alive);
            if (focus == null) focus = state.snakes.Find(s => s.alive);
            if (focus != null && focus.segments.Count > 0)
            {
                Point p = focus.segments[0];
                Vector3 target = new Vector3(p.x, 29, p.y - 10.5f);
                view.transform.position = cameraReady ? Vector3.Lerp(view.transform.position, target, 1f - Mathf.Exp(-5f * Time.deltaTime)) : target;
                view.orthographicSize = Mathf.Lerp(view.orthographicSize, focus.boosting ? 22f : 20f, 1f - Mathf.Exp(-2.5f * Time.deltaTime));
                cameraReady = true;
            }
            for (int i = 0; i < crystals.Count; i++)
                if (crystals[i] != null) crystals[i].Rotate(Vector3.up, Time.deltaTime * (5f + i % 5), Space.World);
            marker.enabled = false;
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
                if (!snake.alive || snake.segments.Count == 0) continue;
                int color = snake.id == playerId ? SkinPalette[Mathf.Clamp(SelectedSkin, 0, SkinPalette.Length - 1)] : Mathf.Abs(snake.color % bodies.Length);
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
                    Vector3 target = new Vector3(p.x, .72f + .045f * Mathf.Sin(Time.time * 7f - i * .55f), p.y);
                    positions[i] = Vector3.Distance(positions[i], target) > 8f ? target :
                        Vector3.Lerp(positions[i], target, 1f - Mathf.Exp(-16f * Time.deltaTime));
                    float taper = i == 0 ? 1.65f : Mathf.Lerp(.72f, 1.40f, Mathf.Clamp01((snake.segments.Count - i) / 6f));
                    list.Add(Matrix4x4.TRS(positions[i], Quaternion.identity, Vector3.one * taper));
                    if (snake.id == playerId && i > 0 && i % (SelectedSkin == 1 ? 3 : 2) == 0)
                    {
                        float scale = SelectedSkin == 2 ? .45f : .35f;
                        scales.Add(Matrix4x4.TRS(positions[i] + Vector3.up * .53f, Quaternion.identity, new Vector3(scale, .17f, scale)));
                    }
                }
                for (int i = 1; i < positions.Count; i++)
                {
                    Vector3 delta = positions[i - 1] - positions[i];
                    if (delta.sqrMagnitude < .0001f) continue;
                    float diameter = Mathf.Lerp(.70f, 1.27f, Mathf.Clamp01((positions.Count - i) / 6f));
                    links[color].Add(Matrix4x4.TRS((positions[i - 1] + positions[i]) * .5f, Quaternion.FromToRotation(Vector3.up, delta), new Vector3(diameter, delta.magnitude * .5f, diameter)));
                }
                Vector3 forward = new Vector3(Mathf.Cos(snake.angle), 0, Mathf.Sin(snake.angle));
                Vector3 side = new Vector3(-forward.z, 0, forward.x);
                Vector3 center = positions[0];
                if (snake.id == playerId)
                {
                    int shape = Mathf.Clamp(SelectedSkin, 0, SkinPalette.Length - 1);
                    if (shape == 5)
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
                    if (lastTick >= 0 && snake.kills > lastKills) { burstStarted = Time.time; burstPosition = center; }
                    lastKills = snake.kills; lastTick = state.tick;
                }
                for (int sign = -1; sign <= 1; sign += 2)
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
                            float size = .29f * (1f - i / 6f) * (1f + .12f * Mathf.Sin(Time.time * 4f + i));
                            Vector3 glow = tail + back * (i * .35f) + Vector3.up * (.12f + .04f * Mathf.Sin(Time.time * 3f + i));
                            trail.Add(Matrix4x4.TRS(glow, Quaternion.identity, Vector3.one * size));
                        }
                    }
                    marker.enabled = true;
                    marker.widthMultiplier = snake.rebound > 0 ? .18f : .08f;
                    for (int i = 0; i < marker.positionCount; i++)
                    {
                        float angle = i * Mathf.PI * 2 / marker.positionCount;
                        marker.SetPosition(i, center + new Vector3(Mathf.Cos(angle) * 1.35f, -.62f, Mathf.Sin(angle) * 1.35f));
                    }
                }
            }
            float burstAge = Time.time - burstStarted;
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
            if (starObject != null)
            {
                starObject.SetActive(state.starVisible);
                if (state.starVisible)
                {
                    starObject.transform.position = new Vector3(state.starPosition.x, 1.5f + .2f * Mathf.Sin(Time.time * 3), state.starPosition.y);
                    starObject.transform.localScale = Vector3.one * (1.2f + .15f * Mathf.Sin(Time.time * 4));
                }
            }
            foreach (var fruit in state.fruits)
            {
                Point p = fruit.position;
                float pulse = .65f + .08f * Mathf.Sin(Time.time * 2.4f + p.x + p.y);
                fruits[Mathf.Abs(fruit.color % fruits.Length)].Add(Matrix4x4.TRS(new Vector3(p.x, .52f, p.y), Quaternion.identity, Vector3.one * pulse));
            }
            for (int i = 0; i < bodies.Length; i++) { DrawBatch(links[i], bodyMaterials[i], cylinder); DrawBatch(bodies[i], bodyMaterials[i], sphere); DrawBatch(horns[i], bodyMaterials[i], cone); }
            for (int i = 0; i < fruits.Length; i++) DrawBatch(fruits[i], fruitMaterials[i]);
            scaleMaterial.color = Color.Lerp(Palette[SkinPalette[Mathf.Clamp(SelectedSkin, 0, SkinPalette.Length - 1)]], Color.white, .65f);
            scaleMaterial.SetColor("_EmissionColor", scaleMaterial.color * .18f);
            DrawBatch(scales, scaleMaterial); DrawBatch(trail, scaleMaterial); DrawBatch(sparks, fruitMaterials[0]); DrawBatch(eyes, eyeMaterial); DrawBatch(pupils, pupilMaterial);
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

        public Vector3 WorldToScreen(Point point) => view.WorldToScreenPoint(new Vector3(point.x, 2.1f, point.y));

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
