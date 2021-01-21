//Requires: EventStatistics
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Game.Rust.Cui;
using Oxide.Core.Plugins;
using UnityEngine;
using System.Linq;
using System.Globalization;

namespace Oxide.Plugins
{
    [Info("EMInterface", "k1lly0u", "2.0.0")]
    [Description("Manages and provides user interface for event games")]
    public class EMInterface : RustPlugin
    {
        #region Fields    
        [PluginReference] private Plugin ImageLibrary;

        public static EMInterface Instance { get; private set; }

        public static ConfigData Configuration { get; set; }
        #endregion

        #region Oxide Hooks        
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(Messages, this);
        }

        private void OnServerInitialized()
        {
            Instance = this;

            RegisterDeathScreenImages();
        }

        private void Unload()
        {            
            foreach (BasePlayer player in BasePlayer.activePlayerList)
                DestroyAllUI(player);

            Instance = null;
            Configuration = null;
        }
        #endregion

        #region UI Menus        
        public enum MenuTab { Event, Statistics, Admin }
        public enum AdminTab { None, OpenEvent, EditEvent, CreateEvent, DeleteEvent, KickPlayer, Selector }
        public enum StatisticTab { Personal, Global, Leaders }
        public enum SelectionType { Field, Event, Player }

        private const float ELEMENT_HEIGHT = 0.035f;

        public void OpenMenu(BasePlayer player, MenuArgs args)
        {
            CuiElementContainer container = UI.Container(UI_MENU, Configuration.Menu.Background.Get, new UI4(0.1f, 0.1f, 0.9f, 0.9f), true);

            UI.Label(container, UI_MENU, Message("UI.Title", player.userID), 20, new UI4(0.005f, 0.94f, 0.995f, 1f), TextAnchor.MiddleLeft);

            AddMenuButtons(player, container, UI_MENU, args.Menu);

            switch (args.Menu)
            {
                case MenuTab.Event:
                    CreateEventDetails(player, container, UI_MENU, args.Page);
                    break;
                case MenuTab.Admin:
                    if (player.IsAdmin || permission.UserHasPermission(player.UserIDString, EventManager.ADMIN_PERMISSION))
                        CreateAdminOptions(player, container, UI_MENU, args);
                    break;
                case MenuTab.Statistics:
                    CreateStatisticsMenu(player, container, args);
                    break;
            }

            CuiHelper.DestroyUi(player, UI_MENU);
            CuiHelper.AddUi(player, container);
        }

        private void CreateMenuPopup(BasePlayer player, string text, float duration = 5f)
        {
            CuiElementContainer container = UI.Container(UI_POPUP, Configuration.Menu.Highlight.Get, new UI4(0.1f, 0.072f, 0.9f, 0.1f));
            UI.Label(container, UI_POPUP, text, 12, UI4.Full);

            CuiHelper.DestroyUi(player, UI_POPUP);
            CuiHelper.AddUi(player, container);

            player.Invoke(() => CuiHelper.DestroyUi(player, UI_POPUP), duration);
        }

        private void AddMenuButtons(BasePlayer player, CuiElementContainer container, string panel, MenuTab menuTab)
        {
            int i = 0;
            float xMin = GetHorizontalPos(i);

            UI.Button(container, panel, menuTab == MenuTab.Event ? Configuration.Menu.Highlight.Get : Configuration.Menu.Button.Get, Message("UI.Menu.Event", player.userID), 13, new UI4(xMin, 0.9f, xMin + 0.14f, 0.94f), menuTab == MenuTab.Event ? "" : $"emui.event 0 {(int)MenuTab.Event}");
            xMin = GetHorizontalPos(i += 1) + (0.002f * i);

            UI.Button(container, panel, menuTab == MenuTab.Statistics ? Configuration.Menu.Highlight.Get : Configuration.Menu.Button.Get, Message("UI.Menu.Statistics", player.userID), 13, new UI4(xMin, 0.9f, xMin + 0.14f, 0.94f), menuTab == MenuTab.Statistics ? "" : $"emui.statistics {(int)StatisticTab.Personal} {(int)EventStatistics.Statistic.Rank}");
            xMin = GetHorizontalPos(i += 1) + (0.002f * i);

            if (player.IsAdmin || permission.UserHasPermission(player.UserIDString, EventManager.ADMIN_PERMISSION))
            {
                UI.Button(container, panel, menuTab == MenuTab.Admin ? Configuration.Menu.Highlight.Get : Configuration.Menu.Button.Get, Message("UI.Menu.Admin", player.userID), 13, new UI4(xMin, 0.9f, xMin + 0.14f, 0.94f), menuTab == MenuTab.Admin ? "" : $"emui.event 0 {(int)MenuTab.Admin}");
                xMin = GetHorizontalPos(i += 1) + (0.002f * i);
            }

            UI.Button(container, panel, Configuration.Menu.Highlight.Get, "X", 16, new UI4(0.975f, 0.96f, 0.995f, 0.9925f), "emui.close");

            UI.Panel(container, UI_MENU, Configuration.Menu.Highlight.Get, new UI4(0.005f, 0.897f, 0.995f, 0.9f));
        }

        #region Event Tab
        private void CreateEventDetails(BasePlayer player, CuiElementContainer container, string panel, int page = 0)
        {
            UI.Panel(container, UI_MENU, Configuration.Menu.Panel.Get, new UI4(0.005f, 0.0075f, 0.499f, 0.836f));
            UI.Panel(container, UI_MENU, Configuration.Menu.Panel.Get, new UI4(0.501f, 0.0075f, 0.995f, 0.836f));

            UI.Panel(container, UI_MENU, Configuration.Menu.Button.Get, new UI4(0.005f, 0.845f, 0.499f, 0.885f));
            UI.Panel(container, UI_MENU, Configuration.Menu.Button.Get, new UI4(0.501f, 0.845f, 0.995f, 0.885f));

            UI.Panel(container, UI_MENU, Configuration.Menu.Highlight.Get, new UI4(0.005f, 0.841f, 0.499f, 0.844f));
            UI.Panel(container, UI_MENU, Configuration.Menu.Highlight.Get, new UI4(0.501f, 0.841f, 0.995f, 0.844f));

            UI.Label(container, UI_MENU, Message("UI.Event.Current", player.userID), 13, new UI4(0.01f, 0.845f, 0.499f, 0.885f), TextAnchor.MiddleLeft);

            if (EventManager.BaseManager != null) // Show current event details on the left and current scores on the right
            {
                #region Current Event Info
                EventManager.BaseEventGame eventGame = EventManager.BaseManager;

                int i = 0;
                CreateListEntryLeft(container, Message("UI.Event.Name", player.userID), eventGame.Config.EventName, GetVerticalPos(i += 1, 0.841f));

                CreateListEntryLeft(container, Message("UI.Event.Type", player.userID), eventGame.Config.EventType, GetVerticalPos(i += 1, 0.841f));
                CreateListEntryLeft(container, Message("UI.Event.Status", player.userID), eventGame.Status.ToString(), GetVerticalPos(i += 1, 0.841f));

                CreateListEntryLeft(container, Message("UI.Event.Players", player.userID),
                    string.Format(Message("UI.Players.Format", player.userID), eventGame.eventPlayers.Count, eventGame.Config.MaximumPlayers, eventGame.joiningPlayers.Count),
                    GetVerticalPos(i += 1, 0.841f));

                if (eventGame.Config.TimeLimit > 0)
                    CreateListEntryLeft(container, Message("UI.Event.TimeLimit", player.userID), $"{eventGame.Config.TimeLimit} seconds", GetVerticalPos(i += 1, 0.841f));

                if (eventGame.Config.ScoreLimit > 0)
                    CreateListEntryLeft(container, Message("UI.Event.ScoreLimit", player.userID), eventGame.Config.ScoreLimit.ToString(), GetVerticalPos(i += 1, 0.841f));

                List<KeyValuePair<string, object>> additionalEventDetails = Facepunch.Pool.GetList<KeyValuePair<string, object>>();

                eventGame.GetAdditionalEventDetails(ref additionalEventDetails, player.userID);

                for (int y = 0; y < additionalEventDetails.Count; y++)
                {
                    KeyValuePair<string, object> kvp = additionalEventDetails[y];

                    CreateListEntryLeft(container, kvp.Key, kvp.Value.ToString(), GetVerticalPos(i += 1, 0.841f));
                }

                Facepunch.Pool.FreeList(ref additionalEventDetails);

                if (EventManager.Configuration.Reward.WinAmount > 0)
                {
                    CreateListEntryLeft(container, Message("UI.Event.WinReward", player.userID),
                        string.Format(Message("UI.Reward.Format", player.userID), EventManager.Configuration.Reward.WinAmount, Message($"UI.Reward.{EventManager.Configuration.Reward.Type}", player.userID)),
                        GetVerticalPos(i += 1, 0.841f));
                }

                if (EventManager.Configuration.Reward.KillAmount > 0)
                {
                    CreateListEntryLeft(container, Message("UI.Event.KillReward", player.userID),
                        string.Format(Message("UI.Reward.Format", player.userID), EventManager.Configuration.Reward.KillAmount, Message($"UI.Reward.{EventManager.Configuration.Reward.Type}", player.userID)),
                        GetVerticalPos(i += 1, 0.841f));
                }

                if (EventManager.Configuration.Reward.HeadshotAmount > 0)
                {
                    CreateListEntryLeft(container, Message("UI.Event.HeadshotReward", player.userID),
                        string.Format(Message("UI.Reward.Format", player.userID), EventManager.Configuration.Reward.HeadshotAmount, Message($"UI.Reward.{EventManager.Configuration.Reward.Type}", player.userID)),
                        GetVerticalPos(i += 1, 0.841f));
                }

                if (EventManager.GetUser(player) || eventGame.joiningPlayers.Contains(player))
                {
                    float yMin = GetVerticalPos(i += 1, 0.841f);
                    UI.Button(container, UI_MENU, Configuration.Menu.Button.Get, Message("UI.Event.Leave", player.userID), 13, new UI4(0.3805f, yMin, 0.499f, yMin + ELEMENT_HEIGHT), "emui.leaveevent");
                }
                else
                {
                    if (eventGame.IsOpen())
                    {
                        float yMin = GetVerticalPos(i += 1, 0.841f);
                        UI.Button(container, UI_MENU, Configuration.Menu.Button.Get, Message("UI.Event.Enter", player.userID), 13, new UI4(0.3805f, yMin, 0.499f, yMin + ELEMENT_HEIGHT), "emui.joinevent");
                    }
                }
                #endregion

                #region Current Event Scores
                UI.Label(container, UI_MENU, Message("UI.Event.CurrentScores", player.userID), 13, new UI4(0.506f, 0.845f, 0.995f, 0.885f), TextAnchor.MiddleLeft);

                if (eventGame.scoreData.Count > 0)
                {
                    int j = 0;
                    const int ELEMENTS_PER_PAGE = 20;

                    if (eventGame.scoreData.Count > (ELEMENTS_PER_PAGE * page) + ELEMENTS_PER_PAGE)
                        UI.Button(container, UI_MENU, Configuration.Menu.Highlight.Get, "> > >", 10, new UI4(0.911f, 0.0075f, 0.995f, 0.0375f), $"emui.event {page + 1} {(int)MenuTab.Event}");
                    if (page > 0)
                        UI.Button(container, UI_MENU, Configuration.Menu.Highlight.Get, "< < <", 10, new UI4(0.005f, 0.0075f, 0.089f, 0.0375f), $"emui.event {page - 1} {(int)MenuTab.Event}");

                    if (eventGame.Plugin.IsTeamEvent)
                    {
                        CreateScoreEntryRight(container, Message("UI.Event.TeamScore", player.userID),
                            string.Format(Message("UI.Score.TeamA", player.userID), eventGame.GetTeamScore(EventManager.Team.A)),
                            string.Format(Message("UI.Score.TeamB", player.userID), eventGame.GetTeamScore(EventManager.Team.B)), GetVerticalPos(j += 1, 0.841f));
                    }

                    for (int k = page * ELEMENTS_PER_PAGE; k < (page * ELEMENTS_PER_PAGE) + ELEMENTS_PER_PAGE; k++)
                    {
                        if (k >= eventGame.scoreData.Count)
                            break;

                        EventManager.ScoreEntry scoreEntry = eventGame.scoreData[k];

                        string score1, score2;
                        eventGame.Plugin.FormatScoreEntry(scoreEntry, player.userID, out score1, out score2);

                        CreateScoreEntryRight(container, scoreEntry.displayName, score1, score2, GetVerticalPos(j += 1, 0.841f));
                    }
                }
                else UI.Label(container, UI_MENU, Message("UI.Event.NoScoresRecorded", player.userID), 13, new UI4(0.506f, 0.806f, 0.88f, 0.841f), TextAnchor.MiddleLeft);
                #endregion
            }
            else 
            {
                UI.Label(container, UI_MENU, Message("UI.Event.Previous", player.userID), 13, new UI4(0.506f, 0.845f, 0.995f, 0.885f), TextAnchor.MiddleLeft);

                UI.Label(container, UI_MENU, Message("UI.Event.NoEvent", player.userID), 12, new UI4(0.01f, 0.801f, 0.495f, 0.845f), TextAnchor.MiddleLeft);

                #region Last Event Scores
                if (EventManager.LastEventResult?.IsValid ?? false)
                {
                    int ELEMENTS_PER_PAGE = EventManager.LastEventResult.Plugin.IsTeamEvent ? 17 : 18;

                    if (EventManager.LastEventResult.Scores.Count > (ELEMENTS_PER_PAGE * page) + ELEMENTS_PER_PAGE)
                        UI.Button(container, UI_MENU, Configuration.Menu.Highlight.Get, "> > >", 10, new UI4(0.911f, 0.0075f, 0.995f, 0.0375f), $"emui.event {page + 1} {(int)MenuTab.Event}");
                    if (page > 0)
                        UI.Button(container, UI_MENU, Configuration.Menu.Highlight.Get, "< < <", 10, new UI4(0.005f, 0.0075f, 0.089f, 0.0375f), $"emui.event {page - 1} {(int)MenuTab.Event}");

                    int i = 0;

                    CreateSplitEntryRight(container, Message("UI.Event.Name", player.userID), EventManager.LastEventResult.EventName, GetVerticalPos(i += 1, 0.841f));
                    CreateSplitEntryRight(container, Message("UI.Event.Type", player.userID), EventManager.LastEventResult.EventType, GetVerticalPos(i += 1, 0.841f));

                    if (EventManager.LastEventResult.Plugin.IsTeamEvent)
                    {                        
                        CreateScoreEntryRight(container, Message("UI.Event.TeamScore", player.userID), 
                            string.Format(Message("UI.Score.TeamA", player.userID), EventManager.LastEventResult.TeamScore.value1), 
                            string.Format(Message("UI.Score.TeamB", player.userID), EventManager.LastEventResult.TeamScore.value2), GetVerticalPos(i += 1, 0.841f));
                    }

                    for (int k = page * ELEMENTS_PER_PAGE; k < (page * ELEMENTS_PER_PAGE) + ELEMENTS_PER_PAGE; k++)
                    {
                        if (k >= EventManager.LastEventResult.Scores.Count)
                            break;

                        EventManager.ScoreEntry scoreEntry = EventManager.LastEventResult.Scores[k];

                        string score1, score2;
                        EventManager.LastEventResult.Plugin.FormatScoreEntry(scoreEntry, player.userID, out score1, out score2);

                        CreateScoreEntryRight(container, scoreEntry.displayName, score1, score2, GetVerticalPos(i += 1, 0.841f));
                    }
                }
                else
                {
                    UI.Label(container, UI_MENU, Message("UI.Event.NoPrevious", player.userID), 12, new UI4(0.506f, 0.801f, 0.995f, 0.845f), TextAnchor.MiddleLeft);
                }
                #endregion
            }
        }

