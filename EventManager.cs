// Requires: EMInterface

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Game.Rust.Cui;
using Oxide.Core.Plugins;
using UnityEngine;
using System.Linq;
using Network;
using Facepunch;
using UI = Oxide.Plugins.EMInterface.UI;
using UI4 = Oxide.Plugins.EMInterface.UI4;

namespace Oxide.Plugins
{
    using EventManagerEx;
    using System.Collections;
    using System.Text.RegularExpressions;

    [Info("EventManager", "k1lly0u", "4.0.0")]
    [Description("The core mechanics for arena combat games")]
    public class EventManager : RustPlugin
    {
        #region Fields        
        private DynamicConfigFile restorationData, eventData;

        [PluginReference]
        private Plugin Economics, Kits, NoEscape, ServerRewards, Spawns, ZoneManager;
        

        private RewardType rewardType;

        private int scrapItemId;

        private static Regex hexFilter;


        public Hash<string, IEventPlugin> EventModes { get; set; } = new Hash<string, IEventPlugin>();

        public EventData Events { get; private set; }

        private RestoreData Restore { get; set; }

        public static EventManager Instance { get; private set; }

        public static BaseEventGame BaseManager { get; internal set; }

        public static ConfigData Configuration { get; set; }

        public static EventResults LastEventResult { get; private set; }

        public static bool IsUnloading { get; private set; }


        internal const string ADMIN_PERMISSION = "eventmanager.admin";
        #endregion
        
        #region Oxide Hooks
        private void Loaded()
        {
            restorationData = Interface.Oxide.DataFileSystem.GetFile("EventManager/restoration_data");

            eventData = Interface.Oxide.DataFileSystem.GetFile("EventManager/event_data");

            permission.RegisterPermission(ADMIN_PERMISSION, this);

            Instance = this;
            IsUnloading = false;
            LastEventResult = new EventResults();

            LoadData();
        }

        protected override void LoadDefaultMessages() => lang.RegisterMessages(Messages, this);

        private void OnServerInitialized()
        {
            if (!CheckDependencies())
                return;            

            rewardType = ParseType<RewardType>(Configuration.Reward.Type);

            scrapItemId = ItemManager.FindItemDefinition("scrap")?.itemid ?? 0;

            hexFilter = new Regex("^([A-Fa-f0-9]{6}|[A-Fa-f0-9]{3})$");

            UnsubscribeAll();

            foreach (BasePlayer player in BasePlayer.activePlayerList)
                OnPlayerConnected(player);
        }

        private void Unload()
        {
            IsUnloading = true;

            SaveRestoreData();
            
            BaseEventPlayer[] eventPlayers = UnityEngine.Object.FindObjectsOfType<BaseEventPlayer>();
            for (int i = 0; i < eventPlayers?.Length; i++)            
                UnityEngine.Object.DestroyImmediate(eventPlayers[i]);
            
            if (BaseManager != null)
                UnityEngine.Object.DestroyImmediate(BaseManager.gameObject);

            hexFilter = null;

            LastEventResult = null;
            BaseManager = null;
            Configuration = null;
            Instance = null;            
        }

        private void OnServerSave() => SaveRestoreData();

        private void OnPlayerConnected(BasePlayer player)
        {
            if (player.IsSleeping() || player.HasPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot))
            {
                timer.Once(1f, () => OnPlayerConnected(player));
                return;
            }

            UnlockInventory(player);
            
            if (Restore.HasRestoreData(player.userID))
                Restore.RestorePlayer(player);
        }
       
        private void OnPlayerDisconnected(BasePlayer player)
        {
            BaseEventPlayer eventPlayer = GetUser(player);
            if (eventPlayer != null)
            {
                if (BaseManager != null)
                    BaseManager.LeaveEvent(player);
                else UnityEngine.Object.DestroyImmediate(eventPlayer);

                if (player.IsAlive())
                    player.DieInstantly();
            }
        }

        private void OnEntityTakeDamage(BaseEntity entity, HitInfo hitInfo)
        {
            if (entity == null || hitInfo == null)
                return;

            BasePlayer player = entity.ToPlayer();

            if (player != null)
            {
                BaseEventPlayer eventPlayer = GetUser(player);
                if (eventPlayer != null)
                {
                    if (BaseManager == null)
                        return;
                    
                    BaseManager.OnPlayerTakeDamage(eventPlayer, hitInfo);
                }
            }
            else
            {
                BaseEventPlayer attacker = GetUser(hitInfo.InitiatorPlayer);
                if (attacker != null)
                {
                    if (BaseManager != null)
                    {
                        if (BaseManager.CanDealEntityDamage(attacker, entity, hitInfo))
                            return;
                    }
                    ClearDamage(hitInfo);
                }
            }
        }

        private object CanBeWounded(BasePlayer player, HitInfo hitInfo)
        {
            BaseEventPlayer eventPlayer = GetUser(player);
            if (eventPlayer != null && BaseManager != null)
                return false;
            return null;
        }

        private object OnPlayerDeath(BasePlayer player, HitInfo hitInfo)
        {
            if (player != null)
            {
                BaseEventPlayer eventPlayer = GetUser(player);
                if (eventPlayer != null && BaseManager != null)
                { 
                    if (!eventPlayer.IsDead)
                        BaseManager.PrePlayerDeath(eventPlayer, hitInfo);
                    return false;                    
                }
            }
            return null;
        }


        private object CanSpectateTarget(BasePlayer player, string name)
        {
            BaseEventPlayer eventPlayer = player.GetComponent<BaseEventPlayer>();
            if (eventPlayer != null && eventPlayer.Player.IsSpectating())
            {
                eventPlayer.UpdateSpectateTarget();
                return false;
            }
            return null;
        }

        private void OnEntityBuilt(Planner planner, GameObject gameObject)
        {
            BasePlayer player = planner?.GetOwnerPlayer();
            if (player == null)
                return;

            BaseCombatEntity baseCombatEntity = gameObject?.ToBaseEntity() as BaseCombatEntity;
            if (baseCombatEntity == null)
                return;

            BaseEventPlayer eventPlayer = GetUser(player);
            if (eventPlayer != null && BaseManager != null)
                BaseManager.OnEntityDeployed(baseCombatEntity);
        }

        private void OnItemDeployed(Deployer deployer, BaseCombatEntity baseCombatEntity)
        {
            BasePlayer player = deployer.GetOwnerPlayer();
            if (player == null)
                return;

            BaseEventPlayer eventPlayer = GetUser(player);
            if (eventPlayer != null && BaseManager != null)
                BaseManager.OnEntityDeployed(baseCombatEntity);
        }

        private object OnCreateWorldProjectile(HitInfo hitInfo, Item item)
        {
            if (hitInfo == null)
                return null;

            if (hitInfo.InitiatorPlayer != null)
            {
                BaseEventPlayer eventPlayer = GetUser(hitInfo.InitiatorPlayer);
                if (eventPlayer != null)
                    return false;
            }

            if (hitInfo.HitEntity?.ToPlayer() != null)
            {
                BaseEventPlayer eventPlayer = GetUser(hitInfo.HitEntity.ToPlayer());
                if (eventPlayer != null)
                    return false;
            }

            return null;
        }

        private object CanDropActiveItem(BasePlayer player)
        {
            BaseEventPlayer eventPlayer = GetUser(player);
            if (eventPlayer != null)                           
                return false;            
            return null;
        }

        private object OnPlayerCommand(BasePlayer player, string command, string[] args)
        {
            BaseEventPlayer eventPlayer = GetUser(player);

            if (player == null || player.IsAdmin || eventPlayer == null)
                return null;

            if (Configuration.Event.CommandBlacklist.Any(x => x.StartsWith("/") ? x.Substring(1).ToLower() == command : x.ToLower() == command))
            {
                SendReply(player, Message("Error.CommandBlacklisted", player.userID));
                return false;
            }
            return null;
        }

        private object OnServerCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            BaseEventPlayer eventPlayer = GetUser(player);

            if (player == null || player.IsAdmin || eventPlayer == null || arg.Args == null)
                return null;

