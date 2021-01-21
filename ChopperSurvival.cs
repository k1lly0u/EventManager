// Requires: EventManager
using Newtonsoft.Json;
using Oxide.Game.Rust.Cui;
using Oxide.Plugins.EventManagerEx;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UI = Oxide.Plugins.EMInterface.UI;
using UI4 = Oxide.Plugins.EMInterface.UI4;

namespace Oxide.Plugins
{
    [Info("ChopperSurvival", "k1lly0u", "3.0.0"), Description("Chopper survival event mode for EventManager")]
    class ChopperSurvival : RustPlugin, IEventPlugin
    {
        private const string HELICOPTER_PREFAB = "assets/prefabs/npc/patrol helicopter/patrolhelicopter.prefab";

        #region Oxide Hooks
        private void OnServerInitialized()
        {
            EventManager.RegisterEvent(Title, this);

            GetMessage = Message;
        }

        protected override void LoadDefaultMessages() => lang.RegisterMessages(Messages, this);

        private void OnEntityTakeDamage(BaseHelicopter baseHelicopter, HitInfo hitInfo)
        {
            if (baseHelicopter == null || hitInfo == null)
                return;

            if (baseHelicopter.GetComponent<EventHelicopter>())
            {
                if (EventManager.GetUser(hitInfo.InitiatorPlayer) == null)
                    EventManager.ClearDamage(hitInfo);
            }            
        }

        private void Unload()
        {
            EventHelicopter[] eventHelicopters = UnityEngine.Object.FindObjectsOfType<EventHelicopter>();
            for (int i = 0; i < eventHelicopters.Length; i++)
                UnityEngine.Object.Destroy(eventHelicopters[i]);

            if (!EventManager.IsUnloading)
                EventManager.UnregisterEvent(Title);

            Configuration = null;
        }
        #endregion

        #region Event Checks
        public bool InitializeEvent(EventManager.EventConfig config) => EventManager.InitializeEvent<ChopperSurvivalEvent>(this, config);

        public bool CanUseClassSelector => true;

        public bool RequireTimeLimit => false;

        public bool RequireScoreLimit => false;

        public bool UseScoreLimit => false;

        public bool UseTimeLimit => false;

        public bool IsTeamEvent => false;

        public void FormatScoreEntry(EventManager.ScoreEntry scoreEntry, ulong langUserId, out string score1, out string score2)
        {
            score1 = string.Empty;
            score2 = string.Format(Message("Score.HitPoints", langUserId), scoreEntry.value1);
        }

        public List<EventManager.EventParameter> AdditionalParameters { get; } = new List<EventManager.EventParameter>
        {
            new EventManager.EventParameter
            {
                DataType = "int",
                Field = "playerLives",
                Input = EventManager.EventParameter.InputType.InputField,                
                IsRequired = true,
                DefaultValue = 1,
                Name = "Player Lives"
            },
            new EventManager.EventParameter
            {
                DataType = "int",
                Field = "rounds",
                Input = EventManager.EventParameter.InputType.InputField,
                IsRequired = true,
                DefaultValue = 5,
                Name = "Rounds"
            },
            new EventManager.EventParameter
            {
                DataType = "int",
                Field = "maxHelicopters",
                Input = EventManager.EventParameter.InputType.InputField,
                IsRequired = true,
                DefaultValue = 3,
                Name = "Maximum Helicopters"
            },
            new EventManager.EventParameter
            {
                DataType = "float",
                Field = "damageScaler",
                Input = EventManager.EventParameter.InputType.InputField,
                IsRequired = true,
                DefaultValue = 1.0f,
                Name = "Damage Scale"
            },
            new EventManager.EventParameter
            {
                DataType = "int",
                Field = "heliHealth",
                Input = EventManager.EventParameter.InputType.InputField,
                IsRequired = true,
                DefaultValue = 5000,
                Name = "Heli Health"
            }
        };

        public string ParameterIsValid(string fieldName, object value) => null;
        #endregion

        #region Functions
        private static string ToOrdinal(int i) => (i + "th").Replace("1th", "1st").Replace("2th", "2nd").Replace("3th", "3rd");
        #endregion

        #region Event Classes
        public class ChopperSurvivalEvent : EventManager.BaseEventGame
        {
            public List<EventManager.BaseEventPlayer> winners;

            private int playerLives;

            private int rounds;

            private int maxHelicopters;

            private int heliHealth;

