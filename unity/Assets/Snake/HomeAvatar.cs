using UnityEngine;

namespace SnakeClub
{
    // A small isolated 3D portrait rendered over the illustrated home screen.
    public sealed class HomeAvatar : MonoBehaviour
    {
        private static readonly Color[] Colors = {
            new Color(.12f, .90f, .71f), new Color(1f, .48f, .19f),
            new Color(.48f, .48f, 1f), new Color(.18f, .82f, .95f)
        };
        private GameObject model;
        private Camera portrait;
        private RenderTexture texture;
        private Material body, accent, eye, pupil;
        private int skin = -1;
        public Texture Texture => texture;
        public bool Visible { get => portrait != null && portrait.enabled; set { if (portrait != null) portrait.enabled = value; } }
        public int Skin { get => skin; set { if (skin != value) ApplySkin(value); } }

        private void Awake() => Initialize();

        private void OnEnable()
        {
            if (Application.isPlaying && (portrait == null || texture == null)) Initialize();
        }

        private void Initialize()
        {
            var oldCamera = transform.Find("Portrait Camera");
            var oldModel = transform.Find("Portrait Snake");
            if (oldCamera != null) { oldCamera.gameObject.SetActive(false); Destroy(oldCamera.gameObject); }
            if (oldModel != null) { oldModel.gameObject.SetActive(false); Destroy(oldModel.gameObject); }
            texture = new RenderTexture(512, 512, 16, RenderTextureFormat.ARGB32) { name = "Snake portrait", antiAliasing = 1 };
            texture.Create();
            var cameraObject = new GameObject("Portrait Camera");
            cameraObject.transform.SetParent(transform, false);
            portrait = cameraObject.AddComponent<Camera>();
            portrait.clearFlags = CameraClearFlags.SolidColor;
            portrait.backgroundColor = Color.clear;
            portrait.cullingMask = 1 << 31;
            portrait.orthographic = true; portrait.orthographicSize = 3.3f;
            portrait.nearClipPlane = .1f; portrait.farClipPlane = 50f; portrait.depth = -10;
            portrait.targetTexture = texture;
            Vector3 origin = new Vector3(0, -1500, 0);
            cameraObject.transform.position = origin + new Vector3(0, 5.5f, -8f);
            cameraObject.transform.LookAt(origin + new Vector3(0, .8f, 0));
            model = new GameObject("Portrait Snake"); model.transform.SetParent(transform, false); model.transform.position = origin;
            body = Material(Color.white, .6f); accent = Material(Color.white, .35f);
            eye = Material(new Color(.98f, 1f, 1f), .2f); pupil = Material(new Color(.02f, .05f, .10f), .1f);
            for (int i = 12; i >= 0; i--)
            {
                float angle = -.65f + i * .34f;
                float radius = 1f + i * .067f;
                var segment = Sphere("Body", model.transform, new Vector3(Mathf.Sin(angle) * radius, .68f, Mathf.Cos(angle) * radius), Vector3.one * (i == 0 ? 1.48f : Mathf.Lerp(.64f, 1.12f, 1f - i / 13f)), body);
                if (i > 0 && i % 2 == 0) Sphere("Glowing scale", segment.transform, new Vector3(0, .46f, -.05f), Vector3.one * .34f, accent);
                if (i == 0)
                {
                    Sphere("Eye", segment.transform, new Vector3(-.27f, .34f, -.38f), Vector3.one * .29f, eye);
                    Sphere("Eye", segment.transform, new Vector3(.27f, .34f, -.38f), Vector3.one * .29f, eye);
                    Sphere("Pupil", segment.transform, new Vector3(-.27f, .37f, -.51f), Vector3.one * .14f, pupil);
                    Sphere("Pupil", segment.transform, new Vector3(.27f, .37f, -.51f), Vector3.one * .14f, pupil);
                }
            }
            ApplySkin(0);
        }

        private static GameObject Sphere(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var item = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            item.name = name; item.layer = 31; item.transform.SetParent(parent, false);
            item.transform.localPosition = position; item.transform.localScale = scale;
            var collider = item.GetComponent<Collider>(); if (collider != null) Destroy(collider);
            item.GetComponent<Renderer>().sharedMaterial = material;
            return item;
        }

        private static Material Material(Color color, float gloss)
        {
            var template = Resources.Load<Material>("ArenaMaterial");
            var material = template != null ? new Material(template) : new Material(Shader.Find("Standard"));
            material.color = color; material.SetFloat("_Glossiness", gloss); material.SetFloat("_Metallic", .2f);
            return material;
        }

        private void ApplySkin(int index)
        {
            skin = Mathf.Clamp(index, 0, Colors.Length - 1);
            if (body == null) return;
            body.color = Colors[skin];
            accent.color = Color.Lerp(Colors[skin], Color.white, .60f);
            accent.EnableKeyword("_EMISSION"); accent.SetColor("_EmissionColor", Colors[skin] * .18f);
        }

        private void Update()
        {
            if (Visible && model != null) model.transform.localRotation = Quaternion.Euler(0, Mathf.Sin(Time.unscaledTime * .55f) * 10f, 0);
        }

        private void OnDestroy()
        {
            if (texture != null) { texture.Release(); Destroy(texture); }
            if (body != null) Destroy(body); if (accent != null) Destroy(accent);
            if (eye != null) Destroy(eye); if (pupil != null) Destroy(pupil);
        }
    }
}
