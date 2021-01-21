// Requires: EventManager
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Plugins.EventManagerEx;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("GunGame", "k1lly0u", "0.5.0"), Description("GunGame event mode for EventManager")]
    class GunGame : RustPlugin, IEventPlugin
    {
        private StoredData storedData;
        private DynamicConfigFile data;

        private string[] _validWeapons;

        private static Func<string, StoredData.WeaponSet> GetWeaponSet;

        #region Oxide Hooks
        private void OnServerInitialized()
        {
            data = Interface.Oxide.DataFileSystem.GetFile("EventManager/gungame_weaponsets");
            LoadData();

            FindValidWeapons();

            EventManager.RegisterEvent(Title, this);

            GetMessage = Message;
            GetWeaponSet = storedData.GetWeaponSet;
        }

        protected override void LoadDefaultMessages() => lang.RegisterMessages(Messages, this);

        private void Unload()
        {
            if (!EventManager.IsUnloading)
                EventManager.UnregisterEvent(Title);

            Configuration = null;
        }
        #endregion

        #region Functions
        private void FindValidWeapons()
        {
            List<string> list = Facepunch.Pool.GetList<string>();

            foreach (ItemDefinition itemDefinition in ItemManager.itemList)
            {
                if (itemDefinition.category == ItemCategory.Weapon || itemDefinition.category == ItemCategory.Tool)
                {
                    if (!itemDefinition.isHoldable)
                        continue;

                    AttackEntity attackEntity = itemDefinition.GetComponent<ItemModEntity>()?.entityPrefab?.Get()?.GetComponent<AttackEntity>();
                    if (attackEntity != null && (attackEntity is BaseMelee || attackEntity is BaseProjectile))
                        list.Add(itemDefinition.shortname);
                }
            }

            list.Sort();

            _validWeapons = list.ToArray();

            Facepunch.Pool.FreeList(ref list);
        }

        private string[] GetGunGameWeapons() => _validWeapons;

        private string[] GetGunGameWeaponSets() => storedData._weaponSets.Keys.ToArray();
        #endregion

        #region Event Checks
        public bool InitializeEvent(EventManager.EventConfig config) => EventManager.InitializeEvent<GunGameEvent>(this, config);

        public bool CanUseClassSelector => true;

        public bool RequireTimeLimit => true;

        public bool RequireScoreLimit => false;

        public bool UseScoreLimit => false;

        public bool UseTimeLimit => true;

        public bool IsTeamEvent => false;

        public void FormatScoreEntry(EventManager.ScoreEntry scoreEntry, ulong langUserId, out string score1, out string score2)
        {
            score1 = string.Format(Message("Score.Rank", langUserId), scoreEntry.value1);
            score2 = string.Format(Message("Score.Kills", langUserId), scoreEntry.value2);
        }

        public List<EventManager.EventParameter> AdditionalParameters { get; } = new List<EventManager.EventParameter>
        {
            new EventManager.EventParameter
            {
                DataType = "string",
                Field = "weaponSet",
                Input = EventManager.EventParameter.InputType.Selector,
                SelectMultiple = false,
                SelectorHook = "GetGunGameWeaponSets",
                IsRequired = true,
                Name = "Weapon Set"
            },
            new EventManager.EventParameter
            {
                DataType = "string",
                Field = "downgradeWeapon",
                Input = EventManager.EventParameter.InputType.Selector,
                SelectMultiple = false,
                SelectorHook = "GetGunGameWeapons",
                IsRequired = false,
                Name = "Downgrade Weapon",
                DefaultValue = "machete"
            }
        };

        public string ParameterIsValid(string fieldName, object value)
        {
            switch (fieldName)
            {
                case "weaponSet":
                    {
                        StoredData.WeaponSet weaponSet;
                        if (!storedData.TryFindWeaponSet((string)value, out weaponSet))
                            return "Unable to find a weapon set with the specified name";

                        return null;
                    }
                case "downgradeWeapon":
                    {
                        if (ItemManager.FindItemDefinition((string)value) == null)
                            return "Unable to find a weapon with the specified shortname";

                        return null;
                    }
                default:
                    return null;
            }
        }
        #endregion

        #region Event Classes
        public class GunGameEvent : EventManager.BaseEventGame
        {
            private StoredData.WeaponSet weaponSet;

            private ItemDefinition downgradeWeapon = null;

            public EventManager.BaseEventPlayer winner;

            internal override void InitializeEvent(IEventPlugin plugin, EventManager.EventConfig config)
            {
                string downgradeShortname = config.GetParameter<string>("downgradeWeapon");

                if (!string.IsNullOrEmpty(downgradeShortname))
                    downgradeWeapon = ItemManager.FindItemDefinition(downgradeShortname);

                weaponSet = GetWeaponSet(config.GetParameter<string>("weaponSet"));
                
                base.InitializeEvent(plugin, config);
            }

            internal override void PrestartEvent()
            {
                CloseEvent();
                base.PrestartEvent();
            }

            protected override EventManager.BaseEventPlayer AddPlayerComponent(BasePlayer player) => player.gameObject.AddComponent<GunGamePlayer>();
            
            protected override void OnKitGiven(EventManager.BaseEventPlayer eventPlayer)
            {
                (eventPlayer as GunGamePlayer).GiveRankWeapon(weaponSet.CreateItem((eventPlayer as GunGamePlayer).Rank));
               
                if (downgradeWeapon != null && eventPlayer.Player.inventory.GetAmount(downgradeWeapon.itemid) == 0)                
                    eventPlayer.Player.GiveItem(ItemManager.Create(downgradeWeapon), BaseEntity.GiveItemReason.PickedUp);                                
            }

            internal override void OnEventPlayerDeath(EventManager.BaseEventPlayer victim, EventManager.BaseEventPlayer attacker = null, HitInfo hitInfo = null)
            {
                if (victim == null)
                    return;

                victim.OnPlayerDeath(attacker, Configuration.RespawnTime);

                if (attacker != null && victim != attacker)
                {
                    if (Configuration.ResetHealthOnKill)
                    {
                        attacker.Player.health = attacker.Player.MaxHealth();
                        attacker.Player.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                    }

                    attacker.OnKilledPlayer(hitInfo);

                    string attackersWeapon = GetWeaponShortname(hitInfo);

                    if (!string.IsNullOrEmpty(attackersWeapon))
                    {
                        if (KilledByRankedWeapon(attacker as GunGamePlayer, attackersWeapon))
                        {
                            (attacker as GunGamePlayer).Rank += 1;

                            if ((attacker as GunGamePlayer).Rank > weaponSet.Count)
                            {
                                winner = attacker;
                                InvokeHandler.Invoke(this, EndEvent, 0.1f);
                                return;
                            }
                            else
                            {
                                (attacker as GunGamePlayer).RemoveRankWeapon();
                                (attacker as GunGamePlayer).GiveRankWeapon(weaponSet.CreateItem((attacker as GunGamePlayer).Rank));
                            }
                        }
                        else if (KilledByDowngradeWeapon(attackersWeapon))
                        {
                            (victim as GunGamePlayer).Rank = Mathf.Clamp((victim as GunGamePlayer).Rank - 1, 1, weaponSet.Count);
                        }
                    }
                }

                UpdateScoreboard();
                base.OnEventPlayerDeath(victim, attacker);
            }

            protected override bool CanDropBackpack() => false;

            private string GetWeaponShortname(HitInfo hitInfo)
            {
                Item item = hitInfo?.Weapon?.GetItem();
                if (item != null)
                    return item.info.shortname;

                BaseEntity weaponPrefab = hitInfo.WeaponPrefab;
                if (weaponPrefab != null)
                {
                    string shortname = weaponPrefab.name.Replace(".prefab", string.Empty)
                                                        .Replace(".deployed", string.Empty)
                                                        .Replace(".entity", string.Empty)
                                                        .Replace("_", ".");

                    if (shortname.StartsWith("rocket."))
                        shortname = "rocket.launcher";
                    else if (shortname.StartsWith("40mm."))
                        shortname = "multiplegrenadelauncher";
                    
                    return shortname;
                }

                return string.Empty;
            }

            private bool KilledByRankedWeapon(GunGamePlayer attacker, string weapon) => attacker.RankWeapon?.info.shortname.Equals(weapon) ?? false;

            private bool KilledByDowngradeWeapon(string weapon) => downgradeWeapon?.shortname.Equals(weapon) ?? false;

            protected override void GetWinningPlayers(ref List<EventManager.BaseEventPlayer> winners)
            {
                if (winner == null)
                {
                    if (eventPlayers.Count > 0)
                    {
                        int rank = 0;
                        int kills = 0;

                        for (int i = 0; i < eventPlayers.Count; i++)
                        {
                            GunGamePlayer eventPlayer = eventPlayers[i] as GunGamePlayer;
                            if (eventPlayer == null)
                                continue;

                            if (eventPlayer.Rank > rank)
                            {
                                winner = eventPlayer;
                                kills = eventPlayer.Kills;
                                rank = eventPlayer.Rank;
                            }
                            else if (eventPlayer.Rank == rank)
                            {
                                if (eventPlayer.Kills > rank)
                                {
                                    winner = eventPlayer;
                                    kills = eventPlayer.Kills;
                                    rank = eventPlayer.Rank;
                                }
                            }
                        }
                    }
                }

                if (winner != null)
                    winners.Add(winner);
            }

            #region Scoreboards
            protected override void BuildScoreboard()
            {
                scoreContainer = EMInterface.CreateScoreboardBase(this);

                int index = -1;

                EMInterface.CreatePanelEntry(scoreContainer, string.Format(GetMessage("Score.Limit", 0UL), weaponSet.Count), index += 1);

                EMInterface.CreateScoreEntry(scoreContainer, string.Empty, string.Empty, "R", index += 1);

                for (int i = 0; i < Mathf.Min(scoreData.Count, 15); i++)
                {
                    EventManager.ScoreEntry score = scoreData[i];
                    EMInterface.CreateScoreEntry(scoreContainer, score.displayName, string.Empty, ((int)score.value1).ToString(), i + index + 1);
                }
            }

            protected override float GetFirstScoreValue(EventManager.BaseEventPlayer eventPlayer) => (eventPlayer as GunGamePlayer).Rank;

            protected override float GetSecondScoreValue(EventManager.BaseEventPlayer eventPlayer) => eventPlayer.Kills;

            protected override void SortScores(ref List<EventManager.ScoreEntry> list)
            {
                list.Sort(delegate (EventManager.ScoreEntry a, EventManager.ScoreEntry b)
                {
                    int primaryScore = a.value1.CompareTo(b.value1);

                    if (primaryScore == 0)
                        return a.value2.CompareTo(b.value2);

                    return primaryScore;
                });
            }
            #endregion

            internal override void GetAdditionalEventDetails(ref List<KeyValuePair<string, object>> list, ulong playerId)
            {
                list.Add(new KeyValuePair<string, object>(GetMessage("UI.RankLimit", playerId), weaponSet.Count));

                if (downgradeWeapon != null)
                    list.Add(new KeyValuePair<string, object>(GetMessage("UI.DowngradeWeapon", playerId), downgradeWeapon.displayName.english));
            }
        }

        private class GunGamePlayer : EventManager.BaseEventPlayer
        {
            public int Rank { get; set; } = 1;

            public Item RankWeapon { get; private set; }

            public void RemoveRankWeapon()
            {
                if (RankWeapon != null)
                {
                    RankWeapon.RemoveFromContainer();
                    RankWeapon.Remove();
                }
            }

            public void GiveRankWeapon(Item item)
            {
                RankWeapon = item;
                Player.GiveItem(item, BaseEntity.GiveItemReason.PickedUp);

                BaseProjectile baseProjectile = item.GetHeldEntity() as BaseProjectile;
                if (baseProjectile != null)
                {
                    Item ammo = ItemManager.Create(baseProjectile.primaryMagazine.ammoType, baseProjectile.primaryMagazine.capacity * 5);
                    Player.GiveItem(ammo);
                }

                FlameThrower flameThrower = item.GetHeldEntity() as FlameThrower;
                if (flameThrower != null)
                {
                    Item ammo = ItemManager.CreateByName("lowgradefuel", 1500);
                    Player.GiveItem(ammo);
                }
            }
        }
        #endregion

        #region Weapon Set Creation
        private Hash<ulong, StoredData.WeaponSet> setCreator = new Hash<ulong, StoredData.WeaponSet>();

        [ChatCommand("ggset")]
        private void cmdGunGameSet(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, EventManager.ADMIN_PERMISSION))
            {
                player.ChatMessage("You do not have permission to use this command");
                return;
            }

            StoredData.WeaponSet weaponSet;
            setCreator.TryGetValue(player.userID, out weaponSet);

            if (args.Length == 0)
            {
                player.ChatMessage("/ggset new - Start creating a new weapon set");
                player.ChatMessage("/ggset edit <name> - Edits the specified weapon set");
                player.ChatMessage("/ggset delete <name> - Deletes the specified weapon set");
                player.ChatMessage("/ggset list - Lists all available weapon sets");

                if (weaponSet != null)
                {
                    player.ChatMessage("/ggset add <opt:rank> - Adds the weapon you are holding to the weapon set. If you specify a rank the weapon will be inserted at that position");
                    player.ChatMessage("/ggset remove <number> - Removes the specified rank from the weapon set");
                    player.ChatMessage("/ggset ranks - List the weapons and ranks in the weapon set");
                    player.ChatMessage("/ggset save <name> - Saves the weapon set you are currently editing");
                }
                return;
            }

            switch (args[0].ToLower())
            {
                case "new":
                    setCreator[player.userID] = new StoredData.WeaponSet();
                    player.ChatMessage("You are now creating a new weapon set");
                    return;

                case "edit":
                    if (args.Length < 2)
                    {
                        player.ChatMessage("You must enter the name of a weapon set to edit");
                        return;
                    }

                    if (!storedData.TryFindWeaponSet(args[1], out weaponSet))
                    {
                        player.ChatMessage("Unable to find a weapon set with the specified name");
                        return;
                    }

                    setCreator[player.userID] = weaponSet;
                    player.ChatMessage($"You are now editing the weapon set {args[1]}");
                    return;

                case "delete":
                    if (args.Length < 2)
                    {
                        player.ChatMessage("You must enter the name of a weapon set to delete");
                        return;
                    }

                    if (!storedData.TryFindWeaponSet(args[1], out weaponSet))
                    {
                        player.ChatMessage("Unable to find a weapon set with the specified name");
                        return;
                    }

                    storedData._weaponSets.Remove(args[1]);
                    SaveData();
                    player.ChatMessage($"You have deleted the weapon set {args[1]}");
                    return;

                case "list":
                    player.ChatMessage($"Available weapon sets;\n{GetGunGameWeaponSets().ToSentence()}");
                    return;

                case "add":
                    if (weaponSet == null)
                    {
                        player.ChatMessage("You are not currently editing a weapon set");
                        return;
                    }

                    Item item = player.GetActiveItem();
                    if (item == null)
                    {
                        player.ChatMessage("You must hold a weapon in your hands to add it to the weapon set");
                        return;
                    }

                    if (!_validWeapons.Contains(item.info.shortname))
                    {
                        player.ChatMessage("This item is not an allowed weapon");
                        return;
                    }

                    int index = -1;
                    if (args.Length == 2 && int.TryParse(args[1], out index))
                        index = Mathf.Clamp(index, 1, weaponSet.Count);

                    int rank = weaponSet.AddItem(item, index);
                    player.ChatMessage($"This weapon has been added at rank {rank}");
                    return;

                case "remove":
                    if (weaponSet == null)
                    {
                        player.ChatMessage("You are not currently editing a weapon set");
                        return;
                    }

                    int delete;
                    if (args.Length != 2 || !int.TryParse(args[1], out delete))
                    {
                        player.ChatMessage("You must enter the rank number to remove a item");
                        return;
                    }

                    if (delete < 1 || delete > weaponSet.Count)
                    {
                        player.ChatMessage("The rank you entered is out of range");
                        return;
                    }

                    weaponSet.weapons.RemoveAt(delete - 1);
                    player.ChatMessage($"You have removed the weapon at rank {delete}");
                    return;
                case "ranks":
                    if (weaponSet == null)
                    {
                        player.ChatMessage("You are not currently editing a weapon set");
                        return;
                    }

                    string str = string.Empty;
                    for (int i = 0; i < weaponSet.Count; i++)
                    {
                        str += $"Rank {i + 1} : {ItemManager.itemDictionary[weaponSet.weapons[i].itemid].displayName.english}\n";
                    }

                    player.ChatMessage(str);
                    return;
                case "save":
                    if (weaponSet == null)
                    {
                        player.ChatMessage("You are not currently editing a weapon set");
                        return;
                    }

                    if (weaponSet.Count < 1)
                    {
                        player.ChatMessage("You have not added any weapons to this weapon set");
                        return;
                    }

                    if (args.Length != 2)
                    {
                        player.ChatMessage("You must enter a name for this weapon set");
                        return;
                    }

                    storedData._weaponSets[args[1]] = weaponSet;
                    SaveData();
                    setCreator.Remove(player.userID);
                    player.ChatMessage($"You have saved this weapon set as {args[1]}");
                    return;

                default:
                    break;
            }
        }
        #endregion

        #region Config        
        private static ConfigData Configuration;

        private class ConfigData
        {
            [JsonProperty(PropertyName = "Respawn time (seconds)")]
            public int RespawnTime { get; set; }

            [JsonProperty(PropertyName = "Reset heath when killing an enemy")]
            public bool ResetHealthOnKill { get; set; }

            public Oxide.Core.VersionNumber Version { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            Configuration = Config.ReadObject<ConfigData>();

            if (Configuration.Version < Version)
                UpdateConfigValues();

            Config.WriteObject(Configuration, true);
        }

        protected override void LoadDefaultConfig() => Configuration = GetBaseConfig();

        private ConfigData GetBaseConfig()
        {
            return new ConfigData
            {
                ResetHealthOnKill = false,
                RespawnTime = 5,
                Version = Version
            };
        }

        protected override void SaveConfig() => Config.WriteObject(Configuration, true);

        private void UpdateConfigValues()
        {
            PrintWarning("Config update detected! Updating config values...");

            Configuration.Version = Version;
            PrintWarning("Config update completed!");
        }

        #endregion

        #region Data Management
        private void SaveData() => data.WriteObject(storedData);

        private void LoadData()
        {
            try
            {
                storedData = data.ReadObject<StoredData>();
            }
            catch
            {
                storedData = new StoredData();
            }
        }

        private class StoredData
        {
            public Hash<string, WeaponSet> _weaponSets = new Hash<string, WeaponSet>();

            public bool TryFindWeaponSet(string name, out WeaponSet weaponSet) => _weaponSets.TryGetValue(name, out weaponSet);

            public WeaponSet GetWeaponSet(string name) => _weaponSets[name];
                       

            public class WeaponSet
            {
                public List<EventManager.ItemData> weapons = new List<EventManager.ItemData>();

                public int Count => weapons.Count;

                public Item CreateItem(int rank) => EventManager.CreateItem(weapons[rank - 1]);

                public int AddItem(Item item, int index)
                {
                    EventManager.ItemData itemData = EventManager.SerializeItem(item);
                    if (index < 0)                    
                        weapons.Add(itemData);                    
                    else weapons.Insert(Mathf.Min(index - 1, weapons.Count), itemData);

                    return weapons.IndexOf(itemData) + 1;
                }
            }
        }
        #endregion

        #region Localization
        public string Message(string key, ulong playerId = 0U) => lang.GetMessage(key, this, playerId != 0U ? playerId.ToString() : null);

        private static Func<string, ulong, string> GetMessage;

        private readonly Dictionary<string, string> Messages = new Dictionary<string, string>
        {
            ["Score.Rank"] = "Rank: {0}",
            ["Score.Kills"] = "Kills: {0}",            
            ["Score.Name"] = "Rank",
            ["Score.Limit"] = "Rank Limit : {0}",
            ["UI.RankLimit"] = "Rank Limit",
            ["UI.DowngradeWeapon"] = "Downgrade Weapon"
        };
        #endregion
    }
}