            private float damageScaler;

            private int currentRound = 0;

            private List<EventHelicopter> eventHelicopters;

            private Color COLOR_GREEN = new Color(0.0745098039215f, 0.79215686274509f, 0.329411764705882f);
            private Color COLOR_RED = new Color(0.79215686274509f, 0.2588235294117647f, 0.074509803921568f);

            internal override void InitializeEvent(IEventPlugin plugin, EventManager.EventConfig config)
            {
                playerLives = config.GetParameter<int>("playerLives");
                rounds = config.GetParameter<int>("rounds");
                maxHelicopters = config.GetParameter<int>("maxHelicopters");
                heliHealth = config.GetParameter<int>("heliHealth");
                damageScaler = config.GetParameter<float>("damageScaler");

                eventHelicopters = Facepunch.Pool.GetList<EventHelicopter>();
                winners = Facepunch.Pool.GetList<EventManager.BaseEventPlayer>();

                base.InitializeEvent(plugin, config);
            }

            protected override void OnDestroy()
            {
                for (int i = eventHelicopters.Count - 1; i >= 0; i--)
                    Destroy(eventHelicopters[i]);

                Facepunch.Pool.FreeList(ref eventHelicopters);
                Facepunch.Pool.FreeList(ref winners);

                base.OnDestroy();
            }

            internal override void PrestartEvent()
            {
                CloseEvent();
                base.PrestartEvent();
            }

            protected override void StartEvent()
            {
                base.StartEvent();

                GodmodeEnabled = true;

                InvokeHandler.Invoke(this, StartRound, Configuration.TimeBetweenRounds);

                BroadcastToPlayers(GetMessage, "Notification.RoundStartsIn", Configuration.TimeBetweenRounds);

                InvokeHandler.InvokeRepeating(this, UpdateScoreboard, 1f, 1f);
            }

            internal override void EndEvent()
            {
                InvokeHandler.CancelInvoke(this, UpdateScoreboard);
                
                base.EndEvent();
            }

            protected override EventManager.BaseEventPlayer AddPlayerComponent(BasePlayer player)
            {
                ChopperSurvivalPlayer eventPlayer = player.gameObject.AddComponent<ChopperSurvivalPlayer>();
                eventPlayer.LivesRemaining = playerLives;
                return eventPlayer;
            }

            protected override void OnPlayerSpawned(EventManager.BaseEventPlayer eventPlayer)
            {
                if (Status == EventManager.EventStatus.Started)
                    BroadcastToPlayer(eventPlayer, string.Format(GetMessage("Notification.LivesRemaining", eventPlayer.Player.userID), (eventPlayer as ChopperSurvivalPlayer).LivesRemaining));
            }

            internal override bool CanDealEntityDamage(EventManager.BaseEventPlayer attacker, BaseEntity entity, HitInfo hitInfo)
            {
                EventHelicopter eventHelicopter = entity.GetComponent<EventHelicopter>();
                if (eventHelicopter == null)
                    return false;

                if (damageScaler != 1f)
                    hitInfo.damageTypes.ScaleAll(damageScaler);

                int hitPoints;
                if (!eventHelicopter.DealDamage(hitInfo, out hitPoints))
                    EventManager.ClearDamage(hitInfo);

                (attacker as ChopperSurvivalPlayer).HitPoints += hitPoints;

                return true;
            }

            internal void OnHelicopterKilled(EventHelicopter eventHelicopter)
            {
                eventHelicopters.Remove(eventHelicopter);

                if (eventHelicopters.Count == 0)
                    EndRound();
            }

            internal override void OnPlayerTakeDamage(EventManager.BaseEventPlayer eventPlayer, HitInfo hitInfo)
            {
                EventManager.BaseEventPlayer attacker = EventManager.GetUser(hitInfo.InitiatorPlayer);
                if (attacker != null)
                {
                    EventManager.ClearDamage(hitInfo);
                    return;
                }

                base.OnPlayerTakeDamage(eventPlayer, hitInfo);
            }

            internal override void OnEventPlayerDeath(EventManager.BaseEventPlayer victim, EventManager.BaseEventPlayer attacker = null, HitInfo info = null)
            {
                if (victim == null)
                    return;

                (victim as ChopperSurvivalPlayer).LivesRemaining -= 1;

                if (GetPlayersRemainingCount() == 0)
                {
                    victim.AddPlayerDeath(null);

                    InvokeHandler.Invoke(this, EndEvent, 0.1f);
                    return;
                }

                victim.OnPlayerDeath(attacker, Configuration.RespawnTime);

                base.OnEventPlayerDeath(victim, attacker);
            }
            
