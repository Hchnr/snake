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
            new Color(1f, .45f, .79f), new Color(.70f, .91f, .30f)
        };
        private static readonly Color[] FruitColors =
        {
            new Color(1f, .66f, .26f), new Color(.98f, .32f, .47f),
            new Color(.35f, .92f, .70f), new Color(.62f, .57f, 1f)
        };
        private readonly List<Matrix4x4>[] bodies = new List<Matrix4x4>[8];
        private readonly List<Matrix4x4>[] fruits = new List<Matrix4x4>[4];
        private readonly List<Matrix4x4> eyes = new List<Matrix4x4>();
        private readonly List<Matrix4x4> pupils = new List<Matrix4x4>();
        private readonly Dictionary<string, List<Vector3>> smoothed = new Dictionary<string, List<Vector3>>();
        private Material[] bodyMaterials;
        private Material[] fruitMaterials;
        private Material eyeMaterial;
        private Material pupilMaterial;
        private Mesh sphere;
        private Camera view;
        private LineRenderer marker;
        private bool cameraReady;

        private void Awake()
        {
            for (int i = 0; i < bodies.Length; i++) bodies[i] = new List<Matrix4x4>();
            for (int i = 0; i < fruits.Length; i++) fruits[i] = new List<Matrix4x4>();
            var sample = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere = sample.GetComponent<MeshFilter>().sharedMesh;
            Destroy(sample);
            bodyMaterials = new Material[Palette.Length];
            fruitMaterials = new Material[FruitColors.Length];
            for (int i = 0; i < Palette.Length; i++) bodyMaterials[i] = MakeMaterial(Palette[i], .28f);
            for (int i = 0; i < FruitColors.Length; i++) fruitMaterials[i] = MakeMaterial(FruitColors[i], .15f);
            eyeMaterial = MakeMaterial(new Color(.96f, .99f, 1f), .15f);
            pupilMaterial = MakeMaterial(new Color(.025f, .035f, .075f), .1f);
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
            RenderSettings.ambientLight = new Color(.44f, .48f, .61f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(.025f, .045f, .09f);
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 95;
            RenderSettings.fogEndDistance = 180;

            var cameraObject = new GameObject("Arena Camera");
            view = cameraObject.AddComponent<Camera>();
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = RenderSettings.fogColor;
            view.orthographic = true;
            view.orthographicSize = 21;
            view.nearClipPlane = .3f;
            view.farClipPlane = 250;
            view.transform.rotation = Quaternion.Euler(70, 0, 0);
            cameraObject.tag = "MainCamera";

            var lightObject = new GameObject("Sun");
            var sun = lightObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.15f;
            sun.color = new Color(.83f, .91f, 1f);
            sun.shadows = LightShadows.None;
            lightObject.transform.rotation = Quaternion.Euler(50, -30, 0);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Arena Floor";
            floor.transform.localScale = Vector3.one * (Arena.WorldHalfSize * 2 / 10f);
            Destroy(floor.GetComponent<Collider>());
            var floorMat = MakeMaterial(new Color(.17f, .26f, .36f), .05f);
            var grid = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            grid.wrapMode = TextureWrapMode.Repeat;
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            {
                float line = (x < 2 || y < 2) ? .22f : 0f;
                float pattern = ((x / 32 + y / 32) % 2 == 0) ? .035f : 0f;
                grid.SetPixel(x, y, new Color(.20f + line + pattern, .30f + line + pattern, .40f + line + pattern, 1));
            }
            grid.Apply();
            floorMat.mainTexture = grid;
            floorMat.mainTextureScale = Vector2.one * 44;
            floor.GetComponent<Renderer>().sharedMaterial = floorMat;

            var railMat = MakeMaterial(new Color(.20f, .89f, .86f), .1f);
            railMat.EnableKeyword("_EMISSION");
            railMat.SetColor("_EmissionColor", new Color(.025f, .20f, .18f));
            float edge = Arena.WorldHalfSize;
            for (int i = 0; i < 4; i++)
            {
                var rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rail.name = "Reflecting Boundary";
                Destroy(rail.GetComponent<Collider>());
                rail.GetComponent<Renderer>().sharedMaterial = railMat;
                rail.transform.position = i < 2 ? new Vector3(i == 0 ? -edge : edge, .45f, 0) : new Vector3(0, .45f, i == 2 ? -edge : edge);
                rail.transform.localScale = i < 2 ? new Vector3(.65f, .9f, edge * 2) : new Vector3(edge * 2, .9f, .65f);
            }

            var ring = new GameObject("Player Marker");
            marker = ring.AddComponent<LineRenderer>();
            marker.useWorldSpace = true;
            marker.loop = true;
            marker.positionCount = 48;
            marker.widthMultiplier = .08f;
            marker.material = MakeMaterial(new Color(.6f, 1f, .87f), 0);
            marker.startColor = marker.endColor = new Color(.7f, 1f, .9f, .9f);
            marker.shadowCastingMode = ShadowCastingMode.Off;
        }

        public void Draw(ArenaState state, string playerId)
        {
            if (state == null) return;
            foreach (var list in bodies) list.Clear();
            foreach (var list in fruits) list.Clear();
            eyes.Clear(); pupils.Clear();
            SnakeState focus = state.snakes.Find(s => s.id == playerId && s.alive);
            if (focus == null) focus = state.snakes.Find(s => s.alive);
            if (focus != null && focus.segments.Count > 0)
            {
                Point p = focus.segments[0];
                Vector3 target = new Vector3(p.x, 29, p.y - 10.5f);
                view.transform.position = cameraReady ? Vector3.Lerp(view.transform.position, target, 1f - Mathf.Exp(-5f * Time.deltaTime)) : target;
                cameraReady = true;
            }
            marker.enabled = false;
            foreach (var snake in state.snakes)
            {
                if (!snake.alive || snake.segments.Count == 0) continue;
                var list = bodies[Mathf.Abs(snake.color % bodies.Length)];
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
                    Vector3 target = new Vector3(p.x, .72f, p.y);
                    positions[i] = Vector3.Distance(positions[i], target) > 8f ? target :
                        Vector3.Lerp(positions[i], target, 1f - Mathf.Exp(-16f * Time.deltaTime));
                    float taper = i == 0 ? 1.65f : Mathf.Lerp(1.05f, 1.38f, Mathf.Clamp01((snake.segments.Count - i) / 5f));
                    list.Add(Matrix4x4.TRS(positions[i], Quaternion.identity, Vector3.one * taper));
                }
                Vector3 forward = new Vector3(Mathf.Cos(snake.angle), 0, Mathf.Sin(snake.angle));
                Vector3 side = new Vector3(-forward.z, 0, forward.x);
                Vector3 center = positions[0];
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    Vector3 eye = center + forward * .52f + side * (.37f * sign) + Vector3.up * .37f;
                    eyes.Add(Matrix4x4.TRS(eye, Quaternion.identity, Vector3.one * .38f));
                    pupils.Add(Matrix4x4.TRS(eye + forward * .14f + Vector3.up * .06f, Quaternion.identity, Vector3.one * .18f));
                }
                if (snake.id == playerId)
                {
                    marker.enabled = true;
                    for (int i = 0; i < marker.positionCount; i++)
                    {
                        float angle = i * Mathf.PI * 2 / marker.positionCount;
                        marker.SetPosition(i, center + new Vector3(Mathf.Cos(angle) * 1.35f, -.62f, Mathf.Sin(angle) * 1.35f));
                    }
                }
            }
            foreach (var fruit in state.fruits)
            {
                Point p = fruit.position;
                float pulse = .65f + .08f * Mathf.Sin(Time.time * 2.4f + p.x + p.y);
                fruits[Mathf.Abs(fruit.color % fruits.Length)].Add(Matrix4x4.TRS(new Vector3(p.x, .52f, p.y), Quaternion.identity, Vector3.one * pulse));
            }
            for (int i = 0; i < bodies.Length; i++) DrawBatch(bodies[i], bodyMaterials[i]);
            for (int i = 0; i < fruits.Length; i++) DrawBatch(fruits[i], fruitMaterials[i]);
            DrawBatch(eyes, eyeMaterial); DrawBatch(pupils, pupilMaterial);
        }

        public Vector3 WorldToScreen(Point point) => view.WorldToScreenPoint(new Vector3(point.x, 2.1f, point.y));

        private void DrawBatch(List<Matrix4x4> matrices, Material material)
        {
            if (matrices.Count == 0) return;
            if (SystemInfo.supportsInstancing && matrices.Count <= 1023)
                Graphics.DrawMeshInstanced(sphere, 0, material, matrices, null, ShadowCastingMode.Off, false);
            else
                foreach (var matrix in matrices) Graphics.DrawMesh(sphere, matrix, material, 0, null, 0, null, false, false);
        }
    }
}
