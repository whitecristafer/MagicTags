using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("MagicTags", "whitecristafer", "2.1.0")]
    [Description("MagicTags 2.1.0 - configurable overhead prefix renderer with permission/group rules, personal prefixes and RU/EN localization.")]
    public class MagicTags : RustPlugin
    {
        private const ulong PluginIcon = 76561198209258869;
        private const string ChatPrefix = "<size=12><color=#cc66ff><b>MagicTags</b></color></size> |";

        private const string PermSee = "magictags.see";
        private const string PermHide = "magictags.hide";
        private const string PermCustomPrefix = "magictags.customprefix";
        private const string PermCustomColor = "magictags.customcolor";
        private const string PermPersonal = "magictags.personal";
        private const string PermReload = "magictags.reload";
        private const string PermManage = "magictags.manage";

        private const string DataFileName = "MagicTags_Data";

        private PluginConfig _config;
        private StoredData _data;
        private Timer _refreshTimer;
        private readonly HashSet<ulong> _temporaryAdminFlags = new HashSet<ulong>();

        #region Configuration

        private class PluginConfig
        {
            [JsonProperty("General")]
            public GeneralSettings General = new GeneralSettings();

            [JsonProperty("Default Prefix")]
            public PrefixStyle DefaultPrefix = new PrefixStyle();

            [JsonProperty("Prefixes")]
            public List<PrefixRule> Prefixes = new List<PrefixRule>();
        }

        private class GeneralSettings
        {
            [JsonProperty("Enabled")]
            public bool Enabled = true;

            [JsonProperty("Update interval (seconds)")]
            public float UpdateInterval = 0.5f;

            [JsonProperty("View distance (meters)")]
            public float ViewDistance = 60f;

            [JsonProperty("Text lifetime (seconds)")]
            public float TextLifetime = 0.75f;

            [JsonProperty("Text height offset")]
            public float TextHeight = 0.80f;

            [JsonProperty("Show tags to self")]
            public bool ShowSelf = false;

            [JsonProperty("Show only for permission")]
            public bool ShowOnlyForPermission = true;

            [JsonProperty("Require admin flag for radar mode")]
            public bool RequireAdminFlagForRadarMode = true;

            [JsonProperty("Use default fallback")]
            public bool UseDefaultFallback = true;

            [JsonProperty("Log debug")]
            public bool LogDebug = false;
        }

        private class PrefixStyle
        {
            [JsonProperty("Enabled")]
            public bool Enabled = true;

            [JsonProperty("Text")]
            public string Text = "[PLAYER]";

            [JsonProperty("Color")]
            public string Color = "#ffffff";
        }

        private enum PrefixAccessType
        {
            Permission,
            Group,
            Any
        }

        private class PrefixRule
        {
            [JsonProperty("Key")]
            public string Key = "vip";

            [JsonProperty("Enabled")]
            public bool Enabled = true;

            [JsonProperty("Priority")]
            public int Priority = 0;

            [JsonProperty("Type")]
            public PrefixAccessType Type = PrefixAccessType.Permission;

            [JsonProperty("Access")]
            public string Access = string.Empty;

            [JsonProperty("Text")]
            public string Text = "[VIP]";

            [JsonProperty("Color")]
            public string Color = "#ff66cc";

            [JsonProperty("Size")]
            public int Size = 16;
        }

        protected override void LoadDefaultConfig()
        {
            _config = new PluginConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<PluginConfig>();
            }
            catch
            {
                PrintWarning("MagicTags config is corrupted. Creating a new default config.");
                _config = null;
            }

            if (_config == null)
                _config = new PluginConfig();

            NormalizeConfig();
            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        private void NormalizeConfig()
        {
            if (_config.General == null)
                _config.General = new GeneralSettings();

            if (_config.DefaultPrefix == null)
                _config.DefaultPrefix = new PrefixStyle();

            if (_config.Prefixes == null)
                _config.Prefixes = new List<PrefixRule>();

            _config.General.UpdateInterval = Mathf.Clamp(_config.General.UpdateInterval, 0.1f, 10f);
            _config.General.ViewDistance = Mathf.Clamp(_config.General.ViewDistance, 1f, 500f);
            _config.General.TextLifetime = Mathf.Clamp(_config.General.TextLifetime, 0.1f, 10f);
            _config.General.TextHeight = Mathf.Clamp(_config.General.TextHeight, 0.1f, 20f);

            _config.DefaultPrefix.Text = NormalizeText(_config.DefaultPrefix.Text, "[PLAYER]");
            _config.DefaultPrefix.Color = NormalizeHex(_config.DefaultPrefix.Color, "#cfcfcf");

            for (int i = 0; i < _config.Prefixes.Count; i++)
            {
                PrefixRule rule = _config.Prefixes[i];
                if (rule == null)
                {
                    _config.Prefixes[i] = new PrefixRule();
                    rule = _config.Prefixes[i];
                }

                rule.Key = string.IsNullOrWhiteSpace(rule.Key) ? "rule_" + i : rule.Key.Trim().ToLowerInvariant();
                rule.Access = string.IsNullOrWhiteSpace(rule.Access) ? string.Empty : rule.Access.Trim();
                rule.Text = NormalizeText(rule.Text, "[TAG]");
                rule.Color = NormalizeHex(rule.Color, "#ffffff");
                rule.Size = Mathf.Clamp(rule.Size, 10, 24);
            }

            _config.Prefixes = _config.Prefixes
                .Where(x => x != null)
                .OrderByDescending(x => x.Priority)
                .ThenBy(x => x.Key)
                .ToList();
        }

        #endregion

        #region Data

        private class StoredData
        {
            [JsonProperty("Players")]
            public Dictionary<ulong, PlayerProfile> Players = new Dictionary<ulong, PlayerProfile>();
        }

        private class PlayerProfile
        {
            [JsonProperty("Hidden")]
            public bool Hidden;

            [JsonProperty("CustomPrefix")]
            public string CustomPrefix = string.Empty;

            [JsonProperty("CustomPrefixColor")]
            public string CustomPrefixColor = string.Empty;

            [JsonProperty("UpdatedBy")]
            public string UpdatedBy = string.Empty;

            [JsonProperty("UpdatedAt")]
            public string UpdatedAt = string.Empty;
        }

        private void LoadData()
        {
            try
            {
                _data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(DataFileName);
            }
            catch
            {
                _data = null;
            }

            if (_data == null)
                _data = new StoredData();

            if (_data.Players == null)
                _data.Players = new Dictionary<ulong, PlayerProfile>();
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(DataFileName, _data);
        }

        private PlayerProfile GetProfile(ulong userId)
        {
            if (_data.Players == null)
                _data.Players = new Dictionary<ulong, PlayerProfile>();

            PlayerProfile profile;
            if (!_data.Players.TryGetValue(userId, out profile) || profile == null)
            {
                profile = new PlayerProfile();
                _data.Players[userId] = profile;
            }

            return profile;
        }

        #endregion

        #region Localization

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Prefix"] = ChatPrefix,
                ["NoPermission"] = "You don't have permission to use this command.",
                ["PluginDisabled"] = "MagicTags is disabled in the config.",
                ["Help"] = "MagicTags commands:\n" +
                           "/magictags info - plugin info\n" +
                           "/magictags hide - hide your tag\n" +
                           "/magictags show - show your tag\n" +
                           "/magictags prefix <text> - set your custom prefix\n" +
                           "/magictags color <#hex> - set your custom prefix color\n" +
                           "/magictags clear - clear your custom prefix\n" +
                           "/magictags personal <set|color|clear|hide|show|info> <player> [value] - admin tools\n" +
                           "/magictags list [page] - list configured prefix rules\n" +
                           "/magictags sync - refresh all players\n" +
                           "/magictags reload - reload config and data",
                ["Info"] = "MagicTags v{0}\nPlayers with data: {1}\nUpdate interval: {2}s\nView distance: {3}m\nRules: {4}",
                ["Reloaded"] = "MagicTags reloaded.",
                ["Synced"] = "All players refreshed.",
                ["HiddenOn"] = "Your tag is now hidden.",
                ["HiddenOff"] = "Your tag is now visible.",
                ["PrefixSet"] = "Custom prefix saved: {0}",
                ["ColorSet"] = "Custom color saved: {0}",
                ["Cleared"] = "Custom prefix data cleared.",
                ["InvalidColor"] = "Invalid hex color. Example: #ff66cc",
                ["InvalidUsage"] = "Invalid usage.",
                ["PlayerNotFound"] = "Player not found.",
                ["PersonalSet"] = "Custom prefix set for {0}.",
                ["PersonalColor"] = "Custom color set for {0}.",
                ["PersonalHidden"] = "Hidden state changed for {0}.",
                ["PersonalCleared"] = "Custom data cleared for {0}.",
                ["PersonalInfo"] = "Player {0}: hidden={1}, prefix='{2}', color='{3}', updated_by={4}, updated_at={5}",
                ["RuleListHeader"] = "Prefix rules page {0}/{1}:",
                ["RuleListEntry"] = "  {0}. key={1} enabled={2} priority={3} type={4} access={5} text='{6}' color={7}",
                ["RuleAdded"] = "Prefix rule '{0}' added.",
                ["RuleRemoved"] = "Prefix rule '{0}' removed.",
                ["RuleUpdated"] = "Prefix rule '{0}' updated.",
                ["RuleNotFound"] = "Prefix rule '{0}' not found.",
                ["RuleExists"] = "Prefix rule '{0}' already exists.",
                ["CommandDenied"] = "You need the required permission for this action."
            }, this, "en");

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Prefix"] = ChatPrefix,
                ["NoPermission"] = "У вас нет прав на использование этой команды.",
                ["PluginDisabled"] = "MagicTags выключен в конфиге.",
                ["Help"] = "Команды MagicTags:\n" +
                           "/magictags info - информация о плагине\n" +
                           "/magictags hide - скрыть свой тег\n" +
                           "/magictags show - показать свой тег\n" +
                           "/magictags prefix <текст> - установить свой префикс\n" +
                           "/magictags color <#hex> - установить цвет префикса\n" +
                           "/magictags clear - очистить свой префикс\n" +
                           "/magictags personal <set|color|clear|hide|show|info> <игрок> [значение] - админка\n" +
                           "/magictags list [страница] - список префикс-правил\n" +
                           "/magictags sync - обновить всех игроков\n" +
                           "/magictags reload - перезагрузить конфиг и данные",
                ["Info"] = "MagicTags v{0}\nИгроков с данными: {1}\nИнтервал обновления: {2}с\nДальность отображения: {3}м\nПравил: {4}",
                ["Reloaded"] = "MagicTags перезагружен.",
                ["Synced"] = "Все игроки обновлены.",
                ["HiddenOn"] = "Тег теперь скрыт.",
                ["HiddenOff"] = "Тег теперь виден.",
                ["PrefixSet"] = "Префикс сохранён: {0}",
                ["ColorSet"] = "Цвет сохранён: {0}",
                ["Cleared"] = "Префикс очищен.",
                ["InvalidColor"] = "Неверный hex-цвет. Пример: #ff66cc",
                ["InvalidUsage"] = "Неверное использование.",
                ["PlayerNotFound"] = "Игрок не найден.",
                ["PersonalSet"] = "Префикс установлен для {0}.",
                ["PersonalColor"] = "Цвет установлен для {0}.",
                ["PersonalHidden"] = "Статус скрытия изменён для {0}.",
                ["PersonalCleared"] = "Данные очищены для {0}.",
                ["PersonalInfo"] = "Игрок {0}: hidden={1}, prefix='{2}', color='{3}', updated_by={4}, updated_at={5}",
                ["RuleListHeader"] = "Префиксы, страница {0}/{1}:",
                ["RuleListEntry"] = "  {0}. key={1} enabled={2} priority={3} type={4} access={5} text='{6}' color={7}",
                ["RuleAdded"] = "Правило префикса '{0}' добавлено.",
                ["RuleRemoved"] = "Правило префикса '{0}' удалено.",
                ["RuleUpdated"] = "Правило префикса '{0}' обновлено.",
                ["RuleNotFound"] = "Правило префикса '{0}' не найдено.",
                ["RuleExists"] = "Правило префикса '{0}' уже существует.",
                ["CommandDenied"] = "Для этого действия нужны права."
            }, this, "ru");
        }

        private string Lang(string key, string playerId, params object[] args)
        {
            return string.Format(lang.GetMessage(key, this, playerId ?? "0"), args);
        }

        #endregion

        #region Lifecycle

        private void Init()
        {
            RegisterPermissions();
            LoadConfig();
            LoadData();
        }

        private void OnServerInitialized()
        {
            StartRefreshTimer();
            NextTick(RefreshAllPlayers);
            PrintBanner();
        }

        private void Unload()
        {
            if (_refreshTimer != null)
            {
                _refreshTimer.Destroy();
                _refreshTimer = null;
            }

            RemoveTemporaryAdminFlags();
            SaveData();
        }

        private void PrintBanner()
        {
            Puts("=================================================");
            Puts(" MagicTags v" + Version + " by whitecristafer");
            Puts(" Config-driven overhead tags via ddraw.text");
            Puts("=================================================");
        }

        #endregion

        #region Permissions

        private void RegisterPermissions()
        {
            permission.RegisterPermission(PermSee, this);
            permission.RegisterPermission(PermHide, this);
            permission.RegisterPermission(PermCustomPrefix, this);
            permission.RegisterPermission(PermCustomColor, this);
            permission.RegisterPermission(PermPersonal, this);
            permission.RegisterPermission(PermReload, this);
            permission.RegisterPermission(PermManage, this);
        }

        private bool HasPermission(BasePlayer player, string perm)
        {
            if (player == null)
                return true;

            if (player.IsAdmin)
                return true;

            return permission.UserHasPermission(player.UserIDString, perm);
        }

        private bool CanSeeTags(BasePlayer player)
        {
            if (player == null)
                return false;

            if (player.IsAdmin)
                return true;

            return permission.UserHasPermission(player.UserIDString, PermSee);
        }

        #endregion

        #region Hooks

        private void OnPlayerConnected(BasePlayer player)
        {
            if (player == null)
                return;

            NextTick(delegate
            {
                RefreshPlayer(player);
            });
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null)
                return;

            _temporaryAdminFlags.Remove(player.userID);
        }

        private void OnUserGroupAdded(string id, string groupName)
        {
            RefreshByUserId(id);
        }

        private void OnUserGroupRemoved(string id, string groupName)
        {
            RefreshByUserId(id);
        }

        private void OnUserPermissionGranted(string id, string perm)
        {
            RefreshByUserId(id);
        }

        private void OnUserPermissionRevoked(string id, string perm)
        {
            RefreshByUserId(id);
        }

        private object OnPlayerChat(BasePlayer player, string message, ConVar.Chat.ChatChannel channel)
        {
            if (player == null || string.IsNullOrEmpty(message))
                return null;

            if (!_config.General.Enabled)
                return null;

            PlayerProfile profile = GetProfile(player.userID);
            if (profile != null && profile.Hidden)
                return null;

            PrefixOutput output = ResolvePrefix(player);
            if (output == null || string.IsNullOrWhiteSpace(output.Text))
                return null;

            string prefix = BuildChatPrefix(output);
            string formatted = string.Format("{0}{1}: {2}", prefix, player.displayName, message);
            return formatted;
        }

        #endregion

        #region Refresh Loop

        private void StartRefreshTimer()
        {
            if (_refreshTimer != null)
            {
                _refreshTimer.Destroy();
                _refreshTimer = null;
            }

            _refreshTimer = timer.Every(_config.General.UpdateInterval, RefreshAllPlayers);
        }

        private void RefreshAllPlayers()
        {
            if (_config == null || _config.General == null || !_config.General.Enabled)
                return;

            for (int i = 0; i < BasePlayer.activePlayerList.Count; i++)
            {
                BasePlayer viewer = BasePlayer.activePlayerList[i];
                if (viewer == null || !viewer.IsConnected)
                    continue;

                if (!CanSeeTags(viewer))
                    continue;

                bool tempFlag = EnsureTemporaryAdminFlag(viewer);

                for (int j = 0; j < BasePlayer.activePlayerList.Count; j++)
                {
                    BasePlayer target = BasePlayer.activePlayerList[j];
                    if (target == null || !target.IsConnected)
                        continue;

                    if (!ShouldShowToViewer(viewer, target))
                        continue;

                    DrawTag(viewer, target);
                }

                if (tempFlag)
                    ScheduleRemoveTemporaryFlag(viewer);
            }
        }

        private void RefreshPlayer(BasePlayer viewer)
        {
            if (viewer == null || !viewer.IsConnected)
                return;

            if (!CanSeeTags(viewer))
                return;

            bool tempFlag = EnsureTemporaryAdminFlag(viewer);

            for (int i = 0; i < BasePlayer.activePlayerList.Count; i++)
            {
                BasePlayer target = BasePlayer.activePlayerList[i];
                if (target == null || !target.IsConnected)
                    continue;

                if (!ShouldShowToViewer(viewer, target))
                    continue;

                DrawTag(viewer, target);
            }

            if (tempFlag)
                ScheduleRemoveTemporaryFlag(viewer);
        }

        private void RefreshByUserId(string id)
        {
            ulong userId;
            if (!ulong.TryParse(id, out userId))
                return;

            BasePlayer player = BasePlayer.FindByID(userId);
            if (player != null)
                RefreshPlayer(player);
        }

        #endregion

        #region Tag Resolution

        private class PrefixOutput
        {
            public string Text;
            public string Color;
            public int Size;
        }

        private bool ShouldShowToViewer(BasePlayer viewer, BasePlayer target)
        {
            if (viewer == null || target == null)
                return false;

            if (!viewer.IsConnected || !target.IsConnected)
                return false;

            if (!_config.General.ShowSelf && viewer.userID == target.userID)
                return false;

            if (_config.General.ShowOnlyForPermission && !viewer.IsAdmin && !permission.UserHasPermission(viewer.UserIDString, PermSee))
                return false;

            float distance = Vector3.Distance(GetViewPosition(viewer), GetViewPosition(target));
            return distance <= _config.General.ViewDistance;
        }

        private PrefixOutput ResolvePrefix(BasePlayer target)
        {
            if (target == null)
                return null;

            PlayerProfile profile = GetProfile(target.userID);
            if (profile != null && profile.Hidden)
                return null;

            if (profile != null && !string.IsNullOrWhiteSpace(profile.CustomPrefix))
            {
                PrefixOutput personal = new PrefixOutput();
                personal.Text = profile.CustomPrefix.Trim();
                personal.Color = NormalizeHex(profile.CustomPrefixColor, "#ff66cc");
                personal.Size = 16;
                return personal;
            }

            PrefixRule matched = FindMatchingRule(target);
            if (matched != null)
            {
                PrefixOutput output = new PrefixOutput();
                output.Text = matched.Text;
                output.Color = matched.Color;
                output.Size = matched.Size;
                return output;
            }

            if (_config.General.UseDefaultFallback && _config.DefaultPrefix != null && _config.DefaultPrefix.Enabled)
            {
                PrefixOutput fallback = new PrefixOutput();
                fallback.Text = _config.DefaultPrefix.Text;
                fallback.Color = _config.DefaultPrefix.Color;
                fallback.Size = 16;
                return fallback;
            }

            return null;
        }

        private PrefixRule FindMatchingRule(BasePlayer player)
        {
            if (player == null || _config.Prefixes == null)
                return null;

            for (int i = 0; i < _config.Prefixes.Count; i++)
            {
                PrefixRule rule = _config.Prefixes[i];
                if (rule == null || !rule.Enabled)
                    continue;

                if (MatchesRule(player, rule))
                    return rule;
            }

            return null;
        }

        private bool MatchesRule(BasePlayer player, PrefixRule rule)
        {
            if (player == null || rule == null)
                return false;

            string access = string.IsNullOrWhiteSpace(rule.Access) ? string.Empty : rule.Access.Trim();

            if (string.IsNullOrWhiteSpace(access))
                return false;

            switch (rule.Type)
            {
                case PrefixAccessType.Permission:
                    return permission.UserHasPermission(player.UserIDString, access);

                case PrefixAccessType.Group:
                    return permission.UserHasGroup(player.UserIDString, access);

                case PrefixAccessType.Any:
                    return permission.UserHasPermission(player.UserIDString, access) || permission.UserHasGroup(player.UserIDString, access);

                default:
                    return false;
            }
        }

        private string BuildChatPrefix(PrefixOutput output)
        {
            if (output == null || string.IsNullOrWhiteSpace(output.Text))
                return string.Empty;

            int size = Mathf.Clamp(output.Size, 10, 24);
            string color = NormalizeHex(output.Color, "#ff66cc");
            return "<size=" + size + "><color=" + color + "><b>" + EscapeRichText(output.Text) + "</b></color></size> ";
        }

        private void DrawTag(BasePlayer viewer, BasePlayer target)
        {
            if (viewer == null || target == null)
                return;

            PrefixOutput output = ResolvePrefix(target);
            if (output == null || string.IsNullOrWhiteSpace(output.Text))
                return;

            Vector3 position = GetViewPosition(target) + new Vector3(0f, _config.General.TextHeight, 0f);
            Color drawColor;
            if (!TryParseColor(output.Color, out drawColor))
                drawColor = Color.white;

            string tagText = "<size=" + Mathf.Clamp(output.Size, 10, 24) + "><color=" + NormalizeHex(output.Color, "#ffffff") + "><b>" + EscapeRichText(output.Text) + "</b></color></size>";
            viewer.SendConsoleCommand("ddraw.text", _config.General.TextLifetime, drawColor, position, tagText);
        }

        private Vector3 GetViewPosition(BasePlayer player)
        {
            if (player == null)
                return Vector3.zero;

            if (player.eyes != null)
                return player.eyes.position;

            return player.transform != null ? player.transform.position : Vector3.zero;
        }

        #endregion

        #region Temporary Admin Flag

        private bool EnsureTemporaryAdminFlag(BasePlayer player)
        {
            if (player == null)
                return false;

            if (!_config.General.RequireAdminFlagForRadarMode)
                return false;

            if (player.IsAdmin)
                return false;

            if (_temporaryAdminFlags.Contains(player.userID))
                return false;

            if (!permission.UserHasPermission(player.UserIDString, PermSee))
                return false;

            player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, true);
            player.SendNetworkUpdateImmediate();
            _temporaryAdminFlags.Add(player.userID);
            return true;
        }

        private void ScheduleRemoveTemporaryFlag(BasePlayer player)
        {
            if (player == null)
                return;

            timer.Once(0.1f, delegate
            {
                if (player == null || !player.IsConnected)
                    return;

                if (!_temporaryAdminFlags.Contains(player.userID))
                    return;

                player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false);
                player.SendNetworkUpdateImmediate();
                _temporaryAdminFlags.Remove(player.userID);
            });
        }

        private void RemoveTemporaryAdminFlags()
        {
            List<ulong> ids = _temporaryAdminFlags.ToList();
            for (int i = 0; i < ids.Count; i++)
            {
                ulong id = ids[i];
                BasePlayer player = BasePlayer.FindByID(id);
                if (player == null)
                    continue;

                if (player.IsDestroyed)
                    continue;

                player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false);
                player.SendNetworkUpdateImmediate();
            }

            _temporaryAdminFlags.Clear();
        }

        #endregion

        #region Commands

        [ChatCommand("magictags")]
        private void CmdMagicTags(BasePlayer player, string command, string[] args)
        {
            HandleCommand(player, args);
        }

        [ChatCommand("mtags")]
        private void CmdMTags(BasePlayer player, string command, string[] args)
        {
            HandleCommand(player, args);
        }

        [ConsoleCommand("magictags")]
        private void CCmdMagicTags(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            string[] args = arg.Args != null ? arg.Args.Select(x => x.ToString()).ToArray() : new string[0];
            HandleCommand(player, args);
        }

        private void HandleCommand(BasePlayer player, string[] args)
        {
            if (args == null || args.Length == 0)
            {
                Reply(player, "Help");
                return;
            }

            string sub = args[0].ToLowerInvariant();

            switch (sub)
            {
                case "help":
                    Reply(player, "Help");
                    return;

                case "info":
                    Reply(player, "Info", Version, _data.Players.Count, _config.General.UpdateInterval, _config.General.ViewDistance, _config.Prefixes.Count);
                    return;

                case "hide":
                    HandleHide(player, true);
                    return;

                case "show":
                    HandleHide(player, false);
                    return;

                case "prefix":
                    HandleSelfPrefix(player, args);
                    return;

                case "color":
                    HandleSelfColor(player, args);
                    return;

                case "clear":
                    HandleClearSelf(player);
                    return;

                case "personal":
                    HandlePersonal(player, args);
                    return;

                case "list":
                    HandleList(player, args);
                    return;

                case "add":
                    HandleAddRule(player, args);
                    return;

                case "set":
                    HandleSetRule(player, args);
                    return;

                case "remove":
                case "delete":
                    HandleRemoveRule(player, args);
                    return;

                case "reload":
                    if (!HasPermission(player, PermReload) && !HasPermission(player, PermManage))
                    {
                        Reply(player, "CommandDenied");
                        return;
                    }

                    LoadConfig();
                    LoadData();
                    StartRefreshTimer();
                    RefreshAllPlayers();
                    Reply(player, "Reloaded");
                    return;

                case "sync":
                    if (!HasPermission(player, PermManage))
                    {
                        Reply(player, "CommandDenied");
                        return;
                    }

                    RefreshAllPlayers();
                    Reply(player, "Synced");
                    return;

                default:
                    Reply(player, "Help");
                    return;
            }
        }

        private void HandleHide(BasePlayer player, bool hide)
        {
            if (player == null)
                return;

            if (!HasPermission(player, PermHide) && !HasPermission(player, PermPersonal))
            {
                Reply(player, "NoPermission");
                return;
            }

            PlayerProfile profile = GetProfile(player.userID);
            profile.Hidden = hide;
            profile.UpdatedBy = player.UserIDString;
            profile.UpdatedAt = DateTime.UtcNow.ToString("u");
            SaveData();
            RefreshPlayer(player);
            Reply(player, hide ? "HiddenOn" : "HiddenOff");
        }

        private void HandleSelfPrefix(BasePlayer player, string[] args)
        {
            if (player == null)
                return;

            if (!HasPermission(player, PermCustomPrefix) && !HasPermission(player, PermPersonal))
            {
                Reply(player, "NoPermission");
                return;
            }

            if (args.Length < 2)
            {
                Reply(player, "InvalidUsage");
                return;
            }

            string prefix = string.Join(" ", args.Skip(1).ToArray()).Trim();
            PlayerProfile profile = GetProfile(player.userID);
            profile.CustomPrefix = prefix;
            profile.UpdatedBy = player.UserIDString;
            profile.UpdatedAt = DateTime.UtcNow.ToString("u");
            SaveData();
            RefreshPlayer(player);
            Reply(player, "PrefixSet", prefix);
        }

        private void HandleSelfColor(BasePlayer player, string[] args)
        {
            if (player == null)
                return;

            if (!HasPermission(player, PermCustomColor) && !HasPermission(player, PermPersonal))
            {
                Reply(player, "NoPermission");
                return;
            }

            if (args.Length < 2)
            {
                Reply(player, "InvalidUsage");
                return;
            }

            string color = NormalizeHex(args[1], string.Empty);
            if (string.IsNullOrWhiteSpace(color))
            {
                Reply(player, "InvalidColor");
                return;
            }

            PlayerProfile profile = GetProfile(player.userID);
            profile.CustomPrefixColor = color;
            profile.UpdatedBy = player.UserIDString;
            profile.UpdatedAt = DateTime.UtcNow.ToString("u");
            SaveData();
            RefreshPlayer(player);
            Reply(player, "ColorSet", color);
        }

        private void HandleClearSelf(BasePlayer player)
        {
            if (player == null)
                return;

            if (!HasPermission(player, PermCustomPrefix) && !HasPermission(player, PermPersonal) && !HasPermission(player, PermHide))
            {
                Reply(player, "NoPermission");
                return;
            }

            PlayerProfile profile = GetProfile(player.userID);
            profile.CustomPrefix = string.Empty;
            profile.CustomPrefixColor = string.Empty;
            profile.Hidden = false;
            profile.UpdatedBy = player.UserIDString;
            profile.UpdatedAt = DateTime.UtcNow.ToString("u");
            SaveData();
            RefreshPlayer(player);
            Reply(player, "Cleared");
        }

        private void HandlePersonal(BasePlayer player, string[] args)
        {
            if (!HasPermission(player, PermPersonal) && !HasPermission(player, PermManage))
            {
                Reply(player, "CommandDenied");
                return;
            }

            if (args.Length < 3)
            {
                Reply(player, "InvalidUsage");
                return;
            }

            string action = args[1].ToLowerInvariant();
            string targetText = args[2];

            BasePlayer target;
            if (!TryFindPlayer(targetText, out target))
            {
                Reply(player, "PlayerNotFound");
                return;
            }

            PlayerProfile profile = GetProfile(target.userID);

            switch (action)
            {
                case "set":
                    if (args.Length < 4)
                    {
                        Reply(player, "InvalidUsage");
                        return;
                    }

                    profile.CustomPrefix = string.Join(" ", args.Skip(3).ToArray()).Trim();
                    profile.UpdatedBy = player != null ? player.UserIDString : "console";
                    profile.UpdatedAt = DateTime.UtcNow.ToString("u");
                    SaveData();
                    RefreshPlayer(target);
                    Reply(player, "PersonalSet", target.displayName);
                    return;

                case "color":
                    if (args.Length < 4)
                    {
                        Reply(player, "InvalidUsage");
                        return;
                    }

                    string color = NormalizeHex(args[3], string.Empty);
                    if (string.IsNullOrWhiteSpace(color))
                    {
                        Reply(player, "InvalidColor");
                        return;
                    }

                    profile.CustomPrefixColor = color;
                    profile.UpdatedBy = player != null ? player.UserIDString : "console";
                    profile.UpdatedAt = DateTime.UtcNow.ToString("u");
                    SaveData();
                    RefreshPlayer(target);
                    Reply(player, "PersonalColor", target.displayName);
                    return;

                case "hide":
                    profile.Hidden = true;
                    profile.UpdatedBy = player != null ? player.UserIDString : "console";
                    profile.UpdatedAt = DateTime.UtcNow.ToString("u");
                    SaveData();
                    RefreshPlayer(target);
                    Reply(player, "PersonalHidden", target.displayName);
                    return;

                case "show":
                    profile.Hidden = false;
                    profile.UpdatedBy = player != null ? player.UserIDString : "console";
                    profile.UpdatedAt = DateTime.UtcNow.ToString("u");
                    SaveData();
                    RefreshPlayer(target);
                    Reply(player, "PersonalHidden", target.displayName);
                    return;

                case "clear":
                    profile.CustomPrefix = string.Empty;
                    profile.CustomPrefixColor = string.Empty;
                    profile.Hidden = false;
                    profile.UpdatedBy = player != null ? player.UserIDString : "console";
                    profile.UpdatedAt = DateTime.UtcNow.ToString("u");
                    SaveData();
                    RefreshPlayer(target);
                    Reply(player, "PersonalCleared", target.displayName);
                    return;

                case "info":
                    Reply(player, "PersonalInfo", target.displayName, profile.Hidden, profile.CustomPrefix, profile.CustomPrefixColor, profile.UpdatedBy, profile.UpdatedAt);
                    return;

                default:
                    Reply(player, "InvalidUsage");
                    return;
            }
        }

        private void HandleList(BasePlayer player, string[] args)
        {
            if (!HasPermission(player, PermManage))
            {
                Reply(player, "CommandDenied");
                return;
            }

            int page = 1;
            if (args.Length > 1)
            {
                int parsed;
                if (int.TryParse(args[1], out parsed))
                    page = parsed;
            }

            int perPage = 7;
            List<PrefixRule> rules = _config.Prefixes ?? new List<PrefixRule>();
            if (rules.Count == 0)
            {
                SendLine(player, Lang("RuleListHeader", player != null ? player.UserIDString : "0", 1, 1));
                return;
            }

            int pages = Mathf.CeilToInt((float)rules.Count / perPage);
            page = Mathf.Clamp(page, 1, pages);

            SendLine(player, Lang("RuleListHeader", player != null ? player.UserIDString : "0", page, pages));

            int start = (page - 1) * perPage;
            int end = Mathf.Min(start + perPage, rules.Count);
            int index = start + 1;

            for (int i = start; i < end; i++)
            {
                PrefixRule rule = rules[i];
                SendLine(player, Lang("RuleListEntry", player != null ? player.UserIDString : "0", index, rule.Key, rule.Enabled, rule.Priority, rule.Type, rule.Access, rule.Text, rule.Color));
                index++;
            }
        }

        private void HandleAddRule(BasePlayer player, string[] args)
        {
            if (!HasPermission(player, PermManage))
            {
                Reply(player, "CommandDenied");
                return;
            }

            if (args.Length < 5)
            {
                Reply(player, "InvalidUsage");
                return;
            }

            string key = args[1].Trim().ToLowerInvariant();
            if (_config.Prefixes.Any(x => x != null && x.Key == key))
            {
                Reply(player, "RuleExists", key);
                return;
            }

            PrefixRule rule = new PrefixRule();
            rule.Key = key;
            rule.Type = ParseAccessType(args[2]);
            rule.Access = args[3].Trim();
            rule.Text = args[4];
            rule.Color = args.Length > 5 ? NormalizeHex(args[5], "#ff66cc") : "#ff66cc";
            if (args.Length > 6)
            {
                int pr;
                if (int.TryParse(args[6], out pr))
                    rule.Priority = pr;
            }

            _config.Prefixes.Add(rule);
            NormalizeConfig();
            SaveConfig();
            RefreshAllPlayers();
            Reply(player, "RuleAdded", key);
        }

        private void HandleSetRule(BasePlayer player, string[] args)
        {
            if (!HasPermission(player, PermManage))
            {
                Reply(player, "CommandDenied");
                return;
            }

            if (args.Length < 4)
            {
                Reply(player, "InvalidUsage");
                return;
            }

            string key = args[1].Trim().ToLowerInvariant();
            PrefixRule rule = _config.Prefixes.FirstOrDefault(x => x != null && x.Key == key);
            if (rule == null)
            {
                Reply(player, "RuleNotFound", key);
                return;
            }

            string field = args[2].Trim().ToLowerInvariant();
            string value = string.Join(" ", args.Skip(3).ToArray());

            switch (field)
            {
                case "enabled":
                    bool enabled;
                    if (bool.TryParse(value, out enabled))
                        rule.Enabled = enabled;
                    else
                    {
                        Reply(player, "InvalidUsage");
                        return;
                    }
                    break;

                case "priority":
                    int priority;
                    if (int.TryParse(value, out priority))
                        rule.Priority = priority;
                    else
                    {
                        Reply(player, "InvalidUsage");
                        return;
                    }
                    break;

                case "type":
                    rule.Type = ParseAccessType(value);
                    break;

                case "access":
                    rule.Access = value.Trim();
                    break;

                case "text":
                    rule.Text = value;
                    break;

                case "color":
                    rule.Color = NormalizeHex(value, "#ffffff");
                    break;

                case "size":
                    int size;
                    if (int.TryParse(value, out size))
                        rule.Size = Mathf.Clamp(size, 10, 24);
                    else
                    {
                        Reply(player, "InvalidUsage");
                        return;
                    }
                    break;

                default:
                    Reply(player, "InvalidUsage");
                    return;
            }

            NormalizeConfig();
            SaveConfig();
            RefreshAllPlayers();
            Reply(player, "RuleUpdated", key);
        }

        private void HandleRemoveRule(BasePlayer player, string[] args)
        {
            if (!HasPermission(player, PermManage))
            {
                Reply(player, "CommandDenied");
                return;
            }

            if (args.Length < 2)
            {
                Reply(player, "InvalidUsage");
                return;
            }

            string key = args[1].Trim().ToLowerInvariant();
            PrefixRule rule = _config.Prefixes.FirstOrDefault(x => x != null && x.Key == key);
            if (rule == null)
            {
                Reply(player, "RuleNotFound", key);
                return;
            }

            _config.Prefixes.Remove(rule);
            SaveConfig();
            RefreshAllPlayers();
            Reply(player, "RuleRemoved", key);
        }

        #endregion

        #region Helpers

        private void Reply(BasePlayer player, string key, params object[] args)
        {
            string text = Lang(key, player != null ? player.UserIDString : "0", args);

            if (player != null)
                player.SendConsoleCommand("chat.add", 2, PluginIcon, ChatPrefix + " " + text);
            else
                Puts(ChatPrefix + " " + text);
        }

        private void SendLine(BasePlayer player, string message)
        {
            if (player != null)
                player.ChatMessage(ChatPrefix + " " + message);
            else
                Puts(ChatPrefix + " " + message);
        }

        private static string NormalizeHex(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback;

            value = value.Trim();
            if (!value.StartsWith("#"))
                value = "#" + value;

            return Regex.IsMatch(value, "^#(?:[0-9a-fA-F]{3}){1,2}$") ? value : fallback;
        }

        private static bool TryParseColor(string value, out Color color)
        {
            value = NormalizeHex(value, "#ffffff");
            return ColorUtility.TryParseHtmlString(value, out color);
        }

        private static string NormalizeText(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static string EscapeRichText(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Replace("<", string.Empty).Replace(">", string.Empty);
        }

        private PrefixAccessType ParseAccessType(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return PrefixAccessType.Permission;

            value = value.Trim().ToLowerInvariant();
            if (value == "group")
                return PrefixAccessType.Group;
            if (value == "both" || value == "any")
                return PrefixAccessType.Any;
            return PrefixAccessType.Permission;
        }

        private bool TryFindPlayer(string input, out BasePlayer player)
        {
            player = null;

            if (string.IsNullOrWhiteSpace(input))
                return false;

            ulong id;
            if (ulong.TryParse(input, out id))
            {
                player = BasePlayer.FindByID(id);
                if (player == null)
                    player = BasePlayer.FindSleeping(id);

                return player != null;
            }

            foreach (BasePlayer candidate in BasePlayer.activePlayerList)
            {
                if (candidate == null || string.IsNullOrEmpty(candidate.displayName))
                    continue;

                if (candidate.displayName.IndexOf(input, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    player = candidate;
                    return true;
                }
            }

            return false;
        }

        #endregion
    }
}
