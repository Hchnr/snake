using System.Collections.Generic;
using SnakeClub.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace SnakeClub
{
    // A real-time character portrait with a continuously deformed body.
    public sealed class HomeAvatar : MonoBehaviour
    {
        private const int Rings = 26;
        private const int Sides = 12;
        private static readonly Color[] Colors = {
            new Color(.12f, .83f, .69f), new Color(1f, .43f, .20f),
            new Color(.44f, .39f, .91f), new Color(.23f, .78f, .93f),
            new Color(.94f, .95f, .98f), new Color(.88f, .18f, .22f)
        };
        private static readonly Color[] Accents = {
            new Color(.68f, 1f, .66f), new Color(1f, .78f, .25f),
            new Color(1f, .73f, .96f), new Color(.72f, .94f, 1f),
            new Color(1f, .73f, .81f), new Color(1f, .53f, .30f)
        };
        private readonly List<Material> ownedMaterials = new List<Material>();
        private readonly Transform[] marks = new Transform[8];
        private readonly Transform[] motes = new Transform[9];
        private readonly Transform[] eyes = new Transform[2];
        private GameObject model, ornaments;
        private Transform head, tail;
        private Camera portrait;
        private SpriteRenderer referenceCharacter, referenceHead;
        private Transform referenceHeadPivot;
        private RenderTexture texture;
        private Mesh bodyMesh, coneMesh;
        private Vector3[] vertices;
        private Material body, accent, eyeWhite, dark, blush, sparkle;
        private int skin = -1;

        public Texture Texture => texture;
        public bool Visible { get => portrait != null && portrait.enabled; set { if (portrait != null) portrait.enabled = value; } }
        public int Skin { get => skin; set { if (skin != value) ApplySkin(value); } }
        public bool ReducedEffects { get; set; }
        private bool resultVisible, resultWon;
        private float resultStartedAt;

        public void SetResult(bool visible, bool won)
        {
            if (visible && (!resultVisible || won != resultWon)) resultStartedAt = Time.unscaledTime;
            resultVisible = visible; resultWon = visible && won;
        }

        private void Awake() => Initialize();
        private void OnEnable() { if (Application.isPlaying && (portrait == null || texture == null)) Initialize(); }

        private void Initialize()
        {
            var oldCamera = transform.Find("Portrait Camera");
            var oldModel = transform.Find("Portrait Snake");
            var oldReference = transform.Find("Full character reference");
            if (oldCamera != null) { oldCamera.gameObject.SetActive(false); Destroy(oldCamera.gameObject); }
            if (oldModel != null) { oldModel.gameObject.SetActive(false); Destroy(oldModel.gameObject); }
            if (oldReference != null) { oldReference.gameObject.SetActive(false); Destroy(oldReference.gameObject); }
            texture = new RenderTexture(512, 512, 16, RenderTextureFormat.ARGB32) { name = "Character portrait", antiAliasing = 1 };
            texture.Create();
            var cameraObject = new GameObject("Portrait Camera");
            cameraObject.transform.SetParent(transform, false);
            portrait = cameraObject.AddComponent<Camera>();
            portrait.clearFlags = CameraClearFlags.SolidColor;
            portrait.backgroundColor = Color.clear;
            portrait.cullingMask = 1 << 31;
            portrait.orthographic = true; portrait.orthographicSize = 2.45f;
            portrait.nearClipPlane = .1f; portrait.farClipPlane = 40f; portrait.depth = -10;
            portrait.targetTexture = texture;
            Vector3 origin = new Vector3(0, -1500, 0);
            cameraObject.transform.position = origin + new Vector3(0, 4.4f, -7.5f);
            cameraObject.transform.LookAt(origin + new Vector3(.45f, .75f, .35f));

            var reference = new GameObject("Full character reference");
            reference.layer = 31; reference.transform.SetParent(transform, false);
            reference.transform.position = portrait.transform.position + portrait.transform.forward * 8f;
            reference.transform.rotation = portrait.transform.rotation;
            referenceCharacter = reference.AddComponent<SpriteRenderer>();
            referenceCharacter.enabled = false;
            var pivot = new GameObject("Reference head pivot");
            pivot.layer = 31; pivot.transform.SetParent(reference.transform, false);
            pivot.transform.localPosition = new Vector3(0, -.32f, 0); referenceHeadPivot = pivot.transform;
            var headLayer = new GameObject("Reference head");
            headLayer.layer = 31; headLayer.transform.SetParent(pivot.transform, false);
            headLayer.transform.localPosition = new Vector3(0, .32f, 0);
            referenceHead = headLayer.AddComponent<SpriteRenderer>(); referenceHead.sortingOrder = 1; referenceHead.enabled = false;

            model = new GameObject("Portrait Snake"); model.transform.SetParent(transform, false); model.transform.position = origin;
            body = Material(Color.white, .48f); accent = Material(Color.white, .3f);
            eyeWhite = Material(new Color(1f, .99f, .96f), .12f);
            dark = Material(new Color(.035f, .055f, .11f), .12f);
            blush = Material(new Color(1f, .55f, .64f), .1f);
            sparkle = Material(new Color(1f, .91f, .59f), .2f);
            sparkle.EnableKeyword("_EMISSION"); sparkle.SetColor("_EmissionColor", new Color(.55f, .29f, .04f));
            coneMesh = CreateCone();
            BuildBody();

            head = new GameObject("Expressive head").transform;
            head.SetParent(model.transform, false);
            Primitive(PrimitiveType.Sphere, "Head", head, Vector3.zero, new Vector3(1.58f, 1.28f, 1.30f), body);
            Primitive(PrimitiveType.Sphere, "Muzzle", head, new Vector3(0, -.23f, -.57f), new Vector3(.70f, .30f, .22f), accent);
            for (int i = 0; i < 2; i++)
            {
                float sign = i == 0 ? -1 : 1;
                var eyeRoot = new GameObject("Animated eye").transform;
                eyeRoot.SetParent(head, false); eyeRoot.localPosition = new Vector3(sign * .34f, .14f, -.58f);
                eyes[i] = eyeRoot;
                Primitive(PrimitiveType.Sphere, "Eye white", eyeRoot, Vector3.zero, new Vector3(.37f, .43f, .16f), eyeWhite);
                Primitive(PrimitiveType.Sphere, "Pupil", eyeRoot, new Vector3(sign * .018f, -.025f, -.095f), new Vector3(.19f, .24f, .08f), dark);
                Primitive(PrimitiveType.Sphere, "Eye glint", eyeRoot, new Vector3(-.045f, .055f, -.145f), new Vector3(.07f, .075f, .035f), eyeWhite);
                Primitive(PrimitiveType.Sphere, "Cheek", head, new Vector3(sign * .55f, -.20f, -.45f), new Vector3(.20f, .10f, .08f), blush);
            }
            Primitive(PrimitiveType.Sphere, "Nose", head, new Vector3(0, -.15f, -.72f), new Vector3(.085f, .055f, .04f), dark);
            AddSmile();
            for (int i = 0; i < marks.Length; i++)
                marks[i] = Primitive(PrimitiveType.Sphere, "Body marking", model.transform, Vector3.zero, Vector3.one * .17f, accent).transform;
            tail = Primitive(PrimitiveType.Sphere, "Tail tip", model.transform, Vector3.zero, new Vector3(.30f, .20f, .40f), accent).transform;
            for (int i = 0; i < motes.Length; i++)
                motes[i] = Primitive(PrimitiveType.Sphere, "Floating sparkle", model.transform, Vector3.zero, Vector3.one * .08f, sparkle).transform;
            skin = -1;
            ApplySkin(0);
        }

        private Material Material(Color color, float gloss)
        {
            var template = Resources.Load<Material>("ArenaMaterial");
            var result = template != null ? new Material(template) : new Material(Shader.Find("Standard"));
            result.color = color; result.SetFloat("_Glossiness", gloss); result.SetFloat("_Metallic", .08f);
            ownedMaterials.Add(result); return result;
        }

        private static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var item = GameObject.CreatePrimitive(type);
            item.name = name; item.layer = 31; item.transform.SetParent(parent, false);
            item.transform.localPosition = position; item.transform.localScale = scale;
            var collider = item.GetComponent<Collider>(); if (collider != null) Destroy(collider);
            var renderer = item.GetComponent<Renderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            return item;
        }

        private void BuildBody()
        {
            bodyMesh = new Mesh { name = "Continuous animated snake body" };
            bodyMesh.MarkDynamic();
            vertices = new Vector3[(Rings + 1) * Sides];
            var triangles = new int[Rings * Sides * 6];
            var uv = new Vector2[vertices.Length];
            int at = 0;
            for (int i = 0; i <= Rings; i++) for (int j = 0; j < Sides; j++)
            {
                uv[i * Sides + j] = new Vector2(j / (float)Sides, i / (float)Rings);
                if (i == Rings) continue;
                int a = i * Sides + j, b = i * Sides + (j + 1) % Sides;
                int c = a + Sides, d = b + Sides;
                triangles[at++] = a; triangles[at++] = b; triangles[at++] = c;
                triangles[at++] = b; triangles[at++] = d; triangles[at++] = c;
            }
            bodyMesh.vertices = vertices; bodyMesh.uv = uv; bodyMesh.triangles = triangles;
            var tube = new GameObject("Smooth body"); tube.layer = 31; tube.transform.SetParent(model.transform, false);
            tube.AddComponent<MeshFilter>().sharedMesh = bodyMesh;
            var renderer = tube.AddComponent<MeshRenderer>(); renderer.sharedMaterial = body;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        }

        private static Vector3 Curve(float s, float time)
        {
            float wave = Mathf.Sin(s * 7.4f - time * 2.2f) * Mathf.Sin(s * Mathf.PI);
            float curl = Mathf.Sin(s * Mathf.PI) * 1.55f;
            return new Vector3(curl + wave * .24f, .58f + wave * .12f, -.82f + s * 2.75f);
        }

        private void AnimateBody(float time)
        {
            for (int i = 0; i <= Rings; i++)
            {
                float s = i / (float)Rings;
                Vector3 center = Curve(s, time);
                Vector3 tangent = (Curve(Mathf.Min(1f, s + .01f), time) - Curve(Mathf.Max(0f, s - .01f), time)).normalized;
                Vector3 side = Vector3.Cross(Vector3.up, tangent).normalized;
                float radius = Mathf.Lerp(.61f, .10f, Mathf.Pow(s, 1.3f));
                for (int j = 0; j < Sides; j++)
                {
                    float angle = j * Mathf.PI * 2f / Sides;
                    vertices[i * Sides + j] = center + (side * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle)) * radius;
                }
            }
            bodyMesh.vertices = vertices; bodyMesh.RecalculateNormals(); bodyMesh.RecalculateBounds();
            for (int i = 0; i < marks.Length; i++)
            {
                float s = (i + 1f) / (marks.Length + 2f);
                marks[i].localPosition = Curve(s, time) + Vector3.up * Mathf.Lerp(.60f, .16f, s);
                marks[i].localScale = new Vector3(.18f, .07f, .25f) * Mathf.Lerp(1f, .48f, s);
            }
            tail.localPosition = Curve(1, time); tail.localRotation = Quaternion.Euler(0, Mathf.Sin(time * 2.2f) * 18f, 0);
        }

        private void AddSmile()
        {
            var mouth = new GameObject("Smile"); mouth.layer = 31; mouth.transform.SetParent(head, false);
            var line = mouth.AddComponent<LineRenderer>(); line.useWorldSpace = false;
            line.sharedMaterial = dark; line.widthMultiplier = .035f; line.positionCount = 11;
            line.shadowCastingMode = ShadowCastingMode.Off;
            for (int i = 0; i < 11; i++)
            {
                float x = (i - 5) * .027f;
                line.SetPosition(i, new Vector3(x, -.245f + x * x * 1.1f, -.70f));
            }
        }

        private Mesh CreateCone()
        {
            const int sides = 16;
            var mesh = new Mesh { name = "Character ornament" };
            var points = new Vector3[sides + 2];
            var triangles = new int[sides * 6];
            points[0] = Vector3.up; points[1] = Vector3.zero;
            for (int i = 0; i < sides; i++)
            {
                float angle = i * Mathf.PI * 2 / sides;
                points[i + 2] = new Vector3(Mathf.Cos(angle) * .5f, 0, Mathf.Sin(angle) * .5f);
                int next = (i + 1) % sides + 2, index = i * 6;
                triangles[index] = 0; triangles[index + 1] = next; triangles[index + 2] = i + 2;
                triangles[index + 3] = 1; triangles[index + 4] = i + 2; triangles[index + 5] = next;
            }
            mesh.vertices = points; mesh.triangles = triangles; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        private Transform Ornament(string name, Vector3 position, Vector3 scale, Vector3 rotation, Material material)
        {
            var item = new GameObject(name); item.layer = 31; item.transform.SetParent(ornaments.transform, false);
            item.transform.localPosition = position; item.transform.localScale = scale; item.transform.localRotation = Quaternion.Euler(rotation);
            item.AddComponent<MeshFilter>().sharedMesh = coneMesh;
            var renderer = item.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            return item.transform;
        }

        private void ApplySkin(int index)
        {
            skin = Mathf.Clamp(index, 0, SkinCatalog.All.Length - 1);
            if (body == null || head == null) return;
            var reference = SkinVisual.DisplaySprite(skin);
            if (referenceCharacter != null)
            {
                Sprite layerBody = null, layerHead = null;
                float neckY = 0;
                bool layered = reference != null && SkinVisual.TryReferenceLayers(skin, out layerBody, out layerHead, out neckY);
                if (layered && referenceHeadPivot != null && referenceHead != null)
                { referenceHeadPivot.localPosition = new Vector3(0, neckY, 0); referenceHead.transform.localPosition = new Vector3(0, -neckY, 0); }
                referenceCharacter.sprite = layered ? layerBody : reference;
                referenceCharacter.enabled = reference != null;
                if (referenceHead != null) { referenceHead.sprite = layered ? layerHead : null; referenceHead.enabled = layered; }
                if (referenceHeadPivot != null) referenceHeadPivot.localRotation = Quaternion.identity;
            }
            model.SetActive(reference == null);
            if (reference != null) return;
            int visualSkin = Mathf.Clamp(skin, 0, Colors.Length - 1);
            body.color = Colors[visualSkin]; accent.color = Accents[visualSkin];
            accent.EnableKeyword("_EMISSION"); accent.SetColor("_EmissionColor", Accents[visualSkin] * .12f);
            if (ornaments != null) { ornaments.SetActive(false); Destroy(ornaments); }
            ornaments = new GameObject("Character details"); ornaments.layer = 31; ornaments.transform.SetParent(head, false);
            if (skin == 5)
            {
                for (int i = 0; i < 5; i++)
                    Ornament("Red crest", new Vector3((i - 2) * .20f, .43f - Mathf.Abs(i - 2) * .035f, .03f),
                        new Vector3(.27f, .40f + (2 - Mathf.Abs(i - 2)) * .08f, .25f), new Vector3(0, 0, (2 - i) * 13f), body);
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    var brow = Primitive(PrimitiveType.Cube, "Expressive brow", ornaments.transform, new Vector3(sign * .34f, .40f, -.63f), new Vector3(.32f, .07f, .08f), dark);
                    brow.transform.localRotation = Quaternion.Euler(0, 0, -sign * 17f);
                }
            }
            else
            {
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    float height = skin == 4 ? .78f : skin == 3 ? .60f : .46f;
                    Ornament(skin == 4 ? "Cat ear" : "Crown", new Vector3(sign * .47f, .38f, .02f),
                        new Vector3(skin == 4 ? .42f : .31f, height, .28f), new Vector3(0, 0, -sign * 17f), skin == 4 ? body : accent);
                    if (skin == 4)
                        Ornament("Inner ear", new Vector3(sign * .47f, .46f, -.14f), new Vector3(.19f, .43f, .05f), new Vector3(0, 0, -sign * 17f), blush);
                }
                if (skin == 2)
                    Ornament("Star crown", new Vector3(0, .53f, -.02f), new Vector3(.28f, .58f, .22f), Vector3.zero, accent);
                if (skin == 4)
                    for (int sign = -1; sign <= 1; sign += 2)
                        Ornament("Cheek tuft", new Vector3(sign * .63f, -.08f, -.15f), new Vector3(.24f, .32f, .20f), new Vector3(0, 0, -sign * 70f), body);
            }
        }

        private void Update()
        {
            if (!Visible || model == null || bodyMesh == null) return;
            float time = Time.unscaledTime;
            ResultPose resultPose = ResultMotion.Sample(resultVisible && resultWon, ReducedEffects, time - resultStartedAt);
            if (referenceCharacter != null && referenceCharacter.enabled)
            {
                bool original = OriginalIdleMotion.TrySample(skin, time, ReducedEffects, out var pose);
                float motion = ReducedEffects ? 0f : Mathf.Sin(time * 2f);
                float lift = original ? pose.Lift : motion * .055f;
                float tilt = original ? pose.Tilt : motion * 1.4f;
                referenceCharacter.transform.position = portrait.transform.position + portrait.transform.forward * 8f + portrait.transform.up * (lift + resultPose.Lift);
                referenceCharacter.transform.rotation = portrait.transform.rotation * Quaternion.Euler(0, 0, tilt + resultPose.Tilt);
                referenceCharacter.transform.localScale = original ? new Vector3(pose.ScaleX, pose.ScaleY, 1f) : Vector3.one;
                if (referenceHead != null && referenceHead.enabled)
                    referenceHeadPivot.localRotation = Quaternion.Euler(0, 0, ReducedEffects ? 0 : Mathf.Sin(time * 2.4f) * 2.5f);
                return;
            }
            float motionTime = ReducedEffects ? 0f : time;
            AnimateBody(motionTime);
            head.localPosition = new Vector3(0, .94f + Mathf.Sin(motionTime * 2.1f) * .045f + resultPose.Lift, -.92f);
            head.localRotation = Quaternion.Euler(Mathf.Sin(motionTime * 1.6f) * 3f, Mathf.Sin(motionTime * .9f) * 9f, Mathf.Sin(motionTime * 1.2f) * 3f + resultPose.Tilt);
            float blinkTime = Mathf.Repeat(motionTime + 1.3f, 4.7f);
            float blink = blinkTime < .16f ? Mathf.Max(.08f, Mathf.Abs(blinkTime - .08f) / .08f) : 1f;
            foreach (var eye in eyes) if (eye != null) eye.localScale = new Vector3(1, blink, 1);
            for (int i = 0; i < motes.Length; i++)
            {
                if (motes[i].gameObject.activeSelf == ReducedEffects) motes[i].gameObject.SetActive(!ReducedEffects);
                if (ReducedEffects) continue;
                float angle = time * (.42f + i * .012f) + i * Mathf.PI * 2 / motes.Length;
                float radius = 1.45f + .14f * Mathf.Sin(time * 1.5f + i);
                motes[i].localPosition = new Vector3(Mathf.Cos(angle) * radius, .9f + .48f * Mathf.Sin(angle * 1.7f + i), .45f + Mathf.Sin(angle) * 1.05f);
                float size = .055f + .035f * (1f + Mathf.Sin(time * 3f + i)) * .5f;
                motes[i].localScale = Vector3.one * size;
            }
            model.transform.localRotation = Quaternion.Euler(0, Mathf.Sin(motionTime * .55f) * 6f, 0);
        }

        private void OnDestroy()
        {
            if (texture != null) { texture.Release(); Destroy(texture); }
            if (bodyMesh != null) Destroy(bodyMesh);
            if (coneMesh != null) Destroy(coneMesh);
            foreach (var material in ownedMaterials) if (material != null) Destroy(material);
        }
    }
}
