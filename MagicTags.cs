using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine;
using Oxide.Core.Libraries;

namespace Oxide.Plugins
{
    [Info("MagicTags", "whitecristafer", "1.0.0")]
    [Description("Prefix management for IQPermissions, TimedPermissions, Oxide Groups. " +
                 "Developer whitecristafer sponsored by infunv.ru for evolve.infunv.ru. Open‑source.")]
    public class MagicTags : RustPlugin
    {
        #region Constants & Permissions

        private const string PluginPrefix = "MagicPrefix";
        private const string UpdateSourceUrl = "https://raw.githubusercontent.com/whitecristafer/MagicTags/main/MagicTags.cs";
        private const string PermissionManage = "magictags.manage";
        private const string PermissionReload = "magictags.reload";
        private const string PermissionPersonal = "magictags.personal";
        private const string PermissionInfo = "magictags.info";
        private const string DataFileName = "MagicTags_Data";

        #endregion

        #region Configuration

        private Configuration _config;

        public class Configuration
        {
            [JsonProperty("General")]
            public GeneralSettings General = new GeneralSettings();

            [JsonProperty("Default Prefix")]
            public DefaultPrefixSettings DefaultPrefix = new DefaultPrefixSettings();

            [JsonProperty("Prefixes")]
            public List<PrefixEntry> Prefixes = new List<PrefixEntry>();

            public static Configuration CreateDefault()
            {
                return new Configuration
                {
                    General = new GeneralSettings
                    {
                        UpdateInterval = 0.5f,
                        MaxDisplayNameLength = 32,
                        ShowOverheadToSelf = false,
                        LogDebug = false
                    },
                    DefaultPrefix = new DefaultPrefixSettings
                    {
                        Enabled = true,
                        OverheadPrefix = "[Player]",
                        OverheadPrefixColor = "#a0a0a0",
                        OverheadNameColor = "#ffffff",
                        ChatPrefix = "",
                        ChatPrefixColor = "#ffffff"
                    },
                    Prefixes = new List<PrefixEntry>
                    {
                        new PrefixEntry
                        {
                            Key = "admin",
                            PermissionSuffix = "admin",
                            Type = PermissionType.Permission,
                            Enabled = true,
                            Priority = 100,
                            OverheadPrefix = "[Admin]",
                            OverheadPrefixColor = "#ff4444",
                            OverheadNameColor = "#ffffff",
                            ChatPrefix = "[Admin]",
                            ChatPrefixColor = "#ff4444"
                        },
                        new PrefixEntry
                        {
                            Key = "moderator",
                            PermissionSuffix = "moderator",
                            Type = PermissionType.Permission,
                            Enabled = true,
                            Priority = 50,
                            OverheadPrefix = "[Mod]",
                            OverheadPrefixColor = "#44ff44",
                            OverheadNameColor = "#ffffff",
                            ChatPrefix = "[Mod]",
                            ChatPrefixColor = "#44ff44"
                        }
                    }
                };
            }
        }

        public class GeneralSettings
        {
            [JsonProperty("Update interval (seconds)")]
            public float UpdateInterval = 0.5f;

            [JsonProperty("Max display name length (characters)")]
            public int MaxDisplayNameLength = 32;

            [JsonProperty("Show overhead prefix to self")]
            public bool ShowOverheadToSelf = false;

            [JsonProperty("Log debug info")]
            public bool LogDebug = false;
        }

        public class DefaultPrefixSettings
        {
            [JsonProperty("Enabled")]
            public bool Enabled = true;

            [JsonProperty("Overhead prefix")]
            public string OverheadPrefix = "[Player]";

            [JsonProperty("Overhead prefix color")]
            public string OverheadPrefixColor = "#a0a0a0";

            [JsonProperty("Overhead name color")]
            public string OverheadNameColor = "#ffffff";

            [JsonProperty("Chat prefix")]
            public string ChatPrefix = "";

            [JsonProperty("Chat prefix color")]
            public string ChatPrefixColor = "#ffffff";
        }

        public enum PermissionType
        {
            Permission,
            Group
        }

        public class PrefixEntry
        {
            [JsonProperty("Key")]
            public string Key = "admin";

            [JsonProperty("Permission / Group suffix")]
            public string PermissionSuffix = "admin";

            [JsonProperty("Type")]
            public PermissionType Type = PermissionType.Permission;

            [JsonProperty("Enabled")]
            public bool Enabled = true;

            [JsonProperty("Priority")]
            public int Priority = 0;

