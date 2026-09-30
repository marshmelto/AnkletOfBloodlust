using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using R2API;
using RoR2;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AnkletOfBloodlust
{
    // Where the anklet sits on each survivor.
    //
    // Every survivor that has a vanilla Paul's Goat Hoof placement gets the anklet at that same
    // ankle spot automatically. To hand-place a survivor instead, use ItemDisplayPlacementHelper
    // in game and add its output to HandPlaced(); hand-placed rules always win.
    internal static class ItemDisplays
    {
        // Commando's anklet scale, relative to his hoof; every other survivor scales the same way
        private const float CommandoScale = 0.2f;
        // The calf bone's +Y runs down toward the foot, so flip the anklet to hang the drop downward
        private static readonly Vector3 AnkletAngles = new Vector3(180f, 0f, 0f);

        // The anklet's inner radius at scale 1 (ring radius minus band thickness in AnkletAssets),
        // and how much room to leave around a measured leg
        private const float AnkletInnerRadius = 0.32f;
        private const float LegClearance = 1.15f;

        private static GameObject displayPrefab;

        public static ItemDisplayRuleDict Create(GameObject display)
        {
            displayPrefab = display;
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
                ["MercBody"] = Rule("CalfR", new Vector3(-0.00708F, 0.32992F, 0.01145F), flip, Vector3.one * 0.21176F),
                ["LoaderBody"] = Rule("CalfR", new Vector3(0.01126F, 0.412F, 0.00776F), flip, Vector3.one * 0.28235F),
                ["CrocoBody"] = Rule("CalfR", new Vector3(0.10253F, 2.68812F, -0.01258F), flip, Vector3.one * 1.88235F), // Acrid
                ["CaptainBody"] = Rule("CalfR", new Vector3(-0.00282F, 0.30095F, 0.02098F), flip, Vector3.one * 0.23529F),
                ["VoidSurvivorBody"] = Rule("CalfR", new Vector3(0.00669F, 0.37404F, -0.00169F), flip, Vector3.one * 0.2F), // Void Fiend
                ["SeekerBody"] = Rule("CalfR", new Vector3(-0.00105F, 0.42784F, 0.00928F), flip, Vector3.one * 0.20366F),
                ["FalseSonBody"] = Rule("CalfR", new Vector3(-0.02895F, 0.51184F, 0.00256F), flip, Vector3.one * 0.27812F),
                ["DrifterBody"] = Rule("CalfR", new Vector3(-0.07849F, -0.01391F, -0.01058F),
                    new Vector3(344.7775F, 167.481F, 268.9557F), Vector3.one * 0.37632F),
                ["DroneTechBody"] = Rule("CalfR", new Vector3(0.53767F, -0.00194F, -0.03339F), // Operator
                    new Vector3(34.70116F, 187.0946F, 280.0397F), Vector3.one * 0.18549F),
            };
        }

        public static ItemDisplayRule Rule(string bone, Vector3 position, Vector3 angles, Vector3 scale)
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
        public static void ApplyDisplayRules(ItemDef anklet, ManualLogSource log)
        {
            ItemDef hoof = RoR2Content.Items.Hoof;
            FieldInfo runtimeGroups = typeof(ItemDisplayRuleSet).GetField("runtimeItemRuleGroups", BindingFlags.Instance | BindingFlags.NonPublic);

            // Keep the stored rules and the runtime lookup (what CharacterModel reads) in sync
            void SetRule(ItemDisplayRuleSet ruleSet, ItemDisplayRule rule)
            {
                var group = new DisplayRuleGroup { rules = new[] { rule } };
                ruleSet.SetDisplayRuleGroup(anklet, group);
                if (runtimeGroups?.GetValue(ruleSet) is DisplayRuleGroup[] runtime && (int)anklet.itemIndex < runtime.Length)
                    runtime[(int)anklet.itemIndex] = group;
            }

            int placed = 0;
            foreach (KeyValuePair<string, ItemDisplayRule> entry in HandPlaced())
            {
                ItemDisplayRuleSet ruleSet = RuleSetOf(BodyCatalog.FindBodyPrefab(entry.Key));
                if (!ruleSet)
                {
                    log.LogWarning($"No body named {entry.Key}; its hand-placed anklet was skipped.");
                    continue;
                }
                SetRule(ruleSet, entry.Value);
                placed++;
            }
            log.LogInfo($"Anklet hand-placed on {placed} survivors.");

            // Fallback default placement for anyone not hand-placed above: survivors not placed yet,
            // survivors added by future updates, and modded survivors. Any body with a Goat Hoof
            // placement gets the anklet at that ankle.
            float scalePerHoof = 0f;
            ItemDisplayRuleSet commando = RuleSetOf(BodyCatalog.FindBodyPrefab("CommandoBody"));
            if (commando && TryGetHoofRule(commando, hoof, out ItemDisplayRule commandoHoof))
                scalePerHoof = CommandoScale / commandoHoof.localScale.x;
            if (scalePerHoof <= 0f)
            {
                log.LogWarning("Couldn't read Commando's hoof placement; the anklet won't show on survivors without a hand-placed rule.");
                return;
            }

            int added = 0, measured = 0;
            foreach (GameObject body in BodyCatalog.allBodyPrefabs)
            {
                ItemDisplayRuleSet ruleSet = RuleSetOf(body);
                if (!ruleSet) continue;
                if (!ruleSet.GetItemDisplayRuleGroup(anklet.itemIndex).isEmpty) continue; // already placed
                if (!TryGetHoofRule(ruleSet, hoof, out ItemDisplayRule hoofRule)) continue;

                // Fallback: the hoof's height on the bone, centered on the bone, sized from the hoof
                float height = hoofRule.localPos.y;
                var position = new Vector3(0f, height, 0f);
                float scale = hoofRule.localScale.x * scalePerHoof;

                // Better: measure the actual leg mesh around that height for its center and thickness
                if (TryMeasureLeg(body, hoofRule.childName, height, out Vector2 center, out float legRadius))
                {
                    position = new Vector3(center.x, height, center.y);
                    scale = legRadius * LegClearance / AnkletInnerRadius;
                    measured++;
                }

                SetRule(ruleSet, Rule(hoofRule.childName, position, AnkletAngles, Vector3.one * scale));
                added++;
            }
            log.LogInfo($"Anklet placed on {added} survivors/bodies from their Goat Hoof positions ({measured} fitted to the measured leg mesh).");
        }

        // Finds the leg's cross-section at the given height along the bone, in the bone's own space
        // (the space the display rule's position is in), from the vertices skinned to that bone.
        private static bool TryMeasureLeg(GameObject body, string boneName, float height, out Vector2 center, out float radius)
        {
            center = Vector2.zero;
            radius = 0f;

            ModelLocator locator = body.GetComponent<ModelLocator>();
            Transform model = locator ? locator.modelTransform : null;
            ChildLocator children = model ? model.GetComponent<ChildLocator>() : null;
            Transform bone = children ? children.FindChild(boneName) : null;
            if (!bone) return false;

            var slice = new List<(float dy, Vector3 p)>();
            foreach (SkinnedMeshRenderer skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = skin.sharedMesh;
                if (!mesh || !mesh.isReadable) continue;
                int boneIndex = Array.IndexOf(skin.bones, bone);
                if (boneIndex < 0 || boneIndex >= mesh.bindposes.Length) continue;

                Matrix4x4 toBone = mesh.bindposes[boneIndex];
                Vector3[] vertices = mesh.vertices;
                BoneWeight[] weights = mesh.boneWeights;
                for (int i = 0; i < vertices.Length && i < weights.Length; i++)
                {
                    if (weights[i].boneIndex0 != boneIndex || weights[i].weight0 < 0.5f) continue;
                    Vector3 p = toBone.MultiplyPoint3x4(vertices[i]);
                    slice.Add((Mathf.Abs(p.y - height), p));
                }
            }

            // The ring of vertices closest to the anklet's height
            if (slice.Count < 12) return false;
            slice.Sort((a, b) => a.dy.CompareTo(b.dy));
            int count = Mathf.Min(slice.Count, 48);

            Vector2 sum = Vector2.zero;
            for (int i = 0; i < count; i++) sum += new Vector2(slice[i].p.x, slice[i].p.z);
            center = sum / count;

            float total = 0f;
            for (int i = 0; i < count; i++) total += Vector2.Distance(center, new Vector2(slice[i].p.x, slice[i].p.z));
            radius = total / count;
            return radius > 0f;
        }

        private static ItemDisplayRuleSet RuleSetOf(GameObject body)
        {
            if (!body) return null;
            ModelLocator locator = body.GetComponent<ModelLocator>();
            if (!locator || !locator.modelTransform) return null;
            CharacterModel model = locator.modelTransform.GetComponent<CharacterModel>();
            return model ? model.itemDisplayRuleSet : null;
        }

        private static bool TryGetHoofRule(ItemDisplayRuleSet ruleSet, ItemDef hoof, out ItemDisplayRule rule)
        {
            ItemDisplayRule[] rules = ruleSet.GetItemDisplayRuleGroup(hoof.itemIndex).rules;
            if (rules != null)
            {
                foreach (ItemDisplayRule r in rules)
                {
                    if (r.ruleType == ItemDisplayRuleType.ParentedPrefab && !string.IsNullOrEmpty(r.childName))
                    {
                        rule = r;
                        return true;
                    }
                }
            }
            rule = default;
            return false;
        }
    }
}
