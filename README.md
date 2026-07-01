# MagicTags

[Русский](README.ru.md)

[![Version](https://img.shields.io/badge/version-2.0.0-blue.svg)](#)
[![Status](https://img.shields.io/badge/status-stable-green.svg)](#)
[![Rust](https://img.shields.io/badge/game-Rust-orange.svg)](#)
[![Oxide](https://img.shields.io/badge/framework-Oxide%20%2F%20uMod-yellow.svg)](#)
[![Language](https://img.shields.io/badge/language-C%23-239120.svg)](#)
[![License](https://img.shields.io/badge/license-Apache%20License%202.0-lightgrey.svg)](LICENSE)

MagicTags is a Rust plugin for Oxide/uMod that shows configurable overhead tags and chat prefixes without rewriting player display names. It supports per-player custom prefixes, hidden tags, permission-based visibility, team and admin labels, multilingual chat output, and a clean visual style for server communities.

The plugin is designed for servers that want:

- overhead tag rendering through `ddraw.text`
- chat prefixes with a custom chat icon
- per-permission and per-group tag styling
- personal prefix overrides for trusted players
- player-controlled hide/show support
- global visibility distance in the configuration
- English and Russian localization

## Features

- Overhead tags rendered separately from `displayName`
- Chat prefixes with the configured plugin icon
- Global view distance for overhead tag rendering
- Per-player hidden tag state
- Custom personal prefix and prefix color
- Permission-based admin/team/default tag styles
- Support for Russian and English messages
- Simple admin and player command set
- Safe config/data generation on first start
- Clean and flexible configuration structure

## Commands

| Command | Description |
| --- | --- |
| `/magictags help` | Show help |
| `/magictags info` | Show plugin information |
| `/magictags hide` | Hide your own tag |
| `/magictags show` | Show your own tag |
| `/magictags prefix <text>` | Set your personal prefix |
| `/magictags color <#hex>` | Set your personal prefix color |
| `/magictags clear` | Clear personal prefix data |
| `/magictags personal <set|color|hide|show|clear|info> <player/steamid> [value]` | Admin personal tag tools |
| `/magictags sync` | Refresh all online players |
| `/magictags reload` | Reload config and data |

Alias: `/mtags`

## Permissions

| Permission | Description |
| --- | --- |
| `magictags.see` | Allows the player to receive overhead tag rendering |
| `magictags.hide` | Allows the player to hide their tag |
| `magictags.customprefix` | Allows the player to set a custom prefix |
| `magictags.customcolor` | Allows the player to set a custom prefix color |
| `magictags.personal` | Allows access to admin personal tag commands |
| `magictags.reload` | Allows plugin reload |
| `magictags.manage` | Allows sync and management actions |
| `magictags.admin` | Full admin-style access and admin tag style |

## Configuration

MagicTags creates its configuration automatically on first load.

### Settings
```json
{
  "Settings": {
    "Enabled": true,
    "Update interval (seconds)": 0.5,
    "View distance (meters)": 60.0,
    "Text lifetime (seconds)": 0.75,
    "Text height offset": 2.15,
    "Show tags to self": false,
    "Show only for permission": true,
    "Require admin flag for radar mode": true,
    "Use team prefix": true,
    "Default prefix text": "[PLAYER]",
    "Default prefix color": "#cfcfcf",
    "Admin prefix text": "[ADMIN]",
    "Admin prefix color": "#ff66ff",
    "Team prefix text": "[TEAM]",
    "Team prefix color": "#66ccff",
    "Custom prefix color default": "#ff66cc",
    "Log debug": false
  }
}
```

### Example
```json
{
  "Settings": {
    "Enabled": true,
    "Update interval (seconds)": 0.5,
    "View distance (meters)": 60.0,
    "Text lifetime (seconds)": 0.75,
    "Text height offset": 2.15,
    "Show tags to self": false,
    "Show only for permission": true,
    "Require admin flag for radar mode": true,
    "Use team prefix": true,
    "Default prefix text": "[PLAYER]",
    "Default prefix color": "#cfcfcf",
    "Admin prefix text": "[ADMIN]",
    "Admin prefix color": "#ff66ff",
    "Team prefix text": "[TEAM]",
    "Team prefix color": "#66ccff",
    "Custom prefix color default": "#ff66cc",
    "Log debug": false
  }
}
```

## How It Works

MagicTags does not replace a player's `displayName`. Instead, it draws overhead tags directly to clients using `ddraw.text`.

The plugin logic is simple:

1. It checks whether the viewer has permission to see tags.
2. It reads the target's personal data first.
3. If no personal override exists, it falls back to admin, team, or default styles.
4. It renders the text above the target player within the configured view distance.
5. In chat, the plugin adds a prefix using the configured chat icon.

This makes the plugin safer for compatibility with other plugins because the real player name is not rewritten.

## Localization

Built-in languages:

- English
- Russian

Language messages are registered through Oxide's localization system. Additional languages can be added in the usual Oxide language file structure.

## Installation

1. Place `MagicTags2.cs` into `oxide/plugins`.
2. Restart the server or run `oxide.reload MagicTags`.
3. The plugin will generate its configuration and data files automatically.
4. Adjust the configuration to fit your server style.
5. Grant permissions to your staff or trusted players.

## Data File

Personal tag settings are stored in:

`oxide/data/MagicTags_Data.json`

## Notes

- Overhead text depends on the client-side debug draw method used by Rust.
- The `Require admin flag for radar mode` setting controls whether temporary admin-style viewing is enabled for radar-like rendering.
- Personal prefix colors should be valid hex colors, for example `#ff66cc`.
- The plugin icon used in chat is configurable in the source and can be replaced with your own SteamID.

## License

This project is open-source and released under the Apache License 2.0. See `LICENSE` for details.
