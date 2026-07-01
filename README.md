# MagicTags

[English](README.md)

[![Version](https://img.shields.io/badge/version-1.0.0-blue.svg)](#)
[![Status](https://img.shields.io/badge/status-stable-green.svg)](#)
[![Rust](https://img.shields.io/badge/game-Rust-orange.svg)](#)
[![Oxide](https://img.shields.io/badge/framework-Oxide%20%2F%20uMod-yellow.svg)](#)
[![Language](https://img.shields.io/badge/language-C%23-239120.svg)](#)
[![License](https://img.shields.io/badge/license-Apache License 2.0-lightgrey.svg)](LICENSE)

MagicTags is a Rust plugin for Oxide/uMod that manages prefixes for IQPermissions, TimedPermissions, and Oxide groups. It supports overhead prefixes (display name), chat prefixes, personal overrides, and automatic updates from a GitHub repository. Developed by **whitecristafer**, sponsored by **infunv.ru** for **evolve.infunv.ru**.

## Overview

MagicTags gives server administrators full control over player prefixes both above the head and in chat. It automatically collects permissions and groups, applies prefixes based on priority, and allows each player to have a personal prefix stored in data files.  

It is designed for servers that want:

- Dynamic, per‑permission prefixes
- Overhead display name integration with color support
- Chat message prefixes without modifying other plugins
- Personal prefix overrides for trusted players
- Simple, unified command interface
- Automatic update checks from the official repository

## Features

- Overhead prefix (displayName) and chat prefix separation
- Per‑permission and per‑group prefix definitions
- Global default prefix for players without a matching permission
- Priority system – the highest priority matching prefix is used
- Personal prefix storage in `data/` – overrides all other prefixes
- Automatic data and config generation on first start
- Automatic update check against the latest GitHub release source
- Only stable versions are considered – development builds are ignored
- Configurable max display name length to prevent overflow
- Localization support for English and Russian
- Clean console logging with the `MagicPrefix` tag
- Smart refresh timer – only updates players when their prefix changes

## Commands

| Command | Description |
| --- | --- |
| `/magictags help` | Show help message |
| `/magictags info` | Display plugin information |
| `/magictags list [page]` | List all configured prefixes |
| `/magictags add <key> <permissionSuffix> [type:perm/group]` | Add a new prefix |
| `/magictags set <key> <field> <value>` | Modify an existing prefix |
| `/magictags remove <key>` | Remove a prefix |
| `/magictags personal set <player/steamid> <text>` | Set a personal overhead prefix |
| `/magictags personal color <player/steamid> <hex>` | Set personal overhead prefix color |
| `/magictags personal info <player/steamid>` | View personal prefix details |
| `/magictags personal clear <player/steamid>` | Remove a personal prefix |
| `/magictags reload` | Reload configuration and data |
| `/magictags sync` | Force refresh all online players |

Alias: `/mtags` can be used instead of `/magictags`.

## Permissions

| Permission | Description |
| --- | --- |
| `magictags.manage` | Access to add / set / remove / list / sync commands |
| `magictags.reload` | Access to the reload command |
| `magictags.personal` | Access to personal prefix commands |
| `magictags.info` | Access to the info command |

## Configuration

MagicTags creates its configuration file automatically on the first load.  
Main settings:

### General Settings
```json
{
  "General": {
    "Update interval (seconds)": 0.5,
    "Max display name length (characters)": 32,
    "Show overhead prefix to self": false,
    "Log debug info": false
  }
}
```

### Default Prefix
```json
{
  "Default Prefix": {
    "Enabled": true,
    "Overhead prefix": "[Player]",
    "Overhead prefix color": "#a0a0a0",
    "Overhead name color": "#ffffff",
    "Chat prefix": "",
    "Chat prefix color": "#ffffff"
  }
}
```

### Prefixes List
```json
"Prefixes": [
  {
    "Key": "admin",
    "Permission / Group suffix": "admin",
    "Type": "Permission",
    "Enabled": true,
    "Priority": 100,
    "Overhead prefix": "[Admin]",
    "Overhead prefix color": "#ff4444",
    "Overhead name color": "#ffffff",
    "Chat prefix": "[Admin]",
    "Chat prefix color": "#ff4444"
  }
]
```

**Note:**  
- `Type` can be `Permission` or `Group`.  
- If the suffix matches an Oxide group, it is automatically treated as a group even if `Type` is set to `Permission`.  
- Personal prefixes are stored in `oxide/data/MagicTags_Data.json`.

## How It Works

When a player spawns or their permissions change, MagicTags:

1. Checks for a personal prefix (data file) – if enabled, uses it immediately.
2. Otherwise, scans the configured prefix list, ordered by priority (highest first).
3. Finds the first prefix where the player has the required permission or belongs to the Oxide group.
4. If no match is found, the global default prefix is applied (if enabled).
5. Builds the final `displayName` respecting the `Max display name length` liApache License 2.0.
6. On chat messages, the plugin injects the chat prefix using the `OnPlayerChat` hook.

A refresh timer periodically recalculates prefixes only for players whose resolved prefix has actually changed, minimising network updates.

## Update System

MagicTags automatically checks for updates from the official GitHub repository on server startup.

Update rules:
- Only stable version numbers (e.g., `1.0.0`) are considered.
- Development versions (like `d1.0.1` or containing `-dev`) are ignored.
- The plugin updates only if the remote version is strictly greater than the local one.
- After a successful download, the plugin reloads itself automatically.

The update source URL is embedded in the plugin code and points to:  
`https://raw.githubusercontent.com/whitecristafer/MagicTags/main/MagicTags.cs`

## Installation

1. Download `MagicTags.cs` and place it into the `oxide/plugins` folder.
2. Restart the server or run `oxide.reload MagicTags`.
3. The plugin will generate the default configuration and data files.
4. Adjust `oxide/config/MagicTags.json` to your needs.
5. Grant permissions to your staff (e.g., `oxide.grant group admin magictags.manage`).

## Localization

Built‑in languages:
- English
- Russian

The plugin uses Oxide's localization system; language files can be extended by adding additional languages in the `lang` directory.

## Logging

MagicTags writes start‑up banner, configuration loading info, update check results, and (if enabled) debug messages to the server console.  
All messages are prefixed with `[MagicPrefix]` for easy filtering.

## Requirements

- Rust dedicated server
- Oxide/uMod (latest version recommended)
- C# plugin support

## Notes

- Overhead prefix colors work only if the server allows rich text in display names (enabled by default in vanilla Rust).
- The max display name length applies to the final string **after** adding color tags; truncation may remove part of the prefix or name.
- Personal prefixes are stored per SteamID and persist across server restarts.

## License

This project is open‑source. Released under the Apache License 2.0 License. See `LICENSE` for full details.