        #region Helpers
        private void CreateListEntryLeft(CuiElementContainer container, string key, string value, float yMin)
        {
            UI.Panel(container, UI_MENU, Configuration.Menu.Button.Get, new UI4(0.005f, yMin, 0.38f, yMin + ELEMENT_HEIGHT));
            UI.Panel(container, UI_MENU, Configuration.Menu.Highlight.Get, new UI4(0.3805f, yMin, 0.499f, yMin + ELEMENT_HEIGHT));
            UI.Label(container, UI_MENU, key, 12, new UI4(0.01f, yMin, 0.38f, yMin + ELEMENT_HEIGHT), TextAnchor.MiddleLeft);
            UI.Label(container, UI_MENU, value, 12, new UI4(0.3805f, yMin, 0.494f, yMin + ELEMENT_HEIGHT), TextAnchor.MiddleRight);
        }

        private void CreateListEntryRight(CuiElementContainer container, string key, string value, float yMin)
        {
            UI.Panel(container, UI_MENU, Configuration.Menu.Button.Get, new UI4(0.501f, yMin, 0.88f, yMin + ELEMENT_HEIGHT));
            UI.Panel(container, UI_MENU, Configuration.Menu.Highlight.Get, new UI4(0.8805f, yMin, 0.995f, yMin + ELEMENT_HEIGHT));
            UI.Label(container, UI_MENU, key, 12, new UI4(0.506f, yMin, 0.88f, yMin + ELEMENT_HEIGHT), TextAnchor.MiddleLeft);
            UI.Label(container, UI_MENU, value, 12, new UI4(0.8805f, yMin, 0.99f, yMin + ELEMENT_HEIGHT), TextAnchor.MiddleRight);
        }

        private void CreateScoreEntryRight(CuiElementContainer container, string displayName, string score1, string score2, float yMin)
        {
            UI.Panel(container, UI_MENU, Configuration.Menu.Button.Get, new UI4(0.501f, yMin, 0.748f, yMin + ELEMENT_HEIGHT));
            UI.Label(container, UI_MENU, displayName, 12, new UI4(0.506f, yMin, 0.748f, yMin + ELEMENT_HEIGHT), TextAnchor.MiddleLeft);

            if (!string.IsNullOrEmpty(score1))
            {
                UI.Panel(container, UI_MENU, Configuration.Menu.Highlight.Get, new UI4(0.7485f, yMin, 0.8725f, yMin + ELEMENT_HEIGHT));
                UI.Label(container, UI_MENU, score1, 12, new UI4(0.7535f, yMin, 0.8675f, yMin + ELEMENT_HEIGHT), TextAnchor.MiddleRight);
            }
            else UI.Panel(container, UI_MENU, Configuration.Menu.Button.Get, new UI4(0.7485f, yMin, 0.8725f, yMin + ELEMENT_HEIGHT));

            if (!string.IsNullOrEmpty(score2))
            {
                UI.Panel(container, UI_MENU, Configuration.Menu.Highlight.Get, new UI4(0.875f, yMin, 0.995f, yMin + ELEMENT_HEIGHT));
                UI.Label(container, UI_MENU, score2, 12, new UI4(0.88f, yMin, 0.99f, yMin + ELEMENT_HEIGHT), TextAnchor.MiddleRight);
            }
            else UI.Panel(container, UI_MENU, Configuration.Menu.Button.Get, new UI4(0.875f, yMin, 0.995f, yMin + ELEMENT_HEIGHT));
        }

        private void CreateSplitEntryRight(CuiElementContainer container, string key, string value, float yMin)
        {
            UI.Panel(container, UI_MENU, Configuration.Menu.Button.Get, new UI4(0.501f, yMin, 0.748f, yMin + ELEMENT_HEIGHT));
            UI.Panel(container, UI_MENU, Configuration.Menu.Highlight.Get, new UI4(0.7485f, yMin, 0.995f, yMin + ELEMENT_HEIGHT));
            UI.Label(container, UI_MENU, key, 12, new UI4(0.506f, yMin, 0.748f, yMin + ELEMENT_HEIGHT), TextAnchor.MiddleLeft);
            UI.Label(container, UI_MENU, value, 12, new UI4(0.7485f, yMin, 0.99f, yMin + ELEMENT_HEIGHT), TextAnchor.MiddleRight);
        }
        #endregion
        #endregion

        #region Admin Tab
        private void CreateAdminOptions(BasePlayer player, CuiElementContainer container, string panel, MenuArgs args)
        {
            UI.Panel(container, UI_MENU, Configuration.Menu.Button.Get, new UI4(0.005f, 0.845f, 0.175f, 0.885f));
            UI.Panel(container, UI_MENU, Configuration.Menu.Highlight.Get, new UI4(0.005f, 0.841f, 0.175f, 0.844f));

            UI.Panel(container, UI_MENU, Configuration.Menu.Button.Get, new UI4(0.177f, 0.845f, 0.995f, 0.885f));
            UI.Panel(container, UI_MENU, Configuration.Menu.Highlight.Get, new UI4(0.177f, 0.841f, 0.995f, 0.844f));

            UI.Label(container, UI_MENU, Message("UI.Admin.Title", player.userID), 13, new UI4(0.01f, 0.845f, 0.175f, 0.885f), TextAnchor.MiddleLeft);

            UI.Panel(container, UI_MENU, Configuration.Menu.Panel.Get, new UI4(0.005f, 0.0075f, 0.175f, 0.836f));
            UI.Panel(container, UI_MENU, Configuration.Menu.Panel.Get, new UI4(0.177f, 0.0075f, 0.995f, 0.836f));

            int i = 1;
            float yMin = GetVerticalPos(i, 0.836f);

            if (EventManager.BaseManager != null)
            {
                if ((int)EventManager.BaseManager.Status < 2)
                {
                    UI.Button(container, UI_MENU, Configuration.Menu.Button.Get, Message("UI.Admin.Start", player.userID), 12, new UI4(0.01f, yMin, 0.17f, yMin + ELEMENT_HEIGHT), "emui.startevent");
                    yMin = GetVerticalPos(i += 1, 0.836f);
                }

                if (EventManager.BaseManager.IsOpen())
                {
                    UI.Button(container, UI_MENU, Configuration.Menu.Button.Get, Message("UI.Admin.Close", player.userID), 12, new UI4(0.01f, yMin, 0.17f, yMin + ELEMENT_HEIGHT), "emui.closeevent");
                    yMin = GetVerticalPos(i += 1, 0.836f);
                }

                UI.Button(container, UI_MENU, Configuration.Menu.Button.Get, Message("UI.Admin.End", player.userID), 12, new UI4(0.01f, yMin, 0.17f, yMin + ELEMENT_HEIGHT), "emui.endevent");
                yMin = GetVerticalPos(i += 1, 0.836f);

                //UI.Button(container, UI_MENU, args.Admin == AdminTab.KickPlayer ? Configuration.Menu.Highlight.Get : Configuration.Menu.Button.Get, Message("UI.Admin.Kick", player.userID), 12, new UI4(0.01f, yMin, 0.17f, yMin + ELEMENT_HEIGHT), "emui.kickplayer");
                //yMin = GetVerticalPos(i += 1, 0.836f);
            }
            else
            {
                UI.Button(container, UI_MENU, args.Admin == AdminTab.OpenEvent ? Configuration.Menu.Highlight.Get : Configuration.Menu.Button.Get, Message("UI.Admin.Open", player.userID), 12, new UI4(0.01f, yMin, 0.17f, yMin + ELEMENT_HEIGHT), $"emui.eventselector {(int)AdminTab.OpenEvent}");
                yMin = GetVerticalPos(i += 1, 0.836f);

                UI.Button(container, UI_MENU, args.Admin == AdminTab.EditEvent ? Configuration.Menu.Highlight.Get : Configuration.Menu.Button.Get, Message("UI.Admin.Edit", player.userID), 12, new UI4(0.01f, yMin, 0.17f, yMin + ELEMENT_HEIGHT), $"emui.eventselector {(int)AdminTab.EditEvent}");
                yMin = GetVerticalPos(i += 1, 0.836f);

                UI.Button(container, UI_MENU, args.Admin == AdminTab.CreateEvent ? Configuration.Menu.Highlight.Get : Configuration.Menu.Button.Get, Message("UI.Admin.Create", player.userID), 12, new UI4(0.01f, yMin, 0.17f, yMin + ELEMENT_HEIGHT), "emui.create");
                yMin = GetVerticalPos(i += 1, 0.836f);

                UI.Button(container, UI_MENU, args.Admin == AdminTab.DeleteEvent ? Configuration.Menu.Highlight.Get : Configuration.Menu.Button.Get, Message("UI.Admin.Delete", player.userID), 12, new UI4(0.01f, yMin, 0.17f, yMin + ELEMENT_HEIGHT), $"emui.eventselector {(int)AdminTab.DeleteEvent}");
                yMin = GetVerticalPos(i += 1, 0.836f);
            }

            switch (args.Admin)
            {                
                case AdminTab.OpenEvent:
                case AdminTab.EditEvent:
                case AdminTab.DeleteEvent:
                    OpenEventSelector(player, container, UI_MENU, args.Selector, args.Page);                   
                    break;
                case AdminTab.CreateEvent:
                    EventCreatorMenu(player, container, UI_MENU);
                    break;                
                case AdminTab.KickPlayer:
                    break;
                case AdminTab.Selector:
                    OpenSelector(player, container, UI_MENU, args.Selector, args.Page);
                    break;
                default:
                    break;
            }
        }
        #endregion