            protected override void DisplayKillToChat(EventManager.BaseEventPlayer victim, string attackerName)
            {
                if (victim.IsOutOfBounds)
                    BroadcastToPlayers(GetMessage, "Notification.Death.OOB", victim.Player.displayName);
                else BroadcastToPlayers(GetMessage, "Notification.Death.Killed", victim.Player.displayName);
            }

            private int GetPlayersRemainingCount()
            {
                int count = 0;

                for (int i = 0; i < eventPlayers.Count; i++)
                {
                    if ((eventPlayers[i] as ChopperSurvivalPlayer).LivesRemaining > 0)
                        count++;
                }

                return count;
            }

            protected override void GetWinningPlayers(ref List<EventManager.BaseEventPlayer> winners)
            {
                winners.AddRange(this.winners);
            }

            #region Round Management
            private void StartRound()
            {
                GodmodeEnabled = false;

                currentRound += 1;

                StartCoroutine(SpawnRoundHelicopters());
            }

            private void EndRound()
            {
                GodmodeEnabled = true;

                if (currentRound >= rounds)
                {
                    winners.AddRange(eventPlayers);
                    InvokeHandler.Invoke(this, EndEvent, 0.1f);
                }
                else
                {
                    InvokeHandler.Invoke(this, StartRound, Configuration.TimeBetweenRounds);
                    BroadcastToPlayers(GetMessage, "Notification.RoundStartsIn", Configuration.TimeBetweenRounds);

                    StartCoroutine(ResetPlayers());
                }
            }

            private IEnumerator ResetPlayers()
            {
                List<EventManager.BaseEventPlayer> currentPlayers = Facepunch.Pool.GetList<EventManager.BaseEventPlayer>();
                currentPlayers.AddRange(eventPlayers);

                for (int i = 0; i < currentPlayers.Count; i++)
                {
                    EventManager.BaseEventPlayer eventPlayer = currentPlayers[i];
                    if (eventPlayer != null)
                    {
                        if (eventPlayer.IsDead)
                            EventManager.ResetPlayer(eventPlayer.Player);
                        else
                        {
                            EventManager.StripInventory(eventPlayer.Player);
                            EventManager.ResetMetabolism(eventPlayer.Player);
                            EventManager.GiveKit(eventPlayer.Player, eventPlayer.Kit);
                        }
                    }

                    yield return CoroutineEx.waitForEndOfFrame;
                    yield return CoroutineEx.waitForEndOfFrame;
                }

                Facepunch.Pool.FreeList(ref currentPlayers);
            }
            #endregion

            #region Spawn Helicopters
            private IEnumerator SpawnRoundHelicopters()
            {
                int helicoptersToSpawn = Mathf.Max(1, Mathf.RoundToInt((float)maxHelicopters * ((float)currentRound / (float)rounds)));

                for (int i = 0; i < helicoptersToSpawn; i++)
                {
                    Vector3 destination = _spawnSelectorA.GetSpawnPoint();
                    Vector2 random = (UnityEngine.Random.insideUnitCircle.normalized * Configuration.MaxTravelDistance);

                    Vector3 position = destination + new Vector3(random.x, 50f, random.y);

                    BaseHelicopter baseHelicopter = GameManager.server.CreateEntity(HELICOPTER_PREFAB, position) as BaseHelicopter;
                    baseHelicopter.enableSaving = false;
                    baseHelicopter.Spawn();

                    EventHelicopter eventHelicopter = baseHelicopter.gameObject.AddComponent<EventHelicopter>();
                    eventHelicopter.OnHelicopterSpawned(this, i + 1);

                    eventHelicopter.Entity.health = heliHealth;
                    eventHelicopter.Entity.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);

                    eventHelicopters.Add(eventHelicopter);

                    yield return CoroutineEx.waitForSeconds(1f);

                    eventHelicopter.SetPositionDestination(position, destination);
                }
            }
            #endregion

