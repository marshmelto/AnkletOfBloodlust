using System.Collections.Generic;
using System.Reflection;
using R2API;
using RoR2;
using UnityEngine;

namespace AnkletOfBloodlust
{
    // Where the anklet sits on each survivor. Only the survivors listed in HandPlaced() show it;
    // place new ones with ItemDisplayPlacementHelper in game and add its output there.
    internal static class ItemDisplays
    {
        private static GameObject displayPrefab;

        public static ItemDisplayRuleDict Create(GameObject prefab)
        {
            // The same prefab is worn on survivors; ItemDisplay lets the character model hide it in
            // first person and apply overlays (cloak, freeze, ...) to its renderers
            displayPrefab = prefab;
            var infos = new List<CharacterModel.RendererInfo>();
            foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                infos.Add(new CharacterModel.RendererInfo
                {
                    renderer = renderer,
                    defaultMaterial = renderer.sharedMaterial,
                    defaultShadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On,
                    ignoreOverlays = false
                });
            }
            prefab.AddComponent<ItemDisplay>().rendererInfos = infos.ToArray();
            // Placements are applied in ApplyDisplayRules once the game has loaded, keyed by body
            // name, since some DLC survivors' model names aren't known ahead of time
            return new ItemDisplayRuleDict(null);
        }

        // Hand-placed with ItemDisplayPlacementHelper, keyed by body name
        private static Dictionary<string, ItemDisplayRule> HandPlaced()
        {
            var flip = new Vector3(-0.00001F, 180F, 180F);
            return new Dictionary<string, ItemDisplayRule>
            {
                ["CommandoBody"] = Rule("CalfR", new Vector3(-0.0019F, 0.34219F, 0.01456F), flip, Vector3.one * 0.2F),
                ["HuntressBody"] = Rule("CalfR", new Vector3(0.0144F, 0.3496F, 0.01774F), flip, Vector3.one * 0.17647F),
                ["Bandit2Body"] = Rule("CalfR", new Vector3(0.00564F, 0.33653F, 0.01252F), flip, Vector3.one * 0.2F),
                ["EngiBody"] = Rule("CalfR", new Vector3(0.00236F, 0.27453F, 0.02143F), flip, Vector3.one * 0.29349F),
                ["MageBody"] = Rule("CalfR", new Vector3(0.00187F, 0.47008F, 0.00318F), flip, // Artificer
                    new Vector3(0.10649F, 0.10649F, 0.1027F)),
                ["MercBody"] = Rule("CalfR", new Vector3(-0.00708F, 0.32992F, 0.01145F), flip, Vector3.one * 0.21176F),
                ["LoaderBody"] = Rule("CalfR", new Vector3(0.01126F, 0.412F, 0.00776F), flip, Vector3.one * 0.28235F),
                ["CrocoBody"] = Rule("CalfR", new Vector3(0.10253F, 2.68812F, -0.01258F), flip, Vector3.one * 1.88235F), // Acrid
                ["CaptainBody"] = Rule("CalfR", new Vector3(-0.00282F, 0.30095F, 0.02098F), flip, Vector3.one * 0.23529F),
                ["RailgunnerBody"] = Rule("CalfR", new Vector3(-0.00281F, 0.41252F, 0.00307F), flip, Vector3.one * 0.18913F),
                ["VoidSurvivorBody"] = Rule("CalfR", new Vector3(0.00669F, 0.37404F, -0.00169F), flip, Vector3.one * 0.2F), // Void Fiend
                ["SeekerBody"] = Rule("CalfR", new Vector3(-0.00105F, 0.42784F, 0.00928F), flip, Vector3.one * 0.20366F),
                ["FalseSonBody"] = Rule("CalfR", new Vector3(-0.02895F, 0.51184F, 0.00256F), flip, Vector3.one * 0.27812F),
                ["DrifterBody"] = Rule("CalfR", new Vector3(-0.07849F, -0.01391F, -0.01058F),
                    new Vector3(344.7775F, 167.481F, 268.9557F), Vector3.one * 0.37632F),
                ["DroneTechBody"] = Rule("CalfR", new Vector3(0.53767F, -0.00194F, -0.03339F), // Operator
                    new Vector3(34.70116F, 187.0946F, 280.0397F), Vector3.one * 0.18549F),
            };
        }

        private static ItemDisplayRule Rule(string bone, Vector3 position, Vector3 angles, Vector3 scale)
        {
            return new ItemDisplayRule
            {
                ruleType = ItemDisplayRuleType.ParentedPrefab,
                followerPrefab = displayPrefab,
                childName = bone,
                localPos = position,
                localAngles = angles,
                localScale = scale
            };
        }

        // Runs once the game has loaded, after every survivor's display rules are built
        public static void ApplyDisplayRules(ItemDef anklet)
        {
            FieldInfo runtimeGroups = typeof(ItemDisplayRuleSet).GetField("runtimeItemRuleGroups", BindingFlags.Instance | BindingFlags.NonPublic);

            foreach (KeyValuePair<string, ItemDisplayRule> entry in HandPlaced())
            {
                ItemDisplayRuleSet ruleSet = BodyCatalog.FindBodyPrefab(entry.Key)
                    .GetComponent<ModelLocator>().modelTransform
                    .GetComponent<CharacterModel>().itemDisplayRuleSet;

                // Keep the stored rules and the runtime lookup (what CharacterModel reads) in sync
                var group = new DisplayRuleGroup { rules = new[] { entry.Value } };
                ruleSet.SetDisplayRuleGroup(anklet, group);
                ((DisplayRuleGroup[])runtimeGroups.GetValue(ruleSet))[(int)anklet.itemIndex] = group;
            }
        }
    }
}