        #region Event Creation
        private Hash<ulong, EventManager.EventConfig> _eventCreators = new Hash<ulong, EventManager.EventConfig>();

        private void EventCreatorMenu(BasePlayer player, CuiElementContainer container, string panel)
        {
            EventManager.EventConfig eventConfig;
            _eventCreators.TryGetValue(player.userID, out eventConfig);

            int i = 0;

            if (eventConfig == null || string.IsNullOrEmpty(eventConfig.EventType))
            {
                UI.Label(container, UI_MENU, "Select an event type", 13, new UI4(0.182f, 0.845f, 0.99f, 0.885f), TextAnchor.MiddleLeft);

                foreach (string eventName in EventManager.Instance.EventModes.Keys)
                {
                    float yMin = GetVerticalPos(i += 1, 0.836f);
                    UI.Button(container, panel, Configuration.Menu.Button.Get, eventName, 12, new UI4(0.182f, yMin, 0.3f, yMin + ELEMENT_HEIGHT), $"emui.create {CommandSafe(eventName)}");
                }
            }
            else
            {
                UI.Label(container, UI_MENU, $"Creating Event ({eventConfig.EventType})", 13, new UI4(0.182f, 0.845f, 0.99f, 0.885f), TextAnchor.MiddleLeft);

                UI.Button(container, UI_MENU, Configuration.Menu.Highlight.Get, "Save", 12, new UI4(0.925f, 0.845f, 0.995f, 0.885f), "emui.saveevent");
                UI.Button(container, UI_MENU, Configuration.Menu.Highlight.Get, "Dispose", 12, new UI4(0.85f, 0.845f, 0.92f, 0.885f), "emui.disposeevent");

                AddInputField(container, panel, i += 1, "Event Name", "eventName", eventConfig.EventName);

                AddSelectorField(container, panel, i += 1, "Zone ID", "zoneID", eventConfig.ZoneID, "GetZoneIDs");

                if (eventConfig.Plugin.IsTeamEvent)
                {
                    AddSelectorField(container, panel, i += 1, "Team A Spawnfile", "teamASpawnfile", eventConfig.TeamConfigA.Spawnfile, "GetSpawnfileNames");

                    AddSelectorField(container, panel, i += 1, "Team A Kit(s)", "teamAKits", GetSelectorLabel(eventConfig.TeamConfigA.Kits), "GetAllKits", eventConfig.AllowClassSelection);

                    AddInputField(container, panel, i += 1, "Team A Color (Hex)", "teamAColor", eventConfig.TeamConfigA.Color);

                    AddSelectorField(container, panel, i += 1, "Team A Clothing", "teamAClothing", eventConfig.TeamConfigA.Clothing, "GetAllKits", false);

                    AddSelectorField(container, panel, i += 1, "Team B Spawnfile", "teamBSpawnfile", eventConfig.TeamConfigB.Spawnfile, "GetSpawnfileNames");

                    AddSelectorField(container, panel, i += 1, "Team B Kit(s)", "teamBKits", GetSelectorLabel(eventConfig.TeamConfigB.Kits), "GetAllKits", eventConfig.AllowClassSelection);

                    AddInputField(container, panel, i += 1, "Team B Color (Hex)", "teamBColor", eventConfig.TeamConfigB.Color);

                    AddSelectorField(container, panel, i += 1, "Team B Clothing", "teamBClothing", eventConfig.TeamConfigB.Clothing, "GetAllKits", false);
                }
                else
                {
                    AddSelectorField(container, panel, i += 1, "Spawnfile", "teamASpawnfile", eventConfig.TeamConfigA.Spawnfile, "GetSpawnfileNames");

                    AddSelectorField(container, panel, i += 1, "Kit(s)", "teamAKits", GetSelectorLabel(eventConfig.TeamConfigA.Kits), "GetAllKits", eventConfig.AllowClassSelection);
                }

                if (eventConfig.Plugin.CanUseClassSelector)
                    AddToggleField(container, panel, i += 1, "Use Class Selector", "useClassSelector", eventConfig.AllowClassSelection);

                if (eventConfig.Plugin.UseTimeLimit)
                    AddInputField(container, panel, i += 1, "Time Limit (seconds)", "timeLimit", eventConfig.TimeLimit);

                if (eventConfig.Plugin.UseScoreLimit)
                    AddInputField(container, panel, i += 1, "Score Limit", "scoreLimit", eventConfig.ScoreLimit);

                AddInputField(container, panel, i += 1, "Minimum Players", "minimumPlayers", eventConfig.MinimumPlayers);
                AddInputField(container, panel, i += 1, "Maximum Players", "maximumPlayers", eventConfig.MaximumPlayers);

                List<EventManager.EventParameter> eventParameters = eventConfig.Plugin.AdditionalParameters;

                for (int y = 0; y < eventParameters?.Count; y++)
                {
                    EventManager.EventParameter eventParameter = eventParameters[y];

                    switch (eventParameter.Input)
                    {
                        case EventManager.EventParameter.InputType.InputField:
                            {
                                string parameter = eventConfig.GetParameter<string>(eventParameter.Field);
                                AddInputField(container, panel, i += 1, eventParameter.Name, eventParameter.Field, string.IsNullOrEmpty(parameter) ? null : parameter);
                                break;
                            }
                        case EventManager.EventParameter.InputType.Toggle:
                            {
                                bool parameter = eventConfig.GetParameter<bool>(eventParameter.Field);
                                AddToggleField(container, panel, i += 1, eventParameter.Name, eventParameter.Field, parameter);
                                break;
                            }
                        case EventManager.EventParameter.InputType.Selector:
                            {
                                string parameter = eventConfig.GetParameter<string>(eventParameter.Field);
                                AddSelectorField(container, panel, i += 1, eventParameter.Name, eventParameter.Field, parameter, eventParameter.SelectorHook);
                            }
                            break;
                    }
                }
            }
        }

        private void AddInputField(CuiElementContainer container, string panel, int index, string title, string fieldName, object currentValue)
        {
            float yMin = GetVerticalPos(index >= 21 ? index - 20 : index, 0.836f);
            float hMin = index >= 21 ? 0.59f : 0.182f;

            UI.Label(container, panel, title, 12, new UI4(hMin, yMin, hMin + 0.118f, yMin + ELEMENT_HEIGHT), TextAnchor.MiddleLeft);

            UI.Panel(container, panel, Configuration.Menu.Button.Get, new UI4(hMin + 0.118f, yMin, hMin + 0.4f, yMin + ELEMENT_HEIGHT));

            string label = GetInputLabel(currentValue);
            if (!string.IsNullOrEmpty(label))
            {
                UI.Label(container, panel, label, 12, new UI4(hMin + 0.123f, yMin, hMin + 0.4f, yMin + ELEMENT_HEIGHT), TextAnchor.MiddleLeft);
                UI.Button(container, panel, Configuration.Menu.Highlight.Get, "X", 12, new UI4(hMin + 0.38f, yMin, hMin + 0.4f, yMin + ELEMENT_HEIGHT), $"emui.clear {fieldName}");
            }
            else UI.Input(container, panel, string.Empty, 12, $"emui.creator {fieldName}", new UI4(hMin + 0.123f, yMin, hMin + 0.4f, yMin + ELEMENT_HEIGHT));
        }

        private void AddLabelField(CuiElementContainer container, string panel, int index, string title, string value)
        {
            float yMin = GetVerticalPos(index >= 21 ? index - 20 : index, 0.836f);
            float hMin = index >= 21 ? 0.59f : 0.182f;

            UI.Label(container, panel, title, 12, new UI4(hMin, yMin, hMin + 0.118f, yMin + ELEMENT_HEIGHT), TextAnchor.MiddleLeft);

            UI.Panel(container, panel, Configuration.Menu.Button.Get, new UI4(hMin + 0.118f, yMin, hMin + 0.4f, yMin + ELEMENT_HEIGHT));
            UI.Label(container, panel, value, 12, new UI4(hMin + 0.123f, yMin, hMin + 0.4f, yMin + ELEMENT_HEIGHT), TextAnchor.MiddleLeft);
        }

        private void AddToggleField(CuiElementContainer container, string panel, int index, string title, string fieldName, bool currentValue)
        {
            float yMin = GetVerticalPos(index >= 21 ? index - 20 : index, 0.836f);
            float hMin = index >= 21 ? 0.59f : 0.182f;

            UI.Label(container, panel, title, 12, new UI4(hMin, yMin, hMin + 0.118f, yMin + ELEMENT_HEIGHT), TextAnchor.MiddleLeft);
            UI.Toggle(container, panel, Configuration.Menu.Button.Get, 12, new UI4(hMin + 0.118f, yMin, hMin + 0.138f, yMin + ELEMENT_HEIGHT), $"emui.creator {fieldName} {!currentValue}", currentValue);
        }

        private void AddSelectorField(CuiElementContainer container, string panel, int index, string title, string fieldName, string currentValue, string hook, bool allowMultiple = false)
        {
            float yMin = GetVerticalPos(index >= 21 ? index - 20 : index, 0.836f);
            float hMin = index >= 21 ? 0.59f : 0.182f;

            UI.Label(container, panel, title, 12, new UI4(hMin, yMin, hMin + 0.118f, yMin + ELEMENT_HEIGHT), TextAnchor.MiddleLeft);

            UI.Panel(container, panel, Configuration.Menu.Button.Get, new UI4(hMin + 0.118f, yMin, hMin + 0.4f, yMin + ELEMENT_HEIGHT));

            if (!string.IsNullOrEmpty(currentValue))
                UI.Label(container, panel, currentValue.ToString(), 12, new UI4(hMin + 0.123f, yMin, hMin + 0.4f, yMin + ELEMENT_HEIGHT), TextAnchor.MiddleLeft);

            UI.Button(container, panel, Configuration.Menu.Highlight.Get, "Select", 12, new UI4(hMin + 0.35f, yMin, hMin + 0.4f, yMin + ELEMENT_HEIGHT), $"emui.fieldselector {CommandSafe(title)} {fieldName} {hook} {allowMultiple}");
        }

        private string GetSelectorLabel(IEnumerable<object> list) => list.Count() == 0 ? "Nothing Selected" : list.Count() > 1 ? "Multiple Selected" : list.ElementAt(0).ToString();

        private string GetInputLabel(object obj)
        {
            if (obj is string)
                return string.IsNullOrEmpty(obj as string) ? null : obj.ToString();
            else if (obj is int)
                return (int)obj <= 0 ? null : obj.ToString();
            else if (obj is float)
                return (float)obj <= 0 ? null : obj.ToString();
            return null;
        }

        #region Selector
        private void OpenEventSelector(BasePlayer player, CuiElementContainer container, string panel, SelectorArgs args, int page)
        {
            UI.Label(container, UI_MENU, args.Title, 13, new UI4(0.182f, 0.845f, 0.99f, 0.885f), TextAnchor.MiddleLeft);

            int i = 0;
            foreach (KeyValuePair<string, EventManager.EventConfig> kvp in EventManager.Instance.Events.events)
            {
                UI.Button(container, panel, Configuration.Menu.Button.Get, $"{kvp.Key} <size=8>({kvp.Value.EventType})</size>", 11, GetGridLayout(i, 0.182f, 0.796f, 0.1578f, 0.035f, 5, 20), $"{args.Callback} {CommandSafe(kvp.Key)}");
                i++;
            }            
        }