            #region Scoreboards
            protected override void BuildScoreboard()
            {
                scoreContainer = EMInterface.CreateScoreboardBase(this);

                int index = -1;

                EMInterface.CreatePanelEntry(scoreContainer, string.Format(GetMessage("Score.RoundNumber", 0UL), currentRound, rounds), index += 1);

                for (int i = 0; i < eventHelicopters.Count; i++)
                {
                    EventHelicopter eventHelicopter = eventHelicopters[i];

                    CreateHealthBar(scoreContainer, string.Format(GetMessage("Score.Heli", 0UL), eventHelicopter.ID), eventHelicopter.Entity.health / heliHealth, index += 1);
                }

                EMInterface.CreatePanelEntry(scoreContainer, string.Format(GetMessage("Score.Remaining", 0UL), eventPlayers.Count), index += 1);
                
                for (int i = 0; i < Mathf.Min(scoreData.Count, 15); i++)
                {
                    EventManager.ScoreEntry score = scoreData[i];
                    EMInterface.CreateScoreEntry(scoreContainer, $"{score.displayName} | ({score.value1} pts)", string.Empty, string.Empty, i + index + 1);
                }
            }

            private void CreateHealthBar(CuiElementContainer container, string text, float health, int index)
            {
                float yMax = -(1f * index);
                float yMin = -(1f * (index + 1));

                UI.Panel(container, EMInterface.UI_SCORES, EMInterface.Configuration.Scoreboard.Foreground.Get, new UI4(0f, yMin + 0.02f, 1f, yMax - 0.02f));

                UI.Label(container, EMInterface.UI_SCORES, text, 11, new UI4(0.05f, yMin, 1f, yMax), TextAnchor.MiddleLeft);

                UI.Panel(container, EMInterface.UI_SCORES, GetInterpolatedColor(health, 0.75f), new UI4(0.25f, yMin + 0.05f, 0.25f + (0.74f * health), yMax - 0.05f));
            }

            private string GetInterpolatedColor(float delta, float alpha)
            {
                Color col = Color.Lerp(COLOR_RED, COLOR_GREEN, delta);
                return $"{col.r} {col.g} {col.b} {alpha}";
            }

            protected override float GetFirstScoreValue(EventManager.BaseEventPlayer eventPlayer) => (eventPlayer as ChopperSurvivalPlayer).HitPoints;

