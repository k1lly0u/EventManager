using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Game.Rust.Cui;
using Oxide.Core.Plugins;
using UnityEngine;
using System.Linq;
using Rust;
using System.Reflection;
using Oxide.Core.Libraries;
using Oxide.Plugins;
using System.Collections;
using System.Text.RegularExpressions;

namespace Oxide.Plugins
{
    [Info("BaseEvent", "k1lly0u", "0.1.0", ResourceId = 0)]
    class BaseEvent : RustPlugin
    {
        #region Fields
        [PluginReference] EventManager EventManager;

        private bool usingEvent;
        private bool hasStarted;
        private bool isEnding;

        private string currentKit;
        private string currentZone;
        private string currentSpawns;
        private int scoreLimit;

        private List<DeathmatchPlayer> eventPlayers = new List<DeathmatchPlayer>();

        #endregion

        #region Oxide Hooks
        void Loaded()
        {
            
        }
        void OnServerInitialized()
        {
            LoadVariables();
            currentZone = configData.EventSettings.DefaultZoneID;
            currentKit = configData.EventSettings.DefaultKit;
            currentSpawns = configData.EventSettings.DefaultSpawns;
            scoreLimit = configData.GameSettings.ScoreLimit;
        }
        #endregion

        #region Player Component
        class DeathmatchPlayer : MonoBehaviour
        {
            public BasePlayer player;
            public int kills;

            void Awake()
            {
                player = GetComponent<BasePlayer>();
                enabled = false;
                kills = 0;
            }
        }
        #endregion

        #region Event Manager Hooks
        void RegisterGame()
        {
            EventManager.Events eventData = new EventManager.Events
            {
                CloseOnStart = false,
                DisableItemPickup = false,
                EnemiesToSpawn = 0,
                EventType = Title,
                GameMode = EventManager.GameMode.Normal,
                GameRounds = 0,
                Kit = currentKit,
                MaximumPlayers = 0,
                MinimumPlayers = 2,
                ScoreLimit = scoreLimit,
                Spawnfile = currentSpawns,
                Spawnfile2 = null,
                SpawnType = EventManager.SpawnType.Consecutive,
                RespawnType = EventManager.RespawnType.Timer,
                RespawnTimer = 5,
                UseClassSelector = false,
                WeaponSet = null,
                ZoneID = currentZone
            };
            EventManager.EventSetting eventSettings = new EventManager.EventSetting
            {
                CanChooseRespawn = true,
                CanUseClassSelector = true,
                CanPlayBattlefield = true,
                ForceCloseOnStart = false,
                IsRoundBased = false,
                LockClothing = false,
                RequiresKit = true,
                RequiresMultipleSpawns = false,
                RequiresSpawns = true,
                ScoreType = "Kills",
                SpawnsEnemies = false
            };
            var success = EventManager.RegisterEventGame(Title, eventSettings, eventData);
            if (success == null)
            {
                Puts("Event plugin doesn't exist");
                return;
            }
        }
        void OnSelectEventGamePost(string name)
        {
            if (Title == name)
                usingEvent = true;
            else usingEvent = false;
        }
        void OnEventPlayerSpawn(BasePlayer player)
        {
            if (usingEvent && hasStarted && !isEnding)
            {
                if (!player.GetComponent<DeathmatchPlayer>()) return;
                if (player.IsSleeping())
                {
                    player.EndSleeping();
                    timer.In(1, () => OnEventPlayerSpawn(player));
                    return;
                }
                player.inventory.Strip();
                EventManager.GivePlayerKit(player, currentKit);
                player.health = configData.GameSettings.StartHealth;
            }
        }
        object CanEventOpen()
        {
            if (usingEvent)
            {

            }
            return null;
        }
        object CanEventStart()
        {
            if (usingEvent)
            {

            }
            return null;
        }
        void OnEventOpenPost()
        {
            if (usingEvent)
                EventManager.BroadcastToChat("");
        }
        void OnEventCancel()
        {
            if (usingEvent && hasStarted)
                CheckScores(null, true);
        }
        void OnEventClosePost()
        {
            if (usingEvent)
            {

            }
        }
        void OnEventEndPre()
        {
            if (usingEvent && hasStarted)
            {
                CheckScores(null, true);
            }
        }
        void OnEventEndPost()
        {
            if (usingEvent)
            {
                hasStarted = false;
                eventPlayers.Clear();
            }
        }
        void OnEventStartPre()
        {
            if (usingEvent)
            {
                hasStarted = true;
                isEnding = false;
            }
        }
        object OnEventStartPost()
        {
            if (usingEvent)
                UpdateScores();
            return null;
        }
        object CanEventJoin()
        {
            if (usingEvent)
            {

            }
            return null;
        }
        void OnSelectKit(string kitname)
        {
            if (usingEvent)
            {
                currentKit = kitname;
            }
        }
        void OnEventJoinPost(BasePlayer player)
        {
            if (usingEvent)
            {
                if (player.GetComponent<DeathmatchPlayer>())
                    UnityEngine.Object.Destroy(player.GetComponent<DeathmatchPlayer>());
                eventPlayers.Add(player.gameObject.AddComponent<DeathmatchPlayer>());
                EventManager.CreateScoreboard(player);
            }
        }
        void OnEventLeavePre(BasePlayer player)
        {
            if (usingEvent)
            {
            }
        }
        void OnEventLeavePost(BasePlayer player)
        {
            if (usingEvent)
            {
                if (player.GetComponent<DeathmatchPlayer>())
                {
                    eventPlayers.Remove(player.GetComponent<DeathmatchPlayer>());
                    UnityEngine.Object.Destroy(player.GetComponent<DeathmatchPlayer>());
                    CheckScores();
                }
            }
        }
        void OnPlayerSelectClass(BasePlayer player)
        {
        }

