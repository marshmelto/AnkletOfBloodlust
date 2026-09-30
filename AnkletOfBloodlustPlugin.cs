using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;

namespace AnkletOfBloodlust
{
    [BepInDependency(ItemAPI.PluginGUID)]
    [BepInDependency(LanguageAPI.PluginGUID)]
    [BepInDependency(RecalculateStatsAPI.PluginGUID)]
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class AnkletOfBloodlustPlugin : BaseUnityPlugin
    {
        public const string PluginGUID = "prana.AnkletOfBloodlust";
        public const string PluginName = "AnkletOfBloodlust";
        public const string PluginVersion = "1.0.0";

        // Change these to rename the item in game
        private const string ItemName = "Anklet of Bloodlust";
        private const string BuffName = "Bloodlust";

        private static ItemDef itemDef;
        private static BuffDef bloodlustBuff;

        private static ConfigEntry<int> maxStacksPerItem;
        private static ConfigEntry<float> attackSpeedPerStack;
        private static ConfigEntry<float> moveSpeedPerStack;
        private static ConfigEntry<float> healthPenaltyPerItem;
        private static ConfigEntry<float> stacksLostOnHit;
        private static ConfigEntry<float> minHitFraction;

        public void Awake()
        {
            BindConfig();
            CreateBuff();
            CreateItem();
            AddLanguage();

            GlobalEventManager.onCharacterDeathGlobal += OnCharacterDeath;
            GlobalEventManager.onServerDamageDealt += OnServerDamageDealt;
            RecalculateStatsAPI.GetStatCoefficients += OnGetStatCoefficients;
            RoR2Application.onLoad += () => ItemDisplays.ApplyDisplayRules(itemDef, Logger);
        }

        private void BindConfig()
        {
            maxStacksPerItem = Config.Bind("Bloodlust", "MaxStacksPerItem", 10,
                "Maximum Bloodlust stacks granted per copy of the item.");
            attackSpeedPerStack = Config.Bind("Bloodlust", "AttackSpeedPerStack", 0.05f,
                "Attack speed bonus per Bloodlust stack (0.05 = +5%).");
            moveSpeedPerStack = Config.Bind("Bloodlust", "MoveSpeedPerStack", 0.04f,
                "Movement speed bonus per Bloodlust stack (0.04 = +4%).");
            stacksLostOnHit = Config.Bind("Bloodlust", "StacksLostOnHit", 0.5f,
                "Fraction of Bloodlust stacks lost when hit, rounded up (0.5 = half, 1 = all).");
            minHitFraction = Config.Bind("Bloodlust", "MinHitFraction", 0.05f,
                "Hits smaller than this fraction of your max health + shield don't remove stacks (0.05 = 5%).");
            healthPenaltyPerItem = Config.Bind("Downside", "HealthPenaltyPerItem", 0.15f,
                "Max health reduction per copy, applied multiplicatively (0.15 = -15%, two copies = -27.75%).");
        }

        private static void CreateBuff()
        {
            bloodlustBuff = ScriptableObject.CreateInstance<BuffDef>();
            bloodlustBuff.name = "bdAnkletOfBloodlust";
            bloodlustBuff.buffColor = new Color(0.8f, 0.1f, 0.15f);
            bloodlustBuff.canStack = true;
            bloodlustBuff.isDebuff = false;
            bloodlustBuff.isCooldown = false;
            bloodlustBuff.isHidden = false;
            // White silhouette; the game tints it with buffColor
            bloodlustBuff.iconSprite = AnkletAssets.LoadSprite("texBloodlustBuffIcon.png");
            ContentAddition.AddBuffDef(bloodlustBuff);
        }

        private static void CreateItem()
        {
            itemDef = ScriptableObject.CreateInstance<ItemDef>();
            itemDef.name = "ANKLETOFBLOODLUST";
            itemDef.nameToken = "ANKLETOFBLOODLUST_NAME";
            itemDef.pickupToken = "ANKLETOFBLOODLUST_PICKUP";
            itemDef.descriptionToken = "ANKLETOFBLOODLUST_DESC";
            itemDef.loreToken = "ANKLETOFBLOODLUST_LORE";
            SetLunarTier(itemDef);
            itemDef.canRemove = true;
            itemDef.hidden = false;
            itemDef.tags = new[] { ItemTag.Damage, ItemTag.Utility };

#pragma warning disable CS0618 // pickupModelPrefab is kept for mods
            itemDef.pickupModelPrefab = AnkletAssets.CreatePickupModel();
#pragma warning restore CS0618
            itemDef.pickupIconSprite = AnkletAssets.LoadSprite("texAnkletOfBloodlustIcon.png");

            ItemAPI.Add(new CustomItem(itemDef, ItemDisplays.Create(AnkletAssets.CreateDisplayModel())));
        }

        // ItemDef.tier's setter looks the tier up in ItemTierCatalog, which is still empty while mods
        // load, so it silently leaves the item as Tier1. Assign the lunar ItemTierDef asset directly.
        private static void SetLunarTier(ItemDef def)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            ItemTierDef lunarTier = Addressables.LoadAssetAsync<ItemTierDef>("RoR2/Base/Common/LunarTierDef.asset").WaitForCompletion();
            typeof(ItemDef).GetField("_itemTierDef", flags).SetValue(def, lunarTier);
            typeof(ItemDef).GetField("deprecatedTier", flags).SetValue(def, ItemTier.Lunar);
        }

