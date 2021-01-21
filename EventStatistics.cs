using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Plugins;
using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Event Statistics", "k1lly0u", "0.1.0"), Description("Manages and provides API for statistics gathered from EventManager")]
    public class EventStatistics : RustPlugin
    {
        #region Fields
        private DynamicConfigFile statisticsData;
        
        public static Statistics Data { get; private set; }
        #endregion

        #region Oxide Hooks
        private void Loaded()
        {
            statisticsData = Interface.Oxide.DataFileSystem.GetFile("EventManager/statistics_data");

            LoadData();

            Data.UpdateRankingScores();
        }

        private void OnServerSave() => SaveData();

        private void Unload()
        {
            if (!ServerMgr.Instance.Restarting)
                SaveData();

            Data = null;
        }
        #endregion

        #region API
        [HookMethod("AddStatistic")]
        public void AddStatistic(BasePlayer player, string statistic, int amount = 1) => Data.AddStatistic(player, statistic, amount);

        [HookMethod("AddStatistic")]
        public void AddStatistic(ulong playerId, string statistic, int amount = 1) => Data.AddStatistic(playerId, statistic, amount);

        [HookMethod("AddGlobalStatistic")]
        public void AddGlobalStatistic(string statistic, int amount = 1) => Data.AddGlobalStatistic(statistic, amount);

        [HookMethod("OnGamePlayed")]
        public void OnGamePlayed(string eventName) => Data.OnGamePlayed(eventName);

        [HookMethod("OnGamePlayed")]
        public void OnGamePlayed(BasePlayer player, string eventName) => Data.OnGamePlayed(player, eventName);

        [HookMethod("GetStatistic")]
        public int GetStatistic(ulong playerId, string statistic) => Data.GetStatistic(playerId, statistic);

        [HookMethod("GetRank")]
        public int GetRank(ulong playerId) => Data.GetRank(playerId);

        [HookMethod("GetEventStatistic")]
        public int GetEventStatistic(ulong playerId, string eventName) => Data.GetEventStatistic(playerId, eventName);

        [HookMethod("GetPlayerStatistics")]
        public void GetPlayerStatistics(ref List<KeyValuePair<string, int>> list, ulong playerId) => Data.GetPlayerStatistics(ref list, playerId);

        [HookMethod("GetPlayerEvents")]
        public void GetPlayerEvents(ref List<KeyValuePair<string, int>> list, ulong playerId) => Data.GetPlayerEvents(ref list, playerId);

        [HookMethod("GetGlobalStatistic")]
        public int GetGlobalStatistic(string statistic) => Data.GetGlobalStatistic(statistic);

        [HookMethod("GetGlobalEventStatistic")]
        public int GetGlobalEventStatistic(string eventName) => Data.GetGlobalEventStatistic(eventName);

        [HookMethod("GetGlobalStatistics")]
        public void GetGlobalStatistics(ref List<KeyValuePair<string, int>> list) => Data.GetGlobalStatistics(ref list);

        [HookMethod("GetGlobalEvents")]
        public void GetGlobalEvents(ref List<KeyValuePair<string, int>> list) => Data.GetGlobalEvents(ref list);

        [HookMethod("GetStatisticNames")]
        public void GetStatisticNames(ref List<string> list) => Data.GetStatisticNames(ref list);
        #endregion

        #region Helpers
        private static string RemoveTags(string str)
        {
            foreach (KeyValuePair<string, string> kvp in _tags)
            {
                if (str.StartsWith(kvp.Key) && str.Contains(kvp.Value) && str.Length > str.IndexOf(kvp.Value))
                {
                    str = str.Substring(str.IndexOf(kvp.Value) + 1).Trim();
                }
            }
            return str;
        }

        private static List<KeyValuePair<string, string>> _tags = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("[", "]"),
            new KeyValuePair<string, string>("{", "}"),
            new KeyValuePair<string, string>("(", ")"),
            new KeyValuePair<string, string>("<", ">"),
        };
        #endregion

        #region Statistics
        public enum Statistic { Rank, Name, Kills, Deaths, Assists, Headshots, Melee, Wins, Losses, Played }

        public class Statistics
        {
            public Hash<ulong, Data> players = new Hash<ulong, Data>();

            public Data global = new Data(0UL, "Global");

            [JsonIgnore]
            private Hash<Statistic, List<Data>> _cachedSortResults = new Hash<Statistic, List<Data>>();

            public Data Find(ulong playerId)
            {
                Data data;
                if (players.TryGetValue(playerId, out data))
                    return data;
                return null;
            }            
            
            public void OnGamePlayed(string eventName)
            {
                global.AddGamePlayed(eventName);
                ClearCachedSortResults();
                UpdateRankingScores();
            }

            public void OnGamePlayed(BasePlayer player, string eventName)
            {
                Data data;
                if (!players.TryGetValue(player.userID, out data))
                    players[player.userID] = data = new Data(player.userID, player.displayName);
                else data.UpdateName(player.displayName);

                data.AddGamePlayed(eventName);

                data.UpdateRankingScore();
            }

            public void OnGamePlayed(ulong playerId, string eventName)
            {
                Data data;
                if (!players.TryGetValue(playerId, out data))
                    players[playerId] = data = new Data(playerId, "Unknown");

                data.AddGamePlayed(eventName);

                data.UpdateRankingScore();
            }

            public void AddStatistic(BasePlayer player, string statistic, int amount = 1)
            {
                global.AddStatistic(statistic, amount);

                Data data;
                if (!players.TryGetValue(player.userID, out data))
                    players[player.userID] = data = new Data(player.userID, player.displayName);

                data.AddStatistic(statistic, amount);
            }

            public void AddStatistic(ulong playerId, string statistic, int amount = 1)
            {
                global.AddStatistic(statistic, amount);

                Data data;
                if (!players.TryGetValue(playerId, out data))
                    players[playerId] = data = new Data(playerId, "Unknown");

                data.AddStatistic(statistic, amount);
            }

            public void AddGlobalStatistic(string statistic, int amount = 1)
            {
                global.AddStatistic(statistic, amount);
            }

            public int GetStatistic(ulong playerId, string statistic)
            {
                Data data;
                if (players.TryGetValue(playerId, out data))
                {
                    int amount;
                    if (data.statistics.TryGetValue(statistic, out amount))
                        return amount;
                }
                return 0;
            }

            public void GetPlayerStatistics(ref List<KeyValuePair<string, int>> list, ulong playerId)
            {
                Data data;
                if (players.TryGetValue(playerId, out data))                
                    list.AddRange(data.statistics);
            }

            public void GetPlayerEvents(ref List<KeyValuePair<string, int>> list, ulong playerId)
            {
                Data data;
                if (players.TryGetValue(playerId, out data))
                    list.AddRange(data.events);
            }

            public int GetRank(ulong playerId)
            {
                List<Data> list = SortStatisticsBy(Statistic.Rank);

                for (int i = 0; i < list.Count; i++)
                {
                    Data data = list[i];
                    if (data.UserID.Equals(playerId))
                        return i + 1;
                }               
                
                return -1;
            }

            public int GetEventStatistic(ulong playerId, string eventName)
            {
                Data data;
                if (players.TryGetValue(playerId, out data))
                {
                    int amount;
                    if (data.events.TryGetValue(eventName, out amount))
                        return amount;
                }
                return 0;
            }

            public int GetGlobalStatistic(string statistic)
            {
                int amount;
                if (global.statistics.TryGetValue(statistic, out amount))
                    return amount;
                return 0;
            }

            public int GetGlobalEventStatistic(string eventName)
            {
                int amount;
                if (global.events.TryGetValue(eventName, out amount))
                    return amount;
                return 0;
            }

            public void GetGlobalStatistics(ref List<KeyValuePair<string, int>> list)
            {
                list.AddRange(global.statistics);
            }

            public void GetGlobalEvents(ref List<KeyValuePair<string, int>> list)
            {
                list.AddRange(global.events);                
            }

            public void GetStatisticNames(ref List<string> list) => list.AddRange(global.statistics.Keys);

            public void ClearCachedSortResults()
            {
                foreach (KeyValuePair<Statistic, List<Data>> kvp in _cachedSortResults)
                {
                    List<Data> list = kvp.Value;
                    Facepunch.Pool.FreeList(ref list);
                }

                _cachedSortResults.Clear();
            }

            public List<Data> SortStatisticsBy(Statistic statistic)
            {
                List<Data> list;
                if (_cachedSortResults.TryGetValue(statistic, out list))
                    return list;
                else
                {
                    _cachedSortResults[statistic] = list = Facepunch.Pool.GetList<Data>();

                    string statisticString = statistic.ToString();

                    list.AddRange(players.Values);
                    list.Sort(delegate (Data a, Data b)
                    {
                        if (a == null || b == null)
                            return 0;

                        switch (statistic)
                        {
                            case Statistic.Rank:
                                return a.Score.CompareTo(b.Score);
                            case Statistic.Name:
                                return a.DisplayName.CompareTo(b.DisplayName);
                            case Statistic.Kills:
                            case Statistic.Deaths:
                            case Statistic.Assists:
                            case Statistic.Headshots:
                            case Statistic.Melee:
                            case Statistic.Wins:
                            case Statistic.Losses:
                            case Statistic.Played:
                                return a.GetStatistic(statisticString).CompareTo(b.GetStatistic(statisticString));
                        }

                        return 0;
                    });

                    if (statistic != Statistic.Name)
                        list.Reverse();

                    return list;
                }
            }

            public void UpdateRankingScores()
            {
                foreach (KeyValuePair<ulong, Data> player in players)
                    player.Value.UpdateRankingScore();

                List<Data> list = SortStatisticsBy(Statistic.Rank);

                for (int i = 0; i < list.Count; i++)
                {
                    list[i].Rank = i + 1;
                }
            }

            public class Data
            {
                public Hash<string, int> events = new Hash<string, int>();

                public Hash<string, int> statistics = new Hash<string, int>()
                {
                    ["Kills"] = 0,
                    ["Deaths"] = 0,
                    ["Assists"] = 0,
                    ["Headshots"] = 0,
                    ["Melee"] = 0,
                    ["Wins"] = 0,
                    ["Losses"] = 0,
                    ["Played"] = 0
                };   
                               
                public string DisplayName { get; set; }

                public ulong UserID { get; set; }

                [JsonIgnore]
                public float Score { get; private set; }

                [JsonIgnore]
                public int Rank { get; set; }
                               
                public Data(ulong userID, string displayName)
                {
                    this.UserID = userID;
                    this.DisplayName = RemoveTags(displayName);
                }

                public void UpdateName(string displayName)
                {
                    this.DisplayName = RemoveTags(displayName);
                }

                public void AddStatistic(string statisticName, int value)
                {
                    statistics[statisticName] += value;
                }

                public void AddGamePlayed(string name)
                {
                    events[name] += 1;
                    UpdateRankingScore();
                }

                public int GetStatistic(string statistic)
                {
                    int value = 0;
                    statistics.TryGetValue(statistic, out value);
                    return value;
                }

                public void UpdateRankingScore()
                {
                    Score = 0;
                    Score += GetStatistic("Kills");
                    Score += Mathf.CeilToInt(GetStatistic("Assists") * 0.25f);
                    Score += Mathf.CeilToInt(GetStatistic("Melee") * 0.25f);
                    Score += Mathf.CeilToInt(GetStatistic("Headshots") * 0.5f);
                    Score += Mathf.CeilToInt(GetStatistic("Played") * 0.5f);
                    Score += GetStatistic("Wins") * 2;
                }
            }
        }
        #endregion

        #region Data Management
        private void SaveData() => statisticsData.WriteObject(Data);

        private void LoadData()
        {
            try
            {
                Data = statisticsData.ReadObject<Statistics>();

                if (Data == null)
                    Data = new Statistics();
            }
            catch
            {
                Data = new Statistics();
            }
        }
        #endregion
    }
}
