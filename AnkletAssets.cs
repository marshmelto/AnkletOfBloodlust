using System.Collections.Generic;
using System.IO;
using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace AnkletOfBloodlust
{
    // Builds the pickup model in code and loads the icons embedded in the DLL,
    // so the mod needs no Unity project or AssetBundle.
    internal static class AnkletAssets
    {
        // The pickup spins around Y, so a flat ring never turns edge-on to the camera
        private const float RingRadius = 0.42f;
        private const float BandThickness = 0.1f;
        private const float TiltDegrees = 8f;
        private const int SpikeCount = 8;
        private const float DropWidth = 0.22f;
        private const float DropHeight = 0.33f;

        private static readonly Color BandColor = new Color(0.36f, 0.04f, 0.06f);
        private static readonly Color BoneColor = new Color(0.78f, 0.74f, 0.66f);
        // deep red with a faint glow
        private static readonly Color BloodColor = new Color(0.42f, 0f, 0.03f);
        private static readonly Color BloodGlow = new Color(0.6f, 0f, 0.04f);

        private static Shader hgStandard;

        public static Sprite LoadSprite(string resourceName)
        {
            using Stream stream = typeof(AnkletAssets).Assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                Debug.LogError($"[AnkletOfBloodlust] Missing embedded resource {resourceName}");
                return null;
            }

            var bytes = new byte[stream.Length];
            stream.Read(bytes, 0, bytes.Length);

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(bytes);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }

        private static Material material;
        private static GameObject prefabHolder;

        // Shared setup for the pickup and display models
        private static void EnsureShared()
        {
            if (material) return;
            hgStandard = Addressables.LoadAssetAsync<Shader>("RoR2/Base/Shaders/HGStandard.shader").WaitForCompletion();
            material = MakeAtlasMaterial();

            // Children of an inactive holder stay "active" themselves, so clones spawn active
            // while the templates never appear in the world.
            prefabHolder = new GameObject("AnkletOfBloodlustPrefabs");
            prefabHolder.SetActive(false);
            Object.DontDestroyOnLoad(prefabHolder);
        }

        // One mesh, one submesh, one material: PickupDisplay sizes a dropped item from its first
        // renderer's bounds, and the pickup outline only draws submesh 0. Each part picks its
        // color from a strip texture through its UVs.
        private static Mesh BuildAnkletMesh(Matrix4x4 body, float tiltDegrees, out Vector3 dropTop)
        {
            var parts = new List<CombineInstance>();

            Matrix4x4 ring = body * Matrix4x4.Rotate(Quaternion.Euler(tiltDegrees, 0f, 0f));
            AddPart(parts, WithColor(Torus(RingRadius, BandThickness, 48, 12), BandU), ring);

            Mesh spike = WithColor(Cone(0.055f, 0.18f, 10), BoneU);
            for (int i = 0; i < SpikeCount; i++)
            {
                float angle = i * Mathf.PI * 2f / SpikeCount;
                var outward = new Vector3(Mathf.Sin(angle), -0.15f, Mathf.Cos(angle)).normalized;
                var basePos = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * (RingRadius + BandThickness * 0.6f);
                AddPart(parts, spike, ring * Matrix4x4.TRS(basePos, Quaternion.FromToRotation(Vector3.up, outward), Vector3.one));
            }

            // Where the front of the tilted band sits; the charm hangs straight down from there
            float tilt = tiltDegrees * Mathf.Deg2Rad;
            var front = new Vector3(0f, -(RingRadius + BandThickness) * Mathf.Sin(tilt), (RingRadius + BandThickness) * Mathf.Cos(tilt));

            AddPart(parts, WithColor(Torus(0.07f, 0.024f, 16, 8), BoneU),
                body * Matrix4x4.TRS(front + new Vector3(0f, -0.09f, 0f), Quaternion.Euler(0f, 0f, 90f), Vector3.one));

            dropTop = front + new Vector3(0f, -0.13f, 0f);
            AddPart(parts, WithColor(Teardrop(DropWidth, DropHeight, 24, 20), BloodU), body * Matrix4x4.Translate(dropTop));

            var mesh = new Mesh { name = "AnkletOfBloodlustMesh" };
            mesh.CombineMeshes(parts.ToArray(), true, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        // The anklet worn on a survivor: centered on the band, not tilted, with ItemDisplay so the
        // character model can hide it in first person and apply overlays (cloak, freeze, ...)
        public static GameObject CreateDisplayModel()
        {
            EnsureShared();
            Mesh mesh = BuildAnkletMesh(Matrix4x4.identity, 0f, out _);

            var root = new GameObject("dispAnkletOfBloodlust");
            root.transform.SetParent(prefabHolder.transform, false);
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;

            root.AddComponent<ItemDisplay>().rendererInfos = new[]
            {
                new CharacterModel.RendererInfo
                {
                    renderer = renderer,
                    defaultMaterial = material,
                    defaultShadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On,
                    ignoreOverlays = false
                }
            };
            return root;
        }

        public static GameObject CreatePickupModel()
        {
            EnsureShared();

            // Offset so the whole model is roughly centered on the pickup's origin
            Matrix4x4 body = Matrix4x4.Translate(new Vector3(0f, 0.2f, 0f));
            Mesh mesh = BuildAnkletMesh(body, TiltDegrees, out Vector3 dropTop);

            var root = new GameObject("mdlAnkletOfBloodlust");
            root.transform.SetParent(prefabHolder.transform, false);
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            root.AddComponent<MeshRenderer>().sharedMaterial = material;

            var light = new GameObject("Glow").AddComponent<Light>();
            light.transform.SetParent(root.transform, false);
            light.transform.localPosition = body.MultiplyPoint3x4(dropTop + new Vector3(0f, -DropHeight * 0.6f, 0f));
            light.type = LightType.Point;
            light.color = BloodGlow;
            light.range = 0.9f;
            light.intensity = 0.5f;

            // Camera framing for the logbook
            var focus = new GameObject("FocusPoint").transform;
            focus.SetParent(root.transform, false);
            var cam = new GameObject("CameraPosition").transform;
            cam.SetParent(root.transform, false);
            cam.localPosition = new Vector3(0f, 0.4f, 2.4f);

            var panel = root.AddComponent<ModelPanelParameters>();
            panel.focusPointTransform = focus;
            panel.cameraPositionTransform = cam;
            panel.modelRotation = Quaternion.identity;
            panel.minDistance = 1.2f;
            panel.maxDistance = 4f;

            return root;
        }

        // Strip texture: band | bone | blood, each part samples the center of its third
        private const float BandU = 1f / 6f;
        private const float BoneU = 0.5f;
        private const float BloodU = 5f / 6f;

        private static Texture2D Strip(Color band, Color bone, Color blood)
        {
            var tex = new Texture2D(3, 1, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            tex.SetPixels(new[] { band, bone, blood });
            tex.Apply();
            return tex;
        }

        private static Material MakeAtlasMaterial()
        {
            Texture2D albedo = Strip(BandColor, BoneColor, BloodColor);
            Texture2D glow = Strip(Color.black, Color.black, BloodGlow); // only the drop glows

            Material mat;
            if (hgStandard)
            {
                mat = new Material(hgStandard);
                mat.SetTexture("_MainTex", albedo);
                mat.SetColor("_Color", Color.white);
                mat.SetFloat("_Smoothness", 0.75f);
                mat.SetFloat("_Cull", 0f); // two-sided, so mesh winding never matters
                // Only glow if the shader can limit it to the drop; otherwise the whole anklet would glow
                if (mat.HasProperty("_EmTex"))
                {
                    mat.SetTexture("_EmTex", glow);
                    mat.SetColor("_EmColor", Color.white);
                    mat.SetFloat("_EmPower", 0.5f);
                }
                else
                {
                    mat.SetColor("_EmColor", Color.black);
                    mat.SetFloat("_EmPower", 0f);
                }
            }
            else
            {
                mat = new Material(Shader.Find("Standard"));
                mat.mainTexture = albedo;
                mat.SetFloat("_Glossiness", 0.75f);
                mat.EnableKeyword("_EMISSION");
                mat.SetTexture("_EmissionMap", glow);
                mat.SetColor("_EmissionColor", Color.white * 0.5f);
            }
            return mat;
        }

        private static Mesh WithColor(Mesh mesh, float u)
        {
            var uvs = new Vector2[mesh.vertexCount];
            for (int i = 0; i < uvs.Length; i++) uvs[i] = new Vector2(u, 0.5f);
            mesh.uv = uvs;
            return mesh;
        }

        private static void AddPart(List<CombineInstance> parts, Mesh mesh, Matrix4x4 transform)
        {
            parts.Add(new CombineInstance { mesh = mesh, transform = transform });
        }

        // Ring lying in the XZ plane
        private static Mesh Torus(float radius, float thickness, int segments, int sides)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i < segments; i++)
            {
                float u = i * Mathf.PI * 2f / segments;
                var center = new Vector3(Mathf.Sin(u), 0f, Mathf.Cos(u)) * radius;
                for (int j = 0; j < sides; j++)
                {
                    float v = j * Mathf.PI * 2f / sides;
                    var outward = new Vector3(Mathf.Sin(u), 0f, Mathf.Cos(u));
                    verts.Add(center + (outward * Mathf.Cos(v) + Vector3.up * Mathf.Sin(v)) * thickness);
                }
            }
            for (int i = 0; i < segments; i++)
            {
                int ni = (i + 1) % segments;
                for (int j = 0; j < sides; j++)
                {
                    int nj = (j + 1) % sides;
                    int a = i * sides + j, b = ni * sides + j, c = ni * sides + nj, d = i * sides + nj;
                    tris.AddRange(new[] { a, b, c, a, c, d });
                }
            }
            return BuildMesh(verts, tris);
        }

        // Point along +Y
        private static Mesh Cone(float baseRadius, float length, int segments)
        {
            return Lathe(new[] { new Vector2(0f, 0f), new Vector2(baseRadius, 0f), new Vector2(0f, length) }, segments);
        }

        // Pointed at the top (origin), round at the bottom
        private static Mesh Teardrop(float width, float height, int segments, int steps)
        {
            var profile = new Vector2[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                float t = Mathf.PI * i / steps;
                float r = width * 0.5f * Mathf.Sin(t) * (1f - Mathf.Cos(t)) * 0.5f * 1.3f;
                // Filled bottom-up like the other lathed parts, so the faces point outward;
                // inward faces were skipped by the pickup outline and lit from inside
                profile[steps - i] = new Vector2(r, -height * (1f - Mathf.Cos(t)) * 0.5f);
            }
            return Lathe(profile, segments);
        }

        // Spins a (radius, height) profile around the Y axis
        private static Mesh Lathe(Vector2[] profile, int segments)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            foreach (Vector2 p in profile)
            {
                for (int i = 0; i < segments; i++)
                {
                    float a = i * Mathf.PI * 2f / segments;
                    verts.Add(new Vector3(Mathf.Sin(a) * p.x, p.y, Mathf.Cos(a) * p.x));
                }
            }
            for (int k = 0; k < profile.Length - 1; k++)
            {
                for (int i = 0; i < segments; i++)
                {
                    int ni = (i + 1) % segments;
                    int a = k * segments + i, b = k * segments + ni, c = (k + 1) * segments + ni, d = (k + 1) * segments + i;
                    tris.AddRange(new[] { a, b, c, a, c, d });
                }
            }
            return BuildMesh(verts, tris);
        }

        private static Mesh BuildMesh(List<Vector3> verts, List<int> tris)
        {
            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