            if (Configuration.Event.CommandBlacklist.Any(x => arg.cmd.FullName.Contains(x.ToLower())))
            {
                SendReply(player, Message("Error.CommandBlacklisted", player.userID));
                return false;
            }
            return null;
        }
        #endregion

        #region Event Construction
        public static void RegisterEvent(string eventName, IEventPlugin plugin) => Instance.EventModes[eventName] = plugin;

        public static void UnregisterEvent(string eventName) => Instance.EventModes.Remove(eventName);

        public object OpenEvent(string eventName)
        {
            EventConfig eventConfig;

            if (Events.events.TryGetValue(eventName, out eventConfig))
            {
                IEventPlugin plugin;
                if (!EventModes.TryGetValue(eventConfig.EventType, out plugin))                
                    return $"Unable to find event plugin for game mode: {eventConfig.EventType}";
                
                if (plugin == null)                
                    return $"Unable to initialize event plugin: {eventConfig.EventType}. Plugin is either unloaded or the class does not derive from IEventGame";

                object success = ValidateEventConfig(eventConfig);
                if (success is string)
                    return $"Failed to open event : {(string)success}";

                if (!plugin.InitializeEvent(eventConfig))
                    return $"There was a error initializing the event : {eventConfig.EventType}";
                return null;
            }
            else return "Failed to find a event with the specified name";
        }

        public static bool InitializeEvent<T>(IEventPlugin plugin, EventConfig config) where T : BaseEventGame
        {
            if (BaseManager != null)
                return false;

            BaseManager = new GameObject(config.EventName).AddComponent<T>();
            BaseManager.InitializeEvent(plugin, config);

            return true;
        }
        #endregion

        #region Functions
        public IEventPlugin GetPlugin(string name)
        {
            IEventPlugin eventPlugin;
            if (EventModes.TryGetValue(name, out eventPlugin))
                return eventPlugin;

            return null;
        }

        private bool CheckDependencies()
        {
            if (!Spawns)
            {
                PrintError("Unable to load EventManager - Spawns database not found. Please download Spawns database to continue");
                rust.RunServerCommand("oxide.unload", "EventManager");
                return false;
            }

            if (!ZoneManager)            
                PrintError("ZoneManager is not installed! Unable to restrict event players to zones");
               
            if (!Kits)
                PrintError("Kits is not installed! Unable to issue any weapon kits");

            return true;
        }

        private void UnsubscribeAll()
        {
            Unsubscribe(nameof(OnEntityTakeDamage));
            Unsubscribe(nameof(CanBeWounded));
            Unsubscribe(nameof(OnPlayerDeath));
            Unsubscribe(nameof(OnEntityBuilt));
            Unsubscribe(nameof(OnItemDeployed));
            Unsubscribe(nameof(OnCreateWorldProjectile));
            Unsubscribe(nameof(CanDropActiveItem));
            Unsubscribe(nameof(OnPlayerCommand));
            Unsubscribe(nameof(OnServerCommand));
        }

        private void SubscribeAll()
        {
            Subscribe(nameof(OnEntityTakeDamage));
            Subscribe(nameof(CanBeWounded));
            Subscribe(nameof(OnPlayerDeath));
            Subscribe(nameof(OnEntityBuilt));
            Subscribe(nameof(OnItemDeployed));
            Subscribe(nameof(OnCreateWorldProjectile));
            Subscribe(nameof(CanDropActiveItem));
            Subscribe(nameof(OnPlayerCommand));
            Subscribe(nameof(OnServerCommand));
        }

        private static void Broadcast(string key, params object[] args)
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
                player.ChatMessage(string.Format(Message(key, player.userID), args));
        }

        internal static bool IsValidHex(string s) => hexFilter.IsMatch(s);
        #endregion

        #region Classes and Components  
        public class BaseEventGame : MonoBehaviour
        {
            internal IEventPlugin Plugin { get; private set; }

            internal EventConfig Config { get; private set; }

            public EventStatus Status { get; protected set; }

            protected GameTimer Timer { get; set; }


            internal SpawnSelector _spawnSelectorA;

            internal SpawnSelector _spawnSelectorB;

            protected CuiElementContainer scoreContainer = null;

            internal List<BasePlayer> joiningPlayers = Pool.GetList<BasePlayer>();

            internal List<BaseEventPlayer> eventPlayers = Pool.GetList<BaseEventPlayer>();

            internal List<ScoreEntry> scoreData = Pool.GetList<ScoreEntry>();

            private List<BaseCombatEntity> _deployedObjects = Pool.GetList<BaseCombatEntity>();
                        
            private bool _isClosed = false;


            internal string TeamAColor { get; set; }

            internal string TeamBColor { get; set; }

            internal string TeamAClothing { get; set; }

            internal string TeamBClothing { get; set; }

            public bool GodmodeEnabled { get; protected set; } = true;

            internal string EventInformation
            {
                get
                {
                    string str = string.Format(Message("Info.Event.Current"), Config.EventName, Config.EventType);
                    str += string.Format(Message("Info.Event.Player"), eventPlayers.Count, Config.MaximumPlayers);
                    return str;
                }
            }

            internal string EventStatus => string.Format(Message("Info.Event.Status"), Status);
            
            #region Initialization and Destruction 
            /// <summary>
            /// Called when the event GameObject is destroyed
            /// </summary>
            protected virtual void OnDestroy()
            {
                CleanupEntities();

                for (int i = eventPlayers.Count - 1; i >= 0; i--)
                {
                    BaseEventPlayer eventPlayer = eventPlayers[i];

                    if (eventPlayer.IsDead)
                        ResetPlayer(eventPlayer.Player);

                    LeaveEvent(eventPlayer);
                }
                
                Pool.FreeList(ref scoreData);
                Pool.FreeList(ref joiningPlayers);
                Pool.FreeList(ref _deployedObjects);
                Pool.FreeList(ref eventPlayers);

                _spawnSelectorA?.Destroy();
                _spawnSelectorB?.Destroy();

                Timer?.StopTimer();

                Instance?.UnsubscribeAll();

                Destroy(gameObject);
            }

            /// <summary>
            /// The first function called when an event is being opened
            /// </summary>
            /// <param name="plugin">The plugin the event game belongs to</param>
            /// <param name="config">The event config</param>
            internal virtual void InitializeEvent(IEventPlugin plugin, EventConfig config)
            {
                this.Plugin = plugin;
                this.Config = config;

                _spawnSelectorA = new SpawnSelector(config.EventName, config.TeamConfigA.Spawnfile);

                if (plugin.IsTeamEvent)
                {                    
                    TeamAColor = config.TeamConfigA.Color;
                    TeamBColor = config.TeamConfigB.Color;

                    if (string.IsNullOrEmpty(TeamAColor) || TeamAColor.Length < 6 || TeamAColor.Length > 6 || !hexFilter.IsMatch(TeamAColor))
                        TeamAColor = "#EA3232";
                    else TeamAColor = "#" + TeamAColor;

                    if (string.IsNullOrEmpty(TeamBColor) || TeamBColor.Length < 6 || TeamBColor.Length > 6 || !hexFilter.IsMatch(TeamBColor))
                        TeamBColor = "#3232EA";
                    else TeamBColor = "#" + TeamBColor;

                    _spawnSelectorB = new SpawnSelector(config.EventName, config.TeamConfigB.Spawnfile);
                }

                Timer = new GameTimer(this);

                GodmodeEnabled = true;

                OpenEvent();
            }
            #endregion

            #region Event Management 
            /// <summary>
            /// Opens the event for players to join
            /// </summary>
            internal virtual void OpenEvent()
            {
                _isClosed = false;
                Status = EventManager.EventStatus.Open;

                Broadcast("Notification.EventOpen", Config.EventName, Config.EventType, Configuration.Timer.Start);

                InvokeHandler.Invoke(this, PrestartEvent, Configuration.Timer.Start);
            }

            /// <summary>
            /// Closes the event and prevent's more players from joining
            /// </summary>
            internal virtual void CloseEvent()
            {
                _isClosed = true;
                Broadcast("Notification.EventClosed");
            }

            /// <summary>
            /// The event prestart where players are created and sent to the arena
            /// </summary>
            internal virtual void PrestartEvent()
            {
                if (!HasMinimumRequiredPlayers())
                {
                    Broadcast("Notification.NotEnoughToStart");
                    EndEvent();
                    return;
                }

                Instance.SubscribeAll();

                Status = EventManager.EventStatus.Prestarting;

                StartCoroutine(CreateEventPlayers());                
            }
            
            /// <summary>
            /// Start's the event
            /// </summary>
            protected virtual void StartEvent()
            {
                InvokeHandler.CancelInvoke(this, PrestartEvent);

                if (!HasMinimumRequiredPlayers())
                {
                    Broadcast("Notification.NotEnoughToStart");
                    EndEvent();
                    return;
                }

                Timer.StopTimer();

                Status = EventManager.EventStatus.Started;

                if (Config.TimeLimit > 0)
                    Timer.StartTimer(Config.TimeLimit, string.Empty, EndEvent);

                GodmodeEnabled = false;

                eventPlayers.ForEach((BaseEventPlayer eventPlayer) =>
                {
                    if (eventPlayer?.Player == null)
                        return;

                    if (eventPlayer.IsDead)
                        RespawnPlayer(eventPlayer);
                    else
                    {
                        ResetPlayer(eventPlayer.Player);
                        OnPlayerRespawn(eventPlayer);
                    }
                });
            }

            /// <summary>
            /// End's the event and restore's all player's back to the state they were in prior to the event starting
            /// </summary>
            internal virtual void EndEvent()
            {
                InvokeHandler.CancelInvoke(this, PrestartEvent);

                Timer.StopTimer();

                Status = EventManager.EventStatus.Finished;

                GodmodeEnabled = true;

                LastEventResult.UpdateFromEvent(this);

                ProcessWinners();

                eventPlayers.ForEach((BaseEventPlayer eventPlayer) =>
                {
                    if (eventPlayer?.Player == null)
                        return;

                    if (eventPlayer.IsDead)  
                        ResetPlayer(eventPlayer.Player);
                    
                    EventStatistics.Data.OnGamePlayed(eventPlayer.Player, Config.EventType);
                });

                EventStatistics.Data.OnGamePlayed(Config.EventType);

                EjectAllPlayers();

                DestroyImmediate(this);
            }
            #endregion

            #region Player Management
            internal bool IsOpen()
            {
                if (_isClosed || Status == EventManager.EventStatus.Finished)
                    return false;

                if (((int)Status < 2 && joiningPlayers.Count >= Config.MaximumPlayers) || eventPlayers.Count >= Config.MaximumPlayers)
                    return false;

                if (!string.IsNullOrEmpty(CanJoinEvent()))
                    return false;

                return true;
            }

            internal bool CanJoinEvent(BasePlayer player)
            {
                if (_isClosed)
                {
                    player.ChatMessage(Message("Notification.EventClosed", player.userID));
                    return false;
                }

                if (Status == EventManager.EventStatus.Finished)
                {
                    player.ChatMessage(Message("Notification.EventFinished", player.userID));
                    return false;
                }

                if (((int)Status < 2 && joiningPlayers.Count >= Config.MaximumPlayers) || eventPlayers.Count >= Config.MaximumPlayers)
                {
                    player.ChatMessage(Message("Notification.MaximumPlayers", player.userID));
                    return false;
                }

                string str = CanJoinEvent();

                if (!string.IsNullOrEmpty(str))
                {
                    player.ChatMessage(str);
                    return false;
                }

                return true;
            }

            /// <summary>
            /// Allow or disallow players to join the event
            /// </summary>
            /// <returns>Supply a (string) reason to disallow, or a empty string to allow</returns>
            protected virtual string CanJoinEvent()
            {
                return string.Empty;
            }

            /// <summary>
            /// Override to perform additional logic when a player joins an event
            /// </summary>
            /// <param name="player">The BasePlayer object of the player joining the event</param>
            /// <param name="team">The team the player should be placed in</param>
            internal virtual void JoinEvent(BasePlayer player, Team team = Team.None)
            {
                if (Status == EventManager.EventStatus.Started)
                    CreateEventPlayer(player, team);
                else joiningPlayers.Add(player);

                if (Configuration.Message.BroadcastJoiners)
                    Broadcast("Notification.PlayerJoined", player.displayName, Config.EventName);                
            }

            /// <summary>
            /// Override to perform additional logic when a player leaves an event. This is called when the player uses the leave chat command prior to destroying the BaseEventPlayer
            /// </summary>
            /// <param name="player">The BasePlayer object of the player leaving the event</param>
            internal virtual void LeaveEvent(BasePlayer player)
            {
                if (joiningPlayers.Contains(player))
                {
                    joiningPlayers.Remove(player);

                    if (Configuration.Message.BroadcastLeavers)
                        Broadcast("Notification.PlayerLeft", player.displayName, Config.EventName);
                }

                BaseEventPlayer eventPlayer = GetUser(player);
                if (eventPlayer == null)
                    return;

                LeaveEvent(eventPlayer);
            }

            /// <summary>
            /// Override to perform additional logic when a event player leaves an event
            /// </summary>
            /// <param name="eventPlayer">The BaseEventPlayer object of the player leaving the event</param>
            internal virtual void LeaveEvent(BaseEventPlayer eventPlayer)
            {
                if (!string.IsNullOrEmpty(Config.ZoneID))
                    Instance.ZoneManager?.Call("RemovePlayerFromZoneWhitelist", Config.ZoneID, eventPlayer.Player);

                eventPlayers.Remove(eventPlayer);

                if (!eventPlayer.Player.IsConnected || eventPlayer.Player.IsSleeping() || IsUnloading)
                    eventPlayer.Player.Die();
                else Instance.Restore.RestorePlayer(eventPlayer.Player);

                DestroyImmediate(eventPlayer);

                if (Status != EventManager.EventStatus.Finished && !HasMinimumRequiredPlayers())
                {
                    BroadcastToPlayers("Notification.NotEnoughToContinue");
                    EndEvent();
                }
            }

            private IEnumerator CreateEventPlayers()
            {
                for (int i = joiningPlayers.Count - 1; i >= 0; i--)
                {
                    BasePlayer joiner = joiningPlayers[i];

                    EMInterface.DestroyAllUI(joiner);

                    CreateEventPlayer(joiner, GetPlayerTeam(joiner));

                    yield return CoroutineEx.waitForEndOfFrame;
                    yield return CoroutineEx.waitForEndOfFrame;
                }

                UpdateScoreboard();

                Timer.StartTimer(Configuration.Timer.Prestart, Message("Notification.RoundStartsIn"), StartEvent);
            }

            /// <summary>
            /// Override to perform additional logic when initializing the BaseEventPlayer component
            /// </summary>
            /// <param name="player">The BasePlayer object of the player joining the event</param>
            /// <param name="team">The team this player is on</param>
            protected virtual void CreateEventPlayer(BasePlayer player, Team team = Team.None)
            {
                if (player == null)
                    return;

                joiningPlayers.Remove(player);

                BaseEventPlayer eventPlayer = AddPlayerComponent(player);

                eventPlayer.ResetPlayer();

                eventPlayer.Event = this;

                eventPlayer.Team = team;

                eventPlayers.Add(eventPlayer);

                if (!Config.AllowClassSelection || GetAvailableKits(eventPlayer.Team).Count <= 1)
                    eventPlayer.Kit = GetAvailableKits(team).First();

                SpawnPlayer(eventPlayer, Status == EventManager.EventStatus.Started, true);

                if (!string.IsNullOrEmpty(Config.ZoneID))                
                    Instance.ZoneManager?.Call("AddPlayerToZoneWhitelist", Config.ZoneID, player);                
            }

            /// <summary>
            /// Override to assign players to teams
            /// </summary>
            /// <param name="player"></param>
            /// <returns>The team the player will be assigned to</returns>
            protected virtual Team GetPlayerTeam(BasePlayer player) => Team.None;

            /// <summary>
            /// Add's the BaseEventPlayer component to the player. Override with your own component if you want to extend the BaseEventPlayer class
            /// </summary>
            /// <param name="player"></param>
            /// <param name="team"></param>
            /// <returns>The BaseEventPlayer component</returns>
            protected virtual BaseEventPlayer AddPlayerComponent(BasePlayer player) => player.gameObject.GetComponent<BaseEventPlayer>() ?? player.gameObject.AddComponent<BaseEventPlayer>();                
            
            /// <summary>
            /// Called prior to a event player respawning
            /// </summary>
            /// <param name="baseEventPlayer"></param>
            internal virtual void OnPlayerRespawn(BaseEventPlayer baseEventPlayer)
            {
                SpawnPlayer(baseEventPlayer, Status == EventManager.EventStatus.Started);
            }

            /// <summary>
            /// Spawn's the specified player
            /// </summary>
            /// <param name="eventPlayer"></param>
            /// <param name="giveKit">Should this player recieve a kit?</param>
            /// <param name="sleep">Should this player be put to sleep before teleporting?</param>
            internal void SpawnPlayer(BaseEventPlayer eventPlayer, bool giveKit = true, bool sleep = false)
            {
                BasePlayer player = eventPlayer?.Player;
                if (player == null)
                    return;

                eventPlayer.Player?.GetMounted()?.AttemptDismount(eventPlayer.Player);

                StripInventory(player);

                ResetMetabolism(player);

                MovePosition(player, eventPlayer.Team == Team.B ? _spawnSelectorB.GetSpawnPoint() : _spawnSelectorA.GetSpawnPoint(), sleep);

                if (string.IsNullOrEmpty(eventPlayer.Kit))
                {
                    eventPlayer.ForceSelectClass();
                    EMInterface.DisplayDeathScreen(eventPlayer, Message("UI.SelectClass", eventPlayer.Player.userID), true);
                    return;
                }
                
                UpdateScoreboard(eventPlayer);

                if (giveKit)
                {
                    Instance.NextTick(() =>
                    {
                        if (!CanGiveKit(eventPlayer))
                            return;

                        GiveKit(player, eventPlayer.Kit);

                        OnKitGiven(eventPlayer);                        
                    });
                }

                eventPlayer.ApplyInvincibility();

                OnPlayerSpawned(eventPlayer);
            }

            /// <summary>
            /// Called after a player has spawned/respawned
            /// </summary>
            /// <param name="eventPlayer">The player that has spawned</param>
            protected virtual void OnPlayerSpawned(BaseEventPlayer eventPlayer) { }

            /// <summary>
            /// Kicks all players out of the event
            /// </summary>
            protected void EjectAllPlayers()
            {
                for (int i = eventPlayers.Count - 1; i >= 0; i--)
                    LeaveEvent(eventPlayers[i].Player);
                eventPlayers.Clear();
            }

            /// <summary>
            /// Reset's all players that are currently dead and respawn's them
            /// </summary>
            protected void RespawnAllPlayers()
            {
                for (int i = eventPlayers.Count - 1; i >= 0; i--)
                    RespawnPlayer(eventPlayers[i]);                
            }

            private bool HasMinimumRequiredPlayers()
            {
                if (Status == EventManager.EventStatus.Open)
                    return joiningPlayers.Count >= Config.MinimumPlayers;
                else return eventPlayers.Count >= Config.MinimumPlayers;
            }
            #endregion

            #region Damage and Death
            /// <summary>
            /// Called when a player deals damage to a entity that is not another event player
            /// </summary>
            /// <param name="attacker">The player dealing the damage</param>
            /// <param name="entity">The entity that was hit</param>
            /// <param name="hitInfo">The HitInfo</param>
            /// <returns>True allows damage, false prevents damage</returns>
            internal virtual bool CanDealEntityDamage(BaseEventPlayer attacker, BaseEntity entity, HitInfo hitInfo)
            {
                return false;
            }

            /// <summary>
            /// Scale's player-to-player damage
            /// </summary>
            /// <param name="eventPlayer">The player that is attacking</param>
            /// <returns>1.0f is normal damage</returns>
            protected virtual float GetDamageModifier(BaseEventPlayer eventPlayer) => 1f;

            /// <summary>
            /// Calculates and applies damage to the player
            /// </summary>
            /// <param name="eventPlayer"></param>
            /// <param name="hitInfo"></param>
            internal virtual void OnPlayerTakeDamage(BaseEventPlayer eventPlayer, HitInfo hitInfo)
            {
                BaseEventPlayer attacker = GetUser(hitInfo.InitiatorPlayer);

                if (GodmodeEnabled || eventPlayer.IsDead || eventPlayer.IsInvincible)
                {
                    ClearDamage(hitInfo);
                    return;
                }
                
                float damageModifier = GetDamageModifier(attacker);
                if (damageModifier != 1f)
                    hitInfo.damageTypes.ScaleAll(damageModifier);

                eventPlayer.OnTakeDamage(attacker?.Player.userID ?? 0U);
            }

            /// <summary>
            /// Called prior to event player death logic. Prepares the player for the death cycle by hiding them from other players
            /// </summary>
            /// <param name="eventPlayer"></param>
            /// <param name="hitInfo"></param>
            internal virtual void PrePlayerDeath(BaseEventPlayer eventPlayer, HitInfo hitInfo)
            {
                if (CanDropBackpack())
                    eventPlayer.DropInventory();

                if (eventPlayer.Player.isMounted)
                {
                    BaseMountable baseMountable = eventPlayer.Player.GetMounted();
                    if (baseMountable != null)
                    {
                        baseMountable.DismountPlayer(eventPlayer.Player);
                        eventPlayer.Player.EnsureDismounted();
                    }
                }

                eventPlayer.IsDead = true;

                UpdateDeadSpectateTargets(eventPlayer);

                eventPlayer.Player.limitNetworking = true;

                eventPlayer.Player.DisablePlayerCollider();

                eventPlayer.Player.RemoveFromTriggers();

                eventPlayer.RemoveFromNetwork();                

                OnEventPlayerDeath(eventPlayer, GetUser(hitInfo?.InitiatorPlayer), hitInfo);

                ClearDamage(hitInfo);
            }

            internal virtual void OnEventPlayerDeath(BaseEventPlayer victim, BaseEventPlayer attacker = null, HitInfo hitInfo = null)
            {
                if (victim == null || victim.Player == null)
                    return;

                StripInventory(victim.Player);

                if (Configuration.Message.BroadcastKills)
                    DisplayKillToChat(victim, attacker?.Player != null ? attacker.Player.displayName : string.Empty);
            }

            /// <summary>
            /// Display's the death message in chat
            /// </summary>
            /// <param name="victim"></param>
            /// <param name="attackerName"></param>
            protected virtual void DisplayKillToChat(BaseEventPlayer victim, string attackerName)
            {
                if (string.IsNullOrEmpty(attackerName))
                {
                    if (victim.IsOutOfBounds)
                        BroadcastToPlayers("Notification.Death.OOB", victim.Player.displayName);
                    else BroadcastToPlayers("Notification.Death.Suicide", victim.Player.displayName);
                }
                else BroadcastToPlayers("Notification.Death.Killed", victim.Player.displayName, attackerName);                
            }
            #endregion

            #region Winners
            /// <summary>
            /// Applies winner statistics, give's rewards and print's winner information to chat
            /// </summary>
            protected void ProcessWinners()
            {
                List<BaseEventPlayer> winners = Pool.GetList<BaseEventPlayer>();
                GetWinningPlayers(ref winners);

                for (int i = 0; i < eventPlayers.Count; i++)
                {
                    BaseEventPlayer eventPlayer = eventPlayers[i];
                    if (eventPlayer == null)
                        continue;

                    if (winners.Contains(eventPlayer))
                    {
                        EventStatistics.Data.AddStatistic(eventPlayer.Player, "Wins");
                        Instance.GiveReward(eventPlayer, Configuration.Reward.WinAmount);
                    }
                    else EventStatistics.Data.AddStatistic(eventPlayer.Player, "Losses");

                    EventStatistics.Data.AddStatistic(eventPlayer.Player, "Played");
                }

                if (Configuration.Message.BroadcastWinners && winners.Count > 0)
                {
                    if (Plugin.IsTeamEvent)
                    {
                        Team team = winners[0].Team;
                        Broadcast("Notification.EventWin.Multiple.Team", team == Team.B ? TeamBColor : TeamAColor, team, winners.Select(x => x.Player.displayName).ToSentence());
                    }
                    else
                    {
                        if (winners.Count > 1)
                            Broadcast("Notification.EventWin.Multiple", winners.Select(x => x.Player.displayName).ToSentence());
                        else Broadcast("Notification.EventWin", winners[0].Player.displayName);
                    }
                }

                Pool.FreeList(ref winners);
            }

            /// <summary>
            /// Override to calculate the winning player(s). This should done done on a per event basis
            /// </summary>
            /// <param name="list"></param>
            protected virtual void GetWinningPlayers(ref List<BaseEventPlayer> list) { }
            #endregion

            #region Kits and Items
            /// <summary>
            /// Drop's the players belt and main containers in to a bag on death
            /// </summary>
            /// <returns>Return false to disable this feature</returns>
            protected virtual bool CanDropBackpack() => true;

            /// <summary>
            /// Override to prevent players being given kits
            /// </summary>
            /// <param name="eventPlayer"></param>
            /// <returns></returns>
            protected virtual bool CanGiveKit(BaseEventPlayer eventPlayer) => true;

            /// <summary>
            /// Called after a player has been given a kit. If the event is team based and team attire kits have been set team attire will be given
            /// </summary>
            /// <param name="eventPlayer"></param>
            protected virtual void OnKitGiven(BaseEventPlayer eventPlayer)
            {
                if (Plugin.IsTeamEvent)
                {
                    string kit = eventPlayer.Team == Team.B ? Config.TeamConfigB.Clothing : Config.TeamConfigA.Clothing;
                    if (!string.IsNullOrEmpty(kit))
                    {
                        List<Item> items = eventPlayer.Player.inventory.containerWear.itemList;
                        for (int i = 0; i < items.Count; i++)
                        {
                            Item item = items[i];
                            item.RemoveFromContainer();
                            item.Remove();
                        }

                        GiveKit(eventPlayer.Player, kit);
                    }
                }
            }

            /// <summary>
            /// Get's the list of Kits available for the specified team
            /// </summary>
            /// <param name="team"></param>
            /// <returns></returns>
            internal List<string> GetAvailableKits(Team team) => team == Team.B ? Config.TeamConfigB.Kits : Config.TeamConfigA.Kits;
            #endregion

            #region Overrides
            /// <summary>
            /// Allows you to display additional event details in the event menu. The key should be a localized message for the target player
            /// </summary>
            /// <param name="list"></param>
            /// <param name="playerId">The user's ID for localization purposes</param>
            internal virtual void GetAdditionalEventDetails(ref List<KeyValuePair<string, object>> list, ulong playerId) { }
            #endregion

            #region Spectating
            /// <summary>
            /// Fill's a list with valid spectate targets
            /// </summary>
            /// <param name="list"></param>
            internal virtual void GetSpectateTargets(ref List<BaseEventPlayer> list)
            {
                list.Clear();
                list.AddRange(eventPlayers);
            }

            /// <summary>
            /// Checks all spectating event players and updates their spectate target if the target has just died
            /// </summary>
            /// <param name="victim"></param>
            private void UpdateDeadSpectateTargets(BaseEventPlayer victim)
            {
                List<BaseEventPlayer> list = Pool.GetList<BaseEventPlayer>();
                GetSpectateTargets(ref list);

                bool hasValidSpectateTargets = list.Count > 0;

                for (int i = 0; i < eventPlayers.Count; i++)
                {
                    BaseEventPlayer eventPlayer = eventPlayers[i];

                    if (eventPlayer.Player.IsSpectating() && eventPlayer.SpectateTarget == victim)
                    {
                        if (hasValidSpectateTargets)
                            eventPlayer.UpdateSpectateTarget();
                        else eventPlayer.FinishSpectating();
                    }
                }
            }
            #endregion

            #region Player Counts
            /// <summary>
            /// Count the amount of player's that are alive
            /// </summary>
            /// <returns></returns>
            internal int GetAlivePlayerCount()
            {
                int count = 0;
                for (int i = 0; i < eventPlayers.Count; i++)
                {
                    if (!eventPlayers[i]?.IsDead ?? false)
                        count++;
                }
                return count;
            }

            /// <summary>
            /// Count the amount of player's on the specified team
            /// </summary>
            /// <param name="team"></param>
            /// <returns></returns>
            internal int GetTeamCount(Team team)
            {
                int count = 0;
                for (int i = 0; i < eventPlayers.Count; i++)
                {
                    if (eventPlayers[i]?.Team == team)
                        count++;
                }
                return count;
            }

            /// <summary>
            /// Count the amount of player's that are alive on the specified team
            /// </summary>
            /// <param name="team"></param>
            /// <returns></returns>
            internal int GetTeamAliveCount(Team team)
            {
                int count = 0;
                for (int i = 0; i < eventPlayers.Count; i++)
                {
                    BaseEventPlayer eventPlayer = eventPlayers[i];
                    if (eventPlayer != null && eventPlayer.Team == team && !eventPlayer.IsDead)
                        count++;
                }
                return count;               
            }
            #endregion

            #region Teams
            /// <summary>
            /// Get the score for the specified team
            /// </summary>
            /// <param name="team"></param>
            /// <returns></returns>
            internal virtual int GetTeamScore(Team team) => 0;

            /// <summary>
            /// Balance the team's if one team has > 2 more player's on it
            /// </summary>
            protected void BalanceTeams()
            {
                int aCount = GetTeamCount(Team.A);
                int bCount = GetTeamCount(Team.B);

                int difference = aCount > bCount + 1 ? aCount - bCount : bCount > aCount + 1 ? bCount - aCount : 0;
                Team moveFrom = aCount > bCount + 1 ? Team.A : bCount > aCount + 1 ? Team.B : Team.None;

                if (difference > 1 && moveFrom != Team.None)
                {
                    BroadcastToPlayers("Notification.Teams.Unbalanced");

                    List<BaseEventPlayer> teamPlayers = Pool.GetList<BaseEventPlayer>();

                    eventPlayers.ForEach(x =>
                    {
                        if (x.Team == moveFrom)
                            teamPlayers.Add(x);
                    });

                    for (int i = 0; i < (int)Math.Floor((float)difference / 2); i++)
                    {
                        BaseEventPlayer eventPlayer = teamPlayers.GetRandom();
                        teamPlayers.Remove(eventPlayer);

                        eventPlayer.Team = moveFrom == Team.A ? Team.B : Team.A;
                        BroadcastToPlayer(eventPlayer, string.Format(Message("Notification.Teams.TeamChanged", eventPlayer.Player.userID), eventPlayer.Team));
                    }

                    Pool.FreeList(ref teamPlayers);
                }
            }
            #endregion

            #region Entity Management
            /// <summary>
            /// Keep's track of entities deployed by event players
            /// </summary>
            /// <param name="entity"></param>
            internal void OnEntityDeployed(BaseCombatEntity entity) => _deployedObjects.Add(entity);
            
            /// <summary>
            /// Destroy's any entities deployed by event players
            /// </summary>
            private void CleanupEntities()
            {
                for (int i = _deployedObjects.Count - 1; i >= 0; i--)
                {
                    BaseCombatEntity entity = _deployedObjects[i];
                    if (entity != null && !entity.IsDestroyed)
                        entity.DieInstantly();
                }

                _deployedObjects.Clear();
            }
            #endregion

            #region Scoreboard    
            /// <summary>
            /// Rebuild and send the scoreboard to players
            /// </summary>
            internal void UpdateScoreboard()
            {
                UpdateScores();
                BuildScoreboard();

                if (scoreContainer != null)
                {
                    eventPlayers.ForEach((BaseEventPlayer eventPlayer) =>
                    {
                        if (!eventPlayer.IsDead)
                            eventPlayer.AddUI(EMInterface.UI_SCORES, scoreContainer);
                    });
                }
            }

            /// <summary>
            /// Send the last generated scoreboard to the specified player
            /// </summary>
            /// <param name="eventPlayer"></param>
            protected void UpdateScoreboard(BaseEventPlayer eventPlayer)
            {
                if (scoreContainer != null && !eventPlayer.IsDead)
                    eventPlayer.AddUI(EMInterface.UI_SCORES, scoreContainer);
            }

            /// <summary>
            /// Update the score list and sort it
            /// </summary>
            protected void UpdateScores()
            {
                scoreData.Clear();

                for (int i = 0; i < eventPlayers.Count; i++)
                {
                    BaseEventPlayer eventPlayer = eventPlayers[i];

                    scoreData.Add(new ScoreEntry(eventPlayer, GetFirstScoreValue(eventPlayer), GetSecondScoreValue(eventPlayer)));
                }

                SortScores(ref scoreData);
            }

            /// <summary>
            /// Called when building the scoreboard. This should be done on a per event basis
            /// </summary>
            protected virtual void BuildScoreboard() { }

            /// <summary>
            /// The first score value to be displayed on scoreboards
            /// </summary>
            /// <param name="eventPlayer"></param>
            /// <returns></returns>
            protected virtual float GetFirstScoreValue(BaseEventPlayer eventPlayer) => 0f;

            /// <summary>
            /// The second score value to be displayed on scoreboards
            /// </summary>
            /// <param name="eventPlayer"></param>
            /// <returns></returns>
            protected virtual float GetSecondScoreValue(BaseEventPlayer eventPlayer) => 0f;

            /// <summary>
            /// Sort's the score list. This should be done on a per event basis
            /// </summary>
            /// <param name="list"></param>
            protected virtual void SortScores(ref List<ScoreEntry> list) { }
            #endregion

            #region Event Messaging
            /// <summary>
            /// Broadcasts a localized message to all event players
            /// </summary>
            /// <param name="key">Localizaiton key</param>
            /// <param name="args">Message arguments</param>
            internal void BroadcastToPlayers(string key, params object[] args)
            {
                for (int i = 0; i < eventPlayers.Count; i++)
                {
                    BaseEventPlayer eventPlayer = eventPlayers[i];
                    if (eventPlayer?.Player != null)
                        BroadcastToPlayer(eventPlayer, args != null ? string.Format(Message(key, eventPlayer.Player.userID), args) : Message(key, eventPlayer.Player.userID));
                }
            }

            /// <summary>
            /// Broadcasts a localized message to all event players, using the calling plugins localized messages
            /// </summary>
            /// <param name="key">Localizaiton key</param>
            /// <param name="args">Message arguments</param>
            internal void BroadcastToPlayers(Func<string, ulong, string> GetMessage, string key, params object[] args)
            {
                for (int i = 0; i < eventPlayers.Count; i++)
                {
                    BaseEventPlayer eventPlayer = eventPlayers[i];
                    if (eventPlayer?.Player != null)
                        BroadcastToPlayer(eventPlayer, args != null ? string.Format(GetMessage(key, eventPlayer.Player.userID), args) : GetMessage(key, eventPlayer.Player.userID));
                }
            }

            /// <summary>
            /// Broadcasts a localized message to all event players on the specified team
            /// </summary>
            /// <param name="team">Target team</param>
            /// <param name="key">Localizaiton key</param>
            /// <param name="args">Message arguments</param>
            internal void BroadcastToTeam(Team team, string key, string[] args = null)
            {
                for (int i = 0; i < eventPlayers.Count; i++)
                {
                    BaseEventPlayer eventPlayer = eventPlayers[i];
                    if (eventPlayer?.Player != null && eventPlayer.Team == team)
                        BroadcastToPlayer(eventPlayer, args != null ? string.Format(Message(key, eventPlayer.Player.userID), args) : Message(key, eventPlayer.Player.userID));
                }
            }

            /// <summary>
            /// Sends a message directly to the specified player
            /// </summary>
            /// <param name="eventPlayer"></param>
            /// <param name="message"></param>
            internal void BroadcastToPlayer(BaseEventPlayer eventPlayer, string message) => eventPlayer?.Player?.SendConsoleCommand("chat.add", new object[] { 0, "76561198403299915", message });            
            #endregion
        }

        public class BaseEventPlayer : MonoBehaviour
        {      
            protected float _respawnDurationRemaining;

            protected float _invincibilityEndsAt;

            private double _resetDamageTime;

            private List<ulong> _damageContributors = Pool.GetList<ulong>();

            private bool _isOOB;

            private int _oobTime;

            private int _spectateIndex = 0;


            internal BasePlayer Player { get; private set; }

            internal BaseEventGame Event { get; set; }

            internal Team Team { get; set; } = Team.None;

            internal int Kills { get; set; }

            internal int Deaths { get; set; }
            


            internal bool IsDead { get; set; }

            internal bool AutoRespawn { get; set; }

            internal bool CanRespawn => _respawnDurationRemaining <= 0;

            internal int RespawnRemaining => Mathf.CeilToInt(_respawnDurationRemaining);

            internal bool IsInvincible => Time.time < _invincibilityEndsAt;

            
            internal BaseEventPlayer SpectateTarget { get; private set; } = null;


            internal string Kit { get; set; }

            internal bool IsSelectingClass { get; set; }


            internal bool IsOutOfBounds
            {
                get
                {
                    return _isOOB;
                }
                set
                {
                    if (value)
                    {
                        _oobTime = 10;
                        InvokeHandler.Invoke(this, TickOutOfBounds, 1f);
                    }
                    else InvokeHandler.CancelInvoke(this, TickOutOfBounds);

                    _isOOB = value;
                }
            }
            
            private void Awake()
            {
                Player = GetComponent<BasePlayer>();

                Instance.Restore.AddData(Player);

                Player.metabolism.bleeding.max = 0;
                Player.metabolism.bleeding.value = 0;
                Player.metabolism.radiation_level.max = 0;
                Player.metabolism.radiation_level.value = 0;
                Player.metabolism.radiation_poison.max = 0;
                Player.metabolism.radiation_poison.value = 0;

                Player.metabolism.SendChangesToClient();
            }

            private void OnDestroy()
            {
                Player.metabolism.bleeding.max = 1;
                Player.metabolism.bleeding.value = 0;
                Player.metabolism.radiation_level.max = 100;
                Player.metabolism.radiation_level.value = 0;
                Player.metabolism.radiation_poison.max = 500;
                Player.metabolism.radiation_poison.value = 0;

                Player.metabolism.SendChangesToClient();

                if (Player.isMounted)
                    Player.GetMounted()?.AttemptDismount(Player);

                DestroyUI();

                if (IsUnloading)
                    StripInventory(Player);

                UnlockInventory(Player);
                
                InvokeHandler.CancelInvoke(this, TickOutOfBounds);

                Pool.FreeList(ref _damageContributors);
                Pool.FreeList(ref _openPanels);
            }

            internal void ResetPlayer()
            {
                Team = Team.None;
                Kills = 0;
                Deaths = 0;
                IsDead = false;
                AutoRespawn = false;
                Kit = string.Empty;
                IsSelectingClass = false;

                _spectateIndex = 0;
                _respawnDurationRemaining = 0;
                _invincibilityEndsAt = 0;
                _resetDamageTime = 0;
                _oobTime = 0;
                _isOOB = false;

                _damageContributors.Clear();
            }

            internal void ForceSelectClass()
            {
                IsDead = true;
                IsSelectingClass = true;
            }

            protected void RespawnTick()
            {
                _respawnDurationRemaining = Mathf.Clamp(_respawnDurationRemaining - 1f, 0f, float.MaxValue);

                EMInterface.UpdateRespawnButton(this);

                if (_respawnDurationRemaining <= 0f)
                {
                    InvokeHandler.CancelInvoke(this, RespawnTick);

                    if (AutoRespawn)
                        RespawnPlayer(this);
                }
            }

            #region Death
            internal void OnKilledPlayer(HitInfo hitInfo)
            {
                Kills++;

                int rewardAmount = Configuration.Reward.KillAmount;

                EventStatistics.Data.AddStatistic(Player, "Kills");

                if (hitInfo != null)
                {
                    if (hitInfo.damageTypes.IsMeleeType())
                        EventStatistics.Data.AddStatistic(Player, "Melee");

                    if (hitInfo.isHeadshot)
                    {
                        EventStatistics.Data.AddStatistic(Player, "Headshots");
                        rewardAmount = Configuration.Reward.HeadshotAmount;
                    }
                }

                if (rewardAmount > 0)
                    Instance.GiveReward(this, rewardAmount);
            }

            internal virtual void OnPlayerDeath(BaseEventPlayer attacker = null, float respawnTime = 5f)
            {
                AddPlayerDeath(attacker);

                _respawnDurationRemaining = respawnTime;

                InvokeHandler.InvokeRepeating(this, RespawnTick, 1f, 1f);

                DestroyUI();

                string message = attacker != null ? string.Format(Message("UI.Death.Killed", Player.userID), attacker.Player.displayName) : 
                                 IsOutOfBounds ? Message("UI.Death.OOB", Player.userID) :
                                 Message("UI.Death.Suicide", Player.userID);

                EMInterface.DisplayDeathScreen(this, message, true);
            }

            internal void AddPlayerDeath(BaseEventPlayer attacker = null)
            {
                Deaths++;
                EventStatistics.Data.AddStatistic(Player, "Deaths");
                ApplyAssistPoints(attacker);
            }

            protected void ApplyAssistPoints(BaseEventPlayer attacker = null)
            {
                if (_damageContributors.Count > 1)
                {
                    for (int i = 0; i < _damageContributors.Count - 1; i++)
                    {
                        ulong contributorId = _damageContributors[i];
                        if (attacker != null && attacker.Player.userID == contributorId)
                            continue;

                        EventStatistics.Data.AddStatistic(contributorId, "Assists");
                    }
                }

                _resetDamageTime = 0;
                _damageContributors.Clear();
            }

            internal void ApplyInvincibility() => _invincibilityEndsAt = Time.time + 3f;
            #endregion
            
            protected void TickOutOfBounds()
            {
                if (Player == null)
                {
                    BaseManager.LeaveEvent(this);
                    return;
                }

                if (IsDead)
                    return;

                if (IsOutOfBounds)
                {
                    if (_oobTime == 10)
                        BaseManager.BroadcastToPlayer(this, Message("Notification.OutOfBounds", Player.userID));
                    else if (_oobTime == 0)
                    {
                        Effect.server.Run("assets/prefabs/tools/c4/effects/c4_explosion.prefab", Player.transform.position);

                        if (BaseManager.Status == EventStatus.Started)
                            BaseManager.PrePlayerDeath(this, null);
                        else BaseManager.SpawnPlayer(this, false);
                    }
                    else BaseManager.BroadcastToPlayer(this, string.Format(Message("Notification.OutOfBounds.Time", Player.userID), _oobTime));

                    _oobTime--;

                    InvokeHandler.Invoke(this, TickOutOfBounds, 1f);
                }
            }

            internal void DropInventory()
            {
                const string BACKPACK_PREFAB = "assets/prefabs/misc/item drop/item_drop_backpack.prefab";

                DroppedItemContainer itemContainer = ItemContainer.Drop(BACKPACK_PREFAB, Player.transform.position, Quaternion.identity, new ItemContainer[] { Player.inventory.containerBelt, Player.inventory.containerMain });
                if (itemContainer != null)
                {
                    itemContainer.playerName = Player.displayName;
                    itemContainer.playerSteamID = Player.userID;

                    itemContainer.CancelInvoke(itemContainer.RemoveMe);
                    itemContainer.Invoke(itemContainer.RemoveMe, Configuration.Timer.Bag);
                }
            }

            #region Networking
            internal void RemoveFromNetwork()
            {
                if (Net.sv.write.Start())
                {
                    Net.sv.write.PacketID(Network.Message.Type.EntityDestroy);
                    Net.sv.write.EntityID(Player.net.ID);
                    Net.sv.write.UInt8((byte)BaseNetworkable.DestroyMode.None);
                    Net.sv.write.Send(new SendInfo(Player.net.group.subscribers.Where(x => x.userid != Player.userID).ToList()));
                }
            }

            internal void AddToNetwork() => Player.SendFullSnapshot();            
            #endregion

            #region Damage Contributors
            internal void OnTakeDamage(ulong attackerId)
            {
                float time = Time.realtimeSinceStartup;
                if (time > _resetDamageTime)
                {
                    _resetDamageTime = time + 3f;
                    _damageContributors.Clear();
                }

                if (attackerId != 0U && attackerId != Player.userID)
                {
                    if (_damageContributors.Contains(attackerId))
                        _damageContributors.Remove(attackerId);
                    _damageContributors.Add(attackerId);
                }
            }

            internal List<ulong> DamageContributors => _damageContributors;
            #endregion

            #region Spectating  
            public void BeginSpectating()
            {
                if (Player.IsSpectating())
                    return;

                DestroyUI();

                Player.StartSpectating();
                Player.ChatMessage(Message("Notification.SpectateCycle", Player.userID));
                UpdateSpectateTarget();
            }

            public void FinishSpectating()
            {
                if (!Player.IsSpectating())
                    return;

                Player.SetParent(null, false, false);
                Player.SetPlayerFlag(BasePlayer.PlayerFlags.Spectating, false);
                Player.gameObject.SetLayerRecursive(17);
            }

            public void SetSpectateTarget(BaseEventPlayer eventPlayer)
            {
                SpectateTarget = eventPlayer;

                Event.BroadcastToPlayer(this, $"Spectating: {eventPlayer.Player.displayName}");

                Player.SendEntitySnapshot(eventPlayer.Player);
                Player.gameObject.Identity();
                Player.SetParent(eventPlayer.Player, false, false);
            }

            public void UpdateSpectateTarget()
            {
                List<BaseEventPlayer> list = Pool.GetList<BaseEventPlayer>();

                Event.GetSpectateTargets(ref list);

                int newIndex = (int)Mathf.Repeat(_spectateIndex += 1, list.Count - 1);

                if (list[newIndex] != SpectateTarget)
                {
                    _spectateIndex = newIndex;
                    SetSpectateTarget(list[_spectateIndex]);
                }

                Pool.FreeList(ref list);
            }
            #endregion

            #region UI Management
            private List<string> _openPanels = Pool.GetList<string>();

            internal void AddUI(string panel, CuiElementContainer container)
            {
                DestroyUI(panel);

                _openPanels.Add(panel);
                CuiHelper.AddUi(Player, container);
            }

            internal void DestroyUI()
            {
                foreach (string panel in _openPanels)
                    CuiHelper.DestroyUi(Player, panel);
                _openPanels.Clear();
            }

            internal void DestroyUI(string panel)
            {
                if (_openPanels.Contains(panel))
                    _openPanels.Remove(panel);
                CuiHelper.DestroyUi(Player, panel);
            }
            #endregion
        }

        #region Event Timer
        public class GameTimer
        {
            private BaseEventGame _owner = null;

            private string _message;
            private int _timeRemaining;
            private Action _callback;

            internal GameTimer(BaseEventGame owner)
            {
                _owner = owner;
            }
                        
            internal void StartTimer(int time, string message = "", Action callback = null)
            {
                this._timeRemaining = time;
                this._message = message;
                this._callback = callback;

                InvokeHandler.InvokeRepeating(_owner, TimerTick, 1f, 1f);
            }

            internal void StopTimer()
            {
                InvokeHandler.CancelInvoke(_owner, TimerTick);

                for (int i = 0; i < _owner?.eventPlayers?.Count; i++)                
                    _owner.eventPlayers[i].DestroyUI(EMInterface.UI_TIMER);                
            }

            private void TimerTick()
            {
                _timeRemaining--;
                if (_timeRemaining == 0)
                {
                    StopTimer();
                    _callback?.Invoke();
                }
                else UpdateTimer();                
            }

            private void UpdateTimer()
            {
                string clockTime = string.Empty;

                TimeSpan dateDifference = TimeSpan.FromSeconds(_timeRemaining);
                int hours = dateDifference.Hours;
                int mins = dateDifference.Minutes;
                int secs = dateDifference.Seconds;

                if (hours > 0)
                    clockTime = string.Format("{0:00}:{1:00}:{2:00}", hours, mins, secs);
                else clockTime = string.Format("{0:00}:{1:00}", mins, secs);

                CuiElementContainer container = UI.Container(EMInterface.UI_TIMER, "0.1 0.1 0.1 0.7", new UI4(0.46f, 0.92f, 0.54f, 0.95f), false, "Hud");

                UI.Label(container, EMInterface.UI_TIMER, clockTime, 14, UI4.Full);

                if (!string.IsNullOrEmpty(_message))
                    UI.Label(container, EMInterface.UI_TIMER, _message, 14, new UI4(-5f, 0f, -0.1f, 1), TextAnchor.MiddleRight);

                for (int i = 0; i < _owner.eventPlayers.Count; i++)
                {
                    BaseEventPlayer eventPlayer = _owner.eventPlayers[i];
                    if (eventPlayer == null)
                        continue;

                    eventPlayer.DestroyUI(EMInterface.UI_TIMER);
                    eventPlayer.AddUI(EMInterface.UI_TIMER, container);
                }               
            }            
        }
        #endregion

        #region Spawn Management
        internal class SpawnSelector
        {
            private List<Vector3> _defaultSpawns;
            private List<Vector3> _availableSpawns;

            internal SpawnSelector(string eventName, string spawnFile)
            {
                _defaultSpawns = Instance.Spawns.Call("LoadSpawnFile", spawnFile) as List<Vector3>;
                _availableSpawns = Pool.GetList<Vector3>();
                _availableSpawns.AddRange(_defaultSpawns);
            }

            internal Vector3 GetSpawnPoint()
            {
                Vector3 point = _availableSpawns.GetRandom();
                _availableSpawns.Remove(point);

                if (_availableSpawns.Count == 0)
                    _availableSpawns.AddRange(_defaultSpawns);

                return point;
            }

            internal Vector3 ReserveSpawnPoint(int index)
            {
                Vector3 reserved = _defaultSpawns[index];
                _defaultSpawns.RemoveAt(index);

                _availableSpawns.Clear();
                _availableSpawns.AddRange(_defaultSpawns);

                return reserved;
            }

            internal void Destroy()
            {
                Pool.FreeList(ref _availableSpawns);
            }
        }
        #endregion

        #region Event Config
        public class EventConfig
        {            
            public string EventName { get; set; } = string.Empty;
            public string EventType { get; set; } = string.Empty;

            public string ZoneID { get; set; } = string.Empty;

            public int TimeLimit { get; set; }
            public int ScoreLimit { get; set; }
            public int MinimumPlayers { get; set; }
            public int MaximumPlayers { get; set; }

            public bool AllowClassSelection { get; set; }

            public TeamConfig TeamConfigA { get; set; } = new TeamConfig();
            public TeamConfig TeamConfigB { get; set; } = new TeamConfig();

            public Hash<string, object> AdditionalParams { get; set; } = new Hash<string, object>();

            public EventConfig() { }

            public EventConfig(string type, IEventPlugin eventPlugin)
            {
                this.EventType = type;
                this.Plugin = eventPlugin;

                if (eventPlugin.AdditionalParameters != null)
                {
                    for (int i = 0; i < eventPlugin.AdditionalParameters.Count; i++)
                    {
                        EventParameter eventParameter = eventPlugin.AdditionalParameters[i];

                        if (eventParameter.DefaultValue == null && eventParameter.IsList)
                            AdditionalParams[eventParameter.Field] = new List<string>();
                        else AdditionalParams[eventParameter.Field] = eventParameter.DefaultValue;
                    }
                }
            }

            public T GetParameter<T>(string key)
            {
                try
                {
                    object obj;
                    if (AdditionalParams.TryGetValue(key, out obj))
                        return (T)Convert.ChangeType(obj, typeof(T));
                }
                catch { }
                
                return default(T);
            }

            public string GetString(string fieldName)
            {
                switch (fieldName)
                {
                    case "teamASpawnfile":
                        return TeamConfigA.Spawnfile;
                    case "teamBSpawnfile":
                        return TeamConfigB.Spawnfile;
                    case "zoneID":
                        return ZoneID;
                    default:
                        object obj;
                        if (AdditionalParams.TryGetValue(fieldName, out obj) && obj is string)
                            return obj as string;
                        return null;
                }
            }

            public List<string> GetList(string fieldName)
            {
                switch (fieldName)
                {
                    case "teamAKits":
                        return TeamConfigA.Kits;
                    case "teamBKits":
                        return TeamConfigB.Kits;
                    default:
                        object obj;
                        if (AdditionalParams.TryGetValue(fieldName, out obj) && obj is List<string>)
                            return obj as List<string>;
                        return null;
                }
            }

            public class TeamConfig
            {
                public string Color { get; set; } = string.Empty;
                public string Spawnfile { get; set; } = string.Empty;
                public List<string> Kits { get; set; } = new List<string>();
                public string Clothing { get; set; } = string.Empty;
            }

            [JsonIgnore]
            public IEventPlugin Plugin { get; set; }
        }
        #endregion
        #endregion

        #region Rewards
        private void GiveReward(BaseEventPlayer baseEventPlayer, int amount)
        {
            switch (rewardType)
            {
                case RewardType.ServerRewards:
                    ServerRewards?.Call("AddPoints", baseEventPlayer.Player.UserIDString, amount);
                    break;
                case RewardType.Economics:
                    Economics?.Call("Deposit", baseEventPlayer.Player.UserIDString, (double)amount);
                    break;
                case RewardType.Scrap:
                    Restore.AddPrizeToData(baseEventPlayer.Player.userID, scrapItemId, amount);
                    break;                
            }
        }
        #endregion

        #region Enums
        public enum RewardType { ServerRewards, Economics, Scrap }

        public enum EventStatus { Finished, Open, Prestarting, Started }
        
        public enum Team { A, B, None }
        #endregion

        #region Helpers  
        private T ParseType<T>(string type)
        {
            try
            {
                return (T)Enum.Parse(typeof(T), type, true);
            }
            catch
            {
                return default(T);
            }
        }

        /// <summary>
        /// Get the BaseEventPlayer component on the specified BasePlayer
        /// </summary>
        /// <param name="player"></param>
        /// <returns></returns>
        internal static BaseEventPlayer GetUser(BasePlayer player) => player?.GetComponent<BaseEventPlayer>();

        /// <summary>
        /// Teleport player to the specified position
        /// </summary>
        /// <param name="player"></param>
        /// <param name="destination"></param>
        /// <param name="sleep"></param>
        internal static void MovePosition(BasePlayer player, Vector3 destination, bool sleep)
        {
            if (player.isMounted)
                player.GetMounted().DismountPlayer(player, true);

            if (player.GetParentEntity() != null)
                player.SetParent(null);

            if (sleep)
            {
                player.SetPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot, true);
                player.MovePosition(destination);
                player.UpdateNetworkGroup();
                player.StartSleeping();
                player.SendNetworkUpdateImmediate(false);
                player.ClearEntityQueue(null);
                player.ClientRPCPlayer(null, player, "StartLoading");
                player.SendFullSnapshot();
            }
            else
            {
                player.MovePosition(destination);
                player.ClientRPCPlayer(null, player, "ForcePositionTo", destination);
                player.SendNetworkUpdateImmediate();
                player.ClearEntityQueue(null);
            }
        }

        /// <summary>
        /// Lock the players inventory so they can't remove items
        /// </summary>
        /// <param name="player"></param>
        internal static void LockInventory(BasePlayer player)
        {
            if (player == null)
                return;

            if (!player.inventory.containerWear.HasFlag(ItemContainer.Flag.IsLocked))
            {
                player.inventory.containerWear.SetFlag(ItemContainer.Flag.IsLocked, true);
                player.inventory.SendSnapshot();
            }
        }

        /// <summary>
        /// Unlock the players inventory
        /// </summary>
        /// <param name="player"></param>
        internal static void UnlockInventory(BasePlayer player)
        {
            if (player == null)
                return;

            if (player.inventory.containerWear.HasFlag(ItemContainer.Flag.IsLocked))
            {
                player.inventory.containerWear.SetFlag(ItemContainer.Flag.IsLocked, false);
                player.inventory.SendSnapshot();
            }
        }

        /// <summary>
        /// Removes all items from the players inventory
        /// </summary>
        /// <param name="player"></param>
        internal static void StripInventory(BasePlayer player)
        {
            Item[] allItems = player.inventory.AllItems();

            for (int i = allItems.Length - 1; i >= 0; i--)
            {
                Item item = allItems[i];
                item.RemoveFromContainer();
                item.Remove();
            }
        }

        /// <summary>
        /// Reset the players health and metabolism
        /// </summary>
        /// <param name="player"></param>
        internal static void ResetMetabolism(BasePlayer player)
        {
            player.health = player.MaxHealth();

            player.SetPlayerFlag(BasePlayer.PlayerFlags.Wounded, false);

            player.metabolism.calories.value = player.metabolism.calories.max;
            player.metabolism.hydration.value = player.metabolism.hydration.max;
            player.metabolism.heartrate.Reset();

            player.metabolism.bleeding.value = 0;
            player.metabolism.radiation_level.value = 0;
            player.metabolism.radiation_poison.value = 0;
            player.metabolism.SendChangesToClient();            
        }

        /// <summary>
        /// Gives the player the specified kit
        /// </summary>
        /// <param name="player"></param>
        /// <param name="kitname"></param>
        internal static void GiveKit(BasePlayer player, string kitname) => Instance.Kits?.Call("GiveKit", player, kitname);

        /// <summary>
        /// Nullifies damage being dealt
        /// </summary>
        /// <param name="hitInfo"></param>
        internal static void ClearDamage(HitInfo hitInfo)
        {
            if (hitInfo == null)
                return;

            hitInfo.damageTypes.Clear();
            hitInfo.HitEntity = null;
            hitInfo.HitMaterial = 0;
            hitInfo.PointStart = Vector3.zero;
        }

        /// <summary>
        /// Resets the player so they have max health and are visible to other players
        /// </summary>
        /// <param name="player"></param>
        internal static void ResetPlayer(BasePlayer player)
        {
            BaseEventPlayer eventPlayer = GetUser(player);

            if (eventPlayer == null)
                return;

            if (eventPlayer.Player.IsSpectating())
                eventPlayer.FinishSpectating();

            player.limitNetworking = false;

            player.EnablePlayerCollider();

            player.health = player.MaxHealth();

            player.SetPlayerFlag(BasePlayer.PlayerFlags.Wounded, false);

            eventPlayer.IsDead = false;

            eventPlayer.AddToNetwork();  
        }

        /// <summary>
        /// Respawn the player if they are dead
        /// </summary>
        /// <param name="eventPlayer"></param>
        internal static void RespawnPlayer(BaseEventPlayer eventPlayer)
        {
            if (!eventPlayer.IsDead)
                return;

            eventPlayer.DestroyUI(EMInterface.UI_DEATH);
            eventPlayer.DestroyUI(EMInterface.UI_RESPAWN);            

            ResetPlayer(eventPlayer.Player);

            BaseManager.OnPlayerRespawn(eventPlayer);
        }

        /// <summary>
        /// Strip's clan tags out of a player display name
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        internal static string StripTags(string str)
        {
            if (str.StartsWith("[") && str.Contains("]") && str.Length > str.IndexOf("]"))
                str = str.Substring(str.IndexOf("]") + 1).Trim();

            if (str.StartsWith("[") && str.Contains("]") && str.Length > str.IndexOf("]"))
                StripTags(str);

            return str;
        }

        /// <summary>
        /// Trim's a player's display name to the specified size
        /// </summary>
        /// <param name="str"></param>
        /// <param name="size"></param>
        /// <returns></returns>
        internal static string TrimToSize(string str, int size = 18)
        {
            if (str.Length > size)
                str = str.Substring(0, size);
            return str;
        }
        #endregion

        #region Zone Management
        private void OnExitZone(string zoneId, BasePlayer player)
        {
            if (player == null)
                return;

            BaseEventPlayer eventPlayer = GetUser(player);

            if (eventPlayer == null || eventPlayer.IsDead)
                return;

            if (BaseManager != null && zoneId == BaseManager.Config.ZoneID)            
                eventPlayer.IsOutOfBounds = true;
        }

        private void OnEnterZone(string zoneId, BasePlayer player)
        {           
            BaseEventPlayer eventPlayer = GetUser(player);

            if (eventPlayer == null || eventPlayer.IsDead)
                return;
            
            if (BaseManager != null && zoneId == BaseManager.Config.ZoneID)            
                eventPlayer.IsOutOfBounds = false;   
        }
        #endregion

        #region File Validation
        internal object ValidateEventConfig(EventConfig eventConfig)
        {
            IEventPlugin plugin;

            if (string.IsNullOrEmpty(eventConfig.EventType) || !EventModes.TryGetValue(eventConfig.EventType, out plugin))
                return string.Concat("Event mode ", eventConfig.EventType, " is not currently loaded");

            if (!plugin.CanUseClassSelector && eventConfig.TeamConfigA.Kits.Count == 0)
                return "You must set atleast 1 kit";

            if (eventConfig.MinimumPlayers == 0)
                return "You must set the minimum players";

            if (eventConfig.MaximumPlayers == 0)
                return "You must set the maximum players";

            if (plugin.RequireTimeLimit && eventConfig.TimeLimit == 0)
                return "You must set a time limit";

            if (plugin.RequireScoreLimit && eventConfig.ScoreLimit == 0)
                return "You must set a score limit";

            object success;

            foreach (string kit in eventConfig.TeamConfigA.Kits)
            {
                success = ValidateKit(kit);
                if (success is string)
                    return $"Invalid kit: {kit}";
            }
            
            success = ValidateSpawnFile(eventConfig.TeamConfigA.Spawnfile);
            if (success is string)
                return $"Invalid spawn file: {eventConfig.TeamConfigA.Spawnfile}";

            if (plugin.IsTeamEvent)
            {
                success = ValidateSpawnFile(eventConfig.TeamConfigB.Spawnfile);
                if (success is string)
                    return $"Invalid second spawn file: {eventConfig.TeamConfigB.Spawnfile}";

                if (eventConfig.TeamConfigB.Kits.Count == 0)
                    return "You must set atleast 1 kit for Team B";

                foreach (string kit in eventConfig.TeamConfigB.Kits)
                {
                    success = ValidateKit(kit);
                    if (success is string)
                        return $"Invalid kit: {kit}";
                }
            }

            success = ValidateZoneID(eventConfig.ZoneID);
            if (success is string)
                return $"Invalid zone ID: {eventConfig.ZoneID}";

            for (int i = 0; i < plugin.AdditionalParameters?.Count; i++)
            {
                EventParameter eventParameter = plugin.AdditionalParameters[i];

                if (eventParameter.IsRequired)
                {
                    object value;
                    eventConfig.AdditionalParams.TryGetValue(eventParameter.Field, out value);

                    if (value == null)
                        return $"Missing event parameter: ({eventParameter.DataType}){eventParameter.Field}";
                    else
                    {
                        success = plugin.ParameterIsValid(eventParameter.Field, value);
                        if (success is string)
                            return (string)success;
                    }
                }
            }

            return null;
        }

        internal object ValidateSpawnFile(string name)
        {
            object success = Spawns?.Call("GetSpawnsCount", name);
            if (success is string)
                return (string)success;
            return null;
        }

        internal object ValidateZoneID(string name)
        {
            object success = ZoneManager?.Call("CheckZoneID", name);
            if (name is string && !string.IsNullOrEmpty((string)name))
                return null;
            return $"Zone \"{name}\" does not exist!";
        }

        internal object ValidateKit(string name)
        {
            object success = Kits?.Call("isKit", name);
            if ((success is bool))
            {
                if (!(bool)success)
                    return $"Kit \"{name}\" does not exist!";
            }
            return null;
        }
        #endregion

        #region Scoring
        public struct ScoreEntry
        {
            internal int position;
            internal string displayName;
            internal float value1;
            internal float value2;
            internal Team team;

            internal ScoreEntry(BaseEventPlayer eventPlayer, int position, float value1, float value2)
            {
                this.position = position;
                this.displayName = StripTags(eventPlayer.Player.displayName);
                this.team = eventPlayer.Team;
                this.value1 = value1;
                this.value2 = value2;
            }

            internal ScoreEntry(BaseEventPlayer eventPlayer, float value1, float value2)
            {
                this.position = 0;
                this.displayName = StripTags(eventPlayer.Player.displayName);
                this.team = eventPlayer.Team;
                this.value1 = value1;
                this.value2 = value2;
            }

            internal ScoreEntry(float value1, float value2)
            {
                this.position = 0;
                this.displayName = string.Empty;
                this.team = Team.None;
                this.value1 = value1;
                this.value2 = value2;
            }
        }

        public class EventResults
        {
            public string EventName { get; private set; }

            public string EventType { get; private set; }

            public ScoreEntry TeamScore { get; private set; }

            public IEventPlugin Plugin { get; private set; }

            public List<ScoreEntry> Scores { get; private set; } = new List<ScoreEntry>();

            public bool IsValid => Plugin != null;

            public void UpdateFromEvent(BaseEventGame baseEventGame)
            {
                EventName = baseEventGame.Config.EventName;
                EventType = baseEventGame.Config.EventType;
                Plugin = baseEventGame.Plugin;

                if (Plugin.IsTeamEvent)
                    TeamScore = new ScoreEntry(baseEventGame.GetTeamScore(Team.A), baseEventGame.GetTeamScore(Team.B));
                else TeamScore = default(ScoreEntry);

                Scores.Clear();

                if (baseEventGame.scoreData.Count > 0)
                    Scores.AddRange(baseEventGame.scoreData);
            }
        }
        #endregion

        #region Commands
        [ChatCommand("event")]
        private void cmdEvent(BasePlayer player, string command, string[] args)
        {
            EMInterface.Instance.OpenMenu(player, new EMInterface.MenuArgs(EMInterface.MenuTab.Event));
        }
        #endregion

        #region Config  
        public class ConfigData
        {
            [JsonProperty(PropertyName = "Auto-Event Options")]
            public AutoEventOptions AutoEvents { get; set; }

            [JsonProperty(PropertyName = "Event Options")]
            public EventOptions Event { get; set; }

            [JsonProperty(PropertyName = "Reward Options")]
            public RewardOptions Reward { get; set; }

            [JsonProperty(PropertyName = "Timer Options")]
            public TimerOptions Timer { get; set; }

            [JsonProperty(PropertyName = "Message Options")]
            public MessageOptions Message { get; set; }

            public class EventOptions
            {      
                [JsonProperty(PropertyName = "Blacklisted commands for event players")]
                public string[] CommandBlacklist { get; set; }
            }

            public class RewardOptions
            {                
                [JsonProperty(PropertyName = "Amount rewarded for kills")]
                public int KillAmount { get; set; }

                [JsonProperty(PropertyName = "Amount rewarded for wins")]
                public int WinAmount { get; set; }

                [JsonProperty(PropertyName = "Amount rewarded for headshots")]
                public int HeadshotAmount { get; set; }

                [JsonProperty(PropertyName = "Reward type (ServerRewards, Economics, Scrap)")]
                public string Type { get; set; }
            }

            public class TimerOptions
            {
                [JsonProperty(PropertyName = "Match start timer (seconds)")]
                public int Start { get; set; }

                [JsonProperty(PropertyName = "Match pre-start timer (seconds)")]
                public int Prestart { get; set; }

                [JsonProperty(PropertyName = "Backpack despawn timer (seconds)")]
                public int Bag { get; set; }
            }

            public class MessageOptions
            {
                [JsonProperty(PropertyName = "Announce events when one opens")]
                public bool Announce { get; set; }

                [JsonProperty(PropertyName = "Announce events when a match is playing (to non-event players)")]
                public bool AnnounceDuring { get; set; }

                [JsonProperty(PropertyName = "Event announcement interval (seconds)")]
                public int AnnounceInterval { get; set; }

                [JsonProperty(PropertyName = "Broadcast when a player joins an event to chat")]
                public bool BroadcastJoiners { get; set; }

                [JsonProperty(PropertyName = "Broadcast when a player leaves an event to chat")]
                public bool BroadcastLeavers { get; set; }

                [JsonProperty(PropertyName = "Broadcast the name(s) of the winning player(s) to chat")]
                public bool BroadcastWinners { get; set; }

                [JsonProperty(PropertyName = "Broadcast kills to chat")]
                public bool BroadcastKills { get; set; }  

                [JsonProperty(PropertyName = "Chat icon Steam ID")]
                public ulong ChatIcon { get; set; }
            }

            public class AutoEventOptions
            {
                [JsonProperty(PropertyName = "Enable auto-events")]
                public bool Enabled { get; set; }

                [JsonProperty(PropertyName = "List of event configs to run through")]
                public string[] Events { get; set; }

                [JsonProperty(PropertyName = "Randomize auto-event selection")]
                public bool Randomize { get; set; }
            }

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
                AutoEvents = new ConfigData.AutoEventOptions
                {
                    Enabled = false,
                    Events = new string[0],
                    Randomize = false
                },
                Event = new ConfigData.EventOptions
                {
                    CommandBlacklist = new string[] { "s", "tp" }
                },
                Message = new ConfigData.MessageOptions
                {
                    Announce = true,
                    AnnounceDuring = true,
                    AnnounceInterval = 120,
                    BroadcastJoiners = true,
                    BroadcastLeavers = true,
                    BroadcastWinners = true,
                    BroadcastKills = true,
                    ChatIcon = 76561198403299915
                },
                Reward = new ConfigData.RewardOptions
                {
                    KillAmount = 1,
                    WinAmount = 5,
                    HeadshotAmount = 2,
                    Type = "Scrap"
                },
                Timer = new ConfigData.TimerOptions
                {
                    Start = 60,
                    Prestart = 10,
                    Bag = 30
                },                
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
        internal void SaveEventData() => eventData.WriteObject(Events);

        private void SaveRestoreData() => restorationData.WriteObject(Restore);

        private void LoadData()
        {
            try
            {
                Restore = restorationData.ReadObject<RestoreData>();
            }
            catch
            {
                Restore = new RestoreData();
            }

            try
            {
                Events = eventData.ReadObject<EventData>();
            }
            catch
            {
                Events = new EventData();
            }
        }

        public static void SaveEventConfig(EventConfig eventConfig)
        {
            Instance.Events.events[eventConfig.EventName] = eventConfig;
            Instance.SaveEventData();
        }

        public class EventData
        {
            public Hash<string, EventConfig> events = new Hash<string, EventConfig>();
        }

        public class EventParameter
        {
            public string Name; // The name shown in the UI
            public InputType Input; // The type of input used to select the value in the UI

            public string Field; // The name of the custom field stored in the event config
            public string DataType; // The type of the field (string, int, float, bool, List<string>)

            public bool IsRequired; // Is this field required to complete event creation?

            public string SelectorHook; // The hook that is called to gather the options that can be selected. This should return a string[] (ex. GetZoneIDs from ZoneManager, GetAllKits from Kits)
            public bool SelectMultiple; // Allows the user to select multiple elements when using the selector

            public object DefaultValue; // Set the default value for this field

            [JsonIgnore]
            public bool IsList => Input == InputType.Selector && DataType.Equals("List<string>", StringComparison.OrdinalIgnoreCase);
            
            public enum InputType { InputField, Toggle, Selector }
        }

        #region Player Restoration
        public class RestoreData
        {
            public Hash<ulong, PlayerData> Restore = new Hash<ulong, PlayerData>();

            internal void AddData(BasePlayer player)
            {
                Restore[player.userID] = new PlayerData(player);
            }

            public void AddPrizeToData(ulong playerId, int itemId, int amount)
            {
                PlayerData playerData;
                if (Restore.TryGetValue(playerId, out playerData))
                {
                    ItemData itemData = FindItem(playerData, itemId);
                    if (itemData != null)
                        itemData.amount += amount;
                    else
                    {
                        Array.Resize<ItemData>(ref playerData.containerMain, playerData.containerMain.Length + 1);
                        playerData.containerMain[playerData.containerMain.Length - 1] = new ItemData() { amount = amount, condition = 100, contents = new ItemData[0], itemid = itemId, position = -1, skin = 0UL };
                    }
                }
            }

            private ItemData FindItem(PlayerData playerData, int itemId)
            {
                for (int i = 0; i < playerData.containerMain.Length; i++)
                {
                    ItemData itemData = playerData.containerMain[i];
                    if (itemData.itemid.Equals(itemId))
                        return itemData;
                }

                for (int i = 0; i < playerData.containerBelt.Length; i++)
                {
                    ItemData itemData = playerData.containerBelt[i];
                    if (itemData.itemid.Equals(itemId))
                        return itemData;
                }

                return null;
            }

            internal void RemoveData(ulong playerId)
            {
                if (HasRestoreData(playerId))
                    Restore.Remove(playerId);
            }

            internal bool HasRestoreData(ulong playerId) => Restore.ContainsKey(playerId);

            internal void RestorePlayer(BasePlayer player)
            {
                PlayerData playerData;
                if (Restore.TryGetValue(player.userID, out playerData))
                {
                    StripInventory(player);

                    player.metabolism.Reset();

                    if (player.IsSleeping() || player.HasPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot))
                    {
                        Instance.timer.Once(1, () => RestorePlayer(player));
                        return;
                    }

                    Instance.NextTick(() =>
                    {
                        playerData.SetStats(player);
                        MovePosition(player, playerData.position, true);
                        RestoreAllItems(player, playerData);
                    });
                }
            }

            private void RestoreAllItems(BasePlayer player, PlayerData playerData)
            {
                if (player == null || !player.IsConnected)
                    return;

                if (RestoreItems(player, playerData.containerBelt, Container.Belt) && 
                    RestoreItems(player, playerData.containerWear, Container.Wear) && 
                    RestoreItems(player, playerData.containerMain, Container.Main))
                    RemoveData(player.userID);
            }

            private bool RestoreItems(BasePlayer player, ItemData[] itemData, Container type)
            {
                ItemContainer container = type == Container.Belt ? player.inventory.containerBelt : type == Container.Wear ? player.inventory.containerWear : player.inventory.containerMain;

                for (int i = 0; i < itemData.Length; i++)
                {
                    ItemData data = itemData[i];
                    if (data.amount < 1)
                        continue;

                    Item item = CreateItem(data);
                    item.position = data.position;
                    item.SetParent(container);
                }
                return true;
            }            

            public class PlayerData
            {
                public float[] stats;
                public Vector3 position;
                public ItemData[] containerMain;
                public ItemData[] containerWear;
                public ItemData[] containerBelt;

                public PlayerData() { }

                public PlayerData(BasePlayer player)
                {
                    stats = GetStats(player);
                    position = player.transform.position;

                    containerBelt = GetItems(player.inventory.containerBelt).ToArray();
                    containerMain = GetItems(player.inventory.containerMain).ToArray();
                    containerWear = GetItems(player.inventory.containerWear).ToArray();
                }

                private IEnumerable<ItemData> GetItems(ItemContainer container)
                {
                    return container.itemList.Select(item => SerializeItem(item));
                }

                private float[] GetStats(BasePlayer player) => new float[] { player.health, player.metabolism.hydration.value, player.metabolism.calories.value };

                internal void SetStats(BasePlayer player)
                {
                    player.health = stats[0];
                    player.metabolism.hydration.value = stats[1];
                    player.metabolism.calories.value = stats[2];
                    player.metabolism.SendChangesToClient();
                }
            }
            private enum Container { Belt, Main, Wear }
        }
        #endregion

        #region Serialized Items
        internal static Item CreateItem(ItemData itemData)
        {
            Item item = ItemManager.CreateByItemID(itemData.itemid, itemData.amount, itemData.skin);
            item.condition = itemData.condition;
            item.maxCondition = itemData.maxCondition;

            if (itemData.frequency > 0)
            {
                ItemModRFListener rfListener = item.info.GetComponentInChildren<ItemModRFListener>();
                if (rfListener != null)
                {
                    PagerEntity pagerEntity = BaseNetworkable.serverEntities.Find(item.instanceData.subEntity) as PagerEntity;
                    if (pagerEntity != null)
                    {
                        pagerEntity.ChangeFrequency(itemData.frequency);
                        item.MarkDirty();
                    }
                }
            }

            if (itemData.instanceData?.IsValid() ?? false)
                itemData.instanceData.Restore(item);

            BaseProjectile weapon = item.GetHeldEntity() as BaseProjectile;
            if (weapon != null)
            {
                if (!string.IsNullOrEmpty(itemData.ammotype))
                    weapon.primaryMagazine.ammoType = ItemManager.FindItemDefinition(itemData.ammotype);
                weapon.primaryMagazine.contents = itemData.ammo;
            }

            FlameThrower flameThrower = item.GetHeldEntity() as FlameThrower;
            if (flameThrower != null)
                flameThrower.ammo = itemData.ammo;

            if (itemData.contents != null)
            {
                foreach (ItemData contentData in itemData.contents)
                {
                    Item newContent = ItemManager.CreateByItemID(contentData.itemid, contentData.amount);
                    if (newContent != null)
                    {
                        newContent.condition = contentData.condition;
                        newContent.MoveToContainer(item.contents);
                    }
                }
            }
            return item;
        }

        internal static ItemData SerializeItem(Item item)
        {
            return new ItemData
            {
                itemid = item.info.itemid,
                amount = item.amount,
                ammo = item.GetHeldEntity() is BaseProjectile ? (item.GetHeldEntity() as BaseProjectile).primaryMagazine.contents :
                               item.GetHeldEntity() is FlameThrower ? (item.GetHeldEntity() as FlameThrower).ammo : 0,
                ammotype = (item.GetHeldEntity() as BaseProjectile)?.primaryMagazine.ammoType.shortname ?? null,
                position = item.position,
                skin = item.skin,
                condition = item.condition,
                maxCondition = item.maxCondition,
                frequency = ItemModAssociatedEntity<PagerEntity>.GetAssociatedEntity(item)?.GetFrequency() ?? -1,
                instanceData = new ItemData.InstanceData(item),
                contents = item.contents?.itemList.Select(item1 => new ItemData
                {
                    itemid = item1.info.itemid,
                    amount = item1.amount,
                    condition = item1.condition
                }).ToArray()
            };
        }

        public class ItemData
        {
            public int itemid;
            public ulong skin;
            public int amount;
            public float condition;
            public float maxCondition;
            public int ammo;
            public string ammotype;
            public int position;
            public int frequency;
            public InstanceData instanceData;
            public ItemData[] contents;

            public class InstanceData
            {
                public int dataInt;
                public int blueprintTarget;
                public int blueprintAmount;
                public uint subEntity;

                public InstanceData() { }
                public InstanceData(Item item)
                {
                    if (item.instanceData == null)
                        return;

                    dataInt = item.instanceData.dataInt;
                    blueprintAmount = item.instanceData.blueprintAmount;
                    blueprintTarget = item.instanceData.blueprintTarget;
                }

                public void Restore(Item item)
                {
                    if (item.instanceData == null)
                        item.instanceData = new ProtoBuf.Item.InstanceData();

                    item.instanceData.ShouldPool = false;

                    item.instanceData.blueprintAmount = blueprintAmount;
                    item.instanceData.blueprintTarget = blueprintTarget;
                    item.instanceData.dataInt = dataInt;

                    item.MarkDirty();
                }

                public bool IsValid()
                {
                    return dataInt != 0 || blueprintAmount != 0 || blueprintTarget != 0;
                }
            }
        }
        #endregion
        #endregion

        #region Localization
        public static string Message(string key, ulong playerId = 0U) => Instance.lang.GetMessage(key, Instance, playerId != 0U ? playerId.ToString() : null);

        private readonly Dictionary<string, string> Messages = new Dictionary<string, string>
        {
            ["Notification.NotEnoughToContinue"] = "There are not enough players to continue the event...",
            ["Notification.NotEnoughToStart"] = "There is not enough players to start the event...",
            ["Notification.EventOpen"] = "The event <color=#007acc>{0}</color> (<color=#007acc>{1}</color>) is open for players\nIt will start in <color=#007acc>{2} seconds</color>\nType <color=#007acc>/event</color> to join",
            ["Notification.EventClosed"] = "The event has been closed to new players",
            ["Notification.EventFinished"] = "The event has finished",
            ["Notification.MaximumPlayers"] = "The event is already at maximum capacity",
            ["Notification.PlayerJoined"] = "<color=#007acc>{0}</color> has joined the <color=#007acc>{1}</color> event!",
            ["Notification.PlayerLeft"] = "<color=#007acc>{0}</color> has left the <color=#007acc>{1}</color> event!",
            ["Notification.RoundStartsIn"] = "Round starts in",
            ["Notification.EventWin"] = "<color=#007acc>{0}</color> won the event!",
            ["Notification.EventWin.Multiple"] = "The following players won the event; <color=#007acc>{0}</color>",
            ["Notification.EventWin.Multiple.Team"] = "<color={0}>Team {1}</color> won the event (<color=#007acc>{2}</color>)",
            ["Notification.Teams.Unbalanced"] = "The teams are unbalanced. Shuffling players...",
            ["Notification.Teams.TeamChanged"] = "You were moved to team <color=#007acc>{0}</color>",
            ["Notification.OutOfBounds"] = "You are out of the playable area. <color=#007acc>Return immediately</color> or you will be killed!",
            ["Notification.OutOfBounds.Time"] = "You have <color=#007acc>{0} seconds</color> to return...",
            ["Notification.Death.Suicide"] = "<color=#007acc>{0}</color> killed themselves...",
            ["Notification.Death.OOB"] = "<color=#007acc>{0}</color> tried to run away...",
            ["Notification.Death.Killed"] = "<color=#007acc>{0}</color> was killed by <color=#007acc>{1}</color>",
            ["Notification.Suvival.Remain"] = "(<color=#007acc>{0}</color> players remain)",
            ["Notification.SpectateCycle"] = "Press <color=#007acc>JUMP</color> to cycle spectate targets",
            ["Info.Event.Current"] = "Current Event: {0} ({1})",
            ["Info.Event.Players"] = "\n{0} / {1} Players",
            ["Info.Event.Status"] = "Status : {0}",
            ["UI.SelectClass"] = "Select a class to continue...",
            ["UI.Death.Killed"] = "You were killed by {0}",
            ["UI.Death.Suicide"] = "You are dead...",
            ["UI.Death.OOB"] = "Don't wander off...",            
            ["Error.CommandBlacklisted"] = "You can not run that command whilst playing an event",
        };
        #endregion
    }

    namespace EventManagerEx
    {
        public interface IEventPlugin
        {            
            bool InitializeEvent(EventManager.EventConfig config);

            void FormatScoreEntry(EventManager.ScoreEntry scoreEntry, ulong langUserId, out string score1, out string score2);

            List<EventManager.EventParameter> AdditionalParameters { get; }

            string ParameterIsValid(string fieldName, object value);

            bool CanUseClassSelector { get; }

            bool RequireTimeLimit { get; }

            bool RequireScoreLimit { get; }

            bool UseScoreLimit { get; }

            bool UseTimeLimit { get; }

            bool IsTeamEvent { get; }
        }
    }
}

