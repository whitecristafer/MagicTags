// Apache License 2.0
// Copyright 2026
//
// MagicTags is distributed under the Apache License 2.0. See LICENSE for details.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("MagicTags", "whitecristafer", "3.1.4-BETA")]
    [Description("Configurable overhead prefixes with per-player visibility controls, dynamic rules, and clean chat formatting.")]
    public class MagicTags : RustPlugin
    {
        private const string PermissionView = "magictags.view";
        private const string PermissionHideOwn = "magictags.hide";
        private const string PermissionCustomPrefix = "magictags.customprefix";
        private const string PermissionCustomColor = "magictags.customcolor";
        private const string PermissionCustomNickname = "magictags.nickname";
        private const string PermissionManage = "magictags.manage";
        private const string PermissionReload = "magictags.reload";

        private const string DataFileName = "MagicTags_Data";
        private const int CurrentConfigVersion = 1;
        private const float MinAllowedRange = 4f;
        private const float MaxAllowedRange = 12f;
        private const string DefaultChatLabel = "MagicTags";
        private const ulong DefaultChatIconId = 76561198209258869UL;
        private const string DefaultDisplayNameFormat = "%magictags_nickname%";
        private const string DisplayNameNicknameToken = "%magictags_nickname%";

        private PluginConfig _config;
        private StoredData _data;
        private Timer _refreshTimer;

        [PluginReference] private Plugin PlaceholderAPI;

        // CUI – we use the usual game UI, without the admin flag
        private const string OverheadPanelName = "MagicTags.Overhead";

        #region Configuration

        private class PluginConfig
        {
            [JsonProperty("Config Version")]
            public int ConfigVersion = CurrentConfigVersion;

            [JsonProperty("General")]
            public GeneralSettings General = new GeneralSettings();

            [JsonProperty("Chat")]
            public ChatSettings Chat = new ChatSettings();

            [JsonProperty("Default Prefix")]
            public PrefixStyle DefaultPrefix = new PrefixStyle
            {
                Text = "[PLAYER]",
                Color = "#cfcfcf",
                Size = 12,
                Enabled = true
            };

            [JsonProperty("Admin Prefix")]
            public PrefixStyle AdminPrefix = new PrefixStyle
            {
                Text = "[ADMIN]",
                Color = "#ff66ff",
                Size = 12,
                Enabled = true,
                AlwaysVisible = true
            };

            [JsonProperty("Prefixes")]
            public List<PrefixRule> Prefixes = new List<PrefixRule>();
        }

        private class GeneralSettings
        {
            [JsonProperty("Enabled")]
            public bool Enabled = true;

            [JsonProperty("Update Interval (Seconds)")]
            public float UpdateInterval = 0.5f;

            [JsonProperty("View Distance (Meters)")]
            public float ViewDistance = 6f;

            [JsonProperty("Player Minimum View Distance (Meters)")]
            public float PlayerMinimumViewDistance = MinAllowedRange;

            [JsonProperty("Player Maximum View Distance (Meters)")]
            public float PlayerMaximumViewDistance = MaxAllowedRange;

            [JsonProperty("Text Lifetime (Seconds)")]
            public float TextLifetime = 0.75f;

            [JsonProperty("Text Height Offset")]
            public float TextHeightOffset = 0.25f;

            [JsonProperty("Show Tags To Self")]
            public bool ShowTagsToSelf = false;

            [JsonProperty("Require Permission To See Tags")]
            public bool RequirePermissionToSeeTags = false;

            [JsonProperty("Allow Player Range Control")]
            public bool AllowPlayerRangeControl = true;

            [JsonProperty("Use Chat Prefix")]
            public bool UseChatPrefix = true;

            [JsonProperty("Overhead Field Of View (Degrees)")]
            public float OverheadFieldOfView = 90f;

            [JsonProperty("Overhead Aspect Ratio")]
            public float OverheadAspectRatio = 1.7777778f;

            [JsonProperty("Display Name Format")]
            public string DisplayNameFormat = DefaultDisplayNameFormat;

            [JsonProperty("Debug Logging")]
            public bool DebugLogging = false;
        }

        private class ChatSettings
        {
            [JsonProperty("Enabled")]
            public bool Enabled = true;

            [JsonProperty("Label")]
            public string Label = DefaultChatLabel;

            [JsonProperty("Color")]
            public string Color = "#cc66ff";

            [JsonProperty("Size")]
            public int Size = 12;

            [JsonProperty("Icon Steam ID")]
            public ulong IconId = DefaultChatIconId;
        }

        private class PrefixStyle
        {
            [JsonProperty("Enabled")]
            public bool Enabled = true;

            [JsonProperty("Text")]
            public string Text = "[PLAYER]";

            [JsonProperty("Color")]
            public string Color = "#ffffff";

            [JsonProperty("Size")]
            public int Size = 12;

            [JsonProperty("AlwaysVisible")]
            public bool AlwaysVisible = false;
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
            public int Size = 12;

            [JsonProperty("AlwaysVisible")]
            public bool AlwaysVisible = false;
        }

        protected override void LoadDefaultConfig()
        {
            _config = CreateDefaultConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            bool migrated = false;

            try
            {
                _config = Config.ReadObject<PluginConfig>();
            }
            catch
            {
                PrintWarning("The configuration file is invalid. A fresh default configuration will be created.");
                _config = null;
            }

            if (_config == null)
            {
                _config = CreateDefaultConfig();
                migrated = true;
            }

            migrated |= NormalizeConfig();

            if (migrated)
            {
                SaveConfig();
                PrintWarning("MagicTags configuration was migrated to the latest schema and saved.");
                DebugLog("Configuration migrated and normalized.");
            }
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        private PluginConfig CreateDefaultConfig()
        {
            return new PluginConfig
            {
                ConfigVersion = CurrentConfigVersion,
                General = new GeneralSettings(),
                Chat = new ChatSettings(),
                DefaultPrefix = new PrefixStyle
                {
                    Enabled = true,
                    Text = "[PLAYER]",
                    Color = "#cfcfcf",
                    Size = 12,
                    AlwaysVisible = false
                },
                AdminPrefix = new PrefixStyle
                {
                    Enabled = true,
                    Text = "[ADMIN]",
                    Color = "#ff66ff",
                    Size = 12,
                    AlwaysVisible = true
                },
                Prefixes = new List<PrefixRule>
                {
                    new PrefixRule
                    {
                        Key = "vip",
                        Enabled = true,
                        Priority = 10,
                        Type = PrefixAccessType.Permission,
                        Access = "magictags.vip",
                        Text = "[VIP]",
                        Color = "#ff66cc",
                        Size = 12,
                        AlwaysVisible = false
                    },
                    new PrefixRule
                    {
                        Key = "staff",
                        Enabled = true,
                        Priority = 100,
                        Type = PrefixAccessType.Permission,
                        Access = "magictags.staff",
                        Text = "[STAFF]",
                        Color = "#ff9966",
                        Size = 12,
                        AlwaysVisible = true
                    }
                }
            };
        }

        private bool NormalizeConfig()
        {
            bool migrated = false;

            if (_config.ConfigVersion != CurrentConfigVersion)
            {
                _config.ConfigVersion = CurrentConfigVersion;
                migrated = true;
            }

            if (_config.General == null)
            {
                _config.General = new GeneralSettings();
                migrated = true;
            }

            if (_config.Chat == null)
            {
                _config.Chat = new ChatSettings();
                migrated = true;
            }

            if (_config.DefaultPrefix == null)
            {
                _config.DefaultPrefix = new PrefixStyle();
                migrated = true;
            }

            if (_config.AdminPrefix == null)
            {
                _config.AdminPrefix = new PrefixStyle
                {
                    Enabled = true,
                    Text = "[ADMIN]",
                    Color = "#ff66ff",
                    Size = 12,
                    AlwaysVisible = true
                };
                migrated = true;
            }

            if (_config.Prefixes == null)
            {
                _config.Prefixes = new List<PrefixRule>();
                migrated = true;
            }

            _config.General.UpdateInterval = Clamp(_config.General.UpdateInterval, 0.1f, 10f, 0.5f, ref migrated);
            _config.General.ViewDistance = Clamp(_config.General.ViewDistance, 1f, 500f, 30f, ref migrated);
            _config.General.PlayerMinimumViewDistance = Clamp(_config.General.PlayerMinimumViewDistance, 1f, 500f, MinAllowedRange, ref migrated);
            _config.General.PlayerMaximumViewDistance = Clamp(_config.General.PlayerMaximumViewDistance, 1f, 500f, MaxAllowedRange, ref migrated);
            _config.General.TextLifetime = Clamp(_config.General.TextLifetime, 0.1f, 10f, 0.75f, ref migrated);
            _config.General.TextHeightOffset = Clamp(_config.General.TextHeightOffset, 0.1f, 20f, 2.15f, ref migrated);
            _config.General.OverheadFieldOfView = Clamp(_config.General.OverheadFieldOfView, 50f, 120f, 90f, ref migrated);
            _config.General.OverheadAspectRatio = Clamp(_config.General.OverheadAspectRatio, 1f, 3f, 1.7777778f, ref migrated);

            // The empty line is the conscious choice of the administrator to "hide the nickname from everyone by default",
            // therefore, we do not substitute fallback here, but only remove null and extra spaces at the edges.
            if (_config.General.DisplayNameFormat == null)
            {
                _config.General.DisplayNameFormat = DefaultDisplayNameFormat;
                migrated = true;
            }
            else if (_config.General.DisplayNameFormat.Trim() != _config.General.DisplayNameFormat)
            {
                _config.General.DisplayNameFormat = _config.General.DisplayNameFormat.Trim();
                migrated = true;
            }

            if (_config.General.PlayerMinimumViewDistance > _config.General.PlayerMaximumViewDistance)
            {
                float swap = _config.General.PlayerMinimumViewDistance;
                _config.General.PlayerMinimumViewDistance = _config.General.PlayerMaximumViewDistance;
                _config.General.PlayerMaximumViewDistance = swap;
                migrated = true;
            }

            if (string.IsNullOrWhiteSpace(_config.Chat.Label))
            {
                _config.Chat.Label = DefaultChatLabel;
                migrated = true;
            }

            _config.Chat.Color = NormalizeHex(_config.Chat.Color, "#cc66ff", ref migrated);
            _config.Chat.Size = ClampInt(_config.Chat.Size, 10, 20, 12, ref migrated);
            if (_config.Chat.IconId == 0)
            {
                _config.Chat.IconId = DefaultChatIconId;
                migrated = true;
            }

            _config.DefaultPrefix.Text = NormalizeText(_config.DefaultPrefix.Text, "[PLAYER]", ref migrated);
            _config.DefaultPrefix.Color = NormalizeHex(_config.DefaultPrefix.Color, "#cfcfcf", ref migrated);
            _config.DefaultPrefix.Size = ClampInt(_config.DefaultPrefix.Size, 10, 24, 12, ref migrated);

            _config.AdminPrefix.Text = NormalizeText(_config.AdminPrefix.Text, "[ADMIN]", ref migrated);
            _config.AdminPrefix.Color = NormalizeHex(_config.AdminPrefix.Color, "#ff66ff", ref migrated);
            _config.AdminPrefix.Size = ClampInt(_config.AdminPrefix.Size, 10, 24, 12, ref migrated);
            if (!_config.AdminPrefix.AlwaysVisible)
            {
                _config.AdminPrefix.AlwaysVisible = true;
                migrated = true;
            }

            for (int i = 0; i < _config.Prefixes.Count; i++)
            {
                PrefixRule rule = _config.Prefixes[i];
                if (rule == null)
                {
                    _config.Prefixes[i] = new PrefixRule();
                    rule = _config.Prefixes[i];
                    migrated = true;
                }

                string normalizedKey = NormalizeKey(rule.Key, "rule_" + i, ref migrated);
                if (rule.Key != normalizedKey)
                {
                    rule.Key = normalizedKey;
                    migrated = true;
                }

                if (string.IsNullOrWhiteSpace(rule.Access))
                {
                    rule.Access = string.Empty;
                    migrated = true;
                }
                else
                {
                    string trimmedAccess = rule.Access.Trim();
                    if (trimmedAccess != rule.Access)
                    {
                        rule.Access = trimmedAccess;
                        migrated = true;
                    }
                }

                rule.Text = NormalizeText(rule.Text, "[TAG]", ref migrated);
                rule.Color = NormalizeHex(rule.Color, "#ffffff", ref migrated);
                rule.Size = ClampInt(rule.Size, 10, 24, 12, ref migrated);
            }

            _config.Prefixes = _config.Prefixes
                .Where(x => x != null)
                .OrderByDescending(x => x.Priority)
                .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return migrated;
        }

        #endregion

        #region Persistent Data

        private class StoredData
        {
            [JsonProperty("Players")]
            public Dictionary<ulong, PlayerSettings> Players = new Dictionary<ulong, PlayerSettings>();
        }

        private class PlayerSettings
        {
            [JsonProperty("Hide Own Tag")]
            public bool HideOwnTag = false;

            [JsonProperty("Hide Other Tags")]
            public bool HideOtherTags = false;

            [JsonProperty("Custom View Distance")]
            public float CustomViewDistance = -1f;

            [JsonProperty("Custom Prefix")]
            public string CustomPrefix = string.Empty;

            [JsonProperty("Custom Prefix Color")]
            public string CustomPrefixColor = string.Empty;

            [JsonProperty("Custom Display Name")]
            public string CustomDisplayName = string.Empty;

            [JsonProperty("Updated By")]
            public string UpdatedBy = string.Empty;

            [JsonProperty("Updated At")]
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
            {
                _data = new StoredData();
            }

            if (_data.Players == null)
            {
                _data.Players = new Dictionary<ulong, PlayerSettings>();
            }
        }

        private void SaveData()
        {
            if (_data == null)
            {
                return;
            }

            Interface.Oxide.DataFileSystem.WriteObject(DataFileName, _data);
        }

        private PlayerSettings GetPlayerSettings(ulong userId)
        {
            if (_data.Players == null)
            {
                _data.Players = new Dictionary<ulong, PlayerSettings>();
            }

            PlayerSettings settings;
            if (!_data.Players.TryGetValue(userId, out settings) || settings == null)
            {
                settings = new PlayerSettings();
                _data.Players[userId] = settings;
            }

            return settings;
        }

        #endregion

        #region Localization

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Prefix"] = ChatPrefix(),
                ["NoPermission"] = "You do not have permission to use this command.",
                ["PluginDisabled"] = "MagicTags is disabled in the configuration.",
                ["InvalidUsage"] = "Invalid usage.",
                ["UnknownSubcommand"] = "Unknown subcommand. Use /magictags help.",
                ["PlayerNotFound"] = "Player not found.",
                ["InvalidColor"] = "Invalid hex color. Example: #ff66cc",
                ["InvalidNumber"] = "Invalid number.",
                ["Reloaded"] = "Configuration and data reloaded.",
                ["Synced"] = "All online players have been refreshed.",
                ["Help"] = "MagicTags commands:\n" +
                           "/magictags info - show current status\n" +
                           "/magictags config - show the active configuration summary\n" +
                           "/magictags help - show this help text\n" +
                           "/magictags hide - hide your own tag\n" +
                           "/magictags show - show your own tag again\n" +
                           "/magictags mhide [on|off|toggle] - hide other players' prefixes locally (also available as standalone /mhide)\n" +
                           "/magictags mshow - show other players' prefixes again (also available as standalone /mshow)\n" +
                           "/magictags range <10-40|off> - set your local prefix viewing distance\n" +
                           "/magictags prefix <text> - set your personal prefix\n" +
                           "/magictags color <#hex> - set your personal prefix color\n" +
                           "/magictags nick <text|off> - set or clear your personal display name\n" +
                           "/magictags clear - clear your personal prefix/color/nickname settings\n" +
                           "/magictags list [page] - list configured prefix rules\n" +
                           "/magictags addrule <key> <permission|group|any> <access> <text> [color] [size] [priority] [alwaysVisible]\n" +
                           "/magictags setrule <key> <field> <value>\n" +
                           "/magictags removerule <key> - remove a prefix rule\n" +
                           "/magictags reload - reload config and data\n" +
                           "/magictags sync - force a full refresh",
                ["Info"] = "MagicTags v{0}\n" +
                           "Online players: {1}\n" +
                           "Stored profiles: {2}\n" +
                           "Rules: {3}\n" +
                           "Default view distance: {4:0.##}m\n" +
                           "Player range: {5:0.##}-{6:0.##}m\n" +
                           "Update interval: {7:0.##}s",
                ["ConfigSummary"] = "Active configuration:\n" +
                                    "Enabled: {0}\n" +
                                    "Chat prefix: {1}\n" +
                                    "Default prefix: {2} {3} size={4}\n" +
                                    "Admin prefix: {5} {6} size={7} visible={8}\n" +
                                    "Default view distance: {9:0.##}m\n" +
                                    "Player range: {10:0.##}-{11:0.##}m\n" +
                                    "Text lifetime: {12:0.##}s\n" +
                                    "Text height offset: {13:0.##}\n" +
                                    "Require permission to see tags: {14}\n" +
                                    "Player range control: {15}\n" +
                                    "Overhead FOV/aspect (CUI): {16:0.##}\u00b0 / {17:0.###}\n" +
                                    "Display name format: {18}\n" +
                                    "Rules: {19}",
                ["HiddenOwnOn"] = "Your own tag is now hidden.",
                ["HiddenOwnOff"] = "Your own tag is now visible again.",
                ["ViewerHideOn"] = "You will no longer see other players' prefixes.",
                ["ViewerHideOff"] = "You will now see other players' prefixes again.",
                ["RangeSet"] = "Your local viewing distance is now {0:0.##} meters.",
                ["RangeCleared"] = "Your local viewing distance has been reset to the default.",
                ["PrefixSet"] = "Your personal prefix has been saved: {0}",
                ["ColorSet"] = "Your personal prefix color has been saved: {0}",
                ["NicknameSet"] = "Your personal display name has been saved: {0}",
                ["NicknameCleared"] = "Your personal display name has been reset to the default format.",
                ["Cleared"] = "Your personal prefix settings have been cleared.",
                ["NoRules"] = "No prefix rules are configured.",
                ["RuleListHeader"] = "Prefix rules page {0}/{1}:",
                ["RuleListEntry"] = "{0}. key={1} enabled={2} priority={3} type={4} access={5} text='{6}' color={7} size={8} alwaysVisible={9}",
                ["RuleAdded"] = "Prefix rule '{0}' has been added.",
                ["RuleUpdated"] = "Prefix rule '{0}' has been updated.",
                ["RuleRemoved"] = "Prefix rule '{0}' has been removed.",
                ["RuleNotFound"] = "Prefix rule '{0}' was not found.",
                ["RuleExists"] = "Prefix rule '{0}' already exists.",
                ["CommandDenied"] = "You need the required permission for that action.",
                ["AutoUpdateNote"] = "Configuration migrated and refreshed successfully."
            }, this, "en");
        }

        #endregion

        #region Lifecycle

        private void Init()
        {
            LoadData();
            RegisterPermissions();
            RegisterDynamicRulePermissions();
        }

        private void OnServerInitialized()
        {
            StartRefreshTimer();
            NextTick(RefreshAllPlayers);
            RegisterPlaceholders();
            PrintBanner();
        }

        private void Unload()
        {
            if (_refreshTimer != null)
            {
                _refreshTimer.Destroy();
                _refreshTimer = null;
            }

            ClearAllOverheadUi();
            SaveData();
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (player == null)
            {
                return;
            }

            NextTick(delegate
            {
                RefreshPlayer(player);
                RefreshTargetForAllViewers(player);
            });
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null)
            {
                return;
            }

            // The client's UI is already destroyed by discounting, but we are cleaning our local tracker.
            // (not necessary, but we'll leave it for the sake of order)
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
            if (!string.IsNullOrWhiteSpace(perm))
            {
                RegisterDynamicRulePermissionIfNeeded(perm);
            }
        }

        private void OnUserPermissionRevoked(string id, string perm)
        {
            RefreshByUserId(id);
        }

        private void OnServerSave()
        {
            SaveData();
        }

        private void OnPluginLoaded(Plugin plugin)
        {
            if (plugin != null && plugin.Name == "PlaceholderAPI")
            {
                RegisterPlaceholders();
            }
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

        // Separate mhide/mshow commands are used not only as a subcommand /mtags mhide, but also directly.
        [ChatCommand("mhide")]
        private void CmdMHide(BasePlayer player, string command, string[] args)
        {
            string[] combined = new string[(args?.Length ?? 0) + 1];
            combined[0] = "mhide";
            if (args != null && args.Length > 0)
            {
                Array.Copy(args, 0, combined, 1, args.Length);
            }

            HandleViewerHide(player, combined);
        }

        [ChatCommand("mshow")]
        private void CmdMShow(BasePlayer player, string command, string[] args)
        {
            HandleViewerHide(player, new[] { "mshow", "off" });
        }

        [ConsoleCommand("magictags")]
        private void CCmdMagicTags(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            string[] args = arg.Args?.Select(a => a.ToString()).ToArray() ?? new string[0];
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
                    Reply(player, "Info",
                        Version,
                        GetOnlineCount(),
                        GetStoredProfileCount(),
                        GetRuleCount(),
                        _config.General.ViewDistance,
                        _config.General.PlayerMinimumViewDistance,
                        _config.General.PlayerMaximumViewDistance,
                        _config.General.UpdateInterval);
                    return;

                case "config":
                    Reply(player, "ConfigSummary",
                        _config.General.Enabled,
                        _config.General.UseChatPrefix,
                        _config.DefaultPrefix.Text,
                        _config.DefaultPrefix.Color,
                        _config.DefaultPrefix.Size,
                        _config.AdminPrefix.Text,
                        _config.AdminPrefix.Color,
                        _config.AdminPrefix.Size,
                        _config.AdminPrefix.AlwaysVisible,
                        _config.General.ViewDistance,
                        _config.General.PlayerMinimumViewDistance,
                        _config.General.PlayerMaximumViewDistance,
                        _config.General.TextLifetime,
                        _config.General.TextHeightOffset,
                        _config.General.RequirePermissionToSeeTags,
                        _config.General.AllowPlayerRangeControl,
                        _config.General.OverheadFieldOfView,
                        _config.General.OverheadAspectRatio,
                        string.IsNullOrEmpty(_config.General.DisplayNameFormat) ? "(hidden)" : _config.General.DisplayNameFormat,
                        GetRuleCount());
                    return;

                case "hide":
                    HandleHideOwnTag(player, true);
                    return;

                case "show":
                    HandleHideOwnTag(player, false);
                    return;

                case "mhide":
                    HandleViewerHide(player, args);
                    return;

                case "mshow":
                    HandleViewerHide(player, new[] { "mshow", "off" });
                    return;

                case "range":
                    HandleRangeCommand(player, args);
                    return;

                case "prefix":
                    HandlePersonalPrefix(player, args);
                    return;

                case "color":
                    HandlePersonalColor(player, args);
                    return;

                case "nick":
                case "nickname":
                    HandlePersonalDisplayName(player, args);
                    return;

                case "clear":
                    HandleClearPersonal(player);
                    return;

                case "list":
                    HandleListRules(player, args);
                    return;

                case "addrule":
                case "add":
                    HandleAddRule(player, args);
                    return;

                case "setrule":
                case "set":
                    HandleSetRule(player, args);
                    return;

                case "removerule":
                case "remove":
                case "delete":
                    HandleRemoveRule(player, args);
                    return;

                case "reload":
                    if (!HasPermission(player, PermissionReload) && !HasPermission(player, PermissionManage))
                    {
                        Reply(player, "NoPermission");
                        return;
                    }

                    LoadConfig();
                    LoadData();
                    RegisterPermissions();
                    RegisterDynamicRulePermissions();
                    StartRefreshTimer();
                    RefreshAllPlayers();
                    Reply(player, "Reloaded");
                    return;

                case "sync":
                    if (!HasPermission(player, PermissionManage))
                    {
                        Reply(player, "NoPermission");
                        return;
                    }

                    RefreshAllPlayers();
                    Reply(player, "Synced");
                    return;

                default:
                    Reply(player, "UnknownSubcommand");
                    return;
            }
        }

        private void HandleHideOwnTag(BasePlayer player, bool hide)
        {
            if (player == null)
            {
                return;
            }

            if (!HasPermission(player, PermissionHideOwn))
            {
                Reply(player, "NoPermission");
                return;
            }

            PlayerSettings settings = GetPlayerSettings(player.userID);
            settings.HideOwnTag = hide;
            Stamp(settings, player.UserIDString);
            SaveData();
            RefreshAllPlayers();
            Reply(player, hide ? "HiddenOwnOn" : "HiddenOwnOff");
        }

        private void HandleViewerHide(BasePlayer player, string[] args)
        {
            if (player == null)
            {
                return;
            }

            PlayerSettings settings = GetPlayerSettings(player.userID);
            bool newValue = !settings.HideOtherTags;

            if (args != null && args.Length > 1)
            {
                string value = args[1].ToLowerInvariant();
                if (value == "on" || value == "true" || value == "1" || value == "full")
                {
                    newValue = true;
                }
                else if (value == "off" || value == "false" || value == "0" || value == "show")
                {
                    newValue = false;
                }
                else if (value != "toggle")
                {
                    Reply(player, "InvalidUsage");
                    return;
                }
            }

            settings.HideOtherTags = newValue;
            Stamp(settings, player.UserIDString);
            SaveData();
            RefreshPlayer(player);
            Reply(player, newValue ? "ViewerHideOn" : "ViewerHideOff");
        }

        private void HandleRangeCommand(BasePlayer player, string[] args)
        {
            if (player == null)
            {
                return;
            }

            if (!_config.General.AllowPlayerRangeControl)
            {
                Reply(player, "NoPermission");
                return;
            }

            if (args.Length < 2)
            {
                Reply(player, "InvalidUsage");
                return;
            }

            PlayerSettings settings = GetPlayerSettings(player.userID);
            string value = args[1].Trim().ToLowerInvariant();

            if (value == "off" || value == "default" || value == "reset")
            {
                settings.CustomViewDistance = -1f;
                Stamp(settings, player.UserIDString);
                SaveData();
                RefreshPlayer(player);
                Reply(player, "RangeCleared");
                return;
            }

            float distance;
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out distance))
            {
                Reply(player, "InvalidNumber");
                return;
            }

            distance = Mathf.Clamp(distance, _config.General.PlayerMinimumViewDistance, _config.General.PlayerMaximumViewDistance);
            settings.CustomViewDistance = distance;
            Stamp(settings, player.UserIDString);
            SaveData();
            RefreshPlayer(player);
            Reply(player, "RangeSet", distance);
        }

        private void HandlePersonalPrefix(BasePlayer player, string[] args)
        {
            if (player == null)
            {
                return;
            }

            if (!HasPermission(player, PermissionCustomPrefix))
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
            PlayerSettings settings = GetPlayerSettings(player.userID);
            settings.CustomPrefix = prefix;
            Stamp(settings, player.UserIDString);
            SaveData();
            RefreshAllPlayers();
            Reply(player, "PrefixSet", prefix);
        }

        private void HandlePersonalColor(BasePlayer player, string[] args)
        {
            if (player == null)
            {
                return;
            }

            if (!HasPermission(player, PermissionCustomColor))
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

            PlayerSettings settings = GetPlayerSettings(player.userID);
            settings.CustomPrefixColor = color;
            Stamp(settings, player.UserIDString);
            SaveData();
            RefreshAllPlayers();
            Reply(player, "ColorSet", color);
        }

        private void HandlePersonalDisplayName(BasePlayer player, string[] args)
        {
            if (player == null)
            {
                return;
            }

            if (!HasPermission(player, PermissionCustomNickname))
            {
                Reply(player, "NoPermission");
                return;
            }

            if (args.Length < 2)
            {
                Reply(player, "InvalidUsage");
                return;
            }

            PlayerSettings settings = GetPlayerSettings(player.userID);
            string value = args[1].Trim().ToLowerInvariant();

            if (value == "off" || value == "clear" || value == "reset" || value == "default")
            {
                settings.CustomDisplayName = string.Empty;
                Stamp(settings, player.UserIDString);
                SaveData();
                RefreshAllPlayers();
                Reply(player, "NicknameCleared");
                return;
            }

            string nickname = EscapeRichText(string.Join(" ", args.Skip(1).ToArray()).Trim());
            if (string.IsNullOrWhiteSpace(nickname))
            {
                Reply(player, "InvalidUsage");
                return;
            }

            settings.CustomDisplayName = nickname;
            Stamp(settings, player.UserIDString);
            SaveData();
            RefreshAllPlayers();
            Reply(player, "NicknameSet", nickname);
        }

        private void HandleClearPersonal(BasePlayer player)
        {
            if (player == null)
            {
                return;
            }

            if (!HasPermission(player, PermissionCustomPrefix) && !HasPermission(player, PermissionCustomColor) &&
                !HasPermission(player, PermissionHideOwn) && !HasPermission(player, PermissionCustomNickname))
            {
                Reply(player, "NoPermission");
                return;
            }

            PlayerSettings settings = GetPlayerSettings(player.userID);
            settings.HideOwnTag = false;
            settings.HideOtherTags = false;
            settings.CustomViewDistance = -1f;
            settings.CustomPrefix = string.Empty;
            settings.CustomPrefixColor = string.Empty;
            settings.CustomDisplayName = string.Empty;
            Stamp(settings, player.UserIDString);
            SaveData();
            RefreshAllPlayers();
            Reply(player, "Cleared");
        }

        private void HandleListRules(BasePlayer player, string[] args)
        {
            if (!HasPermission(player, PermissionManage))
            {
                Reply(player, "NoPermission");
                return;
            }

            List<PrefixRule> rules = _config.Prefixes ?? new List<PrefixRule>();
            if (rules.Count == 0)
            {
                SendLine(player, "No prefix rules are configured.");
                return;
            }

            int page = 1;
            if (args.Length > 1)
            {
                int parsed;
                if (int.TryParse(args[1], out parsed))
                {
                    page = parsed;
                }
            }

            const int perPage = 8;
            int totalPages = Mathf.Max(1, Mathf.CeilToInt((float)rules.Count / perPage));
            page = Mathf.Clamp(page, 1, totalPages);

            Reply(player, "RuleListHeader", page, totalPages);

            int start = (page - 1) * perPage;
            int end = Mathf.Min(start + perPage, rules.Count);

            for (int i = start; i < end; i++)
            {
                PrefixRule rule = rules[i];
                Reply(player, "RuleListEntry",
                    i + 1,
                    rule.Key,
                    rule.Enabled,
                    rule.Priority,
                    rule.Type,
                    rule.Access,
                    rule.Text,
                    rule.Color,
                    rule.Size,
                    rule.AlwaysVisible);
            }
        }

        private void HandleAddRule(BasePlayer player, string[] args)
        {
            if (!HasPermission(player, PermissionManage))
            {
                Reply(player, "NoPermission");
                return;
            }

            if (args.Length < 5)
            {
                Reply(player, "InvalidUsage");
                return;
            }

            string key = NormalizeKey(args[1], string.Empty);
            if (string.IsNullOrWhiteSpace(key))
            {
                Reply(player, "InvalidUsage");
                return;
            }

            if (FindRule(key) != null)
            {
                Reply(player, "RuleExists", key);
                return;
            }

            PrefixRule rule = new PrefixRule
            {
                Key = key,
                Type = ParseAccessType(args[2]),
                Access = args[3].Trim(),
                Text = args[4],
                Color = args.Length > 5 ? NormalizeHex(args[5], "#ffffff") : "#ffffff",
                Size = args.Length > 6 ? ParseInt(args[6], 12) : 12,
                Priority = args.Length > 7 ? ParseInt(args[7], 0) : 0,
                AlwaysVisible = args.Length > 8 && ParseBool(args[8], false)
            };

            rule.Size = ClampInt(rule.Size, 10, 24, 12);

            _config.Prefixes.Add(rule);
            _config.Prefixes = _config.Prefixes
                .Where(x => x != null)
                .OrderByDescending(x => x.Priority)
                .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            SaveConfig();
            RegisterDynamicRulePermissions();
            RefreshAllPlayers();
            Reply(player, "RuleAdded", key);
        }

        private void HandleSetRule(BasePlayer player, string[] args)
        {
            if (!HasPermission(player, PermissionManage))
            {
                Reply(player, "NoPermission");
                return;
            }

            if (args.Length < 4)
            {
                Reply(player, "InvalidUsage");
                return;
            }

            string key = NormalizeKey(args[1], string.Empty);
            PrefixRule rule = FindRule(key);
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
                    {
                        bool parsed;
                        if (!TryParseBool(value, out parsed))
                        {
                            Reply(player, "InvalidUsage");
                            return;
                        }

                        rule.Enabled = parsed;
                        break;
                    }

                case "priority":
                    {
                        int parsed;
                        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                        {
                            Reply(player, "InvalidNumber");
                            return;
                        }

                        rule.Priority = parsed;
                        break;
                    }

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
                    {
                        int parsed;
                        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                        {
                            Reply(player, "InvalidNumber");
                            return;
                        }

                        rule.Size = ClampInt(parsed, 10, 24, 12);
                        break;
                    }

                case "alwaysvisible":
                    {
                        bool parsed;
                        if (!TryParseBool(value, out parsed))
                        {
                            Reply(player, "InvalidUsage");
                            return;
                        }

                        rule.AlwaysVisible = parsed;
                        break;
                    }

                default:
                    Reply(player, "InvalidUsage");
                    return;
            }

            _config.Prefixes = _config.Prefixes
                .Where(x => x != null)
                .OrderByDescending(x => x.Priority)
                .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            SaveConfig();
            RegisterDynamicRulePermissions();
            RefreshAllPlayers();
            Reply(player, "RuleUpdated", key);
        }

        private void HandleRemoveRule(BasePlayer player, string[] args)
        {
            if (!HasPermission(player, PermissionManage))
            {
                Reply(player, "NoPermission");
                return;
            }

            if (args.Length < 2)
            {
                Reply(player, "InvalidUsage");
                return;
            }

            string key = NormalizeKey(args[1], string.Empty);
            PrefixRule rule = FindRule(key);
            if (rule == null)
            {
                Reply(player, "RuleNotFound", key);
                return;
            }

            _config.Prefixes.Remove(rule);
            SaveConfig();
            RegisterDynamicRulePermissions();
            RefreshAllPlayers();
            Reply(player, "RuleRemoved", key);
        }

        #endregion

        #region Rendering

        private class PrefixVisual
        {
            public string Text;
            public string Color;
            public int Size;
            public bool AlwaysVisible;
        }

        private bool ShouldRenderForViewer(BasePlayer viewer)
        {
            if (viewer == null || _config == null || _config.General == null)
            {
                return false;
            }

            if (!_config.General.Enabled)
            {
                return false;
            }

            if (viewer.IsAdmin)
            {
                return true;
            }

            if (!_config.General.RequirePermissionToSeeTags)
            {
                return true;
            }

            return permission.UserHasPermission(viewer.UserIDString, PermissionView);
        }

        private bool ShouldShowToViewer(BasePlayer viewer, BasePlayer target, PrefixVisual visual)
        {
            if (viewer == null || target == null || visual == null)
            {
                return false;
            }

            if (!viewer.IsConnected || !target.IsConnected)
            {
                return false;
            }

            if (!_config.General.ShowTagsToSelf && viewer.userID == target.userID)
            {
                return false;
            }

            PlayerSettings viewerSettings = GetPlayerSettings(viewer.userID);
            if (viewerSettings.HideOtherTags && viewer.userID != target.userID && !visual.AlwaysVisible)
            {
                return false;
            }

            float effectiveDistance = GetEffectiveViewDistance(viewerSettings);
            if (effectiveDistance <= 0f)
            {
                return false;
            }

            Vector3 viewerPos = GetViewPosition(viewer);
            Vector3 targetPos = GetViewPosition(target);
            float maxDistance = effectiveDistance * effectiveDistance;
            if ((viewerPos - targetPos).sqrMagnitude > maxDistance)
            {
                return false;
            }

            return true;
        }

        private PrefixVisual ResolvePrefix(BasePlayer target)
        {
            if (target == null)
            {
                return null;
            }

            PlayerSettings settings = GetPlayerSettings(target.userID);
            if (settings.HideOwnTag)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(settings.CustomPrefix))
            {
                return new PrefixVisual
                {
                    Text = settings.CustomPrefix.Trim(),
                    Color = NormalizeHex(settings.CustomPrefixColor, "#ff66cc"),
                    Size = 12,
                    AlwaysVisible = false
                };
            }

            PrefixRule rule = FindMatchingRule(target);
            if (rule != null)
            {
                return new PrefixVisual
                {
                    Text = rule.Text,
                    Color = NormalizeHex(rule.Color, "#ffffff"),
                    Size = rule.Size,
                    AlwaysVisible = rule.AlwaysVisible
                };
            }

            if (target.IsAdmin && _config.AdminPrefix != null && _config.AdminPrefix.Enabled)
            {
                return new PrefixVisual
                {
                    Text = _config.AdminPrefix.Text,
                    Color = _config.AdminPrefix.Color,
                    Size = _config.AdminPrefix.Size,
                    AlwaysVisible = _config.AdminPrefix.AlwaysVisible
                };
            }

            if (_config.DefaultPrefix != null && _config.DefaultPrefix.Enabled)
            {
                return new PrefixVisual
                {
                    Text = _config.DefaultPrefix.Text,
                    Color = _config.DefaultPrefix.Color,
                    Size = _config.DefaultPrefix.Size,
                    AlwaysVisible = _config.DefaultPrefix.AlwaysVisible
                };
            }

            return null;
        }

        private PrefixRule FindMatchingRule(BasePlayer player)
        {
            if (player == null || _config == null || _config.Prefixes == null)
            {
                return null;
            }

            for (int i = 0; i < _config.Prefixes.Count; i++)
            {
                PrefixRule rule = _config.Prefixes[i];
                if (rule == null || !rule.Enabled)
                {
                    continue;
                }

                if (MatchesRule(player, rule))
                {
                    return rule;
                }
            }

            return null;
        }

        private bool MatchesRule(BasePlayer player, PrefixRule rule)
        {
            if (player == null || rule == null)
            {
                return false;
            }

            string access = string.IsNullOrWhiteSpace(rule.Access) ? string.Empty : rule.Access.Trim();
            if (string.IsNullOrWhiteSpace(access))
            {
                return false;
            }

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

        // Returns permission/group, the node that gave the player the current tag (for %magictags_perms% placeholder).
        // For special cases (custom personal prefix/admin tag/default) returns a conditional tag instead of a real node.
        private string ResolveAccessNode(BasePlayer target)
        {
            if (target == null)
            {
                return string.Empty;
            }

            PlayerSettings settings = GetPlayerSettings(target.userID);
            if (settings.HideOwnTag)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(settings.CustomPrefix))
            {
                return "custom";
            }

            PrefixRule rule = FindMatchingRule(target);
            if (rule != null)
            {
                return string.IsNullOrWhiteSpace(rule.Access) ? rule.Key : rule.Access;
            }

            if (target.IsAdmin && _config.AdminPrefix != null && _config.AdminPrefix.Enabled)
            {
                return "admin";
            }

            if (_config.DefaultPrefix != null && _config.DefaultPrefix.Enabled)
            {
                return "default";
            }

            return string.Empty;
        }

        // Resolves the display name of the player according to the format from the config ("Display Name Format").
        // Priority: 1) Player's custom nickname (/mtags nick), 2) Template from the config with substitution
        // %magictags_nickname%. If the final result is empty (including when the template in the config
        // is intentionally left empty) — the nickname is considered hidden, and the calling code should simply not show it.
        private string ResolveDisplayName(BasePlayer target)
        {
            if (target == null)
            {
                return string.Empty;
            }

            PlayerSettings settings = GetPlayerSettings(target.userID);
            if (!string.IsNullOrWhiteSpace(settings.CustomDisplayName))
            {
                return settings.CustomDisplayName.Trim();
            }

            string format = _config != null && _config.General != null ? _config.General.DisplayNameFormat : DefaultDisplayNameFormat;
            if (string.IsNullOrEmpty(format))
            {
                return string.Empty;
            }

            string realNickname = string.IsNullOrEmpty(target.displayName) ? string.Empty : target.displayName;
            return format.Replace(DisplayNameNicknameToken, realNickname).Trim();
        }

        // The render entry point for a single viewer. For real admins (viewer.isAdmin is already true —
        // no flag is faked to anyone here) we draw using ddraw.text: this is a native engine
        // draw-call with accurate projection, without any approximation. For regular players — via CUI,
        // because ddraw is not available for them without artificially replacing the admin flag, and this is just
        // and there was an initial vulnerability. Here, PermissionView does not apply to the admins themselves — they always see everything.
        private void DrawTagsForViewer(BasePlayer viewer)
        {
            if (viewer == null || !viewer.IsConnected)
                return;

            if (!ShouldRenderForViewer(viewer))
            {
                CuiHelper.DestroyUi(viewer, OverheadPanelName);
                return;
            }

            if (viewer.IsAdmin)
            {
                    
                CuiHelper.DestroyUi(viewer, OverheadPanelName);
                DrawTagsViaDdraw(viewer);
                return;
            }

            DrawTagsViaCui(viewer);
        }

        // Rendering for real admins: ddraw.text, exact position, no approximation.
        private void DrawTagsViaDdraw(BasePlayer viewer)
        {
            var targets = BasePlayer.activePlayerList;
            for (int i = 0; i < targets.Count; i++)
            {
                BasePlayer target = targets[i];
                if (target == null || !target.IsConnected)
                {
                    continue;
                }

                PrefixVisual visual = ResolvePrefix(target);
                if (visual == null || string.IsNullOrWhiteSpace(visual.Text))
                {
                    continue;
                }

                if (!ShouldShowToViewer(viewer, target, visual))
                {
                    continue;
                }

                Vector3 position = GetViewPosition(target) + new Vector3(0f, _config.General.TextHeightOffset, 0f);
                Color drawColor;
                if (!TryParseColor(visual.Color, out drawColor))
                {
                    drawColor = Color.white;
                }

                int size = ClampInt(visual.Size, 10, 24, 12);
                viewer.SendConsoleCommand("ddraw.text", _config.General.TextLifetime, drawColor, position, EscapeRichText(visual.Text), size);
            }
        }

        // Draws all the labels for a specific viewer via CUI
        private void DrawTagsViaCui(BasePlayer viewer)
        {
            var container = new CuiElementContainer();
            // Creating an empty container panel (without a background) - it will hold all the labels.
            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, "Overlay", OverheadPanelName);

            int labelIndex = 0;
            var targets = BasePlayer.activePlayerList;
            for (int i = 0; i < targets.Count; i++)
            {
                BasePlayer target = targets[i];
                if (target == null || !target.IsConnected)
                    continue;

                if (TryAddOverheadLabel(viewer, target, container, labelIndex))
                    labelIndex++;
            }

            // Deleting the old UI and adding a new one
            CuiHelper.DestroyUi(viewer, OverheadPanelName);
            if (container.Count > 1) // if there is at least one label
            {
                CuiHelper.AddUi(viewer, container);
            }
        }

        // Builds one CUI label for the pair (viewer, target) and adds it to the container.
        private bool TryAddOverheadLabel(BasePlayer viewer, BasePlayer target, CuiElementContainer container, int elementIndex)
        {
            PrefixVisual visual = ResolvePrefix(target);
            if (visual == null || string.IsNullOrWhiteSpace(visual.Text))
            {
                return false;
            }

            if (!ShouldShowToViewer(viewer, target, visual))
            {
                return false;
            }

            Vector3 worldPosition = GetViewPosition(target) + new Vector3(0f, _config.General.TextHeightOffset, 0f);

            Vector2 screenPoint;
            if (!TryWorldToScreen(viewer, worldPosition, out screenPoint))
            {
                return false;
            }

            int fontSize = ClampInt(visual.Size, 10, 24, 12);
            string cuiColor = ToCuiColor(visual.Color);

            int halfWidth = Mathf.Clamp(fontSize * 5, 40, 160);
            int halfHeight = fontSize + 2;

            string anchor = screenPoint.x.ToString("0.####", CultureInfo.InvariantCulture) + " " +
                             screenPoint.y.ToString("0.####", CultureInfo.InvariantCulture);
            string elementName = OverheadPanelName + "_label_" + elementIndex.ToString(CultureInfo.InvariantCulture);

            container.Add(new CuiLabel
            {
                Text =
                {
                    Text = EscapeRichText(visual.Text),
                    FontSize = fontSize,
                    Align = TextAnchor.MiddleCenter,
                    Color = cuiColor
                },
                RectTransform =
                {
                    AnchorMin = anchor,
                    AnchorMax = anchor,
                    OffsetMin = (-halfWidth).ToString(CultureInfo.InvariantCulture) + " " + (-halfHeight).ToString(CultureInfo.InvariantCulture),
                    OffsetMax = halfWidth.ToString(CultureInfo.InvariantCulture) + " " + halfHeight.ToString(CultureInfo.InvariantCulture)
                }
            }, OverheadPanelName, elementName);

            return true;
        }

        // Manual projection of a world point onto the viewer's screen.
        // The FOV/aspect are taken from the config (not the hardcode), because the players have a real vertical FOV
        // in Rust, it differs from player to player (graphics slider.fov, usually 60-90, default 90) — server
        // does not know the exact value of a specific client. It used to be 60° here with a real default
        // the 90° angle of the engine, which caused the label to systematically move away from the player's native signature
        // (she uses the client's real camera). The configuration in the config allows the admin to adjust
        // projection for the typical settings of your audience, but 100% pixel-by-pixel accuracy
        // it is unattainable without knowing the real FOV of each client — hence the residual, but minimal, drift.
        private bool TryWorldToScreen(BasePlayer viewer, Vector3 worldPosition, out Vector2 screenPoint)
        {
            screenPoint = Vector2.zero;

            if (viewer == null || viewer.eyes == null)
            {
                return false;
            }

            Vector3 eyePosition = viewer.eyes.position;
            Quaternion eyeRotation = viewer.eyes.rotation;

            Vector3 direction = worldPosition - eyePosition;
            Vector3 local = Quaternion.Inverse(eyeRotation) * direction;

            if (local.z <= 0.15f)
            {
                return false;
            }

            float fovDegrees = _config != null && _config.General != null ? _config.General.OverheadFieldOfView : 90f;
            float aspectRatio = _config != null && _config.General != null ? _config.General.OverheadAspectRatio : 1.7777778f;

            float tanHalfFov = Mathf.Tan(fovDegrees * Mathf.Deg2Rad * 0.5f);

            float ndcX = local.x / (local.z * tanHalfFov * aspectRatio);
            float ndcY = local.y / (local.z * tanHalfFov);

            const float edgeMargin = 0.92f;
            if (Mathf.Abs(ndcX) > edgeMargin || Mathf.Abs(ndcY) > edgeMargin)
            {
                return false;
            }

            screenPoint = new Vector2((ndcX + 1f) * 0.5f, (ndcY + 1f) * 0.5f);
            return true;
        }

        private static string ToCuiColor(string hex, float alpha = 1f)
        {
            Color color;
            if (!TryParseColor(hex, out color))
            {
                color = Color.white;
            }

            return color.r.ToString("0.###", CultureInfo.InvariantCulture) + " " +
                   color.g.ToString("0.###", CultureInfo.InvariantCulture) + " " +
                   color.b.ToString("0.###", CultureInfo.InvariantCulture) + " " +
                   alpha.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private Vector3 GetViewPosition(BasePlayer player)
        {
            if (player == null)
            {
                return Vector3.zero;
            }

            if (player.eyes != null)
            {
                return player.eyes.position;
            }

            if (player.transform != null)
            {
                return player.transform.position;
            }

            return Vector3.zero;
        }

        // Clearing the entire UI for all players
        private void ClearAllOverheadUi()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(player, OverheadPanelName);
            }
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

            if (_config == null || _config.General == null || !_config.General.Enabled)
            {
                return;
            }

            _refreshTimer = timer.Every(Mathf.Clamp(_config.General.UpdateInterval, 0.1f, 10f), RefreshAllPlayers);
        }

        private void RefreshAllPlayers()
        {
            if (_config == null || _config.General == null || !_config.General.Enabled)
            {
                return;
            }

            var viewers = BasePlayer.activePlayerList;
            for (int i = 0; i < viewers.Count; i++)
            {
                BasePlayer viewer = viewers[i];
                if (viewer == null || !viewer.IsConnected)
                {
                    continue;
                }

                DrawTagsForViewer(viewer);
            }
        }

        private void RefreshPlayer(BasePlayer viewer)
        {
            if (viewer == null || !viewer.IsConnected)
                return;

            DrawTagsForViewer(viewer);
        }

        private void RefreshTargetForAllViewers(BasePlayer target)
        {
            if (target == null || !target.IsConnected)
                return;

            // Redrawing all viewers who can see this target
            var viewers = BasePlayer.activePlayerList;
            for (int i = 0; i < viewers.Count; i++)
            {
                BasePlayer viewer = viewers[i];
                if (viewer == null || !viewer.IsConnected)
                    continue;

                DrawTagsForViewer(viewer);
            }
        }

        private void RefreshByUserId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return;
            }

            ulong userId;
            if (!ulong.TryParse(id, out userId))
            {
                return;
            }

            BasePlayer player = BasePlayer.FindByID(userId);
            if (player == null)
            {
                player = BasePlayer.FindSleeping(userId);
            }

            if (player != null)
            {
                RefreshPlayer(player);
                RefreshTargetForAllViewers(player);
            }
        }

        #endregion

        #region Permissions

        private void RegisterPermissions()
        {
            permission.RegisterPermission(PermissionView, this);
            permission.RegisterPermission(PermissionHideOwn, this);
            permission.RegisterPermission(PermissionCustomPrefix, this);
            permission.RegisterPermission(PermissionCustomColor, this);
            permission.RegisterPermission(PermissionCustomNickname, this);
            permission.RegisterPermission(PermissionManage, this);
            permission.RegisterPermission(PermissionReload, this);
        }

        private void RegisterDynamicRulePermissions()
        {
            if (_config == null || _config.Prefixes == null)
            {
                return;
            }

            for (int i = 0; i < _config.Prefixes.Count; i++)
            {
                PrefixRule rule = _config.Prefixes[i];
                if (rule == null || string.IsNullOrWhiteSpace(rule.Access))
                {
                    continue;
                }

                if (rule.Type == PrefixAccessType.Group)
                {
                    continue;
                }

                RegisterDynamicRulePermissionIfNeeded(rule.Access);
            }
        }

        private void RegisterDynamicRulePermissionIfNeeded(string access)
        {
            if (string.IsNullOrWhiteSpace(access))
            {
                return;
            }

            string normalized = access.Trim();
            try
            {
                if (!permission.PermissionExists(normalized))
                {
                    permission.RegisterPermission(normalized, this);
                }
            }
            catch
            {
                // Some entries are intentionally groups rather than permissions.
            }
        }

        private bool HasPermission(BasePlayer player, string permissionName)
        {
            if (player == null)
            {
                return true;
            }

            if (player.IsAdmin)
            {
                return true;
            }

            return permission.UserHasPermission(player.UserIDString, permissionName);
        }

        #endregion

        #region Placeholders

        // Integration with the "Placeholder API" plugin: registers %magictags_xxx% placeholders,
        // which can be used by third-party chat plugins (IQChat and the like).
        //
        // IMPORTANT: the "Placeholder API" in the Oxide/Rust ecosystem has different implementations with different
        // the names of the hooks. The most common option used here is the "AddPlaceholder" hook
        // with a signature (Plugin plugin, string placeholder, Func<BasePlayer, string> callback).
        // If you have a different implementation (or IQChat itself does not pull placeholders from the placeholder API,
        // and waiting for another hook to be called) — whistle, we'll fix the signature for a specific plugin.
        // In case the format doesn't match, there's also a public GetMagicTagsPlaceholder hook below.,
        // which any plugin can pull directly through the Interface.Oxide.CallHook.
        private void RegisterPlaceholders()
        {
            if (PlaceholderAPI == null)
            {
                return;
            }

            try
            {
                PlaceholderAPI.Call("AddPlaceholder", this, "magictags_prefix", new Func<BasePlayer, string>(GetPlaceholderPrefixRich));
                PlaceholderAPI.Call("AddPlaceholder", this, "magictags_text", new Func<BasePlayer, string>(GetPlaceholderPrefixText));
                PlaceholderAPI.Call("AddPlaceholder", this, "magictags_size", new Func<BasePlayer, string>(GetPlaceholderPrefixSize));
                PlaceholderAPI.Call("AddPlaceholder", this, "magictags_displayname", new Func<BasePlayer, string>(GetPlaceholderDisplayName));
                PlaceholderAPI.Call("AddPlaceholder", this, "magictags_perms", new Func<BasePlayer, string>(GetPlaceholderAccessNode));
            }
            catch (Exception ex)
            {
                PrintWarning("Could not register placeholders with PlaceholderAPI: " + ex.Message);
            }
        }

        private string GetPlaceholderPrefixRich(BasePlayer player)
        {
            PrefixVisual visual = ResolvePrefix(player);
            if (visual == null || string.IsNullOrWhiteSpace(visual.Text))
            {
                return string.Empty;
            }

            string color = NormalizeHex(visual.Color, "#ffffff");
            return "<color=" + color + ">" + EscapeRichText(visual.Text) + "</color>";
        }

        private string GetPlaceholderPrefixText(BasePlayer player)
        {
            PrefixVisual visual = ResolvePrefix(player);
            return visual == null ? string.Empty : EscapeRichText(visual.Text);
        }

        private string GetPlaceholderPrefixSize(BasePlayer player)
        {
            PrefixVisual visual = ResolvePrefix(player);
            return visual == null ? string.Empty : visual.Size.ToString(CultureInfo.InvariantCulture);
        }

        private string GetPlaceholderDisplayName(BasePlayer player)
        {
            return EscapeRichText(ResolveDisplayName(player));
        }

        private string GetPlaceholderAccessNode(BasePlayer player)
        {
            return ResolveAccessNode(player);
        }

        // Public hook — any other plugin (IQChat, custom integrations and so on) can call this
        // directly, without depending on a specific PlaceholderAPI implementation:
        // Interface.Oxide.CallHook("GetMagicTagsPlaceholder", player, "prefix");
        // key: prefix | text | size | displayname | perms
        private string GetMagicTagsPlaceholder(BasePlayer player, string key)
        {
            if (player == null || string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            switch (key.Trim().ToLowerInvariant())
            {
                case "prefix":
                    return GetPlaceholderPrefixRich(player);
                case "text":
                    return GetPlaceholderPrefixText(player);
                case "size":
                    return GetPlaceholderPrefixSize(player);
                case "displayname":
                    return GetPlaceholderDisplayName(player);
                case "perms":
                    return GetPlaceholderAccessNode(player);
                default:
                    return string.Empty;
            }
        }

        #endregion

        #region Helpers

        private void PrintBanner()
        {
            Puts("=================================================");
            Puts(" MagicTags v" + Version + " loaded");
            Puts(" Overhead prefixes, local visibility controls, and dynamic rules");
            Puts("=================================================");
        }

        private void Reply(BasePlayer player, string key, params object[] args)
        {
            string text = Lang(key, player != null ? player.UserIDString : "0", args);

            if (player != null)
            {
                if (_config != null && _config.Chat != null && _config.Chat.Enabled && _config.General.UseChatPrefix)
                {
                    string prefix = BuildChatPrefix();
                    player.SendConsoleCommand("chat.add", 2, _config.Chat.IconId, string.IsNullOrEmpty(prefix) ? text : prefix + " " + text);
                }
                else
                {
                    player.ChatMessage(text);
                }

                return;
            }

            Puts(ChatPrefix() + " " + text);
        }

        private void SendLine(BasePlayer player, string text)
        {
            if (player != null)
            {
                if (_config != null && _config.Chat != null && _config.Chat.Enabled && _config.General.UseChatPrefix)
                {
                    string prefix = BuildChatPrefix();
                    player.SendConsoleCommand("chat.add", 2, _config.Chat.IconId, string.IsNullOrEmpty(prefix) ? text : prefix + " " + text);
                }
                else
                {
                    player.ChatMessage(text);
                }
            }
            else
            {
                Puts(ChatPrefix() + " " + text);
            }
        }

        private string BuildChatPrefix()
        {
            if (_config == null || _config.Chat == null || !_config.Chat.Enabled)
            {
                return string.Empty;
            }

            string label = EscapeRichText(_config.Chat.Label);
            string color = NormalizeHex(_config.Chat.Color, "#cc66ff");
            int size = ClampInt(_config.Chat.Size, 10, 20, 12);

            return "<size=" + size + "><color=" + color + "><b>" + label + "</b></color></size>";
        }

        private void DebugLog(string message)
        {
            if (_config != null && _config.General != null && _config.General.DebugLogging)
            {
                Puts("[Debug] " + message);
            }
        }

        private float GetEffectiveViewDistance(PlayerSettings settings)
        {
            float distance = _config.General.ViewDistance;
            if (settings != null && settings.CustomViewDistance > 0f)
            {
                distance = settings.CustomViewDistance;
            }

            return Mathf.Clamp(distance, _config.General.PlayerMinimumViewDistance, _config.General.PlayerMaximumViewDistance);
        }

        private void Stamp(PlayerSettings settings, string updatedBy)
        {
            if (settings == null)
            {
                return;
            }

            settings.UpdatedBy = string.IsNullOrWhiteSpace(updatedBy) ? "console" : updatedBy;
            settings.UpdatedAt = DateTime.UtcNow.ToString("u", CultureInfo.InvariantCulture);
        }

        private int GetStoredProfileCount()
        {
            return _data != null && _data.Players != null ? _data.Players.Count : 0;
        }

        private int GetRuleCount()
        {
            return _config != null && _config.Prefixes != null ? _config.Prefixes.Count : 0;
        }

        private int GetOnlineCount()
        {
            return BasePlayer.activePlayerList != null ? BasePlayer.activePlayerList.Count : 0;
        }

        private PrefixRule FindRule(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || _config == null || _config.Prefixes == null)
            {
                return null;
            }

            for (int i = 0; i < _config.Prefixes.Count; i++)
            {
                PrefixRule rule = _config.Prefixes[i];
                if (rule != null && string.Equals(rule.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return rule;
                }
            }

            return null;
        }

        private PrefixAccessType ParseAccessType(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return PrefixAccessType.Permission;
            }

            string normalized = value.Trim().ToLowerInvariant();
            if (normalized == "group")
            {
                return PrefixAccessType.Group;
            }

            if (normalized == "any" || normalized == "both")
            {
                return PrefixAccessType.Any;
            }

            return PrefixAccessType.Permission;
        }

        private static bool TryParseBool(string value, out bool result)
        {
            if (bool.TryParse(value, out result))
            {
                return true;
            }

            string normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
            if (normalized == "1" || normalized == "yes" || normalized == "on" || normalized == "true")
            {
                result = true;
                return true;
            }

            if (normalized == "0" || normalized == "no" || normalized == "off" || normalized == "false")
            {
                result = false;
                return true;
            }

            result = false;
            return false;
        }

        private static bool ParseBool(string value, bool fallback)
        {
            bool result;
            return TryParseBool(value, out result) ? result : fallback;
        }

        private static int ParseInt(string value, int fallback)
        {
            int result;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) ? result : fallback;
        }

        private static float Clamp(float value, float min, float max, float fallback, ref bool migrated)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                migrated = true;
                return fallback;
            }

            float clamped = Mathf.Clamp(value, min, max);
            if (Math.Abs(clamped - value) > 0.0001f)
            {
                migrated = true;
            }

            return clamped;
        }

        private static float Clamp(float value, float min, float max, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return fallback;
            }

            return Mathf.Clamp(value, min, max);
        }

        private static int ClampInt(int value, int min, int max, int fallback, ref bool migrated)
        {
            int clamped = Mathf.Clamp(value, min, max);
            if (clamped != value)
            {
                migrated = true;
            }

            return clamped;
        }

        private static int ClampInt(int value, int min, int max, int fallback)
        {
            if (value < min || value > max)
            {
                return Mathf.Clamp(value, min, max);
            }

            return value;
        }

        private static string NormalizeKey(string value, string fallback, ref bool migrated)
        {
            string result = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();
            if (!string.Equals(result, value, StringComparison.Ordinal))
            {
                migrated = true;
            }

            return result;
        }

        private static string NormalizeKey(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();
        }

        private static string NormalizeText(string value, string fallback, ref bool migrated)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                migrated = true;
                return fallback;
            }

            string trimmed = value.Trim();
            if (!string.Equals(trimmed, value, StringComparison.Ordinal))
            {
                migrated = true;
            }

            return trimmed;
        }

        private static string NormalizeText(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static string NormalizeHex(string value, string fallback, ref bool migrated)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                migrated = true;
                return fallback;
            }

            string trimmed = value.Trim();
            if (!trimmed.StartsWith("#"))
            {
                trimmed = "#" + trimmed;
                migrated = true;
            }

            if (!Regex.IsMatch(trimmed, "^#(?:[0-9a-fA-F]{3}){1,2}$"))
            {
                migrated = true;
                return fallback;
            }

            if (!string.Equals(trimmed, value, StringComparison.Ordinal))
            {
                migrated = true;
            }

            return trimmed;
        }

        private static string NormalizeHex(string value, string fallback)
        {
            bool changed = false;
            return NormalizeHex(value, fallback, ref changed);
        }

        private static bool TryParseColor(string value, out Color color)
        {
            string hex = NormalizeHex(value, "#ffffff");
            return ColorUtility.TryParseHtmlString(hex, out color);
        }

        private string EscapeRichText(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("<", string.Empty).Replace(">", string.Empty);
        }

        private string Lang(string key, string id, params object[] args)
        {
            return string.Format(lang.GetMessage(key, this, id), args);
        }

        private static string ChatPrefix()
        {
            return "<size=12><color=#cc66ff><b>MagicTags</b></color></size>";
        }

        #endregion
    }
}