            protected override float GetSecondScoreValue(EventManager.BaseEventPlayer eventPlayer) => (eventPlayer as ChopperSurvivalPlayer).LivesRemaining;

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
                list.Add(new KeyValuePair<string, object>(GetMessage("UI.Rounds", playerId), rounds));
                list.Add(new KeyValuePair<string, object>(GetMessage("UI.PlayerLives", playerId), playerLives));
                list.Add(new KeyValuePair<string, object>(GetMessage("UI.MaxHelicopters", playerId), maxHelicopters));
                list.Add(new KeyValuePair<string, object>(GetMessage("UI.DamageScaler", playerId), damageScaler));
            }
        }

        private class ChopperSurvivalPlayer : EventManager.BaseEventPlayer
        {
            internal int LivesRemaining { get; set; }

            internal int HitPoints { get; set; }

            internal override void OnPlayerDeath(EventManager.BaseEventPlayer attacker = null, float respawnTime = 5)
            {
                AddPlayerDeath();

                DestroyUI();

                string message = string.Empty;

                if (LivesRemaining <= 0)
                {
                    int position = Event.GetAlivePlayerCount();

                    message = IsOutOfBounds ? string.Format(GetMessage("UI.Death.OOB.Kicked", Player.userID), ToOrdinal(position + 1), position) :
                              string.Format(GetMessage("UI.Death.Killed.Kicked", Player.userID), ToOrdinal(position + 1), position);
                }
                else
                {
                    _respawnDurationRemaining = respawnTime;

                    InvokeHandler.InvokeRepeating(this, RespawnTick, 1f, 1f);

                    message = IsOutOfBounds ? GetMessage("UI.Death.OOB", Player.userID) :
                              GetMessage("UI.Death.Killed", Player.userID);
                }

                EMInterface.DisplayDeathScreen(this, message, LivesRemaining > 0);
            }
        }

        internal class EventHelicopter : MonoBehaviour
        {
            internal BaseHelicopter Entity { get; private set; }

            internal PatrolHelicopterAI AI { get; private set; }

            internal ChopperSurvivalEvent Event { get; private set; }

            internal int ID { get; private set; }

            private Transform tr;

            private Vector3 centerDestination;

            private RaycastHit raycastHit;

            private uint tailRotorBone;
            private uint mainRotorBone;

            private List<PatrolHelicopterAI.targetinfo> _targets;

            private const string HELIEXPLOSION_EFFECT = "assets/prefabs/npc/patrol helicopter/effects/heli_explosion.prefab";

            private void Awake()
            {
                Entity = GetComponent<BaseHelicopter>();
                AI = Entity.myAI;

                tr = Entity.transform;
                AI.enabled = false;

                _targets = Facepunch.Pool.GetList<PatrolHelicopterAI.targetinfo>();

                tailRotorBone = StringPool.Get("tail_rotor_col");
                mainRotorBone = StringPool.Get("main_rotor_col");
            }

            internal void OnHelicopterSpawned(ChopperSurvivalEvent chopperSurvivalEvent, int id)
            {
                this.Event = chopperSurvivalEvent;
                this.ID = id;
            }

            private void Update()
            {
                if (AI.isDead)
                {
                    KillHelicopter();
                    return;
                }

                if (Vector3Ex.Distance2D(tr.position, centerDestination) > Configuration.MaxTravelDistance)
                    AI.SetTargetDestination(centerDestination);

                UpdateTargetList();

                AI.MoveToDestination();
                AI.UpdateRotation();
                AI.UpdateSpotlight();
                AI.AIThink();
                AI.DoMachineGuns();                
            }

            private void OnDestroy()
            {
                Facepunch.Pool.FreeList(ref _targets);

                if (Entity != null && !Entity.IsDestroyed)
                    Entity.Kill(BaseNetworkable.DestroyMode.None);
            }

            internal void SetPositionDestination(Vector3 position, Vector3 destination)
            {
                tr.position = position;
                centerDestination = destination;

                AI.SetTargetDestination(destination);
            }

            private void UpdateTargetList()
            {
                Vector3 strafePos = Vector3.zero;
                bool isStrafing = false;
                bool shouldUseNapalm = false;
                for (int i = _targets.Count - 1; i >= 0; i--)
                {
                    PatrolHelicopterAI.targetinfo targetinfo = _targets[i];

                    if (targetinfo == null || targetinfo.ent == null)
                        _targets.Remove(targetinfo);                    
                    else
                    {
                        if (Time.realtimeSinceStartup > targetinfo.nextLOSCheck)
                        {
                            targetinfo.nextLOSCheck = Time.realtimeSinceStartup + 1f;
                            if (PlayerVisible(targetinfo.ply))
                            {
                                targetinfo.lastSeenTime = Time.realtimeSinceStartup;
                                targetinfo.visibleFor += 1f;
                            }
                            else targetinfo.visibleFor = 0f;                            
                        }

                        bool isDead = targetinfo.ply ? targetinfo.ply.IsDead() : (targetinfo.ent.Health() <= 0f);

                        if (targetinfo.TimeSinceSeen() >= 6f || isDead)
                        {
                            if ((CanStrafe() || CanUseNapalm()) && AI.IsAlive() && !isStrafing && !isDead && (targetinfo.ply == AI.leftGun._target || targetinfo.ply == AI.rightGun._target))
                            {
                                shouldUseNapalm = (!ValidStrafeTarget(targetinfo.ply) || UnityEngine.Random.Range(0f, 1f) > 0.75f);                                
                                strafePos = targetinfo.ply.transform.position;
                                isStrafing = true;
                            }

                            _targets.Remove(targetinfo);
                        }
                    }
                }

                foreach (EventManager.BaseEventPlayer eventPlayer in Event.eventPlayers)
                {
                    BasePlayer player = eventPlayer.Player;

                    if (Vector3Ex.Distance2D(tr.position, player.transform.position) <= 150f)
                    {
                        bool isCurrentTarget = false;
                        for (int i = 0; i < _targets.Count; i++)
                        {
                            PatrolHelicopterAI.targetinfo targetInfo = _targets[i];

                            if (targetInfo.ply == player)
                            {
                                isCurrentTarget = true;
                                break;
                            }
                        }
                       
                        if (!isCurrentTarget && PlayerVisible(player))
                        {
                            _targets.Add(new PatrolHelicopterAI.targetinfo(player, player));
                        }
                    }
                }

                if (isStrafing)
                {
                    AI.ExitCurrentState();
                    AI.State_Strafe_Enter(strafePos, shouldUseNapalm);
                }

                AI._targetList.Clear();
                AI._targetList.AddRange(_targets);
            }

            private bool PlayerVisible(BasePlayer player)
            {                             
                Vector3 targetPosition = player.eyes.position;
                
                Vector3 position = AI.transform.position - (Vector3.up * 6f);                
                Vector3 direction = (targetPosition - position).normalized;
                float maxDistance = Vector3.Distance(targetPosition, position);

                if (GamePhysics.Trace(new Ray(position + (direction * 5f), direction), 0f, out raycastHit, maxDistance * 1.1f, 1218652417, QueryTriggerInteraction.UseGlobal) && raycastHit.collider.gameObject.ToBaseEntity() == player)                
                    return true;                
                return false;
            }

            private bool ValidStrafeTarget(BasePlayer player)
            {                
                return !player.IsNearEnemyBase();
            }

            private bool CanStrafe()
            {                
                if (Time.realtimeSinceStartup - AI.lastStrafeTime < 20f)
                    return false;                
                return AI.CanInterruptState();
            }

            private bool CanUseNapalm()
            {                
                return Time.realtimeSinceStartup - AI.lastNapalmTime >= 30f;
            }

            internal bool DealDamage(HitInfo hitInfo, out int hitPoints)
            {
                hitPoints = hitInfo.HitBone == mainRotorBone || hitInfo.HitBone == tailRotorBone ? Configuration.RotorHitPoints : Configuration.HeliHitPoints;

                float totalDamage = hitInfo.damageTypes.Total();
                if (totalDamage >= Entity.health)
                {
                    KillHelicopter();
                    return false;
                }

                return true;
            }

            private void KillHelicopter()
            {
                Event.OnHelicopterKilled(this);
                Effect.server.Run(HELIEXPLOSION_EFFECT, tr.position);
                Destroy(this);
            }
        }
        #endregion

        #region Config        
        private static ConfigData Configuration;

        private class ConfigData
        {
            [JsonProperty(PropertyName = "Respawn time (seconds)")]
            public int RespawnTime { get; set; }

            [JsonProperty(PropertyName = "Maximum distance helicopters can travel away from the arena")]
            public float MaxTravelDistance { get; set; }

            [JsonProperty(PropertyName = "Amount of points given to players when they shoot a rotor")]
            public int RotorHitPoints { get; set; }

            [JsonProperty(PropertyName = "Amount of points given to players when they shoot the heli")]
            public int HeliHitPoints { get; set; }

            [JsonProperty(PropertyName = "Amount of time between rounds (seconds)")]
            public int TimeBetweenRounds { get; set; }

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
                RespawnTime = 5,
                MaxTravelDistance = 200f,
                RotorHitPoints = 10,
                HeliHitPoints = 1,
                TimeBetweenRounds = 10,
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

        #region Localization
        public string Message(string key, ulong playerId = 0U) => lang.GetMessage(key, this, playerId != 0U ? playerId.ToString() : null);

        private static Func<string, ulong, string> GetMessage;

        private readonly Dictionary<string, string> Messages = new Dictionary<string, string>
        {
            ["Score.HitPoints"] = "Hit Points: {0}",
            ["Score.Name"] = "Hit Points",
            ["Score.RoundNumber"] = "Round {0} / {1}",
            ["Score.HelisRemaining"] = "Helicopters Remaining : {0}",
            ["Score.Remaining"] = "Players Remaining : {0}",
            ["Score.Heli"] = "Heli {0}",

            ["Notification.LivesRemaining"] = "You have {0} lives remaining!",
            ["Notification.Death.OOB"] = "<color=#007acc>{0}</color> tried to run away...",
            ["Notification.Death.Killed"] = "<color=#007acc>{0}</color> has been killed",

            ["UI.Death.Killed.Kicked"] = "You was killed...\nYou placed {0}\n{1} players remain",
            ["UI.Death.OOB.Kicked"] = "You left the playable area\nYou placed {0}\n{1} players remain",

            ["UI.Death.Killed"] = "You was killed...",
            ["UI.Death.OOB"] = "You left the playable area",

            ["UI.Rounds"] = "Rounds",
            ["UI.PlayerLives"] = "Player Lives",
            ["UI.MaxHelicopters"] = "Maximum Helicopters",
            ["UI.DamageScaler"] = "Damage Scale",
            ["UI.HelicoptersRemaining"] = "Helicopters Remaining : {0}",

            ["Notification.RoundStartsIn"] = "Next round starts in <color=#007acc>{0}</color> seconds"
        };
        #endregion
    }
}