        void OnEventPlayerAttack(BasePlayer attacker, HitInfo hitinfo)
        {
            if (usingEvent)
            {
                if (!(hitinfo.HitEntity is BasePlayer))
                {
                    hitinfo.damageTypes = new DamageTypeList();
                    hitinfo.DoHitEffects = false;
                }
            }
        }

        void OnEventPlayerDeath(BasePlayer victim, HitInfo hitinfo)
        {
            if (usingEvent)
            {
                if (hitinfo.Initiator != null)
                {
                    BasePlayer attacker = hitinfo.Initiator.ToPlayer();
                    if (attacker != null)
                    {
                        if (attacker != victim)
                        {
                            AddKill(attacker, victim);
                        }
                    }
                }
            }
            return;
        }
        object EventChooseSpawn(BasePlayer player, Vector3 destination)
        {
            if (usingEvent)
            {

            }
            return null;
        }
        void SetscoreLimit(int scoreLimit) => scoreLimit = scoreLimit;
        object GetRespawnType()
        {
            return null;
        }
        object GetRespawnTime()
        {
            return null;
        }
        void SetEnemyCount(int number)
        {
        }
        void SetGameRounds(int number)
        {
        }
        object FreezeRespawn(BasePlayer player)
        {
            return null;
        }
        void SetEventZone(string zonename)
        {

        }
        #endregion

        #region Score and Token Management
        void AddKill(BasePlayer player, BasePlayer victim)
        {
            if (isEnding) return;
            if (!player.GetComponent<DeathmatchPlayer>())
                return;

            player.GetComponent<DeathmatchPlayer>().kills++;
            EventManager.AddTokens(player.userID, configData.EventSettings.TokensOnKill);
            EventManager.PopupMessage(string.Format("", player.displayName, player.GetComponent<DeathmatchPlayer>().kills, scoreLimit, victim.displayName));
            UpdateScores();
            CheckScores(player.GetComponent<DeathmatchPlayer>());
        }
        void CheckScores(DeathmatchPlayer player = null, bool timelimit = false)
        {
            if (isEnding) return;
            if (player != null)
            {
                if (scoreLimit > 0 && player.kills >= scoreLimit)
                {
                    Winner(player.player);
                    return;
                }
            }
            if (eventPlayers.Count == 0)
            {
                isEnding = true;
                EventManager.BroadcastToChat("");
                EventManager.CloseEvent();
                EventManager.EndEvent();
                return;
            }
            if (eventPlayers.Count == 1)
            {
                Winner(eventPlayers[0].player);
                return;
            }

            if (timelimit)
            {
                BasePlayer winner = null;
                int score = 0;
                foreach (var dmPlayer in eventPlayers)
                {
                    if (dmPlayer.kills > score)
                    {
                        winner = dmPlayer.player;
                    }
                }
                if (winner != null)
                    Winner(winner);
                return;
            }
        }
        private void UpdateScores()
        {
            if (usingEvent && hasStarted)
            {
                var sortedList = eventPlayers.OrderByDescending(pair => pair.kills).ToList();
                var scoreList = new Dictionary<ulong, EventManager.Scoreboard>();
                foreach (var entry in sortedList)
                {
                    if (scoreList.ContainsKey(entry.player.userID)) continue;
                    scoreList.Add(entry.player.userID, new EventManager.Scoreboard { Name = entry.player.displayName, Position = sortedList.IndexOf(entry), Score = entry.kills });
                }
                EventManager.UpdateScoreboard(new EventManager.ScoreData { Additional = null, Scores = scoreList, ScoreType = "Kills" });
            }
        }
        void Winner(BasePlayer player)
        {
            isEnding = true;
            if (player != null)
            {
                EventManager.AddTokens(player.userID, configData.EventSettings.TokensOnWin, true);
                EventManager.BroadcastToChat(string.Format("", player.displayName));
            }
            if (EventManager._Started)
            {
                EventManager.CloseEvent();
                EventManager.EndEvent();
            }
        }
        #endregion

        #region Config 
        class EventSettings
        {
            public string DefaultKit { get; set; }
            public string DefaultZoneID { get; set; }
            public string DefaultSpawns { get; set; }
            public int TokensOnKill { get; set; }
            public int TokensOnWin { get; set; }
        }
        class GameSettings
        {
            public float StartHealth { get; set; }
            public int ScoreLimit { get; set; }
        }        
        class Messaging
        {
            public string MainColor { get; set; }
            public string MSGColor { get; set; }
        }
        private ConfigData configData;
        class ConfigData
        {
            public EventSettings EventSettings { get; set; }
            public GameSettings GameSettings { get; set; }           
            public Messaging Messaging { get; set; }
        }
        private void LoadVariables()
        {
            LoadConfigVariables();
            SaveConfig();
        }
        protected override void LoadDefaultConfig()
        {
            var config = new ConfigData
            {
                EventSettings = new EventSettings
                {
                    DefaultKit = "kit",
                    DefaultZoneID = "zone",
                    DefaultSpawns = "spawnfile",
                    TokensOnKill = 1,
                    TokensOnWin = 5
                },
                GameSettings = new GameSettings
                {
                    StartHealth = 100,
                    ScoreLimit = 5,
                },               
                Messaging = new Messaging
                {
                    MainColor = "<color=orange>",
                    MSGColor = "<color=#939393>"
                }
            };
            SaveConfig(config);
        }
        private void LoadConfigVariables() => configData = Config.ReadObject<ConfigData>();
        void SaveConfig(ConfigData config) => Config.WriteObject(config, true);
        #endregion               
    }
}