        private void AddLanguage()
        {
            LanguageAPI.Add("ANKLETOFBLOODLUST_NAME", ItemName);
            LanguageAPI.Add("ANKLETOFBLOODLUST_PICKUP",
                "Kills build Bloodlust, increasing attack and movement speed. Big hits lose half your Bloodlust. <style=cDeath>Reduces maximum health.</style>");
            LanguageAPI.Add("ANKLETOFBLOODLUST_DESC",
                $"Killing an enemy grants a stack of <style=cIsDamage>{BuffName}</style>, up to <style=cIsUtility>{maxStacksPerItem.Value}</style> <style=cStack>(+{maxStacksPerItem.Value} per stack)</style>. " +
                $"Each stack gives <style=cIsDamage>+{attackSpeedPerStack.Value * 100f:0.#}% attack speed</style> and <style=cIsUtility>+{moveSpeedPerStack.Value * 100f:0.#}% movement speed</style>. " +
                $"Taking at least <style=cIsHealth>{minHitFraction.Value * 100f:0.#}% of your max health</style> in one hit <style=cDeath>removes {stacksLostOnHit.Value * 100f:0.#}% of your stacks</style>. " +
                $"<style=cDeath>Reduces maximum health by {healthPenaltyPerItem.Value * 100f:0.#}%</style> <style=cStack>(+{healthPenaltyPerItem.Value * 100f:0.#}% per stack, multiplicative)</style>.");
            LanguageAPI.Add("ANKLETOFBLOODLUST_LORE", "Every death feeds the next. Every wound drains it all.");
        }

        private static int GetItemCount(CharacterBody body)
        {
            return body && body.inventory ? body.inventory.GetItemCountEffective(itemDef) : 0;
        }

        private static void OnCharacterDeath(DamageReport report)
        {
            if (!NetworkServer.active) return;

            CharacterBody attacker = report.attackerBody;
            int itemCount = GetItemCount(attacker);
            if (itemCount <= 0) return;

            int maxStacks = maxStacksPerItem.Value * itemCount;
            if (attacker.GetBuffCount(bloodlustBuff) < maxStacks)
                attacker.AddBuff(bloodlustBuff);
        }

        private static void OnServerDamageDealt(DamageReport report)
        {
            if (!NetworkServer.active) return;

            CharacterBody victim = report.victimBody;
            if (!victim || report.damageDealt <= 0f) return;
            if (report.attackerBody == victim) return; // ignore self-damage

            int stacks = victim.GetBuffCount(bloodlustBuff);
            if (stacks <= 0) return;

            // Chip damage and small DoT ticks don't cost stacks
            HealthComponent health = victim.healthComponent;
            if (health && report.damageDealt < health.fullCombinedHealth * minHitFraction.Value) return;

            int lost = Mathf.CeilToInt(stacks * Mathf.Clamp01(stacksLostOnHit.Value));
            victim.SetBuffCount(bloodlustBuff.buffIndex, stacks - lost);
        }

        private static void OnGetStatCoefficients(CharacterBody body, RecalculateStatsAPI.StatHookEventArgs args)
        {
            int itemCount = GetItemCount(body);
            if (itemCount <= 0) return;

            int stacks = body.GetBuffCount(bloodlustBuff);
            args.attackSpeedMultAdd += attackSpeedPerStack.Value * stacks;
            args.moveSpeedMultAdd += moveSpeedPerStack.Value * stacks;

            // Multiplicative so max health never reaches zero: 1 copy -25%, 2 copies -43.75%, ...
            float healthKept = Mathf.Pow(1f - healthPenaltyPerItem.Value, itemCount);
            args.healthMultAdd -= 1f - healthKept;
        }
    }
}