        private void OpenSelector(BasePlayer player, CuiElementContainer container, string panel, SelectorArgs args, int page)
        {
            UI.Label(container, UI_MENU, args.Title, 13, new UI4(0.182f, 0.845f, 0.99f, 0.885f), TextAnchor.MiddleLeft);

            UI.Button(container, UI_MENU, Configuration.Menu.Highlight.Get, "Back", 12, new UI4(0.925f, 0.845f, 0.995f, 0.885f), "emui.closeselector");

            string[] array = Interface.CallHook(args.Hook) as string[];
            if (array != null)
            {
                EventManager.EventConfig eventConfig;
                _eventCreators.TryGetValue(player.userID, out eventConfig);

                string stringValue = eventConfig.GetString(args.FieldName);
                List<string> listValue = eventConfig?.GetList(args.FieldName);

                int count = 0;
                for (int i = page * 200; i < Mathf.Min((page + 1) * 200, array.Length); i++)
                {
                    string option = array[i];

                    string color = ((stringValue?.Equals(option, StringComparison.OrdinalIgnoreCase) ?? false) || (listValue?.Contains(option)?? false)) ? Configuration.Menu.Highlight.Get : Configuration.Menu.Button.Get;

                    UI.Button(container, panel, color, array[i], 11, GetGridLayout(count), $"emui.select {CommandSafe(args.Title)} {args.FieldName} {args.Hook} {args.AllowMultiple} {CommandSafe(option)}");
                    count += 1;
                }
            }
            else
            {
                UI.Label(container, UI_MENU, "No options available for selection", 13, new UI4(0.182f, 0.796f, 0.99f, 0.836f), TextAnchor.MiddleLeft);
            }
        }

        private UI4 GetGridLayout(int index, float xMin = 0.182f, float yMin = 0.796f, float width = 0.0764f, float height = 0.035f, int columns = 10, int rows = 20)
        {
            int columnNumber = index == 0 ? 0 : Mathf.FloorToInt(index / (float)columns);
            int rowNumber = index - (columnNumber * columns);

            float x = xMin + ((width + 0.005f) * rowNumber);
            float y = yMin - ((height + 0.0075f) * columnNumber);

            return new UI4(x, y, x + width, y + height);
        }

        private UI4 GetGridLayout(int columnNumber, int rowNumber, float xMin = 0.182f, float yMin = 0.796f, float width = 0.0764f, float height = 0.035f, int columns = 10, int rows = 20)
        {            
            float x = xMin + ((width + 0.005f) * rowNumber);
            float y = yMin - ((height + 0.0075f) * columnNumber);

            return new UI4(x, y, x + width, y + height);
        }
        #endregion        
        #endregion        
        #endregion

        #region Statistics      
        private void CreateStatisticsMenu(BasePlayer player, CuiElementContainer container, MenuArgs args)
        {
            AddStatisticHeader(container, player.userID, args.Statistic);            

            switch (args.Statistic)
            {
                case StatisticTab.Personal:
                    AddStatistics(container, false, player.userID, args.Page);
                    break;
                case StatisticTab.Global:
                    AddStatistics(container, true, player.userID, args.Page);
                    break;
                case StatisticTab.Leaders:
                    AddLeaderBoard(container, player.userID, args.Page, args.StatisticSort);
                    break;  
            }
        }

        private void AddStatisticHeader(CuiElementContainer container, ulong playerId, StatisticTab openTab)
        {
            int i = 0;
            float xMin = GetHorizontalPos(i);

            UI.Button(container, UI_MENU, openTab == StatisticTab.Personal ? Configuration.Menu.Highlight.Get : Configuration.Menu.Button.Get, Message("UI.Statistics.Personal", playerId), 13, new UI4(xMin, 0.85f, xMin + 0.14f, 0.885f), openTab == StatisticTab.Personal ? "" : $"emui.statistics {(int)StatisticTab.Personal} {EventStatistics.Statistic.Rank}");
            xMin = GetHorizontalPos(i += 1) + (0.002f * i);

            UI.Button(container, UI_MENU, openTab == StatisticTab.Global ? Configuration.Menu.Highlight.Get : Configuration.Menu.Button.Get, Message("UI.Statistics.Global", playerId), 13, new UI4(xMin, 0.85f, xMin + 0.14f, 0.885f), openTab == StatisticTab.Global ? "" : $"emui.statistics {(int)StatisticTab.Global} {EventStatistics.Statistic.Rank}");
            xMin = GetHorizontalPos(i += 1) + (0.002f * i);

            UI.Button(container, UI_MENU, openTab == StatisticTab.Leaders ? Configuration.Menu.Highlight.Get : Configuration.Menu.Button.Get, Message("UI.Statistics.Leaders", playerId), 13, new UI4(xMin, 0.85f, xMin + 0.14f, 0.885f), openTab == StatisticTab.Leaders ? "" : $"emui.statistics {(int)StatisticTab.Leaders} {EventStatistics.Statistic.Rank}");
            xMin = GetHorizontalPos(i += 1) + (0.002f * i);
        }

        private void AddStatistics(CuiElementContainer container, bool isGlobal, ulong playerId, int page = 0)
        {
            UI.Panel(container, UI_MENU, Configuration.Menu.Button.Get, new UI4(0.005f, 0.805f, 0.499f, 0.845f));
            UI.Panel(container, UI_MENU, Configuration.Menu.Button.Get, new UI4(0.501f, 0.805f, 0.995f, 0.845f));
            UI.Panel(container, UI_MENU, Configuration.Menu.Highlight.Get, new UI4(0.005f, 0.801f, 0.995f, 0.804f));

            UI.Label(container, UI_MENU, isGlobal ? Message("UI.Statistics.Global", playerId) : Message("UI.Statistics.Personal", playerId), 13, new UI4(0.01f, 0.805f, 0.499f, 0.845f), TextAnchor.MiddleLeft);
            UI.Label(container, UI_MENU, Message("UI.GamesPlayed", playerId), 13, new UI4(0.506f, 0.805f, 0.995f, 0.845f), TextAnchor.MiddleLeft);

            EventStatistics.Statistics.Data data = isGlobal ? EventStatistics.Data.global : EventStatistics.Data.Find(playerId);
            if (data != null)
            {
                const int ELEMENTS_PER_PAGE = 19;
                                
                if (data.events.Count > (ELEMENTS_PER_PAGE * page) + ELEMENTS_PER_PAGE)
                    UI.Button(container, UI_MENU, Configuration.Menu.Highlight.Get, "> > >", 10, new UI4(0.911f, 0.0075f, 0.995f, 0.0375f), 
                        $"emui.statistics {(isGlobal ? (int)StatisticTab.Global : (int)StatisticTab.Personal)} {page + 1}");
                if (page > 0)
                    UI.Button(container, UI_MENU, Configuration.Menu.Highlight.Get, "< < <", 10, new UI4(0.005f, 0.0075f, 0.089f, 0.0375f), 
                        $"emui.statistics {(isGlobal ? (int)StatisticTab.Global : (int)StatisticTab.Personal)} {page - 1}");

                int i = 0;
                if (!isGlobal)
                {
                    CreateListEntryLeft(container, Message("UI.Rank", playerId), data.Rank == -1 ? "-" : data.Rank.ToString(), GetVerticalPos(i+=1, 0.801f));
                    CreateListEntryLeft(container, Message("UI.Score", playerId), data.Score.ToString(), GetVerticalPos(i+=1, 0.801f));
                }

                foreach (KeyValuePair<string, int> score in data.statistics)                
                    CreateListEntryLeft(container, isGlobal ? string.Format(Message("UI.Totals", playerId), score.Key) : Message(score.Key, playerId), score.Value.ToString(), GetVerticalPos(i+=1, 0.801f));

                int j = 1;
                for (int k = page * ELEMENTS_PER_PAGE; k < (page * ELEMENTS_PER_PAGE) + ELEMENTS_PER_PAGE; k++)
                {
                    if (k >= data.events.Count)
                        break;

                    KeyValuePair<string, int> eventGame = data.events.ElementAt(k);

                    CreateListEntryRight(container, eventGame.Key, eventGame.Value.ToString(), GetVerticalPos(j++, 0.801f));
                }
            }
            else
            {
                float yMin = GetVerticalPos(1, 0.801f);
                UI.Label(container, UI_MENU, Message("UI.NoStatisticsSaved", playerId), 13, new UI4(0.01f, yMin, 0.38f, yMin + ELEMENT_HEIGHT), TextAnchor.MiddleLeft);
            }
        }        
        
        private void AddLeaderBoard(CuiElementContainer container, ulong playerId, int page = 0, EventStatistics.Statistic sortBy = EventStatistics.Statistic.Rank)
        {
            const int ELEMENTS_PER_PAGE = 19;

            List<EventStatistics.Statistics.Data> list = EventStatistics.Data.SortStatisticsBy(sortBy);

            if (list.Count > (ELEMENTS_PER_PAGE * page) + ELEMENTS_PER_PAGE)
                UI.Button(container, UI_MENU, Configuration.Menu.Highlight.Get, "> > >", 10, new UI4(0.911f, 0.0075f, 0.995f, 0.0375f), 
                    $"emui.statistics {(int)StatisticTab.Leaders} {(int)sortBy} {page + 1}");
            if (page > 0)
                UI.Button(container, UI_MENU, Configuration.Menu.Highlight.Get, "< < <", 10, new UI4(0.005f, 0.0075f, 0.089f, 0.0375f), 
                    $"emui.statistics {(int)StatisticTab.Leaders} {(int)sortBy} {page - 1}");

            float yMin = 0.81f;

            AddLeaderSortButton(container, UI_MENU, sortBy == EventStatistics.Statistic.Rank ? Configuration.Menu.Highlight.Get : Configuration.Menu.Button.Get, string.Empty, page, EventStatistics.Statistic.Rank, 0.005f, 0.033f, yMin);

            AddLeaderSortButton(container, UI_MENU, sortBy == EventStatistics.Statistic.Name ? Configuration.Menu.Highlight.Get : Configuration.Menu.Button.Get, Message("UI.Player", playerId), page, EventStatistics.Statistic.Name, 0.035f, 0.225f, yMin, TextAnchor.MiddleLeft);

            AddLeaderSortButton(container, UI_MENU, sortBy == EventStatistics.Statistic.Rank ? Configuration.Menu.Highlight.Get : Configuration.Menu.Button.Get, Message("UI.Score", playerId), page, EventStatistics.Statistic.Rank, 0.227f, 0.309f, yMin);

            AddLeaderSortButton(container, UI_MENU, sortBy == EventStatistics.Statistic.Kills ? Configuration.Menu.Highlight.Get : Configuration.Menu.Button.Get, Message("UI.Kills", playerId), page, EventStatistics.Statistic.Kills, 0.311f, 0.393f, yMin);

            AddLeaderSortButton(container, UI_MENU, sortBy == EventStatistics.Statistic.Deaths ? Configuration.Menu.Highlight.Get : Configuration.Menu.Button.Get, Message("UI.Deaths", playerId), page, EventStatistics.Statistic.Deaths, 0.395f, 0.479f, yMin);

            AddLeaderSortButton(container, UI_MENU, sortBy == EventStatistics.Statistic.Assists ? Configuration.Menu.Highlight.Get : Configuration.Menu.Button.Get, Message("UI.Assists", playerId), page, EventStatistics.Statistic.Assists, 0.481f, 0.565f, yMin);

            AddLeaderSortButton(container, UI_MENU, sortBy == EventStatistics.Statistic.Headshots ? Configuration.Menu.Highlight.Get : Configuration.Menu.Button.Get, Message("UI.Headshots", playerId), page, EventStatistics.Statistic.Headshots, 0.567f, 0.651f, yMin);

            AddLeaderSortButton(container, UI_MENU, sortBy == EventStatistics.Statistic.Melee ? Configuration.Menu.Highlight.Get : Configuration.Menu.Button.Get, Message("UI.Melee", playerId), page, EventStatistics.Statistic.Melee, 0.653f, 0.737f, yMin);

            AddLeaderSortButton(container, UI_MENU, sortBy == EventStatistics.Statistic.Wins ? Configuration.Menu.Highlight.Get : Configuration.Menu.Button.Get, Message("UI.Won", playerId), page, EventStatistics.Statistic.Wins, 0.739f, 0.823f, yMin);

            AddLeaderSortButton(container, UI_MENU, sortBy == EventStatistics.Statistic.Losses ? Configuration.Menu.Highlight.Get : Configuration.Menu.Button.Get, Message("UI.Lost", playerId), page, EventStatistics.Statistic.Losses, 0.825f, 0.909f, yMin);

            AddLeaderSortButton(container, UI_MENU, sortBy == EventStatistics.Statistic.Played ? Configuration.Menu.Highlight.Get : Configuration.Menu.Button.Get, Message("UI.Played", playerId), page, EventStatistics.Statistic.Played, 0.911f, 0.995f, yMin);

            UI.Panel(container, UI_MENU, Configuration.Menu.Highlight.Get, new UI4(0.005f, 0.807f, 0.995f, 0.81f));

            int j = 1;
            for (int i = page * ELEMENTS_PER_PAGE; i < (page * ELEMENTS_PER_PAGE) + ELEMENTS_PER_PAGE; i++)
            {
                if (i >= list.Count)
                    break;

                EventStatistics.Statistics.Data userData = list[i];

                yMin = GetVerticalPos(j, 0.81f);
                
                if (userData != null)
                {
                    AddStatistic(container, UI_MENU, Configuration.Menu.Button.Get, userData.Rank.ToString(), 0.005f, 0.033f, yMin);
                    AddStatistic(container, UI_MENU, Configuration.Menu.Button.Get, userData.DisplayName ?? "Unknown", 0.035f, 0.225f, yMin, TextAnchor.MiddleLeft);
                    AddStatistic(container, UI_MENU, Configuration.Menu.Button.Get, userData.Score.ToString(), 0.227f, 0.309f, yMin);
                    AddStatistic(container, UI_MENU, Configuration.Menu.Button.Get, userData.GetStatistic("Kills").ToString(), 0.311f, 0.393f, yMin);
                    AddStatistic(container, UI_MENU, Configuration.Menu.Button.Get, userData.GetStatistic("Deaths").ToString(), 0.395f, 0.479f, yMin);
                    AddStatistic(container, UI_MENU, Configuration.Menu.Button.Get, userData.GetStatistic("Assists").ToString(), 0.481f, 0.565f, yMin);
                    AddStatistic(container, UI_MENU, Configuration.Menu.Button.Get, userData.GetStatistic("Headshots").ToString(), 0.567f, 0.651f, yMin);
                    AddStatistic(container, UI_MENU, Configuration.Menu.Button.Get, userData.GetStatistic("Melee").ToString(), 0.653f, 0.737f, yMin);
                    AddStatistic(container, UI_MENU, Configuration.Menu.Button.Get, userData.GetStatistic("Wins").ToString(), 0.739f, 0.823f, yMin);
                    AddStatistic(container, UI_MENU, Configuration.Menu.Button.Get, userData.GetStatistic("Losses").ToString(), 0.825f, 0.909f, yMin);
                    AddStatistic(container, UI_MENU, Configuration.Menu.Button.Get, userData.GetStatistic("Played").ToString(), 0.9111f, 0.995f, yMin);
                    j++;
                }
            }
        }

