using System.IO;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace AnkletOfBloodlust
{
    // Loads the "ankletofbloodlust" AssetBundle that ships next to the DLL. It is built by the Unity
    // project's Anklet > Build AssetBundle menu and holds the model prefab and the icons.
    internal static class AssetManager
    {
        public const string BundleName = "ankletofbloodlust";

        public static AssetBundle bundle;

        public static void Init(string pluginLocation)
        {
            string path = Path.Combine(Path.GetDirectoryName(pluginLocation), BundleName);
            bundle = AssetBundle.LoadFromFile(path);
            if (!bundle) Log.Error($"Couldn't load the AssetBundle at {path}");
        }

        public static Sprite LoadSprite(string name)
        {
            Sprite sprite = bundle.LoadAsset<Sprite>(name);
            if (!sprite)
            {
                Log.Warning($"Missing sprite {name}. Substituting default...");
                sprite = Addressables.LoadAssetAsync<Sprite>("RoR2/Base/Common/MiscIcons/texMysteryIcon.png").WaitForCompletion();
            }
            return sprite;
        }

        public static GameObject LoadPrefab(string name)
        {
            GameObject prefab = bundle.LoadAsset<GameObject>(name);
            if (!prefab)
            {
                Log.Warning($"Missing prefab {name}. Substituting default...");
                return Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Mystery/PickupMystery.prefab").WaitForCompletion();
            }

            // Materials are made with Unity's Standard shader; switch them to the game's own shader
            // so the model is lit like vanilla items (Standard may not exist in the game build)
            Shader hgStandard = Addressables.LoadAssetAsync<Shader>("RoR2/Base/Shaders/HGStandard.shader").WaitForCompletion();
            foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material.shader.name == "Standard")
                        material.shader = hgStandard;
                }
            }
            return prefab;
        }
    }
}
