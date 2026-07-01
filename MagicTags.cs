using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("MagicTags", "whitecristafer", "2.0.0")]
    [Description("MagicTags 2.0.0 - overhead prefix radar with chat prefixes, custom colors and RU/EN localization.")]
    public class MagicTags : RustPlugin
    {
        private const ulong PluginIcon = 76561198209258869;
        private const string ChatPrefix = "<size=12><color=#cc66ff><b>MagicTags</b></color></size> |";

        private const string PermAdmin = "magictags.admin";
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

        #region Config

        private class PluginConfig
        {
            [JsonProperty("Settings")]
            public SettingsConfig Settings = new SettingsConfig();
        }

        private class SettingsConfig
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

            [JsonProperty("Use team prefix")]
            public bool UseTeamPrefix = true;

            [JsonProperty("Default prefix text")]
            public string DefaultPrefixText = "[PLAYER]";

            [JsonProperty("Default prefix color")]
            public string DefaultPrefixColor = "#cfcfcf";

            [JsonProperty("Admin prefix text")]
            public string AdminPrefixText = "[ADMIN]";

            [JsonProperty("Admin prefix color")]
            public string AdminPrefixColor = "#ff66ff";

            [JsonProperty("Team prefix text")]
            public string TeamPrefixText = "[TEAM]";

            [JsonProperty("Team prefix color")]
            public string TeamPrefixColor = "#66ccff";

            [JsonProperty("Custom prefix color default")]
            public string CustomPrefixColor = "#ff66cc";

            [JsonProperty("Log debug")]
            public bool LogDebug = false;
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
                PrintWarning("MagicTags config was corrupted, creating a new one.");
                _config = null;
            }

            if (_config == null) _config = new PluginConfig();
            NormalizeConfig();
            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        private void NormalizeConfig()
        {
            if (_config == null) _config = new PluginConfig();
            if (_config.Settings == null) _config.Settings = new SettingsConfig();

            _config.Settings.UpdateInterval = Mathf.Clamp(_config.Settings.UpdateInterval, 0.1f, 10f);
            _config.Settings.ViewDistance = Mathf.Clamp(_config.Settings.ViewDistance, 1f, 500f);
            _config.Settings.TextLifetime = Mathf.Clamp(_config.Settings.TextLifetime, 0.1f, 10f);
            _config.Settings.TextHeight = Mathf.Clamp(_config.Settings.TextHeight, 0.1f, 20f);
            _config.Settings.DefaultPrefixColor = NormalizeHex(_config.Settings.DefaultPrefixColor, "#cfcfcf");
            _config.Settings.AdminPrefixColor = NormalizeHex(_config.Settings.AdminPrefixColor, "#ff66ff");
            _config.Settings.TeamPrefixColor = NormalizeHex(_config.Settings.TeamPrefixColor, "#66ccff");
            _config.Settings.CustomPrefixColor = NormalizeHex(_config.Settings.CustomPrefixColor, "#ff66cc");
        }

        #endregion

        #region Data

        private class StoredData
        {
            public Dictionary<ulong, PlayerProfile> Players = new Dictionary<ulong, PlayerProfile>();
        }

        private class PlayerProfile
        {
            public bool Hidden;
            public string CustomPrefix = string.Empty;
            public string CustomPrefixColor = string.Empty;
            public string UpdatedBy = string.Empty;
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

            if (_data == null) _data = new StoredData();
            if (_data.Players == null) _data.Players = new Dictionary<ulong, PlayerProfile>();
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(DataFileName, _data);
        }

        private PlayerProfile GetOrCreateProfile(ulong userId)
        {
            if (_data.Players == null)
                _data.Players = new Dictionary<ulong, PlayerProfile>();

            if (!_data.Players.TryGetValue(userId, out var profile) || profile == null)
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
                ["PluginDisabled"] = "MagicTags is disabled in config.",
                ["Help"] = "MagicTags commands:\n" +
                           "/mtags info - plugin info\n" +
                           "/mtags hide - hide your tag\n" +
                           "/mtags show - show your tag\n" +
                           "/mtags prefix <text> - set your custom prefix\n" +
                           "/mtags color <#hex> - set your custom prefix color\n" +
                           "/mtags clear - clear your custom prefix\n" +
                           "/mtags personal <set|color|clear|hide|show|info> <player> [value] - admin tools\n" +
                           "/mtags sync - refresh all players\n" +
                           "/mtags reload - reload config and data",
                ["Info"] = "MagicTags v{0}\nPlayers with data: {1}\nUpdate interval: {2}s\nView distance: {3}m",
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
                ["CurrentState"] = "Hidden={0}, Prefix='{1}', Color='{2}'",
                ["CommandDenied"] = "You need the required permission for this action.",
                ["TeamPrefixNotice"] = "Team tag is enabled by config.",
            }, this, "en");

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Prefix"] = ChatPrefix,
                ["NoPermission"] = "У вас нет прав на использование этой команды.",
                ["PluginDisabled"] = "MagicTags выключен в конфиге.",
                ["Help"] = "Команды MagicTags:\n" +
                           "/mtags info - информация о плагине\n" +
                           "/mtags hide - скрыть свой тег\n" +
                           "/mtags show - показать свой тег\n" +
                           "/mtags prefix <текст> - установить свой префикс\n" +
                           "/mtags color <#hex> - установить цвет префикса\n" +
                           "/mtags clear - очистить свой префикс\n" +
                           "/mtags personal <set|color|clear|hide|show|info> <игрок> [значение] - админка\n" +
                           "/mtags sync - обновить всех игроков\n" +
                           "/mtags reload - перезагрузить конфиг и данные",
                ["Info"] = "MagicTags v{0}\nИгроков с данными: {1}\nИнтервал обновления: {2}с\nДальность отображения: {3}м",
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
                ["CurrentState"] = "Hidden={0}, Prefix='{1}', Color='{2}'",
                ["CommandDenied"] = "Для этого действия нужны права.",
                ["TeamPrefixNotice"] = "Командный тег включён в конфиге.",
            }, this, "ru");
        }

        private string Lang(string key, string playerId = null, params object[] args)
        {
            return string.Format(lang.GetMessage(key, this, playerId ?? "0"), args);
        }

        #endregion

        #region Init / Unload

        private void Init()
        {
            RegisterPermissions();
            LoadConfig();
            LoadData();
        }

        private void OnServerInitialized()
        {
            StartRefreshTimer();
            NextTick(RefreshAll);
            PrintBanner();
        }

        private void Unload()
        {
            _refreshTimer?.Destroy();
            RemoveTemporaryAdminFlags();
            SaveData();
        }

        private void PrintBanner()
        {
            Puts("=================================================");
            Puts($" MagicTags v{Version} by whitecristafer");
            Puts(" The plugin allows you to display the player's prefix above the player.");
            Puts("=================================================");
        }

        #endregion

        #region Permissions

        private void RegisterPermissions()
        {
            permission.RegisterPermission(PermAdmin, this);
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

            return permission.UserHasPermission(player.UserIDString, PermSee) || permission.UserHasPermission(player.UserIDString, PermAdmin);
        }

        #endregion

        #region Hooks

        private void OnPlayerConnected(BasePlayer player)
        {
            if (player == null) return;
            NextTick(() => RefreshPlayer(player));
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null) return;
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

        #endregion

        #region Refresh Loop

        private void StartRefreshTimer()
        {
            _refreshTimer?.Destroy();
            _refreshTimer = timer.Every(_config.Settings.UpdateInterval, RefreshAll);
        }

        private void RefreshAll()
        {
            if (!_config.Settings.Enabled)
                return;

            foreach (var viewer in BasePlayer.activePlayerList)
            {
                if (!CanSeeTags(viewer))
                    continue;

                bool tempFlag = EnsureTemporaryAdminFlag(viewer);

                foreach (var target in BasePlayer.activePlayerList)
                {
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

        private void RefreshPlayer(BasePlayer player)
        {
            if (player == null || !player.IsConnected)
                return;

            if (!CanSeeTags(player))
                return;

            bool tempFlag = EnsureTemporaryAdminFlag(player);

            foreach (var target in BasePlayer.activePlayerList)
            {
                if (target == null || !target.IsConnected)
                    continue;

                if (!ShouldShowToViewer(player, target))
                    continue;

                DrawTag(player, target);
            }

            if (tempFlag)
                ScheduleRemoveTemporaryFlag(player);
        }

        private void RefreshByUserId(string id)
        {
            ulong userId;
            if (!ulong.TryParse(id, out userId))
                return;

            var player = BasePlayer.FindByID(userId);
            if (player != null)
                RefreshPlayer(player);
        }

        #endregion

        #region Tag Logic

        private bool ShouldShowToViewer(BasePlayer viewer, BasePlayer target)
        {
            if (viewer == null || target == null)
                return false;

            if (!viewer.IsConnected || !target.IsConnected)
                return false;

            if (!_config.Settings.ShowSelf && viewer.userID == target.userID)
                return false;

            if (!viewer.IsAdmin && target.userID != viewer.userID)
            {
                var profile = GetProfile(target.userID);
                if (profile.Hidden)
                    return false;
            }

            if (_config.Settings.ShowOnlyForPermission && !viewer.IsAdmin && !permission.UserHasPermission(viewer.UserIDString, PermSee))
                return false;

            float distance = Vector3.Distance(viewer.eyes.position, target.eyes.position);
            if (distance > _config.Settings.ViewDistance)
                return false;

            return true;
        }

        private string ResolvePrefix(BasePlayer target)
        {
            if (target == null)
                return string.Empty;

            var profile = GetProfile(target.userID);

            // The personal prefix has the highest priority
            if (!string.IsNullOrWhiteSpace(profile.CustomPrefix))
                return profile.CustomPrefix.Trim();

            // The real admin + the right magictags.admin
            bool isRealAdmin = target.IsAdmin || permission.UserHasPermission(target.UserIDString, PermAdmin);

            if (isRealAdmin)
                return _config.Settings.AdminPrefixText;

            // Command prefix
            if (_config.Settings.UseTeamPrefix && target.currentTeam != 0)
                return _config.Settings.TeamPrefixText;

            // An ordinary player
            return _config.Settings.DefaultPrefixText;
        }

        private string ResolvePrefixColor(BasePlayer target)
        {
            if (target == null)
                return _config.Settings.DefaultPrefixColor;

            var profile = GetProfile(target.userID);

            if (!string.IsNullOrWhiteSpace(profile.CustomPrefix))
                return NormalizeHex(profile.CustomPrefixColor, _config.Settings.CustomPrefixColor);

            bool isRealAdmin = target.IsAdmin || permission.UserHasPermission(target.UserIDString, PermAdmin);

            if (isRealAdmin)
                return _config.Settings.AdminPrefixColor;

            if (_config.Settings.UseTeamPrefix && target.currentTeam != 0)
                return _config.Settings.TeamPrefixColor;

            return _config.Settings.DefaultPrefixColor;
        }

        private void DrawTag(BasePlayer viewer, BasePlayer target)
        {
            if (viewer == null || target == null)
                return;

            string text = ResolvePrefix(target);
            if (string.IsNullOrWhiteSpace(text))
                return;

            string color = ResolvePrefixColor(target);
            Vector3 position = target.eyes != null ? target.eyes.position : target.transform.position;
            position += new Vector3(0f, _config.Settings.TextHeight, 0f);

            Color drawColor;
            if (!TryParseColor(color, out drawColor))
                drawColor = Color.white;

            viewer.SendConsoleCommand("ddraw.text", _config.Settings.TextLifetime, drawColor, position, text);
        }

        #endregion

        #region Admin Flag Support

        private bool EnsureTemporaryAdminFlag(BasePlayer player)
        {
            if (player == null || !_config.Settings.RequireAdminFlagForRadarMode)
                return false;

            // We do not give the flag to real admins and those who already have an isAdmin.
            if (player.IsAdmin)
                return false;

            if (_temporaryAdminFlags.Contains(player.userID))
                return false;

            // We give a temporary flag only if we have the right to see
            if (permission.UserHasPermission(player.UserIDString, PermSee) || 
                permission.UserHasPermission(player.UserIDString, PermAdmin))
            {
                player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, true);
                player.SendNetworkUpdateImmediate();
                _temporaryAdminFlags.Add(player.userID);
                return true;
            }

            return false;
        }

        private void ScheduleRemoveTemporaryFlag(BasePlayer player)
        {
            if (player == null)
                return;

            timer.Once(0.1f, () =>
            {
                if (player == null || !player.IsConnected)
                    return;

                if (_temporaryAdminFlags.Contains(player.userID) && !permission.UserHasPermission(player.UserIDString, PermSee) && !permission.UserHasPermission(player.UserIDString, PermAdmin))
                    return;

                if (_temporaryAdminFlags.Contains(player.userID))
                {
                    player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false);
                    player.SendNetworkUpdateImmediate();
                    _temporaryAdminFlags.Remove(player.userID);
                }
            });
        }

        private void RemoveTemporaryAdminFlags()
        {
            foreach (var id in _temporaryAdminFlags.ToArray())
            {
                var player = BasePlayer.FindByID(id);
                if (player != null && !player.IsDestroyed)
                {
                    player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false);
                    player.SendNetworkUpdateImmediate();
                }
            }

            _temporaryAdminFlags.Clear();
        }

        #endregion

        #region Helpers

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

        private PlayerProfile GetProfile(ulong userId)
        {
            return GetOrCreateProfile(userId);
        }

        private bool TryFindPlayer(string input, out BasePlayer player)
        {
            player = null;
            if (string.IsNullOrWhiteSpace(input))
                return false;

            if (ulong.TryParse(input, out var uid))
            {
                player = BasePlayer.FindByID(uid) ?? BasePlayer.FindSleeping(uid);
                return player != null;
            }

            player = BasePlayer.activePlayerList.FirstOrDefault(x =>
                x != null && x.displayName != null &&
                x.displayName.IndexOf(input, StringComparison.OrdinalIgnoreCase) >= 0);

            return player != null;
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
            var player = arg.Player();
            string[] args = arg.Args != null ? arg.Args.Select(a => a.ToString()).ToArray() : Array.Empty<string>();
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
                    break;

                case "info":
                    Reply(player, "Info", Version, _data.Players.Count, _config.Settings.UpdateInterval, _config.Settings.ViewDistance);
                    break;

                case "hide":
                    HandleHide(player, true);
                    break;

                case "show":
                    HandleHide(player, false);
                    break;

                case "prefix":
                    HandleSelfPrefix(player, args);
                    break;

                case "color":
                    HandleSelfColor(player, args);
                    break;

                case "clear":
                    HandleClearSelf(player);
                    break;

                case "personal":
                    HandlePersonal(player, args);
                    break;

                case "reload":
                    if (!HasPermission(player, PermReload) && !HasPermission(player, PermAdmin) && !HasPermission(player, PermManage))
                    {
                        Reply(player, "NoPermission");
                        return;
                    }

                    LoadConfig();
                    LoadData();
                    StartRefreshTimer();
                    RefreshAll();
                    Reply(player, "Reloaded");
                    break;

                case "sync":
                    if (!HasPermission(player, PermManage) && !HasPermission(player, PermAdmin))
                    {
                        Reply(player, "NoPermission");
                        return;
                    }

                    RefreshAll();
                    Reply(player, "Synced");
                    break;

                default:
                    Reply(player, "Help");
                    break;
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

            var profile = GetProfile(player.userID);
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
            var profile = GetProfile(player.userID);
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

            var profile = GetProfile(player.userID);
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

            var profile = GetProfile(player.userID);
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
            if (!HasPermission(player, PermPersonal) && !HasPermission(player, PermAdmin) && !HasPermission(player, PermManage))
            {
                Reply(player, "NoPermission");
                return;
            }

            if (args.Length < 3)
            {
                Reply(player, "InvalidUsage");
                return;
            }

            string action = args[1].ToLowerInvariant();
            string targetInput = args[2];

            if (!TryFindPlayer(targetInput, out var target))
            {
                Reply(player, "PlayerNotFound");
                return;
            }

            var profile = GetProfile(target.userID);

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
                    break;

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
                    break;

                case "hide":
                    profile.Hidden = true;
                    profile.UpdatedBy = player != null ? player.UserIDString : "console";
                    profile.UpdatedAt = DateTime.UtcNow.ToString("u");
                    SaveData();
                    RefreshPlayer(target);
                    Reply(player, "PersonalHidden", target.displayName);
                    break;

                case "show":
                    profile.Hidden = false;
                    profile.UpdatedBy = player != null ? player.UserIDString : "console";
                    profile.UpdatedAt = DateTime.UtcNow.ToString("u");
                    SaveData();
                    RefreshPlayer(target);
                    Reply(player, "PersonalHidden", target.displayName);
                    break;

                case "clear":
                    profile.CustomPrefix = string.Empty;
                    profile.CustomPrefixColor = string.Empty;
                    profile.Hidden = false;
                    profile.UpdatedBy = player != null ? player.UserIDString : "console";
                    profile.UpdatedAt = DateTime.UtcNow.ToString("u");
                    SaveData();
                    RefreshPlayer(target);
                    Reply(player, "PersonalCleared", target.displayName);
                    break;

                case "info":
                    Reply(player, "PersonalInfo", target.displayName, profile.Hidden, profile.CustomPrefix, profile.CustomPrefixColor, profile.UpdatedBy, profile.UpdatedAt);
                    break;

                default:
                    Reply(player, "InvalidUsage");
                    break;
            }
        }

        private void Reply(BasePlayer player, string key, params object[] args)
        {
            string text = Lang(key, player != null ? player.UserIDString : "0", args);

            if (player != null)
            {
                player.SendConsoleCommand("chat.add", 2, PluginIcon, $"{ChatPrefix} {text}");
            }
            else
            {
                Puts($"{ChatPrefix} {text}");
            }
        }

        #endregion
    }
}