        #region Helpers
        private void AddStatistic(CuiElementContainer container, string panel, string color, string message, float xMin, float xMax, float verticalPos, TextAnchor anchor = TextAnchor.MiddleCenter)
        {            
            UI.Panel(container, panel, color, new UI4(xMin, verticalPos, xMax, verticalPos + ELEMENT_HEIGHT));
            UI.Label(container, panel, message, 12, new UI4(xMin + (anchor != TextAnchor.MiddleCenter ? 0.005f : 0f), verticalPos, xMax - (anchor != TextAnchor.MiddleCenter ? 0.005f : 0f), verticalPos + ELEMENT_HEIGHT), anchor);
        }

        private void AddLeaderSortButton(CuiElementContainer container, string panel, string color, string message, int page, EventStatistics.Statistic statistic, float xMin, float xMax, float verticalPos, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            UI4 ui4 = new UI4(xMin, verticalPos, xMax, verticalPos + ELEMENT_HEIGHT);

            UI.Panel(container, panel, color, ui4);
            UI.Label(container, panel, message, 12, new UI4(xMin + (anchor != TextAnchor.MiddleCenter ? 0.005f : 0f), verticalPos, xMax - (anchor != TextAnchor.MiddleCenter ? 0.005f : 0f), verticalPos + ELEMENT_HEIGHT), anchor);
            UI.Button(container, panel, "0 0 0 0", string.Empty, 0, ui4, $"emui.statistics {(int)StatisticTab.Leaders} {(int)statistic} {page}");
        }

        private float GetHorizontalPos(int i, float start = 0.005f, float size = 0.1405f) => start + (size * i);

        private float GetVerticalPos(int i, float start = 0.9f) => start - (i * (ELEMENT_HEIGHT + 0.005f));
        #endregion
        #endregion

        #region Scoreboards        
        public static CuiElementContainer CreateScoreboardBase(EventManager.BaseEventGame baseEventGame)
        {
            CuiElementContainer container = UI.Container(UI_SCORES, Configuration.Scoreboard.Background.Get, Configuration.Scoreboard.Position.UI4, false);

            UI.Panel(container, UI_SCORES, Configuration.Scoreboard.Highlight.Get, UI4.Full);
            UI.Label(container, UI_SCORES, $"{baseEventGame.Config.EventName} ({baseEventGame.Config.EventType})", 11, UI4.Full);

            return container;            
        }

        public static void CreateScoreEntry(CuiElementContainer container, string text, string value1, string value2, int index)
        {
            float yMax = -(1f * index);
            float yMin = -(1f * (index + 1));

            UI.Panel(container, UI_SCORES, Configuration.Scoreboard.Panel.Get, new UI4(0f, yMin + 0.02f, 1f, yMax - 0.02f));

            UI.Label(container, UI_SCORES, text, 11, new UI4(0.05f, yMin, 1f, yMax), TextAnchor.MiddleLeft);

            if (!string.IsNullOrEmpty(value1))
            {
                UI.Panel(container, UI_SCORES, Configuration.Scoreboard.Highlight.Get, new UI4(0.75f, yMin + 0.02f, 0.875f, yMax - 0.02f));
                UI.Label(container, UI_SCORES, value1, 11, new UI4(0.75f, yMin, 0.875f, yMax), TextAnchor.MiddleCenter);
            }

            if (!string.IsNullOrEmpty(value2))
            {
                UI.Panel(container, UI_SCORES, Configuration.Scoreboard.Highlight.Get, new UI4(0.875f, yMin + 0.02f, 1f, yMax - 0.02f));
                UI.Label(container, UI_SCORES, value2, 11, new UI4(0.875f, yMin, 1f, yMax), TextAnchor.MiddleCenter);
            }
        }

        public static void CreatePanelEntry(CuiElementContainer container, string text, int index)
        {
            float yMax = -(1f * index);
            float yMin = -(1f * (index + 1));

            UI.Panel(container, UI_SCORES, Configuration.Scoreboard.Foreground.Get, new UI4(0f, yMin + 0.02f, 1f, yMax - 0.02f));

            UI.Label(container, UI_SCORES, text, 11, new UI4(0.05f, yMin, 1f, yMax), TextAnchor.MiddleCenter);
        }
        #endregion

        #region DeathScreen
        private const string DEATH_SKULL_ICON = "emui.death_skullicon";
        private const string DEATH_BACKGROUND = "emui.death_background";

        private void RegisterDeathScreenImages()
        {
            if (!ImageLibrary)
                return;

            if (!string.IsNullOrEmpty(Configuration.DeathBackground))
                AddImage(DEATH_BACKGROUND, Configuration.DeathBackground);

            if (!string.IsNullOrEmpty(Configuration.DeathIcon))
                AddImage(DEATH_SKULL_ICON, Configuration.DeathIcon);
        }
        
        public static void DisplayDeathScreen(EventManager.BaseEventPlayer victim, string message, bool canRespawn)
        {
            CuiElementContainer container = UI.Container(UI_DEATH, Configuration.Menu.Background.Get, UI4.Full, true);

            if (!string.IsNullOrEmpty(Configuration.DeathBackground))
            {
                string background = Instance.GetImage(DEATH_BACKGROUND);
                if (!string.IsNullOrEmpty(background))
                    UI.Image(container, UI_DEATH, background, UI4.Full);
            }

            if (!string.IsNullOrEmpty(Configuration.DeathIcon))
            {
                string icon = Instance.GetImage(DEATH_SKULL_ICON);
                if (!string.IsNullOrEmpty(icon))
                    UI.Image(container, UI_DEATH, icon, new UI4(0.45f, 0.405f, 0.55f, 0.595f));
            }
            
            UI.Label(container, UI_DEATH, message, 22, new UI4(0.2f, 0.7f, 0.8f, 0.85f));

            victim.DestroyUI(UI_DEATH);
            victim.AddUI(UI_DEATH, container);

            if (canRespawn)
                UpdateRespawnButton(victim);
            else CreateLeaveButton(victim);
        }
        
        public static void UpdateRespawnButton(EventManager.BaseEventPlayer eventPlayer)
        {
            CuiElementContainer container = UI.Container(UI_RESPAWN, Configuration.Menu.Panel.Get, new UI4(0f, 0f, 1f, 0.04f), true);

            UI.Panel(container, UI_RESPAWN, Configuration.Menu.Highlight.Get, new UI4(0f, 1f, 1f, 1.005f));

            UI.Button(container, UI_RESPAWN, eventPlayer.CanRespawn ? Configuration.Menu.Highlight.Get : Configuration.Menu.Button.Get, eventPlayer.CanRespawn ? Message("UI.Death.Respawn", eventPlayer.Player.userID) : string.Format(Message("UI.Death.Respawn.Time", eventPlayer.Player.userID), eventPlayer.RespawnRemaining), 13, new UI4(0.005f, 0.125f, 0.1f, 0.875f), "emui.respawn");

            UI.Label(container, UI_RESPAWN, Message("UI.Death.AutoRespawn", eventPlayer.Player.userID), 13, new UI4(0.1f, 0.125f, 0.17f, 0.875f), TextAnchor.MiddleRight);
            UI.Toggle(container, UI_RESPAWN, Configuration.Menu.Button.Get, 14, new UI4(0.18f, 0.125f, 0.2f, 0.875f), "emui.toggleautospawn", eventPlayer.AutoRespawn);

            List<string> kits = EventManager.BaseManager.GetAvailableKits(eventPlayer.Team);

            if (EventManager.BaseManager.Config.AllowClassSelection && kits.Count > 1)
            {
                UI.Button(container, UI_RESPAWN, eventPlayer.IsSelectingClass ? Configuration.Menu.Highlight.Get : Configuration.Menu.Button.Get, Message("UI.Death.Class", eventPlayer.Player.userID), 13, new UI4(0.895f, 0.125f, 0.995f, 0.875f), $"emui.selectkit {CommandSafe(eventPlayer.Kit)} {!eventPlayer.IsSelectingClass}");

                if (eventPlayer.IsSelectingClass)
                {
                    UI.Panel(container, UI_RESPAWN, Configuration.Menu.Panel.Get, new UI4(0.89f, 1f, 1f, 1f + kits.Count));

                    UI.Panel(container, UI_RESPAWN, Configuration.Menu.Highlight.Get, new UI4(0.88975f, 1f, 0.89f, 1f + kits.Count + 0.005f)); // Side highlight

                    UI.Panel(container, UI_RESPAWN, Configuration.Menu.Highlight.Get, new UI4(0.89f, 1f + kits.Count, 1f, 1f + kits.Count + 0.005f)); // Top highlight

                    for (int i = 0; i < kits.Count; i++)
                    {
                        string kit = kits[i];                        

                        UI.Button(container, UI_RESPAWN, eventPlayer.Kit.Equals(kit, StringComparison.OrdinalIgnoreCase) ? Configuration.Menu.Highlight.Get : Configuration.Menu.Button.Get, kit, 12, new UI4(0.895f, (1f + i) + 0.125f, 0.995f, (1f + (i + 1f)) - 0.125f), $"emui.selectkit {CommandSafe(kit)} true");
                    }
                }
            }

            eventPlayer.DestroyUI(UI_RESPAWN);
            eventPlayer.AddUI(UI_RESPAWN, container);
        }