            [JsonProperty("Overhead prefix")]
            public string OverheadPrefix = "";

            [JsonProperty("Overhead prefix color")]
            public string OverheadPrefixColor = "#ffffff";

            [JsonProperty("Overhead name color")]
            public string OverheadNameColor = "#ffffff";

            [JsonProperty("Chat prefix")]
            public string ChatPrefix = "";

            [JsonProperty("Chat prefix color")]
            public string ChatPrefixColor = "#ffffff";
        }

        protected override void LoadDefaultConfig() => _config = Configuration.CreateDefault();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null) throw new Exception();
            }
            catch
            {
                PrintWarning(Lang("ConfigCorrupted"));
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config, true);

        #endregion

        #region Data (Personal Prefixes)

        private StoredData _data;

        public class StoredData
        {
            public Dictionary<ulong, PersonalPrefix> PersonalPrefixes = new Dictionary<ulong, PersonalPrefix>();
        }

        public class PersonalPrefix
        {
            public bool Enabled = true;
            public string OverheadPrefix = "";
            public string OverheadPrefixColor = "#ffffff";
            public string OverheadNameColor = "#ffffff";
            public string ChatPrefix = "";
            public string ChatPrefixColor = "#ffffff";
            public string UpdatedBy = "";
            public string UpdatedAt = "";
        }

        private void LoadData()
        {
            try
            {
                _data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(DataFileName) ?? new StoredData();
            }
            catch
            {
                _data = new StoredData();
            }
        }

        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(DataFileName, _data);

        #endregion

        #region Localization

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Prefix"] = $"<color=#00bfff>[{PluginPrefix}]</color>",
                ["NoPermission"] = "{0} You don't have permission to use this command.",
                ["Help"] = "<size=14><color=#00bfff>MagicTags</color></size> by <color=#ffaa00>whitecristafer</color>\n" +
                           "/magictags help - Show this help\n" +
                           "/magictags info - Plugin information\n" +
                           "/magictags list [page] - List configured prefixes\n" +
                           "/magictags add <key> <permissionSuffix> [type:perm/group] - Add prefix\n" +
                           "/magictags set <key> <field> <value> - Modify prefix\n" +
                           "/magictags remove <key> - Remove prefix\n" +
                           "/magictags personal <set|clear|info|color> <player|steamid> [value] - Personal prefix\n" +
                           "/magictags reload - Reload configuration\n" +
                           "/magictags sync - Force refresh all players",
                ["Info"] = "MagicTags v{0}\nDeveloper whitecristafer | infunv.ru | evolve.infunv.ru\n" +
                           "Prefixes: {1} total, {2} enabled | Personal: {3}\n" +
                           "Update interval: {4}s | Max name length: {5}",
                ["PrefixList"] = "Prefixes page {0}/{1}:",
                ["PrefixEntry"] = "  {0}. key={1} perm={2} enabled={3} priority={4} overhead='{5}' chat='{6}'",
                ["Added"] = "Prefix '{0}' added.",
                ["Removed"] = "Prefix '{0}' removed.",
                ["Updated"] = "Prefix '{0}' updated.",
                ["NotFound"] = "Prefix '{0}' not found.",
                ["Exists"] = "Prefix '{0}' already exists.",
                ["PersonalSet"] = "Personal prefix set for {0}.",
                ["PersonalCleared"] = "Personal prefix removed for {0}.",
                ["PersonalInfo"] = "Personal prefix for {0}: enabled={1}, overhead='{2}', chat='{3}', by={4}, at={5}.",
                ["Reloaded"] = "Configuration reloaded.",
                ["Synced"] = "All players refreshed.",
                ["ConfigCorrupted"] = "Configuration file corrupted, creating default.",
                ["UpdateCheckStart"] = "Checking for updates...",
                ["UpdateCurrent"] = "You already have the latest version ({0}).",
                ["UpdateAvailable"] = "New version {1} available (current {0}). Downloading...",
                ["UpdateDownloaded"] = "Update downloaded and applied. Plugin will reload.",
                ["UpdateFailed"] = "Update check failed.",
                ["ChatFormat"] = "{0}{1}: {2}"
            }, this, "en");

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Prefix"] = $"<color=#00bfff>[{PluginPrefix}]</color>",
                ["NoPermission"] = "{0} У вас нет прав на использование этой команды.",
                ["Help"] = "<size=14><color=#00bfff>MagicTags</color></size> от <color=#ffaa00>whitecristafer</color>\n" +
                           "/magictags help - Показать справку\n" +
                           "/magictags info - Информация о плагине\n" +
                           "/magictags list [страница] - Список префиксов\n" +
                           "/magictags add <ключ> <суффикс> [тип:perm/group] - Добавить префикс\n" +
                           "/magictags set <ключ> <поле> <значение> - Изменить префикс\n" +
                           "/magictags remove <ключ> - Удалить префикс\n" +
                           "/magictags personal <set|clear|info|color> <игрок|steamid> [значение] - Личный префикс\n" +
                           "/magictags reload - Перезагрузить конфигурацию\n" +
                           "/magictags sync - Принудительно обновить всех игроков",
                ["Info"] = "MagicTags v{0}\nРазработчик whitecristafer | infunv.ru | evolve.infunv.ru\n" +
                           "Префиксов: {1} всего, {2} включено | Личных: {3}\n" +
                           "Интервал обновления: {4}с | Макс. длина имени: {5}",
                ["PrefixList"] = "Префиксы, страница {0}/{1}:",
                ["PrefixEntry"] = "  {0}. ключ={1} права={2} вкл={3} приоритет={4} над головой='{5}' чат='{6}'",
                ["Added"] = "Префикс '{0}' добавлен.",
                ["Removed"] = "Префикс '{0}' удалён.",
                ["Updated"] = "Префикс '{0}' обновлён.",
                ["NotFound"] = "Префикс '{0}' не найден.",
                ["Exists"] = "Префикс '{0}' уже существует.",
                ["PersonalSet"] = "Личный префикс для {0} установлен.",
                ["PersonalCleared"] = "Личный префикс для {0} удалён.",
                ["PersonalInfo"] = "Личный префикс для {0}: включён={1}, над головой='{2}', чат='{3}', кем={4}, когда={5}.",
                ["Reloaded"] = "Конфигурация перезагружена.",
                ["Synced"] = "Все игроки обновлены.",
                ["ConfigCorrupted"] = "Файл конфигурации повреждён, создан новый.",
                ["UpdateCheckStart"] = "Проверка обновлений...",
                ["UpdateCurrent"] = "У вас уже последняя версия ({0}).",
                ["UpdateAvailable"] = "Доступна новая версия {1} (текущая {0}). Загрузка...",
                ["UpdateDownloaded"] = "Обновление загружено и применено. Плагин перезагрузится.",
                ["UpdateFailed"] = "Проверка обновлений не удалась.",
                ["ChatFormat"] = "{0}{1}: {2}"
            }, this, "ru");
        }

        private string Lang(string key, string playerId = null, params object[] args) =>
            string.Format(lang.GetMessage(key, this, playerId ?? "0"), args);

        #endregion

        #region Messaging

        private void Message(object receiver, string key, params object[] args)
        {
            string msg = Lang(key, (receiver as BasePlayer)?.UserIDString, args);
            string prefix = Lang("Prefix", (receiver as BasePlayer)?.UserIDString);
            if (receiver is BasePlayer player)
                player.ChatMessage($"{prefix} {msg}");
            else
                Puts($"{prefix} {msg}");
        }

        private void DebugLog(string message)
        {
            if (_config?.General.LogDebug ?? false)
                Puts($"[MagicTags][DEBUG] {message}");
        }

        #endregion

        #region Auto Update

        private static DateTime _lastUpdateCheck = DateTime.MinValue;
        private bool _updateReloadScheduled;

        private void PrintStartupBanner()
        {
            Puts("============================================================");
            Puts($"  MagicTags v{Version} by whitecristafer");
            Puts("  infunv.ru | evolve.infunv.ru");
            Puts("  Open‑source: https://github.com/whitecristafer/MagicTags");
            Puts("============================================================");
        }

        private void CheckForUpdates()
        {
            if ((DateTime.Now - _lastUpdateCheck).TotalSeconds < 60)
                return;

            Puts(Lang("UpdateCheckStart"));
            webrequest.Enqueue(UpdateSourceUrl, null, (code, response) =>
            {
                if (code != 200 || string.IsNullOrWhiteSpace(response))
                {
                    Puts(Lang("UpdateFailed"));
                    return;
                }

                string remoteVersionStr = ExtractVersion(response);
                if (string.IsNullOrWhiteSpace(remoteVersionStr) || !IsStableVersion(remoteVersionStr))
                {
                    Puts(Lang("UpdateFailed"));
                    return;
                }

                if (!IsStableVersion(Version.ToString()) || CompareVersions(remoteVersionStr, Version.ToString()) <= 0)
                {
                    Puts(Lang("UpdateCurrent", null, Version));
                    return;
                }

                Puts(Lang("UpdateAvailable", null, Version, remoteVersionStr));
                SaveUpdatedFile(response);
            }, this, RequestMethod.GET, null, 15f); 
        }

        // Add this new helper method anywhere in the class (e.g. near the other private methods)
        private int CompareVersions(string v1, string v2)
        {
            try
            {
                var version1 = new Version(v1);
                var version2 = new Version(v2);
                return version1.CompareTo(version2);
            }
            catch
            {
                return 0; // fallback if version format is invalid
            }
        }

        private void SaveUpdatedFile(string content)
        {
            string path = Path.Combine(Interface.Oxide.PluginDirectory, $"{Name}.cs");
            try
            {
                File.WriteAllText(path, content, new UTF8Encoding(false));
                _lastUpdateCheck = DateTime.Now;
                Puts(Lang("UpdateDownloaded"));
                if (!_updateReloadScheduled)
                {
                    _updateReloadScheduled = true;
                    timer.Once(3f, () => Server.Command($"oxide.reload {Name}"));
                }
            }
            catch (Exception ex)
            {
                PrintError($"Failed to write update: {ex.Message}");
            }
        }

        private string ExtractVersion(string source)
        {
            var match = Regex.Match(source, @"\[Info\(\s*""[^""]+""\s*,\s*""[^""]+""\s*,\s*""([^""]+)""\s*\)\]");
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }

        private bool IsStableVersion(string ver)
        {
            if (string.IsNullOrWhiteSpace(ver)) return false;
            return Regex.IsMatch(ver.Trim(), @"^\d+(\.\d+){1,3}$");
        }

        #endregion

        #region Oxide Hooks

        private void Init()
        {
            permission.RegisterPermission(PermissionManage, this);
            permission.RegisterPermission(PermissionReload, this);
            permission.RegisterPermission(PermissionPersonal, this);
            permission.RegisterPermission(PermissionInfo, this);

            LoadData();
            LoadConfig(); // already called, but ensures normalised
            EnsurePermissionsRegistered();
        }

        private void OnServerInitialized()
        {
            PrintStartupBanner();
            CheckForUpdates();
            StartRefreshTimer();
            NextTick(() => RefreshAllPlayers());
        }

        private void OnPlayerInit(BasePlayer player)
        {
            if (player == null) return;
            NextTick(() =>
            {
                if (player == null || !player.IsConnected) return;
                StoreOriginalName(player);
                UpdatePlayer(player, true);
            });
        }

        private void OnPlayerConnected(BasePlayer player) => OnPlayerInit(player);

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null) return;
            RestoreOriginalName(player);
            // _renderStates.Remove(player.userID); // deleted because the dictionary is not in use
        }
        private void OnUserGroupAdded(string id, string groupName)
        {
            if (!ulong.TryParse(id, out var uid)) return;
            var player = BasePlayer.FindByID(uid);
            if (player != null) UpdatePlayer(player, true);
        }

        private void OnUserGroupRemoved(string id, string groupName)
        {
            if (!ulong.TryParse(id, out var uid)) return;
            var player = BasePlayer.FindByID(uid);
            if (player != null) UpdatePlayer(player, true);
        }

        private void OnUserPermissionGranted(string id, string perm)
        {
            if (!ulong.TryParse(id, out var uid)) return;
            var player = BasePlayer.FindByID(uid);
            if (player != null) UpdatePlayer(player, true);
        }

        private void OnUserPermissionRevoked(string id, string perm)
        {
            if (!ulong.TryParse(id, out var uid)) return;
            var player = BasePlayer.FindByID(uid);
            if (player != null) UpdatePlayer(player, true);
        }

        private object OnPlayerChat(BasePlayer player, string message, ConVar.Chat.ChatChannel channel)
        {
            if (player == null) return null;
            var resolved = ResolveTag(player);
            string chatPrefix = resolved?.ChatPrefix ?? "";
            string chatColor = resolved?.ChatPrefixColor ?? "#ffffff";
            if (string.IsNullOrWhiteSpace(chatPrefix)) return null;

            string prefixColored = string.IsNullOrWhiteSpace(chatColor) || chatColor == "#ffffff"
                ? chatPrefix
                : $"<color={chatColor}>{chatPrefix}</color> ";

            string formatted = Lang("ChatFormat", player.UserIDString, prefixColored, player.displayName, message);
            return formatted;
        }

        private void Unload()
        {
            _refreshTimer?.Destroy();
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null) continue;
                RestoreOriginalName(player);
            }
            SaveData();
        }

        #endregion

        #region Core Logic

        private Timer _refreshTimer;
        private readonly Dictionary<ulong, string> _originalNames = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, string> _lastResolvedSignatures = new Dictionary<ulong, string>();

        private void StartRefreshTimer()
        {
            _refreshTimer?.Destroy();
            float interval = Mathf.Clamp(_config?.General?.UpdateInterval ?? 0.5f, 0.1f, 10f);
            _refreshTimer = timer.Every(interval, RefreshAllPlayers);
        }

        private void RefreshAllPlayers()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.IsConnected) continue;
                StoreOriginalName(player);
                UpdatePlayer(player, false);
            }
        }

        private void UpdatePlayer(BasePlayer player, bool force)
        {
            if (player == null || !player.IsConnected) return;

            StoreOriginalName(player);
            var resolved = ResolveTag(player);
            string signature = BuildSignature(resolved);
            if (!force && _lastResolvedSignatures.TryGetValue(player.userID, out var lastSig) && lastSig == signature)
                return;

            _lastResolvedSignatures[player.userID] = signature;

            // Overhead (displayName)
            string newDisplay = BuildDisplayName(player, resolved);
            if (player.displayName != newDisplay)
            {
                player.displayName = newDisplay;
                player.SendNetworkUpdateImmediate();
            }

            DebugLog($"Updated {player.UserIDString}: {newDisplay}");
        }

        private string BuildDisplayName(BasePlayer player, ResolvedTag tag)
        {
            string original = _originalNames.ContainsKey(player.userID) ? _originalNames[player.userID] : player.displayName;
            if (string.IsNullOrWhiteSpace(original)) original = "Player";

            string overheadPrefix = tag?.OverheadPrefix ?? "";
            string prefixColor = tag?.OverheadPrefixColor ?? "#ffffff";
            string nameColor = tag?.OverheadNameColor ?? "#ffffff";

            string coloredPrefix = string.IsNullOrWhiteSpace(overheadPrefix)
                ? ""
                : $"<color={prefixColor}>{overheadPrefix}</color> ";

            string coloredName = nameColor == "#ffffff" ? original : $"<color={nameColor}>{original}</color>";

            string display = $"{coloredPrefix}{coloredName}";

            int maxLen = _config.General.MaxDisplayNameLength;
            if (display.Length > maxLen)
            {
                // Truncate prefix first, then name
                if (!string.IsNullOrWhiteSpace(coloredPrefix))
                {
                    int prefixLen = overheadPrefix.Length;
                    int available = maxLen - (coloredName.Length); // naive, better to strip tags for length
                    if (available <= 0) coloredPrefix = "";
                    else
                    {
                        string rawPrefix = StripRichText(coloredPrefix).Trim();
                        if (rawPrefix.Length > available)
                            rawPrefix = rawPrefix.Substring(0, Math.Max(0, available)) + "…";
                        coloredPrefix = $"<color={prefixColor}>{rawPrefix}</color> ";
                    }
                }
                display = (coloredPrefix + coloredName).Substring(0, maxLen); // final cut
            }

            return display;
        }

        private ResolvedTag ResolveTag(BasePlayer player)
        {
            if (player == null) return null;

            // Personal override
            if (_data.PersonalPrefixes.TryGetValue(player.userID, out var personal) && personal.Enabled)
            {
                return new ResolvedTag
                {
                    OverheadPrefix = personal.OverheadPrefix,
                    OverheadPrefixColor = personal.OverheadPrefixColor,
                    OverheadNameColor = personal.OverheadNameColor,
                    ChatPrefix = personal.ChatPrefix,
                    ChatPrefixColor = personal.ChatPrefixColor,
                    Source = "personal"
                };
            }

            // Find best matching config entry (highest priority)
            PrefixEntry best = null;
            foreach (var entry in _config.Prefixes.OrderByDescending(x => x.Priority))
            {
                if (!entry.Enabled) continue;
                if (PlayerHasAccess(player, entry))
                {
                    best = entry;
                    break;
                }
            }

            if (best != null)
            {
                return new ResolvedTag
                {
                    OverheadPrefix = best.OverheadPrefix,
                    OverheadPrefixColor = best.OverheadPrefixColor,
                    OverheadNameColor = best.OverheadNameColor,
                    ChatPrefix = best.ChatPrefix,
                    ChatPrefixColor = best.ChatPrefixColor,
                    Source = $"config:{best.Key}"
                };
            }

            // Global default
            if (_config.DefaultPrefix.Enabled)
            {
                return new ResolvedTag
                {
                    OverheadPrefix = _config.DefaultPrefix.OverheadPrefix,
                    OverheadPrefixColor = _config.DefaultPrefix.OverheadPrefixColor,
                    OverheadNameColor = _config.DefaultPrefix.OverheadNameColor,
                    ChatPrefix = _config.DefaultPrefix.ChatPrefix,
                    ChatPrefixColor = _config.DefaultPrefix.ChatPrefixColor,
                    Source = "default"
                };
            }

            return null;
        }

        private bool PlayerHasAccess(BasePlayer player, PrefixEntry entry)
        {
            if (player == null) return false;
            string suffix = entry.PermissionSuffix.Trim();
            if (string.IsNullOrWhiteSpace(suffix)) return false;

            if (entry.Type == PermissionType.Group)
                return permission.UserHasGroup(player.UserIDString, suffix);

            // Permission type (auto-detect group if exists)
            if (permission.GroupExists(suffix))
                return permission.UserHasGroup(player.UserIDString, suffix);

            return permission.UserHasPermission(player.UserIDString, suffix);
        }

        private string BuildSignature(ResolvedTag tag)
        {
            if (tag == null) return "none";
            return $"{tag.OverheadPrefix}|{tag.OverheadPrefixColor}|{tag.OverheadNameColor}|{tag.ChatPrefix}|{tag.ChatPrefixColor}";
        }

        private void StoreOriginalName(BasePlayer player)
        {
            if (player == null) return;
            if (!_originalNames.ContainsKey(player.userID) || string.IsNullOrWhiteSpace(_originalNames[player.userID]))
                _originalNames[player.userID] = player.displayName;
        }

        private void RestoreOriginalName(BasePlayer player)
        {
            if (player == null) return;
            if (_originalNames.TryGetValue(player.userID, out var orig) && !string.IsNullOrWhiteSpace(orig))
                player.displayName = orig;
        }

        private void EnsurePermissionsRegistered()
        {
            foreach (var entry in _config.Prefixes)
            {
                if (string.IsNullOrWhiteSpace(entry.PermissionSuffix)) continue;
                string perm = entry.PermissionSuffix.Trim();
                if (!permission.PermissionExists(perm) && !permission.GroupExists(perm))
                {
                    if (entry.Type == PermissionType.Permission)
                        permission.RegisterPermission(perm, this);
                }
            }
        }

        private string StripRichText(string input) => Regex.Replace(input, "<.*?>", string.Empty);

        private class ResolvedTag
        {
            public string OverheadPrefix;
            public string OverheadPrefixColor;
            public string OverheadNameColor;
            public string ChatPrefix;
            public string ChatPrefixColor;
            public string Source;
        }

        #endregion

        #region Commands

        [ChatCommand("magictags")]
        private void CmdMagicTags(BasePlayer player, string command, string[] args) => HandleCommand(player, args);

        [ChatCommand("mtags")]
        private void CmdMTags(BasePlayer player, string command, string[] args) => HandleCommand(player, args);

        [ConsoleCommand("magictags")]
        private void CConsoleMagicTags(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            string[] args = arg.Args?.Select(a => a.ToString()).ToArray() ?? Array.Empty<string>();
            HandleCommand(player, args);
        }

        private void HandleCommand(BasePlayer player, string[] args)
        {
            if (args.Length == 0 || args[0].ToLower() == "help")
            {
                Message(player, "Help");
                return;
            }

            string sub = args[0].ToLower();

            switch (sub)
            {
                case "info":
                    if (!HasPerm(player, PermissionInfo)) return;
                    Message(player, "Info", Version, _config.Prefixes.Count,
                        _config.Prefixes.Count(x => x.Enabled), _data.PersonalPrefixes.Count,
                        _config.General.UpdateInterval, _config.General.MaxDisplayNameLength);
                    break;

                case "list":
                    if (!HasPerm(player, PermissionManage)) return;
                    int page = 1;
                    if (args.Length > 1) int.TryParse(args[1], out page);
                    ShowList(player, page);
                    break;

                case "reload":
                    if (!HasPerm(player, PermissionReload)) return;
                    LoadConfig();
                    LoadData();
                    EnsurePermissionsRegistered();
                    StartRefreshTimer();
                    RefreshAllPlayers();
                    Message(player, "Reloaded");
                    break;

                case "sync":
                    if (!HasPerm(player, PermissionManage)) return;
                    RefreshAllPlayers();
                    Message(player, "Synced");
                    break;

                case "add":
                    if (!HasPerm(player, PermissionManage)) return;
                    if (args.Length < 3)
                    {
                        Message(player, "Help");
                        return;
                    }
                    AddPrefix(player, args[1], args[2], args.Length > 3 ? args[3] : "perm");
                    break;

                case "remove":
                case "delete":
                    if (!HasPerm(player, PermissionManage)) return;
                    if (args.Length < 2)
                    {
                        Message(player, "Help");
                        return;
                    }
                    RemovePrefix(player, args[1]);
                    break;

                case "set":
                    if (!HasPerm(player, PermissionManage)) return;
                    if (args.Length < 4)
                    {
                        Message(player, "Help");
                        return;
                    }
                    SetPrefix(player, args[1], args[2], string.Join(" ", args.Skip(3).ToArray()));
                    break;

                case "personal":
                    if (!HasPerm(player, PermissionPersonal)) return;
                    HandlePersonal(player, args);
                    break;

                default:
                    Message(player, "Help");
                    break;
            }
        }

        private void ShowList(BasePlayer player, int page)
        {
            int perPage = 7;
            var entries = _config.Prefixes.OrderByDescending(x => x.Priority).ThenBy(x => x.Key).ToList();
            if (entries.Count == 0)
            {
                Message(player, "PrefixList", 1, 1);
                return;
            }
            int pages = (int)Math.Ceiling((double)entries.Count / perPage);
            page = Math.Max(1, Math.Min(page, pages));
            var pageEntries = entries.Skip((page - 1) * perPage).Take(perPage).ToList();

            string header = Lang("PrefixList", player.UserIDString, page, pages);
            var lines = new List<string> { header };
            int index = (page - 1) * perPage + 1;
            foreach (var e in pageEntries)
            {
                lines.Add(Lang("PrefixEntry", player.UserIDString, index++, e.Key, e.PermissionSuffix,
                    e.Enabled, e.Priority, e.OverheadPrefix ?? "none", e.ChatPrefix ?? "none"));
            }
            player.ChatMessage(string.Join("\n", lines));
        }

        private void AddPrefix(BasePlayer player, string key, string suffix, string typeStr)
        {
            string normKey = key.ToLower().Trim();
            if (_config.Prefixes.Any(x => x.Key == normKey))
            {
                Message(player, "Exists", normKey);
                return;
            }

            PermissionType type = typeStr.ToLower() == "group" ? PermissionType.Group : PermissionType.Permission;
            _config.Prefixes.Add(new PrefixEntry
            {
                Key = normKey,
                PermissionSuffix = suffix.Trim(),
                Type = type,
                Enabled = true,
                Priority = 0,
                OverheadPrefix = $"[{normKey.ToUpper()}]",
                OverheadPrefixColor = "#ffffff",
                OverheadNameColor = "#ffffff",
                ChatPrefix = "",
                ChatPrefixColor = "#ffffff"
            });
            SaveConfig();
            EnsurePermissionsRegistered();
            RefreshAllPlayers();
            Message(player, "Added", normKey);
        }

        private void RemovePrefix(BasePlayer player, string key)
        {
            string normKey = key.ToLower().Trim();
            var entry = _config.Prefixes.FirstOrDefault(x => x.Key == normKey);
            if (entry == null)
            {
                Message(player, "NotFound", normKey);
                return;
            }
            _config.Prefixes.Remove(entry);
            SaveConfig();
            RefreshAllPlayers();
            Message(player, "Removed", normKey);
        }

        private void SetPrefix(BasePlayer player, string key, string field, string value)
        {
            string normKey = key.ToLower().Trim();
            var entry = _config.Prefixes.FirstOrDefault(x => x.Key == normKey);
            if (entry == null)
            {
                Message(player, "NotFound", normKey);
                return;
            }

            field = field.ToLower();
            switch (field)
            {
                case "overheadprefix":
                    entry.OverheadPrefix = value;
                    break;
                case "overheadcolor":
                case "overheadprefixcolor":
                    entry.OverheadPrefixColor = NormalizeColor(value);
                    break;
                case "overheadnamecolor":
                    entry.OverheadNameColor = NormalizeColor(value);
                    break;
                case "chatprefix":
                    entry.ChatPrefix = value;
                    break;
                case "chatcolor":
                case "chatprefixcolor":
                    entry.ChatPrefixColor = NormalizeColor(value);
                    break;
                case "priority":
                    if (int.TryParse(value, out int p)) entry.Priority = p;
                    else { Message(player, "Help"); return; }
                    break;
                case "enabled":
                    if (bool.TryParse(value, out bool en)) entry.Enabled = en;
                    else { Message(player, "Help"); return; }
                    break;
                case "permission":
                case "perm":
                case "permissionsuffix":
                    entry.PermissionSuffix = value.Trim();
                    break;
                case "type":
                    if (value.ToLower() == "group") entry.Type = PermissionType.Group;
                    else entry.Type = PermissionType.Permission;
                    break;
                default:
                    Message(player, "Help");
                    return;
            }

            SaveConfig();
            EnsurePermissionsRegistered();
            RefreshAllPlayers();
            Message(player, "Updated", normKey);
        }

        private void HandlePersonal(BasePlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                Message(player, "Help");
                return;
            }

            string action = args[1].ToLower();
            if (action == "info")
            {
                if (args.Length < 3) { Message(player, "Help"); return; }
                ulong uid;
                if (!TryFindPlayer(args[2], out uid)) { Message(player, "NotFound", args[2]); return; }
                if (!_data.PersonalPrefixes.TryGetValue(uid, out var pp))
                {
                    Message(player, "NotFound", uid.ToString());
                    return;
                }
                Message(player, "PersonalInfo", uid.ToString(), pp.Enabled, pp.OverheadPrefix, pp.ChatPrefix, pp.UpdatedBy, pp.UpdatedAt);
                return;
            }

            if (args.Length < 3) { Message(player, "Help"); return; }
            if (!TryFindPlayer(args[2], out ulong targetId)) { Message(player, "NotFound", args[2]); return; }

            switch (action)
            {
                case "set":
                    if (args.Length < 4) { Message(player, "Help"); return; }
                    string text = string.Join(" ", args.Skip(3).ToArray());
                    var entry = GetOrCreatePersonal(targetId);
                    entry.Enabled = true;
                    entry.OverheadPrefix = text;
                    entry.UpdatedBy = player?.UserIDString ?? "console";
                    entry.UpdatedAt = DateTime.UtcNow.ToString("u");
                    SaveData();
                    RefreshPlayer(targetId);
                    Message(player, "PersonalSet", targetId);
                    break;

                case "color":
                    if (args.Length < 4) { Message(player, "Help"); return; }
                    var eColor = GetOrCreatePersonal(targetId);
                    string col = NormalizeColor(args[3]);
                    eColor.OverheadPrefixColor = col;
                    eColor.UpdatedBy = player?.UserIDString ?? "console";
                    eColor.UpdatedAt = DateTime.UtcNow.ToString("u");
                    SaveData();
                    RefreshPlayer(targetId);
                    Message(player, "PersonalSet", targetId);
                    break;

                case "clear":
                    if (_data.PersonalPrefixes.Remove(targetId))
                    {
                        SaveData();
                        RefreshPlayer(targetId);
                        Message(player, "PersonalCleared", targetId);
                    }
                    else Message(player, "NotFound", targetId.ToString());
                    break;

                default:
                    Message(player, "Help");
                    break;
            }
        }

        private PersonalPrefix GetOrCreatePersonal(ulong uid)
        {
            if (!_data.PersonalPrefixes.TryGetValue(uid, out var pp))
            {
                pp = new PersonalPrefix();
                _data.PersonalPrefixes[uid] = pp;
            }
            return pp;
        }

        private void RefreshPlayer(ulong uid)
        {
            var player = BasePlayer.FindByID(uid);
            if (player != null) UpdatePlayer(player, true);
        }

        private bool TryFindPlayer(string input, out ulong uid)
        {
            if (ulong.TryParse(input, out uid)) return true;
            var player = BasePlayer.activePlayerList.FirstOrDefault(p => p.displayName.IndexOf(input, StringComparison.OrdinalIgnoreCase) >= 0);
            if (player != null)
            {
                uid = player.userID;
                return true;
            }
            uid = 0;
            return false;
        }

        private string NormalizeColor(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "#ffffff";
            value = value.Trim();
            if (!value.StartsWith("#")) value = "#" + value;
            return Regex.IsMatch(value, "^#(?:[0-9a-fA-F]{3}){1,2}$") ? value : "#ffffff";
        }

        private bool HasPerm(BasePlayer player, string perm)
        {
            if (player == null) return true; // console
            if (player.IsAdmin) return true;
            if (permission.UserHasPermission(player.UserIDString, perm)) return true;
            Message(player, "NoPermission", Lang("Prefix"));
            return false;
        }

        #endregion
    }
}