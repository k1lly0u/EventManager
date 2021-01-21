// Requires: EventManager
using Newtonsoft.Json;
using Oxide.Plugins.EventManagerEx;
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("CaptureTheFlag", "k1lly0u", "0.4.0"), Description("Capture the flag event mode for EventManager")]
    class CaptureTheFlag : RustPlugin, IEventPlugin
    {
        #region Oxide Hooks
        private void OnServerInitialized()
        {      
            EventManager.RegisterEvent(Title, this);

            GetMessage = Message;
        }

        protected override void LoadDefaultMessages() => lang.RegisterMessages(Messages, this);

        private object CanUpdateSign(BasePlayer player, Signage sign) => sign.GetComponentInParent<FlagController>() != null ? (object)true : null;

        private void Unload()
        {
            if (!EventManager.IsUnloading)
                EventManager.UnregisterEvent(Title);

            Configuration = null;
        }
        #endregion

        #region Event Checks
        public bool InitializeEvent(EventManager.EventConfig config) => EventManager.InitializeEvent<CaptureTheFlagEvent>(this, config);

        public bool CanUseClassSelector => true;

        public bool RequireTimeLimit => true;

        public bool RequireScoreLimit => false;

        public bool UseScoreLimit => true;

        public bool UseTimeLimit => true;

        public bool IsTeamEvent => true;

        public void FormatScoreEntry(EventManager.ScoreEntry scoreEntry, ulong langUserId, out string score1, out string score2)
        {
            score1 = string.Format(Message("Score.FlagCaptures", langUserId), scoreEntry.value1);
            score2 = string.Format(Message("Score.Kills", langUserId), scoreEntry.value2);
        }

        public List<EventManager.EventParameter> AdditionalParameters { get; } = new List<EventManager.EventParameter>
        {            
            new EventManager.EventParameter
            {
                DataType = "int",
                Field = "flagRespawnTimer",
                Input = EventManager.EventParameter.InputType.InputField,               
                IsRequired = true,
                Name = "Flag Reset Time",
                DefaultValue = 30
            },
        };

        public string ParameterIsValid(string fieldName, object value) => null;
        #endregion

        #region Event Classes
        public class CaptureTheFlagEvent : EventManager.BaseEventGame
        {
            public EventManager.Team winningTeam;

            internal int flagRespawnTime;

            private int teamAScore;
            private int teamBScore;
            
            private EventManager.Team lastTeam = EventManager.Team.B;

            internal FlagController TeamAFlag { get; private set; }

            internal FlagController TeamBFlag { get; private set; }

            internal override void InitializeEvent(IEventPlugin plugin, EventManager.EventConfig config)
            {
                flagRespawnTime = config.GetParameter<int>("flagRespawnTimer");

                base.InitializeEvent(plugin, config);

                TeamAFlag = FlagController.Create(this, EventManager.Team.A, _spawnSelectorA.ReserveSpawnPoint(0));
                TeamBFlag = FlagController.Create(this, EventManager.Team.B, _spawnSelectorB.ReserveSpawnPoint(0));
            }

            protected override void StartEvent()
            {
                BalanceTeams();
                base.StartEvent();
            }

            internal override void EndEvent()
            {
                TeamAFlag.DropFlag(false);
                TeamBFlag.DropFlag(false);
                
                base.EndEvent();
            }

            protected override void OnDestroy()
            {
                Destroy(TeamAFlag);
                Destroy(TeamBFlag);

                base.OnDestroy();
            }

            protected override EventManager.BaseEventPlayer AddPlayerComponent(BasePlayer player) => player.gameObject.AddComponent<CaptureTheFlagPlayer>();
             
            protected override EventManager.Team GetPlayerTeam(BasePlayer player) => lastTeam = lastTeam == EventManager.Team.B ? EventManager.Team.A : EventManager.Team.B;

            internal override int GetTeamScore(EventManager.Team team) => team == EventManager.Team.B ? teamBScore : teamAScore;

            internal override void OnEventPlayerDeath(EventManager.BaseEventPlayer victim, EventManager.BaseEventPlayer attacker = null, HitInfo info = null)
            {
                if (victim == null)
                    return;

                if ((victim as CaptureTheFlagPlayer).IsCarryingFlag)
                {
                    FlagController flagController = victim.Team == EventManager.Team.B ? TeamAFlag : TeamBFlag;
                    if (flagController.FlagHolder == victim)
                    {
                        flagController.DropFlag(true);
                        BroadcastToPlayers(GetMessage, "Notification.FlagDropped", victim.Player.displayName, flagController.Team, GetTeamColor(victim.Team), GetTeamColor(flagController.Team));
                    }
                }

                victim.OnPlayerDeath(attacker, Configuration.RespawnTime);

                if (attacker != null && victim != attacker && victim.Team != attacker.Team)                                  
                    attacker.OnKilledPlayer(info);

                UpdateScoreboard();
                base.OnEventPlayerDeath(victim, attacker);
            }

            protected override void GetWinningPlayers(ref List<EventManager.BaseEventPlayer> winners)
            {
                if (winningTeam != EventManager.Team.None)
                {
                    if (eventPlayers.Count > 0)
                    {
                        for (int i = 0; i < eventPlayers.Count; i++)
                        {
                            EventManager.BaseEventPlayer eventPlayer = eventPlayers[i];
                            if (eventPlayer == null)
                                continue;

                            if (eventPlayer.Team == winningTeam)
                                winners.Add(eventPlayer);
                        }
                    }
                }
            }

            internal void OnFlagCaptured(CaptureTheFlagPlayer eventPlayer, EventManager.Team team)
            {
                int flagCaptures;

                if (eventPlayer.Team == EventManager.Team.B)
                    flagCaptures = teamBScore += 1;
                else flagCaptures = teamAScore += 1;

                eventPlayer.FlagCaptures += 1;

                BroadcastToPlayers(GetMessage, "Notification.FlagCaptured", eventPlayer.Player.displayName, team, GetTeamColor(eventPlayer.Team), GetTeamColor(team));                

                UpdateScoreboard();

                if (flagCaptures >= Config.ScoreLimit)
                {
                    winningTeam = eventPlayer.Team;
                    InvokeHandler.Invoke(this, EndEvent, 0.1f);
                }
            }

            internal string GetTeamColor(EventManager.Team team) => team == EventManager.Team.B ? TeamBColor : TeamAColor;

            #region Scoreboards
            protected override void BuildScoreboard()
            {
                scoreContainer = EMInterface.CreateScoreboardBase(this);

                int index = -1;
                EMInterface.CreatePanelEntry(scoreContainer, string.Format(GetMessage("Score.Team", 0UL), teamAScore, TeamAColor, TeamBColor, teamBScore), index += 1);

                if (Config.ScoreLimit > 0)
                    EMInterface.CreatePanelEntry(scoreContainer, string.Format(GetMessage("Score.Limit", 0UL), Config.ScoreLimit), index += 1);

                EMInterface.CreateScoreEntry(scoreContainer, string.Empty, "C", "K", index += 1);

                for (int i = 0; i < Mathf.Min(scoreData.Count, 15); i++)
                {
                    EventManager.ScoreEntry score = scoreData[i];
                    EMInterface.CreateScoreEntry(scoreContainer, $"<color={(score.team == EventManager.Team.A ? TeamAColor : TeamBColor)}>{score.displayName}</color>", ((int)score.value1).ToString(), ((int)score.value2).ToString(), i + index + 1);
                }
            }

            protected override float GetFirstScoreValue(EventManager.BaseEventPlayer eventPlayer) => (eventPlayer as CaptureTheFlagPlayer).FlagCaptures;

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
        }

        internal class CaptureTheFlagPlayer : EventManager.BaseEventPlayer
        {
            public int FlagCaptures { get; set; }

            public bool IsCarryingFlag { get; set; }
        }

        internal class FlagController : MonoBehaviour
        {
            private Signage primary;
            private Signage secondary;

            private Transform tr;

            private Vector3 basePosition;
            private BoxCollider boxCollider;

            private uint signImageCRC = 0;

            private CaptureTheFlagEvent captureTheFlagEvent;

            internal EventManager.Team Team { get; set; }

            internal CaptureTheFlagPlayer FlagHolder { get; private set; }

            internal bool IsAtBase { get; private set; } = true;

            private const string SIGN_PREFAB = "assets/prefabs/deployable/signs/sign.post.single.prefab";

            private const float ROTATE_SPEED = 48f;

            internal static FlagController Create(CaptureTheFlagEvent captureTheFlagEvent, EventManager.Team team, Vector3 position)
            {                
                Signage signage = Spawn(position);
                FlagController flagController = signage.gameObject.AddComponent<FlagController>();

                flagController.captureTheFlagEvent = captureTheFlagEvent;
                flagController.Team = team;
                flagController.basePosition = position;

                return flagController;
            }

            private static Signage Spawn(Vector3 position)
            {
                Signage signage = GameManager.server.CreateEntity(SIGN_PREFAB, position) as Signage;
                signage.enableSaving = false;
                signage.Spawn();

                Destroy(signage.GetComponent<MeshCollider>());
                Destroy(signage.GetComponent<DestroyOnGroundMissing>());
                Destroy(signage.GetComponent<GroundWatch>());

                return signage;
            }

            private void Awake()
            {
                primary = GetComponent<Signage>();
                tr = primary.transform;
            }

            private void Start()
            {
                secondary = Spawn(tr.position);
                
                secondary.SetParent(primary);
                secondary.transform.localPosition = Vector3.zero;
                secondary.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

                SetSignImages(primary);
                SetSignImages(secondary);

                primary.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                secondary.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);

                primary.gameObject.layer = (int)Rust.Layer.Reserved1;

                boxCollider = primary.gameObject.AddComponent<BoxCollider>();
                boxCollider.size = new Vector3(1.2f, 2f, 1f);
                boxCollider.center = new Vector3(0f, 1.1f, 0f);
                boxCollider.isTrigger = true;
            }

            private void Update()
            {
                tr.RotateAround(tr.position, tr.up, Time.deltaTime * ROTATE_SPEED);
                primary.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
            }

            private void OnDestroy()
            {
                Destroy(boxCollider);

                if (secondary != null && !secondary.IsDestroyed)
                {
                    secondary.SetParent(null);
                    secondary.Kill(BaseNetworkable.DestroyMode.None);
                }

                if (primary != null && !primary.IsDestroyed)
                {
                    primary.SetParent(null);
                    primary.Kill();
                }
            }

            private void SetSignImages(Signage signage)
            {
                string hex = Team == EventManager.Team.B ? captureTheFlagEvent.TeamBColor : captureTheFlagEvent.TeamAColor;

                if (signImageCRC == 0)
                {
                    hex = hex.TrimStart('#');

                    int red = int.Parse(hex.Substring(0, 2), NumberStyles.AllowHexSpecifier);
                    int green = int.Parse(hex.Substring(2, 2), NumberStyles.AllowHexSpecifier);
                    int blue = int.Parse(hex.Substring(4, 2), NumberStyles.AllowHexSpecifier);

                    Color color = new Color((float)red / 255f, (float)green / 255f, (float)blue / 255f);

                    Color[] array = new Color[256 * 256];
                    for (int i = 0; i < array.Length; i++)
                        array[i] = color;

                    Texture2D texture2D = new Texture2D(256, 256);
                    texture2D.SetPixels(array);
                    byte[] bytes = texture2D.EncodeToPNG();

                    Destroy(texture2D);

                    signImageCRC = FileStorage.server.Store(bytes, FileStorage.Type.png, CommunityEntity.ServerInstance.net.ID);
                }

                Array.Resize<uint>(ref signage.textureIDs, 1);

                signage.textureIDs[0] = signImageCRC;
                signage.SetFlag(BaseEntity.Flags.Locked, true);
            }
            
            private void OnTriggerEnter(Collider col)
            {
                if (captureTheFlagEvent.Status != EventManager.EventStatus.Started)
                    return;

                CaptureTheFlagPlayer eventPlayer = col.GetComponent<CaptureTheFlagPlayer>();
                if (eventPlayer == null || eventPlayer.IsDead)
                    return;

                if (IsAtBase)
                {
                    if (eventPlayer.Team != Team)                    
                        PickupFlag(eventPlayer);                    
                    else
                    {
                        if (eventPlayer.IsCarryingFlag)
                        {
                            FlagController enemyFlag = Team == EventManager.Team.A ? captureTheFlagEvent.TeamBFlag : captureTheFlagEvent.TeamAFlag;
                            enemyFlag.CaptureFlag(eventPlayer);
                        }
                    }
                }
                else
                {
                    if (FlagHolder == null)
                    {
                        if (eventPlayer.Team != Team)                        
                            PickupFlag(eventPlayer);                        
                        else
                        {
                            ResetFlag();
                            captureTheFlagEvent.BroadcastToPlayers(GetMessage, "Notification.FlagReset", eventPlayer.Team, captureTheFlagEvent.GetTeamColor(eventPlayer.Team));
                        }
                    }
                }
            }

            private void PickupFlag(CaptureTheFlagPlayer eventPlayer)
            {
                FlagHolder = eventPlayer;
                eventPlayer.IsCarryingFlag = true;

                IsAtBase = false;
                InvokeHandler.CancelInvoke(this, DroppedTimeExpired);

                primary.SetParent(eventPlayer.Player);
                tr.localPosition = new Vector3(0f, 0.25f, -0.75f);

                captureTheFlagEvent.BroadcastToPlayers(GetMessage, "Notification.FlagPickedUp", eventPlayer.Player.displayName, Team, captureTheFlagEvent.GetTeamColor(eventPlayer.Team), captureTheFlagEvent.GetTeamColor(Team));
            }

            internal void DropFlag(bool resetToBase)
            {                
                primary.SetParent(null, true);

                if (FlagHolder != null)
                {
                    FlagHolder.IsCarryingFlag = false;
                    FlagHolder = null;
                }

                primary.UpdateNetworkGroup();
                primary.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);

                if (resetToBase)
                    InvokeHandler.Invoke(this, DroppedTimeExpired, captureTheFlagEvent.flagRespawnTime);
            }

            private void CaptureFlag(CaptureTheFlagPlayer eventPlayer)
            {
                ResetFlag();
                captureTheFlagEvent.OnFlagCaptured(eventPlayer, Team);                
            }

            private void DroppedTimeExpired()
            {
                captureTheFlagEvent.BroadcastToPlayers(GetMessage, "Notification.FlagReset", Team, captureTheFlagEvent.GetTeamColor(Team));
                ResetFlag();
            }

            private void ResetFlag()
            {
                if (FlagHolder != null)
                {
                    FlagHolder.IsCarryingFlag = false;
                    FlagHolder = null;
                }

                InvokeHandler.CancelInvoke(this, DroppedTimeExpired);

                primary.SetParent(null);

                tr.position = basePosition;

                primary.UpdateNetworkGroup();
                primary.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);

                IsAtBase = true;
            }
        }
        #endregion

        #region Config        
        private static ConfigData Configuration;

        private class ConfigData
        {
            [JsonProperty(PropertyName = "Respawn time (seconds)")]
            public int RespawnTime { get; set; }

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
            ["Score.FlagCaptures"] = "Flag Captures: {0}",
            ["Score.Kills"] = "Kills: {0}",
            ["Score.Name"] = "Kills",
            ["Score.Limit"] = "Score Limit : {0}",
            ["Score.Team"] = "{0} : <color={1}>Team A</color> | <color={2}>Team B</color> : {3}",
            ["Notification.FlagPickedUp"] = "<color={2}>{0}</color> has picked up <color={3}>Team {1}</color>'s flag",
            ["Notification.FlagReset"] = "<color={1}>Team {0}</color>'s flag has been returned to base",
            ["Notification.FlagCaptured"] = "<color={2}>{0}</color> has captured <color={3}>Team {1}</color>'s flag",
            ["Notification.FlagDropped"] = "<color={2}>{0}</color> has dropped <color={3}>Team {1}</color>'s flag"

        };
        #endregion
    }
}