        public static void CreateLeaveButton(EventManager.BaseEventPlayer eventPlayer)
        {
            CuiElementContainer container = UI.Container(UI_RESPAWN, Configuration.Menu.Panel.Get, new UI4(0f, 0f, 1f, 0.04f), true);

            UI.Panel(container, UI_RESPAWN, Configuration.Menu.Highlight.Get, new UI4(0f, 1f, 1f, 1.005f));

            UI.Button(container, UI_RESPAWN, eventPlayer.CanRespawn ? Configuration.Menu.Highlight.Get : Configuration.Menu.Button.Get, Message("UI.Death.Leave", eventPlayer.Player.userID), 13, new UI4(0.005f, 0.125f, 0.1f, 0.875f), "emui.leaveevent");
                        
            eventPlayer.DestroyUI(UI_RESPAWN);
            eventPlayer.AddUI(UI_RESPAWN, container);
        }
        #endregion

        #region ImageLibrary
        internal void AddImage(string imageName, string url) => ImageLibrary.Call("AddImage", url, imageName);

        internal string GetImage(string name) => (string)ImageLibrary.Call("GetImage", name);
        #endregion

        #region UI Args
        public struct MenuArgs
        {
            public int Page;
            public MenuTab Menu;
            public AdminTab Admin;
            public StatisticTab Statistic;
            public EventStatistics.Statistic StatisticSort;
            public SelectorArgs Selector;

            public MenuArgs(MenuTab menu)
            {
                Page = 0;
                Menu = menu;
                Statistic = StatisticTab.Global;
                Admin = AdminTab.None;
                StatisticSort = EventStatistics.Statistic.Rank;
                Selector = default(SelectorArgs);
            }

            public MenuArgs(int page, MenuTab menu)
            {
                Page = page;
                Menu = menu;
                Statistic = StatisticTab.Personal;
                Admin = AdminTab.None;
                StatisticSort = EventStatistics.Statistic.Rank;
                Selector = default(SelectorArgs);
            }

            public MenuArgs(AdminTab admin)
            {
                Page = 0;
                Menu = MenuTab.Admin;
                Statistic = StatisticTab.Personal;
                Admin = admin;
                StatisticSort = EventStatistics.Statistic.Rank;
                Selector = default(SelectorArgs);
            }

            public MenuArgs(StatisticTab statistic, EventStatistics.Statistic sort, int page)
            {
                Page = page;
                Menu = MenuTab.Statistics;
                Statistic = statistic;
                Admin = AdminTab.None;
                StatisticSort = sort;
                Selector = default(SelectorArgs);
            }

            public MenuArgs(SelectorArgs selectorArgs, int page)
            {
                Page = page;
                Selector = selectorArgs;
                Menu = MenuTab.Admin;
                Admin = AdminTab.Selector;
                Statistic = StatisticTab.Personal;
                StatisticSort = EventStatistics.Statistic.Rank;
            }

            public MenuArgs(SelectorArgs selectorArgs, AdminTab admin, int page)
            {
                Page = page;
                Selector = selectorArgs;
                Menu = MenuTab.Admin;
                Admin = admin;
                Statistic = StatisticTab.Personal;
                StatisticSort = EventStatistics.Statistic.Rank;
            }
        }

        public struct SelectorArgs
        {
            public string Title;
            public string FieldName;
            public string Hook;
            public bool AllowMultiple;

            public SelectionType Type;
            public string Callback;

            public SelectorArgs(string title, string fieldName, string hook, bool allowMultiple, SelectionType type = SelectionType.Field)
            {
                Title = title;
                FieldName = fieldName;
                Hook = hook;
                AllowMultiple = allowMultiple;
                Type = SelectionType.Field;
                Callback = string.Empty;
            }

            public SelectorArgs(string title, SelectionType type, string callback)
            {
                Title = title;
                FieldName = string.Empty;
                Hook = string.Empty;
                AllowMultiple = false;
                Type = type;
                Callback = callback;
            }
        }

        #endregion

        #region UI Commands
        #region Creator Commands
        [ConsoleCommand("emui.create")]
        private void ccmdCreateEvent(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null)
                return;

