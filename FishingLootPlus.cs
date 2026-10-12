using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Fishing Loot Plus", "Lix", "1.0.0")]
    [Description("Overpowered fishing loot: vanilla + custom drops, jackpots, per-location and per-bait loot tables.")]
    class FishingLootPlus : RustPlugin
    {
        #region Fields

        private const string PermAdmin = "fishinglootplus.admin";
        private const string PermUse = "fishinglootplus.use";
        private const string PermVip = "fishinglootplus.vip";
        private const string PermGod = "fishinglootplus.god";
        private const string PlayerStatsDataFile = "FishingLootPlus_PlayerStats";
        private const int VipCatchThreshold = 100;
        private const int GodCatchThreshold = 500;

        private ConfigData config;
        private readonly Dictionary<string, ItemDefinition> defCache = new Dictionary<string, ItemDefinition>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> registeredPerms = new HashSet<string>();
        private Dictionary<string, PlayerFishingStats> playerStats = new Dictionary<string, PlayerFishingStats>();

        private int statCatches;
        private int statJackpots;
        private long statItems;

        private class PlayerFishingStats
        {
            public string Name = "";
            public int Casts;
            public int Catches;
            public int Jackpots;
            public long Items;
            public int RewardDrops;
            public int BonusRolls;
        }

        #endregion

        #region Config

        private class LootEntry
        {
            [JsonProperty("Shortname")]
            public string Shortname = "scrap";

            [JsonProperty("Skin ID")]
            public ulong SkinID = 0;

            [JsonProperty("Custom name (blank = default)")]
            public string DisplayName = "";

            [JsonProperty("Weight (higher = more common)")]
            public float Weight = 10f;

            [JsonProperty("Min amount")]
            public int Min = 1;

            [JsonProperty("Max amount")]
            public int Max = 1;
        }

        private class ProfileConfig
        {
            [JsonProperty("Display name")]
            public string DisplayName = "";

            [JsonProperty("Quantity multiplier")]
            public float QuantityMultiplier = 1f;

            [JsonProperty("Jackpot chance multiplier")]
            public float JackpotChanceMultiplier = 1f;

            [JsonProperty("Extra roll chance bonus (percentage points)")]
            public float ExtraRollChanceBonus = 0f;

            [JsonProperty("Additional loot (added to common pool)")]
            public List<LootEntry> Loot = new List<LootEntry>();

            [JsonProperty("Additional jackpot loot (added to jackpot pool)")]
            public List<LootEntry> JackpotLoot = new List<LootEntry>();
        }

        private class LuckTier
        {
            [JsonProperty("Quantity multiplier")]
            public float QuantityMultiplier = 1f;

            [JsonProperty("Jackpot chance multiplier")]
            public float JackpotChanceMultiplier = 1f;
        }

        private class ConfigData
        {
            [JsonProperty("Enabled")]
            public bool Enabled = true;

            [JsonProperty("Require fishinglootplus.use permission")]
            public bool RequirePermission = false;

            [JsonProperty("Chance (%) to let a vanilla catch through untouched")]
            public float VanillaPassthroughChance = 10f;

            [JsonProperty("Global quantity multiplier")]
            public float GlobalQuantityMultiplier = 1f;

            [JsonProperty("Base chance (%) for a bonus roll")]
            public float BaseExtraRollChance = 40f;

            [JsonProperty("Max bonus rolls per catch")]
            public int MaxExtraRolls = 3;

            [JsonProperty("Base jackpot chance (%)")]
            public float BaseJackpotChance = 4f;

            [JsonProperty("Max jackpot chance (%) after multipliers")]
            public float MaxJackpotChance = 30f;

            [JsonProperty("Jackpot also gives normal loot")]
            public bool JackpotAlsoGivesNormalLoot = true;

            [JsonProperty("Announce jackpots to the whole server")]
            public bool AnnounceJackpot = true;

            [JsonProperty("Show catch message to the player")]
            public bool ShowCatchMessage = true;

            [JsonProperty("Jackpot effect prefab (blank = none)")]
            public string JackpotEffect = "assets/prefabs/deployable/research table/effects/research-success.prefab";

            [JsonProperty("Location detection distance in front of player (m)")]
            public float DetectionDistance = 8f;

            [JsonProperty("Default bait key (used when no bait is found)")]
            public string DefaultBaitKey = "default";

            [JsonProperty("Default location key (used when water type is unknown)")]
            public string DefaultLocationKey = "Default";

            [JsonProperty("Common loot (applies everywhere)")]
            public List<LootEntry> Common = new List<LootEntry>();

            [JsonProperty("Jackpot loot (applies everywhere)")]
            public List<LootEntry> Jackpot = new List<LootEntry>();

            [JsonProperty("Locations (keys: Ocean, River, Lake, Default)")]
            public Dictionary<string, ProfileConfig> Locations = new Dictionary<string, ProfileConfig>();

            [JsonProperty("Baits (key = bait item shortname, or 'default')")]
            public Dictionary<string, ProfileConfig> Baits = new Dictionary<string, ProfileConfig>();

            [JsonProperty("Luck permissions")]
            public Dictionary<string, LuckTier> LuckPermissions = new Dictionary<string, LuckTier>();
        }

        private static LootEntry E(string shortname, float weight, int min, int max, ulong skin = 0, string name = "")
        {
            return new LootEntry { Shortname = shortname, Weight = weight, Min = min, Max = max, SkinID = skin, DisplayName = name };
        }

        private static ConfigData CreateDefaultConfig()
        {
            var c = new ConfigData();

            // ---------- COMMON: vanilla fish + custom overpowered loot ----------
            c.Common = new List<LootEntry>
            {
                // Vanilla fish (boosted amounts)
                E("fish.troutsmall", 30, 2, 5),
                E("fish.yellowperch", 25, 2, 5),
                E("fish.herring", 25, 2, 5),
                E("fish.sardine", 25, 2, 5),
                E("fish.anchovy", 25, 2, 5),
                E("fish.salmon", 18, 2, 4),
                E("fish.catfish", 14, 1, 3),
                E("fish.orangeroughy", 10, 1, 3),
                E("fish.smallshark", 6, 1, 2),

                // Custom resources
                E("scrap", 30, 25, 120),
                E("metal.fragments", 25, 500, 1500),
                E("wood", 20, 1000, 3000),
                E("stones", 20, 800, 2500),
                E("cloth", 18, 100, 300),
                E("lowgradefuel", 15, 100, 400),
                E("sulfur", 14, 200, 600),
                E("gunpowder", 14, 100, 400),
                E("charcoal", 14, 500, 1500),
                E("metal.refined", 10, 30, 120),

                // Custom ammo / medical / components
                E("ammo.rifle", 12, 60, 200),
                E("ammo.pistol", 12, 60, 200),
                E("ammo.shotgun", 10, 24, 64),
                E("syringe.medical", 10, 3, 10),
                E("largemedkit", 8, 1, 4),
                E("gears", 8, 5, 20),
                E("techparts", 6, 2, 8),
                E("explosives", 6, 5, 20),
                E("grenade.f1", 4, 1, 5),
                E("explosive.satchel", 3, 1, 3)
            };

            // ---------- JACKPOT ----------
            c.Jackpot = new List<LootEntry>
            {
                E("scrap", 20, 800, 2000),
                E("metal.refined", 18, 800, 2000),
                E("sulfur", 18, 2000, 5000),
                E("explosive.timed", 10, 2, 6),
                E("ammo.rocket.basic", 10, 6, 16),
                E("ammo.rifle.explosive", 10, 100, 300),
                E("rifle.ak", 8, 1, 1),
                E("rifle.lr300", 6, 1, 1),
                E("lmg.m249", 3, 1, 1),
                E("rocket.launcher", 4, 1, 1),
                E("supply.signal", 6, 1, 3),
                E("metal.plate.torso", 6, 1, 1),
                E("metal.facemask", 6, 1, 1),
                E("targeting.computer", 5, 1, 2)
            };

            // ---------- LOCATIONS ----------
            c.Locations["Ocean"] = new ProfileConfig
            {
                DisplayName = "Deep Ocean",
                QuantityMultiplier = 1.5f,
                JackpotChanceMultiplier = 2f,
                ExtraRollChanceBonus = 10f,
                Loot = new List<LootEntry>
                {
                    E("fish.smallshark", 20, 1, 3),
                    E("fish.orangeroughy", 25, 2, 5),
                    E("scrap", 40, 100, 300),
                    E("metal.refined", 18, 60, 200),
                    E("diesel_barrel", 6, 1, 3),
                    E("explosive.satchel", 6, 1, 4)
                },
                JackpotLoot = new List<LootEntry>
                {
                    E("rocket.launcher", 8, 1, 1),
                    E("explosive.timed", 14, 4, 10),
                    E("ammo.rocket.basic", 12, 10, 24),
                    E("supply.signal", 10, 2, 5)
                }
            };

            c.Locations["River"] = new ProfileConfig
            {
                DisplayName = "River",
                QuantityMultiplier = 1.2f,
                JackpotChanceMultiplier = 1.2f,
                ExtraRollChanceBonus = 5f,
                Loot = new List<LootEntry>
                {
                    E("fish.salmon", 25, 3, 6),
                    E("fish.catfish", 20, 2, 4),
                    E("metal.ore", 28, 400, 1200),
                    E("sulfur.ore", 24, 300, 900),
                    E("stones", 20, 1000, 3000)
                },
                JackpotLoot = new List<LootEntry>
                {
                    E("hq.metal.ore", 16, 800, 2500),
                    E("lmg.m249", 4, 1, 1)
                }
            };

            c.Locations["Lake"] = new ProfileConfig
            {
                DisplayName = "Lake",
                QuantityMultiplier = 1.3f,
                JackpotChanceMultiplier = 1.5f,
                ExtraRollChanceBonus = 8f,
                Loot = new List<LootEntry>
                {
                    E("fish.yellowperch", 28, 3, 7),
                    E("fish.herring", 24, 3, 7),
                    E("gunpowder", 18, 200, 600),
                    E("techparts", 8, 4, 12)
                },
                JackpotLoot = new List<LootEntry>
                {
                    E("rifle.lr300", 10, 1, 1),
                    E("scrap", 14, 1500, 3500)
                }
            };

            c.Locations["Default"] = new ProfileConfig
            {
                DisplayName = "Open Water",
                QuantityMultiplier = 1f,
                JackpotChanceMultiplier = 1f,
                ExtraRollChanceBonus = 0f
            };

            // ---------- BAITS ----------
            c.Baits["default"] = new ProfileConfig
            {
                DisplayName = "No bait",
                QuantityMultiplier = 1f,
                JackpotChanceMultiplier = 1f
            };

            c.Baits["worm"] = new ProfileConfig
            {
                DisplayName = "Worm",
                QuantityMultiplier = 1.25f,
                JackpotChanceMultiplier = 1.5f,
                ExtraRollChanceBonus = 5f,
                Loot = new List<LootEntry>
                {
                    E("gunpowder", 12, 150, 450),
                    E("cloth", 12, 150, 400)
                }
            };

            c.Baits["grub"] = new ProfileConfig
            {
                DisplayName = "Grub",
                QuantityMultiplier = 1.5f,
                JackpotChanceMultiplier = 2f,
                ExtraRollChanceBonus = 10f,
                Loot = new List<LootEntry>
                {
                    E("lowgradefuel", 14, 200, 600),
                    E("gears", 10, 8, 25),
                    E("sulfur", 12, 300, 800)
                },
                JackpotLoot = new List<LootEntry>
                {
                    E("explosive.timed", 10, 3, 8)
                }
            };

            c.Baits["fish.anchovy"] = new ProfileConfig
            {
                DisplayName = "Anchovy (premium bait)",
                QuantityMultiplier = 2f,
                JackpotChanceMultiplier = 3f,
                ExtraRollChanceBonus = 20f,
                Loot = new List<LootEntry>
                {
                    E("metal.refined", 16, 100, 300),
                    E("techparts", 10, 5, 15),
                    E("largemedkit", 10, 2, 6)
                },
                JackpotLoot = new List<LootEntry>
                {
                    E("rifle.ak", 10, 1, 2),
                    E("lmg.m249", 5, 1, 1)
                }
            };

            // ---------- LUCK PERMISSIONS ----------
            c.LuckPermissions[PermVip] = new LuckTier { QuantityMultiplier = 1.25f, JackpotChanceMultiplier = 1.25f };
            c.LuckPermissions[PermGod] = new LuckTier { QuantityMultiplier = 2f, JackpotChanceMultiplier = 3f };

            return c;
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<ConfigData>();
                if (config == null) throw new Exception("Config was empty");
            }
            catch (Exception ex)
            {
                PrintWarning("Config invalid (" + ex.Message + "). Loading defaults. Fix or delete oxide/config/FishingLootPlus.json.");
                LoadDefaultConfig();
            }
            Sanitize();
            SaveConfig();
        }

        protected override void LoadDefaultConfig() { config = CreateDefaultConfig(); }
        protected override void SaveConfig() { Config.WriteObject(config, true); }

        private void LoadPlayerStats()
        {
            try
            {
                playerStats = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<string, PlayerFishingStats>>(PlayerStatsDataFile)
                    ?? new Dictionary<string, PlayerFishingStats>();
            }
            catch (Exception ex)
            {
                PrintWarning("Player fishing stats could not be loaded: " + ex.Message);
                playerStats = new Dictionary<string, PlayerFishingStats>();
            }
        }

        private void SavePlayerStats()
        {
            Interface.Oxide.DataFileSystem.WriteObject(PlayerStatsDataFile, playerStats);
        }

        private void Sanitize()
        {
            if (config.Common == null) config.Common = new List<LootEntry>();
            if (config.Jackpot == null) config.Jackpot = new List<LootEntry>();
            if (config.Locations == null) config.Locations = new Dictionary<string, ProfileConfig>();
            if (config.Baits == null) config.Baits = new Dictionary<string, ProfileConfig>();
            if (config.LuckPermissions == null) config.LuckPermissions = new Dictionary<string, LuckTier>();
            foreach (var p in config.Locations.Values.Concat(config.Baits.Values))
            {
                if (p.Loot == null) p.Loot = new List<LootEntry>();
                if (p.JackpotLoot == null) p.JackpotLoot = new List<LootEntry>();
            }
            config.MaxExtraRolls = Mathf.Clamp(config.MaxExtraRolls, 0, 20);
            config.VanillaPassthroughChance = Mathf.Clamp(config.VanillaPassthroughChance, 0f, 100f);
        }

        private IEnumerable<LootEntry> AllEntries()
        {
            foreach (var e in config.Common) yield return e;
            foreach (var e in config.Jackpot) yield return e;
            foreach (var p in config.Locations.Values.Concat(config.Baits.Values))
            {
                foreach (var e in p.Loot) yield return e;
                foreach (var e in p.JackpotLoot) yield return e;
            }
        }

        private int ValidateConfig()
        {
            int bad = 0;
            foreach (var e in AllEntries())
            {
                if (e == null) continue;
                if (GetDef(e.Shortname) == null)
                {
                    bad++;
                    PrintWarning("Unknown item shortname in config (entry will be ignored): '" + e.Shortname + "'");
                }
                if (e.Max < e.Min) e.Max = e.Min;
                if (e.Min < 1) e.Min = 1;
            }
            return bad;
        }

        private void RegisterPerms()
        {
            var all = new List<string> { PermAdmin, PermUse };
            all.AddRange(config.LuckPermissions.Keys);
            foreach (var p in all)
            {
                var perm = p.ToLowerInvariant();
                if (registeredPerms.Contains(perm)) continue;
                permission.RegisterPermission(perm, this);
                registeredPerms.Add(perm);
            }
        }

        #endregion

        #region Lang

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPerm"] = "You don't have permission to use this command.",
                ["Catch"] = "<color=#6cc6ff>Fishing:</color> You reeled in {0} <color=#aaaaaa>[{1} | {2}]</color>",
                ["JackpotSelf"] = "<color=#ffd700>JACKPOT!</color> You hit the fishing jackpot: {0}",
                ["JackpotAnnounce"] = "<color=#ffd700>JACKPOT!</color> <color=#ffffff>{0}</color> just hit the fishing jackpot and reeled in {1}!",
                ["Reloaded"] = "FishingLootPlus config reloaded. Invalid entries: {0}",
                ["Toggled"] = "FishingLootPlus is now {0}.",
                ["PlayerNotFound"] = "Player '{0}' not found (or not online).",
                ["Given"] = "Gave {0} a roll: {1}",
                ["Stats"] = "== Fishing stats: {0} ==\nRank: {1}\nCasts: {2}\nCatches: {3}\nCatch rate: {4}%\nJackpots: {5}\nJackpot rate: {6}%\nItems received: {7}\nReward drops: {8}\nBonus rolls: {9}\nAverage items/catch: {10}\nFishing multiplier: x{11} quantity / x{12} jackpot",
                ["Leaderboard"] = "== Fishing leaderboard ==\n{0}",
                ["Help"] = "FishingLootPlus commands:\n" +
                           "/fishloot reload - reload config\n" +
                           "/fishloot toggle - enable/disable\n" +
                           "/fishloot stats - session stats\n" +
                           "/fishloot list - location & bait keys\n" +
                           "/fishloot info <location> [bait] - effective drop chances\n" +
                           "/fishloot test <location> [bait] [catches] - simulate catches\n" +
                           "/fishloot give <player> [jackpot] - force a loot roll"
            }, this);
        }

        private string Msg(string key, string id = null, params object[] args)
        {
            var m = lang.GetMessage(key, this, id);
            return args != null && args.Length > 0 ? string.Format(m, args) : m;
        }

        #endregion

        #region Oxide Hooks

        private void Init()
        {
            LoadPlayerStats();
            RegisterPerms();
            AddCovalenceCommand("fishloot", nameof(CmdFishLoot));
            AddCovalenceCommand("lb", nameof(CmdLeaderboard));
            AddCovalenceCommand("stats", nameof(CmdStats));
        }

        private void OnServerInitialized()
        {
            int bad = ValidateConfig();
            foreach (var entry in playerStats)
                GrantCatchPermissions(entry.Key, entry.Value);
            SavePlayerStats();
            Puts("Loaded. " + (bad == 0 ? "All loot entries valid." : bad + " invalid entries (see warnings)."));
        }

        private object OnFishCatch(Item fish, BaseFishingRod rod, BasePlayer player)
        {
            if (config == null || !config.Enabled || fish == null || rod == null || player == null) return null;

            if (config.RequirePermission && !permission.UserHasPermission(player.UserIDString, PermUse)) return null;

            var stats = RecordCatch(player);

            // Let some catches stay vanilla ("Vanilla + custom")
            if (Roll(config.VanillaPassthroughChance)) return null;

            string locKey = DetectLocation(player);
            string baitKey = DetectBait(rod);
            LuckTier luck = GetLuck(player);

            var result = Generate(locKey, baitKey, luck.QuantityMultiplier, luck.JackpotChanceMultiplier, false);
            if (result.Rewards.Count == 0) return null;

            // Remove the vanilla fish (next tick covers both blocked and non-blocked cases)
            NextTick(() => { if (fish != null) fish.Remove(); });

            DeliverRewards(player, result);
            stats.Jackpots += result.Jackpot ? 1 : 0;
            stats.Items += result.Rewards.Sum(r => (long)r.Amount);
            stats.RewardDrops += result.Rewards.Count;
            stats.BonusRolls += result.BonusRolls;
            SavePlayerStats();
            return true;
        }

        private void OnFishingRodCast(BaseFishingRod rod, BasePlayer player)
        {
            if (config == null || !config.Enabled || rod == null || player == null) return;
            if (config.RequirePermission && !permission.UserHasPermission(player.UserIDString, PermUse)) return;

            RecordCast(player);
        }

        #endregion

        #region Core Logic

        private class Reward
        {
            public LootEntry Entry;
            public int Amount;
            public bool FromJackpot;
        }

        private class RollResult
        {
            public List<Reward> Rewards = new List<Reward>();
            public bool Jackpot;
            public string LocationKey;
            public string BaitKey;
            public string LocationName;
            public string BaitName;
            public int BonusRolls;
        }

        private RollResult Generate(string locKey, string baitKey, float luckQty, float luckJackpot, bool forceJackpot)
        {
            var res = new RollResult { LocationKey = locKey, BaitKey = baitKey };

            var lp = FindProfile(config.Locations, locKey, config.DefaultLocationKey);
            var bp = FindProfile(config.Baits, baitKey, config.DefaultBaitKey);
            res.LocationName = !string.IsNullOrEmpty(lp.DisplayName) ? lp.DisplayName : locKey;
            res.BaitName = !string.IsNullOrEmpty(bp.DisplayName) ? bp.DisplayName : baitKey;

            float qty = config.GlobalQuantityMultiplier * lp.QuantityMultiplier * bp.QuantityMultiplier * luckQty;
            float jackpotChance = Mathf.Min(
                config.BaseJackpotChance * lp.JackpotChanceMultiplier * bp.JackpotChanceMultiplier * luckJackpot,
                config.MaxJackpotChance);

            bool jackpot = forceJackpot || Roll(jackpotChance);
            if (jackpot)
            {
                var jEntry = Pick(BuildPool(lp, bp, true));
                if (jEntry != null)
                {
                    res.Rewards.Add(new Reward { Entry = jEntry, Amount = RollAmount(jEntry, qty), FromJackpot = true });
                    res.Jackpot = true;
                }
            }

            if (!res.Jackpot || config.JackpotAlsoGivesNormalLoot)
            {
                float extraChance = config.BaseExtraRollChance + lp.ExtraRollChanceBonus + bp.ExtraRollChanceBonus;
                int rolls = 1;
                for (int i = 0; i < config.MaxExtraRolls; i++)
                {
                    if (Roll(extraChance)) rolls++;
                    else break;
                }
                res.BonusRolls = rolls - 1;

                var pool = BuildPool(lp, bp, false);
                for (int i = 0; i < rolls; i++)
                {
                    var entry = Pick(pool);
                    if (entry == null) break;
                    res.Rewards.Add(new Reward { Entry = entry, Amount = RollAmount(entry, qty) });
                }
            }

            return res;
        }

        private void DeliverRewards(BasePlayer player, RollResult result)
        {
            var sb = new StringBuilder();
            var jackpotSb = new StringBuilder();

            foreach (var r in result.Rewards)
            {
                GiveReward(player, r.Entry, r.Amount);
                string text = r.Amount + "x " + ItemName(r.Entry);
                var target = r.FromJackpot ? jackpotSb : sb;
                if (target.Length > 0) target.Append(", ");
                target.Append(text);
                statItems += r.Amount;
            }

            if (result.Jackpot)
            {
                statJackpots++;
                player.ChatMessage(Msg("JackpotSelf", player.UserIDString, jackpotSb.ToString()));
                if (config.AnnounceJackpot)
                    covalence.Server.Broadcast(Msg("JackpotAnnounce", null, player.displayName, jackpotSb.ToString()));
                if (!string.IsNullOrEmpty(config.JackpotEffect))
                {
                    try { Effect.server.Run(config.JackpotEffect, player.transform.position, Vector3.up, null, true); }
                    catch { /* invalid prefab path - ignore */ }
                }
            }

            if (config.ShowCatchMessage && sb.Length > 0)
                player.ChatMessage(Msg("Catch", player.UserIDString, sb.ToString(), result.LocationName, result.BaitName));
        }

        private void GiveReward(BasePlayer player, LootEntry e, int amount)
        {
            var def = GetDef(e.Shortname);
            if (def == null) return;

            int stack = Mathf.Max(1, def.stackable);
            int remaining = amount;
            int guard = 0;
            while (remaining > 0 && guard++ < 200)
            {
                int n = Mathf.Min(remaining, stack);
                var item = ItemManager.Create(def, n, e.SkinID);
                if (item == null) break;
                if (!string.IsNullOrEmpty(e.DisplayName)) item.name = e.DisplayName;
                if (!player.inventory.GiveItem(item))
                    item.Drop(player.GetDropPosition(), player.GetDropVelocity());
                remaining -= n;
            }
        }

        private List<LootEntry> BuildPool(ProfileConfig lp, ProfileConfig bp, bool jackpot)
        {
            var pool = new List<LootEntry>();
            pool.AddRange(jackpot ? config.Jackpot : config.Common);
            pool.AddRange(jackpot ? lp.JackpotLoot : lp.Loot);
            pool.AddRange(jackpot ? bp.JackpotLoot : bp.Loot);
            return pool;
        }

        private LootEntry Pick(List<LootEntry> pool)
        {
            float total = 0f;
            foreach (var e in pool)
                if (IsUsable(e)) total += e.Weight;
            if (total <= 0f) return null;

            float roll = UnityEngine.Random.Range(0f, total);
            LootEntry last = null;
            foreach (var e in pool)
            {
                if (!IsUsable(e)) continue;
                last = e;
                roll -= e.Weight;
                if (roll <= 0f) return e;
            }
            return last;
        }

        private bool IsUsable(LootEntry e)
        {
            return e != null && e.Weight > 0f && GetDef(e.Shortname) != null;
        }

        private int RollAmount(LootEntry e, float multiplier)
        {
            int min = Mathf.Max(1, e.Min);
            int max = Mathf.Max(min, e.Max);
            int baseAmt = UnityEngine.Random.Range(min, max + 1);
            return Mathf.Max(1, Mathf.RoundToInt(baseAmt * multiplier));
        }

        private static bool Roll(float percent)
        {
            if (percent <= 0f) return false;
            if (percent >= 100f) return true;
            return UnityEngine.Random.Range(0f, 100f) < percent;
        }

        private ItemDefinition GetDef(string shortname)
        {
            if (string.IsNullOrEmpty(shortname)) return null;
            ItemDefinition def;
            if (defCache.TryGetValue(shortname, out def)) return def;
            def = ItemManager.FindItemDefinition(shortname);
            defCache[shortname] = def;
            return def;
        }

        private string ItemName(LootEntry e)
        {
            if (!string.IsNullOrEmpty(e.DisplayName)) return e.DisplayName;
            var def = GetDef(e.Shortname);
            return def != null ? def.displayName.english : e.Shortname;
        }

        private ProfileConfig FindProfile(Dictionary<string, ProfileConfig> dict, string key, string fallbackKey)
        {
            foreach (var kv in dict)
                if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            foreach (var kv in dict)
                if (string.Equals(kv.Key, fallbackKey, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            return new ProfileConfig();
        }

        private string DetectLocation(BasePlayer player)
        {
            try
            {
                if (TerrainMeta.TopologyMap == null) return config.DefaultLocationKey;

                Vector3 cast = player.transform.position + player.eyes.HeadForward() * config.DetectionDistance;
                var points = new[] { cast, player.transform.position };
                foreach (var p in points)
                {
                    if (TerrainMeta.TopologyMap.GetTopology(p, TerrainTopology.OCEAN)) return "Ocean";
                    if (TerrainMeta.TopologyMap.GetTopology(p, TerrainTopology.RIVER)) return "River";
                    if (TerrainMeta.TopologyMap.GetTopology(p, TerrainTopology.LAKE)) return "Lake";
                }
            }
            catch (Exception ex)
            {
                PrintWarning("Location detection failed: " + ex.Message);
            }
            return config.DefaultLocationKey;
        }

        private string DetectBait(BaseFishingRod rod)
        {
            try
            {
                var rodItem = rod.GetItem();
                var contents = rodItem != null ? rodItem.contents : null;
                if (contents != null && contents.itemList != null && contents.itemList.Count > 0)
                {
                    var bait = contents.itemList[0];
                    if (bait != null && bait.info != null) return bait.info.shortname;
                }
            }
            catch (Exception ex)
            {
                PrintWarning("Bait detection failed: " + ex.Message);
            }
            return config.DefaultBaitKey;
        }

        private LuckTier GetLuck(BasePlayer player)
        {
            LuckTier best = new LuckTier();
            float bestScore = 0f;
            foreach (var kv in config.LuckPermissions)
            {
                if (!permission.UserHasPermission(player.UserIDString, kv.Key)) continue;
                float score = kv.Value.QuantityMultiplier + kv.Value.JackpotChanceMultiplier;
                if (score > bestScore) { best = kv.Value; bestScore = score; }
            }

            PlayerFishingStats stats;
            if (playerStats.TryGetValue(player.UserIDString, out stats))
            {
                LuckTier catchTier;
                if (stats.Catches >= GodCatchThreshold && config.LuckPermissions.TryGetValue(PermGod, out catchTier))
                {
                    float score = catchTier.QuantityMultiplier + catchTier.JackpotChanceMultiplier;
                    if (score > bestScore) { best = catchTier; bestScore = score; }
                }
                else if (stats.Catches >= VipCatchThreshold && config.LuckPermissions.TryGetValue(PermVip, out catchTier))
                {
                    float score = catchTier.QuantityMultiplier + catchTier.JackpotChanceMultiplier;
                    if (score > bestScore) { best = catchTier; bestScore = score; }
                }
            }
            return best;
        }

        private PlayerFishingStats RecordCatch(BasePlayer player)
        {
            PlayerFishingStats stats;
            if (!playerStats.TryGetValue(player.UserIDString, out stats))
            {
                stats = new PlayerFishingStats();
                playerStats[player.UserIDString] = stats;
            }

            stats.Name = player.displayName;
            stats.Catches++;
            statCatches++;
            GrantCatchPermissions(player.UserIDString, stats);
            SavePlayerStats();
            return stats;
        }

        private void RecordCast(BasePlayer player)
        {
            PlayerFishingStats stats;
            if (!playerStats.TryGetValue(player.UserIDString, out stats))
            {
                stats = new PlayerFishingStats();
                playerStats[player.UserIDString] = stats;
            }

            stats.Name = player.displayName;
            stats.Casts++;
            SavePlayerStats();
        }

        private void GrantCatchPermissions(string userId, PlayerFishingStats stats)
        {
            if (stats == null) return;

            if (stats.Catches >= VipCatchThreshold && config.LuckPermissions.ContainsKey(PermVip) &&
                !permission.UserHasPermission(userId, PermVip))
                permission.GrantUserPermission(userId, PermVip, this);

            if (stats.Catches >= GodCatchThreshold && config.LuckPermissions.ContainsKey(PermGod) &&
                !permission.UserHasPermission(userId, PermGod))
                permission.GrantUserPermission(userId, PermGod, this);
        }

        #endregion

        #region Commands

        private void CmdLeaderboard(IPlayer iplayer, string command, string[] args)
        {
            var ranked = playerStats.Values
                .Where(s => s != null)
                .OrderByDescending(s => s.Catches)
                .ThenByDescending(s => s.Jackpots)
                .ThenBy(s => s.Name)
                .ToList();

            var sb = new StringBuilder();
            if (ranked.Count == 0)
            {
                sb.Append("No fishing catches have been recorded yet.");
            }
            else
            {
                int shown = Math.Min(3, ranked.Count);
                for (int i = 0; i < shown; i++)
                    sb.AppendLine(LeaderboardLine(i + 1, ranked[i]));

                if (!iplayer.IsServer)
                {
                    int ownIndex = FindStatsIndex(ranked, iplayer.Id);
                    if (ownIndex >= 0)
                        sb.Append("Your rank: " + LeaderboardLine(ownIndex + 1, ranked[ownIndex]));
                    else
                        sb.Append("Your rank: unranked (catch something to enter the leaderboard)");
                }
            }

            iplayer.Reply(Msg("Leaderboard", iplayer.Id, sb.ToString().TrimEnd()));
        }

        private string LeaderboardLine(int rank, PlayerFishingStats stats)
        {
            return "#" + rank + " " + DisplayStatsName(stats) + " - " +
                   stats.Casts + " casts, " + stats.Catches + " catches, " +
                   CatchRate(stats) + "% catch rate, " + stats.Jackpots + " jackpots, " +
                   JackpotRate(stats) + "% jackpot rate, " + stats.Items + " items";
        }

        private void CmdStats(IPlayer iplayer, string command, string[] args)
        {
            string id;
            PlayerFishingStats stats;
            string requestedName = args != null && args.Length > 0 ? string.Join(" ", args).Trim() : null;

            if (string.IsNullOrEmpty(requestedName))
            {
                if (iplayer.IsServer)
                {
                    iplayer.Reply("The server cannot view personal stats. Use /stats <player>.");
                    return;
                }
                id = iplayer.Id;
                playerStats.TryGetValue(id, out stats);
                if (stats == null)
                    stats = new PlayerFishingStats { Name = iplayer.Name };
            }
            else
            {
                id = FindStatsId(requestedName);
                stats = id != null ? playerStats[id] : null;
            }

            if (stats == null)
            {
                iplayer.Reply(Msg("PlayerNotFound", iplayer.Id, requestedName ?? iplayer.Name));
                return;
            }

            var ranked = playerStats.Values
                .Where(s => s != null)
                .OrderByDescending(s => s.Catches)
                .ThenByDescending(s => s.Jackpots)
                .ThenBy(s => s.Name)
                .ToList();
            int rank = FindStatsIndex(ranked, id) + 1;
            float quantityMultiplier = 1f;
            float jackpotMultiplier = 1f;
            var target = covalence.Players.FindPlayer(id);
            var targetPlayer = target != null ? target.Object as BasePlayer : null;
            if (targetPlayer != null)
            {
                var luck = GetLuck(targetPlayer);
                quantityMultiplier = luck.QuantityMultiplier;
                jackpotMultiplier = luck.JackpotChanceMultiplier;
            }
            else if (stats.Catches >= GodCatchThreshold && config.LuckPermissions.ContainsKey(PermGod))
            {
                quantityMultiplier = config.LuckPermissions[PermGod].QuantityMultiplier;
                jackpotMultiplier = config.LuckPermissions[PermGod].JackpotChanceMultiplier;
            }
            else if (stats.Catches >= VipCatchThreshold && config.LuckPermissions.ContainsKey(PermVip))
            {
                quantityMultiplier = config.LuckPermissions[PermVip].QuantityMultiplier;
                jackpotMultiplier = config.LuckPermissions[PermVip].JackpotChanceMultiplier;
            }

            iplayer.Reply(Msg("Stats", iplayer.Id, DisplayStatsName(stats), rank > 0 ? "#" + rank : "unranked",
                stats.Casts, stats.Catches, CatchRate(stats), stats.Jackpots, JackpotRate(stats),
                stats.Items, stats.RewardDrops, stats.BonusRolls, AverageItems(stats),
                quantityMultiplier.ToString("0.00"), jackpotMultiplier.ToString("0.00")));
        }

        private string FindStatsId(string query)
        {
            var online = covalence.Players.FindPlayer(query);
            if (online != null && playerStats.ContainsKey(online.Id)) return online.Id;

            foreach (var entry in playerStats)
                if (entry.Value != null &&
                    (string.Equals(entry.Key, query, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(entry.Value.Name, query, StringComparison.OrdinalIgnoreCase)))
                    return entry.Key;

            foreach (var entry in playerStats)
                if (entry.Value != null && !string.IsNullOrEmpty(entry.Value.Name) &&
                    entry.Value.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    return entry.Key;
            return null;
        }

        private int FindStatsIndex(List<PlayerFishingStats> ranked, string userId)
        {
            PlayerFishingStats stats;
            if (!playerStats.TryGetValue(userId, out stats)) return -1;
            return ranked.IndexOf(stats);
        }

        private string DisplayStatsName(PlayerFishingStats stats)
        {
            return stats == null || string.IsNullOrEmpty(stats.Name) ? "Unknown player" : stats.Name;
        }

        private string JackpotRate(PlayerFishingStats stats)
        {
            return stats.Catches > 0 ? (100f * stats.Jackpots / stats.Catches).ToString("0.00") : "0.00";
        }

        private string CatchRate(PlayerFishingStats stats)
        {
            return stats.Casts > 0 ? (100f * stats.Catches / stats.Casts).ToString("0.00") : "0.00";
        }

        private string AverageItems(PlayerFishingStats stats)
        {
            return stats.Catches > 0 ? ((float)stats.Items / stats.Catches).ToString("0.00") : "0.00";
        }

        private void CmdFishLoot(IPlayer iplayer, string command, string[] args)
        {
            if (!iplayer.IsServer && !iplayer.HasPermission(PermAdmin))
            {
                iplayer.Reply(Msg("NoPerm", iplayer.Id));
                return;
            }

            if (args == null || args.Length == 0)
            {
                iplayer.Reply(Msg("Help", iplayer.Id));
                return;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "reload":
                    defCache.Clear();
                    LoadConfig();
                    RegisterPerms();
                    iplayer.Reply(Msg("Reloaded", iplayer.Id, ValidateConfig()));
                    SaveConfig();
                    break;

                case "toggle":
                    config.Enabled = !config.Enabled;
                    SaveConfig();
                    iplayer.Reply(Msg("Toggled", iplayer.Id, config.Enabled ? "ENABLED" : "DISABLED"));
                    break;

                case "stats":
                    iplayer.Reply("Catches handled: " + statCatches + " | Jackpots: " + statJackpots +
                                  " | Items given: " + statItems +
                                  " | Jackpot rate: " + (statCatches > 0 ? (100f * statJackpots / statCatches).ToString("0.00") : "0") + "%");
                    break;

                case "list":
                    iplayer.Reply("Locations: " + string.Join(", ", config.Locations.Keys.ToArray()) +
                                  "\nBaits: " + string.Join(", ", config.Baits.Keys.ToArray()));
                    break;

                case "info":
                    CmdInfo(iplayer, args);
                    break;

                case "test":
                    CmdTest(iplayer, args);
                    break;

                case "give":
                    CmdGive(iplayer, args);
                    break;

                default:
                    iplayer.Reply(Msg("Help", iplayer.Id));
                    break;
            }
        }

        private void CmdInfo(IPlayer iplayer, string[] args)
        {
            string loc = args.Length > 1 ? args[1] : config.DefaultLocationKey;
            string bait = args.Length > 2 ? args[2] : config.DefaultBaitKey;
            var lp = FindProfile(config.Locations, loc, config.DefaultLocationKey);
            var bp = FindProfile(config.Baits, bait, config.DefaultBaitKey);

            float jc = Mathf.Min(config.BaseJackpotChance * lp.JackpotChanceMultiplier * bp.JackpotChanceMultiplier, config.MaxJackpotChance);
            float qty = config.GlobalQuantityMultiplier * lp.QuantityMultiplier * bp.QuantityMultiplier;

            var sb = new StringBuilder();
            sb.AppendLine("== " + loc + " / " + bait + " ==");
            sb.AppendLine("Quantity x" + qty.ToString("0.00") + " | Jackpot " + jc.ToString("0.00") + "% | Extra-roll " +
                          Mathf.Clamp(config.BaseExtraRollChance + lp.ExtraRollChanceBonus + bp.ExtraRollChanceBonus, 0, 100).ToString("0") + "%");
            sb.AppendLine("-- Normal pool (top 15) --");
            sb.Append(DescribePool(BuildPool(lp, bp, false), 15, qty));
            sb.AppendLine("-- Jackpot pool (top 10) --");
            sb.Append(DescribePool(BuildPool(lp, bp, true), 10, qty));
            iplayer.Reply(sb.ToString());
        }

        private string DescribePool(List<LootEntry> pool, int top, float qty)
        {
            float total = pool.Where(IsUsable).Sum(e => e.Weight);
            var sb = new StringBuilder();
            if (total <= 0f) return "(empty)\n";
            foreach (var e in pool.Where(IsUsable).OrderByDescending(e => e.Weight).Take(top))
            {
                sb.AppendLine((100f * e.Weight / total).ToString("0.0") + "%  " + ItemName(e) + "  x" +
                              Mathf.RoundToInt(e.Min * qty) + "-" + Mathf.RoundToInt(e.Max * qty));
            }
            return sb.ToString();
        }

        private void CmdTest(IPlayer iplayer, string[] args)
        {
            string loc = args.Length > 1 ? args[1] : config.DefaultLocationKey;
            string bait = args.Length > 2 ? args[2] : config.DefaultBaitKey;
            int n = 1000;
            if (args.Length > 3) int.TryParse(args[3], out n);
            n = Mathf.Clamp(n, 1, 100000);

            var counts = new Dictionary<string, int>();
            var totals = new Dictionary<string, long>();
            int jackpots = 0;
            long rewardsTotal = 0;

            for (int i = 0; i < n; i++)
            {
                var r = Generate(loc, bait, 1f, 1f, false);
                if (r.Jackpot) jackpots++;
                foreach (var rw in r.Rewards)
                {
                    string key = rw.Entry.Shortname;
                    int c; counts.TryGetValue(key, out c); counts[key] = c + 1;
                    long t; totals.TryGetValue(key, out t); totals[key] = t + rw.Amount;
                    rewardsTotal++;
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine("== Simulated " + n + " catches @ " + loc + " / " + bait + " ==");
            sb.AppendLine("Jackpots: " + jackpots + " (" + (100f * jackpots / n).ToString("0.00") + "%) | Avg rewards/catch: " + ((float)rewardsTotal / n).ToString("0.00"));
            foreach (var kv in counts.OrderByDescending(k => k.Value).Take(15))
                sb.AppendLine(kv.Key + ": " + kv.Value + " drops, avg " + (totals[kv.Key] / (float)kv.Value).ToString("0.#") + " each");
            iplayer.Reply(sb.ToString());
        }

        private void CmdGive(IPlayer iplayer, string[] args)
        {
            if (args.Length < 2) { iplayer.Reply(Msg("Help", iplayer.Id)); return; }

            var target = covalence.Players.FindPlayer(args[1]);
            var bp = target != null ? target.Object as BasePlayer : null;
            if (bp == null) { iplayer.Reply(Msg("PlayerNotFound", iplayer.Id, args[1])); return; }

            bool force = args.Length > 2 && args[2].Equals("jackpot", StringComparison.OrdinalIgnoreCase);
            var luck = GetLuck(bp);
            var result = Generate(DetectLocation(bp), config.DefaultBaitKey, luck.QuantityMultiplier, luck.JackpotChanceMultiplier, force);
            DeliverRewards(bp, result);

            iplayer.Reply(Msg("Given", iplayer.Id, bp.displayName,
                string.Join(", ", result.Rewards.Select(r => r.Amount + "x " + ItemName(r.Entry)).ToArray())));
        }

        #endregion
    }
}