            if (player.IsAdmin || permission.UserHasPermission(player.UserIDString, EventManager.ADMIN_PERMISSION))
            {
                if (arg.HasArgs(1))
                {
                    EventManager.EventConfig eventConfig;
                    if (!_eventCreators.TryGetValue(player.userID, out eventConfig))
                    {
                        string eventName = CommandSafe(arg.GetString(0), true);

                        EventManagerEx.IEventPlugin eventPlugin = EventManager.Instance.GetPlugin(eventName);

                        if (eventPlugin == null)
                            return;

                        _eventCreators[player.userID] = eventConfig = new EventManager.EventConfig(eventName, eventPlugin);
                    }
                }

                OpenMenu(player, new MenuArgs(AdminTab.CreateEvent));
            }
        }

        [ConsoleCommand("emui.saveevent")]
        private void ccmdSaveEvent(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null)
                return;

            if (player.IsAdmin || permission.UserHasPermission(player.UserIDString, EventManager.ADMIN_PERMISSION))
            {
                EventManager.EventConfig eventConfig;
                if (!_eventCreators.TryGetValue(player.userID, out eventConfig))
                    return;

                object success = EventManager.Instance.ValidateEventConfig(eventConfig);
                if (success == null)
                {
                    EventManager.SaveEventConfig(eventConfig);
                    _eventCreators.Remove(player.userID);

                    OpenMenu(player, new MenuArgs(AdminTab.None));

                    CreateMenuPopup(player, $"Successfully saved event {eventConfig.EventName}");
                }
                else CreateMenuPopup(player, (string)success);
            }
        }

        [ConsoleCommand("emui.disposeevent")]
        private void ccmdDisposeEvent(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null)
                return;

            _eventCreators.Remove(player.userID);

            if (player.IsAdmin || permission.UserHasPermission(player.UserIDString, EventManager.ADMIN_PERMISSION))
            {
                OpenMenu(player, new MenuArgs(AdminTab.None));
                CreateMenuPopup(player, "Cancelled event creation");
            }
        }

        [ConsoleCommand("emui.clear")]
        private void ccmdClearField(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null)
                return;

            EventManager.EventConfig eventConfig;
            if (!_eventCreators.TryGetValue(player.userID, out eventConfig))
                return;

            string fieldName = arg.GetString(0);

            switch (fieldName)
            {
                case "eventName":
                    eventConfig.EventName = string.Empty;
                    break;
                case "zoneID":
                    eventConfig.ZoneID = string.Empty;
                    break;
                case "timeLimit":
                    eventConfig.TimeLimit = 0;
                    break;
                case "scoreLimit":
                    eventConfig.ScoreLimit = 0;
                    break;
                case "minimumPlayers":
                    eventConfig.MinimumPlayers = 0;
                    break;
                case "maximumPlayers":
                    eventConfig.MaximumPlayers = 0;
                    break;
                case "teamASpawnfile":
                    eventConfig.TeamConfigA.Spawnfile = string.Empty;
                    break;
                case "teamBSpawnfile":
                    eventConfig.TeamConfigB.Spawnfile = string.Empty;
                    break;
                case "teamAColor":
                    eventConfig.TeamConfigA.Color = string.Empty;
                    break;
                case "teamBColor":
                    eventConfig.TeamConfigB.Color = string.Empty;
                    break;
                default:
                    foreach (KeyValuePair<string, object> kvp in eventConfig.AdditionalParams)
                    {
                        if (kvp.Key.Equals(fieldName, StringComparison.OrdinalIgnoreCase))
                        {
                            eventConfig.AdditionalParams[fieldName] = null;
                            break;
                        }
                    }
                    break;
            }

            OpenMenu(player, new MenuArgs(AdminTab.CreateEvent));
        }

        [ConsoleCommand("emui.creator")]
        private void ccmdSetField(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null)
                return;

            EventManager.EventConfig eventConfig;
            if (!_eventCreators.TryGetValue(player.userID, out eventConfig))
                return;

            if (arg.HasArgs(2))
            {
                SetParameter(player, eventConfig, arg.GetString(0), string.Join(" ", arg.Args.Skip(1)));

                OpenMenu(player, new MenuArgs(AdminTab.CreateEvent));
            }
        }

        #region Creator Helpers
        private void SetParameter(BasePlayer player, EventManager.EventConfig eventConfig, string fieldName, object value)
        {
            if (value == null)
                return;

            switch (fieldName)
            {
                case "eventName":
                    eventConfig.EventName = (string)value;
                    break;
                case "zoneID":
                    eventConfig.ZoneID = (string)value;
                    break;
                case "timeLimit":
                    {
                        int intValue;
                        if (!TryConvertValue<int>(value, out intValue))
                            CreateMenuPopup(player, "You must enter a number");
                        else eventConfig.TimeLimit = intValue;
                    }                   
                    break;
                case "scoreLimit":
                    {
                        int intValue;
                        if (!TryConvertValue<int>(value, out intValue))
                            CreateMenuPopup(player, "You must enter a number");
                        else eventConfig.ScoreLimit = intValue;
                    }                    
                    break;
                case "minimumPlayers":
                    {
                        int intValue;
                        if (!TryConvertValue<int>(value, out intValue))
                            CreateMenuPopup(player, "You must enter a number");
                        else eventConfig.MinimumPlayers = intValue;
                    }                    
                    break;
                case "maximumPlayers":
                    {
                        int intValue;
                        if (!TryConvertValue<int>(value, out intValue))
                            CreateMenuPopup(player, "You must enter a number");
                        else eventConfig.MaximumPlayers = intValue;
                    }                    
                    break;
                case "teamASpawnfile":
                    eventConfig.TeamConfigA.Spawnfile = (string)value;
                    break;
                case "teamBSpawnfile":
                    eventConfig.TeamConfigB.Spawnfile = (string)value;
                    break;
                case "useClassSelector":
                    {
                        bool boolValue;
                        if (!TryConvertValue<bool>(value, out boolValue))
                            CreateMenuPopup(player, "You must enter 'True' or 'False'");
                        else eventConfig.AllowClassSelection = boolValue;
                    }
                    break;
                case "teamAKits":
                    AddToRemoveFromList(eventConfig.TeamConfigA.Kits, (string)value);
                    break;
                case "teamBKits":
                    AddToRemoveFromList(eventConfig.TeamConfigB.Kits, (string)value);
                    break;
                case "teamAClothing":
                    eventConfig.TeamConfigA.Clothing = (string)value;
                    break;
                case "teamBClothing":
                    eventConfig.TeamConfigB.Clothing = (string)value;
                    break;
                case "teamAColor":
                    {
                        string color = (string)value;
                        if (string.IsNullOrEmpty(color) || color.Length < 6 || color.Length > 6 || !EventManager.IsValidHex(color))
                            CreateMenuPopup(player, "The color must be a 6 digit hex color, without the # prefix");
                        else eventConfig.TeamConfigA.Color = color;
                        break;
                    }
                case "teamBColor":
                    {
                        string color = (string)value;
                        if (string.IsNullOrEmpty(color) || color.Length < 6 || color.Length > 6 || !EventManager.IsValidHex(color))
                            CreateMenuPopup(player, "The color must be a 6 digit hex color, without the # prefix");
                        else eventConfig.TeamConfigB.Color = color;
                        break;
                    }
                default:
                    List<EventManager.EventParameter> additionalParameters = eventConfig.Plugin?.AdditionalParameters;
                    if (additionalParameters != null)
                    {
                        for (int i = 0; i < additionalParameters.Count; i++)
                        {
                            EventManager.EventParameter eventParameter = additionalParameters[i];

                            if (!eventConfig.AdditionalParams.ContainsKey(eventParameter.Field))
                            {
                                if (eventParameter.IsList)
                                    eventConfig.AdditionalParams[eventParameter.Field] = new List<string>();
                                else eventConfig.AdditionalParams[eventParameter.Field] = eventParameter.DefaultValue == null ? null : eventParameter.DefaultValue;
                            }

                            if (fieldName.Equals(eventParameter.Field, StringComparison.OrdinalIgnoreCase))
                            {
                                object success = eventConfig.Plugin.ParameterIsValid(fieldName, value);
                                if (success != null)
                                {
                                    CreateMenuPopup(player, (string)success);
                                    return;
                                }

                                switch (eventParameter.DataType)
                                {
                                    case "string":
                                        eventConfig.AdditionalParams[eventParameter.Field] = (string)value;
                                        break;
                                    case "int":
                                        int intValue;
                                        if (!TryConvertValue<int>(value, out intValue))
                                            CreateMenuPopup(player, "You must enter a number");
                                        else eventConfig.AdditionalParams[eventParameter.Field] = intValue;
                                        break;
                                    case "float":
                                        float floatValue;
                                        if (!TryConvertValue<float>(value, out floatValue))
                                            CreateMenuPopup(player, "You must enter a number");
                                        else eventConfig.AdditionalParams[eventParameter.Field] = floatValue;                                        
                                        break;
                                    case "bool":
                                        bool boolValue;
                                        if (!TryConvertValue<bool>(value, out boolValue))
                                            CreateMenuPopup(player, "You must enter 'True' or 'False'");
                                        else eventConfig.AdditionalParams[eventParameter.Field] = boolValue;                                        
                                        break;
                                    case "List<string>":
                                        AddToRemoveFromList(eventConfig.AdditionalParams[eventParameter.Field] as List<string>, (string)value);
                                        break;
                                    default:
                                        break;
                                }
                            }
                        }
                    }
                    return;
            }
        }

        private bool TryConvertValue<T>(object value, out T result)
        {
            try
            {
                result = (T)Convert.ChangeType(value, typeof(T));
                return true;
            }
            catch
            {
                result = default(T);
                return false;
            }            
        }

        private void AddToRemoveFromList(List<string> list, string value)
        {
            if (list.Contains(value))
                list.Remove(value);
            else list.Add(value);
        }
        #endregion
        #endregion

        #region Death Screen Commands
        [ConsoleCommand("emui.toggleautospawn")]
        private void ccmdToggleAutoSpawn(ConsoleSystem.Arg arg)
        {
            EventManager.BaseEventPlayer eventPlayer = arg.Player()?.GetComponent<EventManager.BaseEventPlayer>();
            if (eventPlayer == null || !eventPlayer.IsDead)
                return;

            eventPlayer.AutoRespawn = !eventPlayer.AutoRespawn;
            UpdateRespawnButton(eventPlayer);
        }

        [ConsoleCommand("emui.respawn")]
        private void ccmdRespawn(ConsoleSystem.Arg arg)
        {
            EventManager.BaseEventPlayer eventPlayer = arg.Player()?.GetComponent<EventManager.BaseEventPlayer>();
            if (eventPlayer == null || !eventPlayer.IsDead || !eventPlayer.CanRespawn)
                return;

            if (string.IsNullOrEmpty(eventPlayer.Kit))
                return;

            eventPlayer.IsSelectingClass = false;

            EventManager.RespawnPlayer(eventPlayer);            
        }

        [ConsoleCommand("emui.selectkit")]
        private void ccmdSelectKit(ConsoleSystem.Arg arg)
        {
            EventManager.BaseEventPlayer eventPlayer = arg.Player()?.GetComponent<EventManager.BaseEventPlayer>();
            if (eventPlayer == null || !eventPlayer.IsDead)
                return;

            eventPlayer.Kit = CommandSafe(arg.GetString(0), true);
            eventPlayer.IsSelectingClass = arg.GetBool(1);

            UpdateRespawnButton(eventPlayer);
        }
        #endregion

        #region General Commands
        [ConsoleCommand("emui.close")]
        private void ccmdCloseUI(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null)
                return;

            CuiHelper.DestroyUi(player, UI_MENU);
            CuiHelper.DestroyUi(player, UI_POPUP);
        }

        [ConsoleCommand("emui.joinevent")]
        private void ccmdJoinEvent(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null)
                return;

            if (EventManager.BaseManager.CanJoinEvent(player))
            {
                EventManager.BaseManager.JoinEvent(player);

                if (EventManager.BaseManager.Status < EventManager.EventStatus.Prestarting)
                {
                    OpenMenu(player, new MenuArgs(MenuTab.Event));
                    CreateMenuPopup(player, Message("UI.Popup.EnterEvent", player.userID));
                }
            }
        }

        [ConsoleCommand("emui.leaveevent")]
        private void ccmdLeaveEvent(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null)
                return;

            DestroyAllUI(player);

            EventManager.BaseManager.LeaveEvent(player);

            if (EventManager.BaseManager.Status < EventManager.EventStatus.Prestarting)
            {
                OpenMenu(player, new MenuArgs(MenuTab.Event));
                CreateMenuPopup(player, Message("UI.Popup.LeaveEvent", player.userID));
            }
        }
        #endregion

        #region Menu Selection
        [ConsoleCommand("emui.statistics")]
        private void ccmdStatistics(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null)
                return;

            OpenMenu(player, new MenuArgs((StatisticTab)arg.GetInt(0), (EventStatistics.Statistic)arg.GetInt(1), arg.GetInt(2)));
        }

        [ConsoleCommand("emui.event")]
        private void ccmdEvent(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null)
                return;

            _eventCreators.Remove(player.userID);

            OpenMenu(player, new MenuArgs(arg.GetInt(0), (MenuTab)arg.GetInt(1)));
        }
        #endregion

        #region Event Management
        [ConsoleCommand("emui.eventselector")]
        private void ccmdOpenEventSelector(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null)
                return;

            if (player.IsAdmin || permission.UserHasPermission(player.UserIDString, EventManager.ADMIN_PERMISSION))
            {
                AdminTab adminTab = (AdminTab)arg.GetInt(0);

                switch (adminTab)
                {
                    case AdminTab.OpenEvent:
                        OpenMenu(player, new MenuArgs(new SelectorArgs("Select an event to open", SelectionType.Event, "emui.openevent"), adminTab, 0));
                        break;
                    case AdminTab.EditEvent:
                        OpenMenu(player, new MenuArgs(new SelectorArgs("Select an event to edit", SelectionType.Event, "emui.editevent"), adminTab, 0));
                        break;
                    case AdminTab.DeleteEvent:
                        OpenMenu(player, new MenuArgs(new SelectorArgs("Select an event to delete", SelectionType.Event, "emui.deleteevent"), adminTab, 0));
                        break;
                    default:
                        break;
                }
            }
        }

        [ConsoleCommand("emui.openevent")]
        private void ccmdOpenEvent(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null)
                return;

            if (player.IsAdmin || permission.UserHasPermission(player.UserIDString, EventManager.ADMIN_PERMISSION))
            {
                string eventName = CommandSafe(arg.GetString(0), true);

                object success = EventManager.Instance.OpenEvent(eventName);

                if (success == null)
                {
                    CreateMenuPopup(player, $"Opened event {eventName}");
                    OpenMenu(player, new MenuArgs(0, MenuTab.Event));
                }
                else CreateMenuPopup(player, (string)success);
            }
        }

        [ConsoleCommand("emui.endevent")]
        private void ccmdEndEvent(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null)
                return;

            if (player.IsAdmin || permission.UserHasPermission(player.UserIDString, EventManager.ADMIN_PERMISSION))
            {
                CreateMenuPopup(player, $"Cancelled event {EventManager.BaseManager.Config.EventName}");
                EventManager.BaseManager.EndEvent();

                OpenMenu(player, new MenuArgs(0, MenuTab.Admin));
            }
        }

        [ConsoleCommand("emui.startevent")]
        private void ccmdStartEvent(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null)
                return;

            if (player.IsAdmin || permission.UserHasPermission(player.UserIDString, EventManager.ADMIN_PERMISSION))
            {
                if (EventManager.BaseManager.joiningPlayers.Contains(player))
                    DestroyAllUI(player);
                else
                {
                    OpenMenu(player, new MenuArgs(0, MenuTab.Admin));
                    CreateMenuPopup(player, "Event pre-start initiated");
                }

                EventManager.BaseManager.PrestartEvent();                
            }
        }

        [ConsoleCommand("emui.closeevent")]
        private void ccmdCloseEvent(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null)
                return;

            if (player.IsAdmin || permission.UserHasPermission(player.UserIDString, EventManager.ADMIN_PERMISSION))
            {
                CreateMenuPopup(player, $"Closed event {EventManager.BaseManager.Config.EventName} to new players");
                EventManager.BaseManager.CloseEvent();
                OpenMenu(player, new MenuArgs(0, MenuTab.Admin));
            }
        }
        #endregion

        [ConsoleCommand("emui.editevent")]
        private void ccmdEditEvent(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null)
                return;

            if (player.IsAdmin || permission.UserHasPermission(player.UserIDString, EventManager.ADMIN_PERMISSION))
            {
                string eventName = CommandSafe(arg.GetString(0), true);

                EventManager.EventConfig eventConfig = EventManager.Instance.Events.events[eventName];
                eventConfig.Plugin = EventManager.Instance.GetPlugin(eventConfig.EventType);

                if (eventConfig.Plugin != null)
                {
                    CreateMenuPopup(player, $"Editing event {eventName} ({eventConfig.EventType})");
                    _eventCreators[player.userID] = eventConfig;
                    OpenMenu(player, new MenuArgs(AdminTab.CreateEvent));
                }
                else CreateMenuPopup(player, $"The event plugin {eventConfig.EventType} is not loaded");
            }
        }

        [ConsoleCommand("emui.deleteevent")]
        private void ccmdDeleteEvent(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null)
                return;

            if (player.IsAdmin || permission.UserHasPermission(player.UserIDString, EventManager.ADMIN_PERMISSION))
            {
                string eventName = CommandSafe(arg.GetString(0), true);

                CreateMenuPopup(player, $"Deleted event {eventName}");

                EventManager.Instance.Events.events.Remove(eventName);

                EventManager.Instance.SaveEventData();

                OpenMenu(player, new MenuArgs(AdminTab.None));
            }
        }

        [ConsoleCommand("emui.closeselector")]
        private void ccmdCloseSelector(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null)
                return;

            if (player.IsAdmin || permission.UserHasPermission(player.UserIDString, EventManager.ADMIN_PERMISSION))
            {
                OpenMenu(player, new MenuArgs(AdminTab.CreateEvent));
            }
        }

        [ConsoleCommand("emui.fieldselector")]
        private void ccmdOpenSelector(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null)
                return;

            if (player.IsAdmin || permission.UserHasPermission(player.UserIDString, EventManager.ADMIN_PERMISSION))
            {
                OpenMenu(player, new MenuArgs(new SelectorArgs(CommandSafe(arg.GetString(0), true), arg.GetString(1), arg.GetString(2), arg.GetBool(3)), 0));
            }
        }

        [ConsoleCommand("emui.select")]
        private void ccmdSelect(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null)
                return;

            if (player.IsAdmin || permission.UserHasPermission(player.UserIDString, EventManager.ADMIN_PERMISSION))
            {
                EventManager.EventConfig eventConfig;
                if (!_eventCreators.TryGetValue(player.userID, out eventConfig))
                    return;

                SetParameter(player, eventConfig, arg.GetString(1), CommandSafe(arg.GetString(4), true));

                if (arg.GetBool(3))
                    OpenMenu(player, new MenuArgs(new SelectorArgs(CommandSafe(arg.GetString(0), true), arg.GetString(1), arg.GetString(2), true), 0));

                else OpenMenu(player, new MenuArgs(AdminTab.CreateEvent));
            }
        }

        #region Command Helpers
        private static string CommandSafe(string text, bool unpack = false) => unpack ? text.Replace("▊▊", " ") : text.Replace(" ", "▊▊");
        #endregion
        #endregion

        #region UI
        internal const string UI_MENU = "emui.menu";
        internal const string UI_TIMER = "emui.timer";
        internal const string UI_SCORES = "emui.scores";
        internal const string UI_POPUP = "emui.popup";
        internal const string UI_DEATH = "emui.death";
        internal const string UI_RESPAWN = "emui.respawn";

        internal static void DestroyAllUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UI_MENU);
            CuiHelper.DestroyUi(player, UI_DEATH);
            CuiHelper.DestroyUi(player, UI_POPUP);
            CuiHelper.DestroyUi(player, UI_RESPAWN);
            CuiHelper.DestroyUi(player, UI_SCORES);
            CuiHelper.DestroyUi(player, UI_TIMER);
        }

        public static class UI
        {
            public static CuiElementContainer Container(string panelName, string color, UI4 dimensions, bool useCursor = false, string parent = "Overlay")
            {
                CuiElementContainer container = new CuiElementContainer()
                {
                    {
                        new CuiPanel
                        {
                            Image = { Color = color },
                            RectTransform = { AnchorMin = dimensions.GetMin(), AnchorMax = dimensions.GetMax() },
                            CursorEnabled = useCursor
                        },
                        new CuiElement().Parent = parent,
                        panelName
                    }
                };
                return container;
            }


            public static CuiElementContainer Popup(string panelName, string text, int size, UI4 dimensions, TextAnchor align = TextAnchor.MiddleCenter, string parent = "Overlay")
            {
                CuiElementContainer container = UI.Container(panelName, "0 0 0 0", dimensions, false);

                UI.Label(container, panelName, text, size, UI4.Full, align);

                return container;
            }

            public static void Panel(CuiElementContainer container, string panel, string color, UI4 dimensions)
            {
                container.Add(new CuiPanel
                {
                    Image = { Color = color },
                    RectTransform = { AnchorMin = dimensions.GetMin(), AnchorMax = dimensions.GetMax() }
                },
                panel);
            }

            public static void Label(CuiElementContainer container, string panel, string text, int size, UI4 dimensions, TextAnchor align = TextAnchor.MiddleCenter)
            {
                container.Add(new CuiLabel
                {
                    Text = { FontSize = size, Align = align, Text = text },
                    RectTransform = { AnchorMin = dimensions.GetMin(), AnchorMax = dimensions.GetMax() }
                },
                panel);
            }

            public static void Button(CuiElementContainer container, string panel, string color, string text, int size, UI4 dimensions, string command, TextAnchor align = TextAnchor.MiddleCenter)
            {
                container.Add(new CuiButton
                {
                    Button = { Color = color, Command = command, FadeIn = 0f },
                    RectTransform = { AnchorMin = dimensions.GetMin(), AnchorMax = dimensions.GetMax() },
                    Text = { Text = text, FontSize = size, Align = align }
                },
                panel);
            }

            public static void Input(CuiElementContainer container, string panel, string text, int size, string command, UI4 dimensions)
            {
                container.Add(new CuiElement
                {
                    Name = CuiHelper.GetGuid(),
                    Parent = panel,
                    Components =
                    {
                        new CuiInputFieldComponent
                        {
                            Align = TextAnchor.MiddleLeft,
                            CharsLimit = 300,
                            Command = command + text,
                            FontSize = size,
                            IsPassword = false,
                            Text = text
                        },
                        new CuiRectTransformComponent {AnchorMin = dimensions.GetMin(), AnchorMax = dimensions.GetMax() }
                    }
                });
            }

            public static void Image(CuiElementContainer container, string panel, string png, UI4 dimensions)
            {
                container.Add(new CuiElement
                {
                    Name = CuiHelper.GetGuid(),
                    Parent = panel,
                    Components =
                    {
                        new CuiRawImageComponent {Png = png },
                        new CuiRectTransformComponent { AnchorMin = dimensions.GetMin(), AnchorMax = dimensions.GetMax() }
                    }
                });
            }

            public static void Toggle(CuiElementContainer container, string panel, string boxColor, int fontSize, UI4 dimensions, string command, bool isOn)
            {
                UI.Panel(container, panel, boxColor, dimensions);

                if (isOn)
                    UI.Label(container, panel, "✔", fontSize, dimensions);

                UI.Button(container, panel, "0 0 0 0", string.Empty, 0, dimensions, command);
            }

            public static string Color(string hexColor, float alpha)
            {
                if (hexColor.StartsWith("#"))
                    hexColor = hexColor.TrimStart('#');

                int red = int.Parse(hexColor.Substring(0, 2), NumberStyles.AllowHexSpecifier);
                int green = int.Parse(hexColor.Substring(2, 2), NumberStyles.AllowHexSpecifier);
                int blue = int.Parse(hexColor.Substring(4, 2), NumberStyles.AllowHexSpecifier);

                return $"{(double)red / 255} {(double)green / 255} {(double)blue / 255} {alpha}";
            }
        }

        public class UI4
        {
            public float xMin, yMin, xMax, yMax;

            public UI4(float xMin, float yMin, float xMax, float yMax)
            {
                this.xMin = xMin;
                this.yMin = yMin;
                this.xMax = xMax;
                this.yMax = yMax;
            }

            public string GetMin() => $"{xMin} {yMin}";

            public string GetMax() => $"{xMax} {yMax}";

            private static UI4 _full;

            public static UI4 Full
            {
                get
                {
                    if (_full == null)
                        _full = new UI4(0, 0, 1, 1);
                    return _full;
                }
            }
        }
        #endregion

        #region Config        
        public class ConfigData
        {
            [JsonProperty(PropertyName = "Death screen skull image")]
            public string DeathIcon { get; set; }

            [JsonProperty(PropertyName = "Death screen background image")]
            public string DeathBackground { get; set; }

            [JsonProperty(PropertyName = "Menu Colors")]
            public MenuColors Menu { get; set; }

            [JsonProperty(PropertyName = "Scoreboard Colors")]
            public ScoreboardColors Scoreboard { get; set; }

            public class MenuColors
            {
                [JsonProperty(PropertyName = "Background Color")]
                public UIColor Background { get; set; }

                [JsonProperty(PropertyName = "Foreground Color")]
                public UIColor Foreground { get; set; }

                [JsonProperty(PropertyName = "Panel Color")]
                public UIColor Panel { get; set; }                

                [JsonProperty(PropertyName = "Button Color")]
                public UIColor Button { get; set; }

                [JsonProperty(PropertyName = "Highlight Color")]
                public UIColor Highlight { get; set; }                
            }

            public class ScoreboardColors
            {
                [JsonProperty(PropertyName = "Background Color")]
                public UIColor Background { get; set; }

                [JsonProperty(PropertyName = "Foreground Color")]
                public UIColor Foreground { get; set; }

                [JsonProperty(PropertyName = "Panel Color")]
                public UIColor Panel { get; set; }

                [JsonProperty(PropertyName = "Highlight Color")]
                public UIColor Highlight { get; set; }

                [JsonProperty(PropertyName = "Screen Position")]
                public UIPosition Position { get; set; }
            }

            public class UIColor
            {
                public string Hex { get; set; }
                public float Alpha { get; set; }

                [JsonIgnore]
                private string _color;

                [JsonIgnore]
                public string Get
                {
                    get
                    {
                        if (string.IsNullOrEmpty(_color))
                            _color = EMInterface.UI.Color(Hex, Alpha);
                        return _color;
                    }
                }
            }

            public class UIPosition
            {                
                [JsonProperty(PropertyName = "Center Position X (0.0 - 1.0)")]
                public float CenterX { get; set; }

                [JsonProperty(PropertyName = "Center Position Y (0.0 - 1.0)")]
                public float CenterY { get; set; }

                [JsonProperty(PropertyName = "Panel Width")]
                public float Width { get; set; }

                [JsonProperty(PropertyName = "Panel Height")]
                public float Height { get; set; }

                private UI4 _ui4;

                public UI4 UI4
                {
                    get
                    {
                        if (_ui4 == null)
                            _ui4 = new UI4(CenterX - (Width * 0.5f), CenterY - (Height * 0.5f), CenterX + (Width * 0.5f), CenterY + (Height * 0.5f));
                        return _ui4;
                    }
                }
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
                DeathBackground = "",
                DeathIcon = "https://www.rustedit.io/images/skullicon.png",
                Menu = new ConfigData.MenuColors
                {                    
                    Background = new ConfigData.UIColor { Hex = "#232323", Alpha = 1f },
                    Foreground = new ConfigData.UIColor { Hex = "#252526", Alpha = 1f },
                    Panel = new ConfigData.UIColor { Hex = "#2d2d30", Alpha = 1f },
                    Button = new ConfigData.UIColor { Hex = "#3e3e42", Alpha = 1f },
                    Highlight = new ConfigData.UIColor { Hex = "#007acc", Alpha = 1f },
                },     
                Scoreboard = new ConfigData.ScoreboardColors
                {
                    Background = new ConfigData.UIColor { Hex = "#232323", Alpha = 0.8f },
                    Foreground = new ConfigData.UIColor { Hex = "#252526", Alpha = 0.8f },
                    Panel = new ConfigData.UIColor { Hex = "#2d2d30", Alpha = 0.8f },
                    Highlight = new ConfigData.UIColor { Hex = "#007acc", Alpha = 0.8f },
                    Position = new ConfigData.UIPosition { CenterX = 0.9325f, CenterY = 0.98f, Width = 0.125f, Height = 0.02f }
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

        #region Localization
        private static string Message(string key, ulong playerId = 0UL) => Instance.lang.GetMessage(key, Instance, playerId != 0UL ? playerId.ToString() : null);

        private readonly Dictionary<string, string> Messages = new Dictionary<string, string>
        {
            ["UI.Title"] = "Event Manager",
            ["UI.Statistics.Title"] = "Event Statistics",

            ["UI.Event.Current"] = "Current Event",
            ["UI.Event.NoEvent"] = "No event in progress",
            ["UI.Event.CurrentScores"] = "Scoreboard",
            ["UI.Event.NoScoresRecorded"] = "No scores have been recorded yet",
            ["UI.Event.TeamScore"] = "Team Scores",
            ["UI.Event.Previous"] = "Previous Event Scores",
            ["UI.Event.NoPrevious"] = "No event has been played yet",

            ["UI.Event.Name"] = "Name",
            ["UI.Event.Type"] = "Type",
            ["UI.Event.Status"] = "Status",
            ["UI.Event.Players"] = "Players",
            ["UI.Players.Format"] = "{0} / {1} ({2} joining)",
            ["UI.Event.TimeLimit"] = "Time Limit",
            ["UI.Event.ScoreLimit"] = "Score Limit",
            ["UI.Event.WinReward"] = "Win Reward",
            ["UI.Event.KillReward"] = "Kill Reward",
            ["UI.Event.HeadshotReward"] = "Headshot Reward",

            ["UI.Event.Leave"] = "Leave Event",
            ["UI.Event.Enter"] = "Enter Event",
            ["UI.Popup.EnterEvent"] = "You have entered the event",
            ["UI.Popup.LeaveEvent"] = "You have left the event",

            ["UI.Reward.Format"] = "{0} {1}",
            ["UI.Reward.Scrap"] = "Scrap",
            ["UI.Reward.Economics"] = "Coins",
            ["UI.Reward.ServerRewards"] = "RP",

            ["UI.Admin.Title"] = "Admin Options",
            ["UI.Admin.Start"] = "Start Event",
            ["UI.Admin.Close"] = "Close Event",
            ["UI.Admin.End"] = "End Event",
            ["UI.Admin.Kick"] = "Kick Player",
            ["UI.Admin.Open"] = "Open Event",
            ["UI.Admin.Edit"] = "Edit Event",
            ["UI.Admin.Create"] = "Create Event",
            ["UI.Admin.Delete"] = "Delete Event",

            ["UI.Menu.Admin"] = "Admin",
            ["UI.Menu.Statistics"] = "Statistics",
            ["UI.Menu.Event"] = "Event",

            ["UI.LeaveEvent"] = "Leave Event",
            ["UI.JoinEvent"] = "Join Event",

            ["UI.Statistics.Personal"] = "Personal Statistics",
            ["UI.Statistics.Global"] = "Global Statistics",
            ["UI.Statistics.Leaders"] = "Leader Boards",
            ["UI.NoStatisticsSaved"] = "No statistics have been recorded yet",

            ["UI.Rank"] = "Rank",
            ["UI.GamesPlayed"] = "Games Played",

            ["UI.Next"] = "Next",
            ["UI.Back"] = "Back",

            ["UI.Player"] = "Player",
            ["UI.Score"] = "Score",
            ["UI.Kills"] = "Kills",
            ["UI.Deaths"] = "Deaths",
            ["UI.Assists"] = "Kill Assists",
            ["UI.Headshots"] = "Headshots",
            ["UI.Melee"] = "Melee Kills",
            ["UI.Won"] = "Games Won",
            ["UI.Lost"] = "Games Lost",
            ["UI.Played"] = "Games Played",

            ["UI.Totals"] = "Total {0}",

            ["UI.Return"] = "Return",

            ["UI.Death.Leave"] = "Leave",
            ["UI.Death.Respawn"] = "Respawn",
            ["UI.Death.Respawn.Time"] = "Respawn ({0})",
            ["UI.Death.AutoRespawn"] = "Auto-Respawn",
            ["UI.Death.Class"] = "Change Class",

            ["UI.Score.TeamA"] = "Team A : {0}",
            ["UI.Score.TeamB"] = "Team B : {0}",
        };
        #endregion
    }
}
