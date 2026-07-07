# MagicTags

[![Version](https://img.shields.io/badge/version-3.0.0-blue.svg)](#)
[![Status](https://img.shields.io/badge/status-release-green.svg)](#)
[![Game](https://img.shields.io/badge/game-Rust-orange.svg)](#)
[![Framework](https://img.shields.io/badge/framework-Oxide%20%2F%20uMod-yellow.svg)](#)
[![Language](https://img.shields.io/badge/language-C%23-239120.svg)](#)
[![License](https://img.shields.io/badge/license-Apache%202.0-lightgrey.svg)](LICENSE)

MagicTags is a Rust plugin for Oxide/uMod that renders overhead prefixes without rewriting player names. It supports personal prefixes, permission-based and group-based rules, admin prefixes that stay visible even when a player hides other tags, local visibility controls, and a clean chat presentation.

## What this release focuses on

- Clean English-only localization
- Optimized refresh flow and config normalization
- Local `mhide` / `mshow` controls for the viewer only
- Per-player local distance control inside a strict config range
- Default global view distance set to **30 meters**
- Admin prefixes with `AlwaysVisible` support
- Dynamic permission and group rule handling
- Runtime config reload and data reload
- Ready-to-ship Apache 2.0 open-source release

## Commands

Alias: `/mtags`

### Player commands

| Command | Description |
| --- | --- |
| `/magictags help` | Show command help |
| `/magictags info` | Show plugin status |
| `/magictags config` | Show the active configuration summary |
| `/magictags hide` | Hide your own prefix |
| `/magictags show` | Show your own prefix again |
| `/magictags mhide [on\|off\|toggle\|full]` | Hide other players' prefixes locally |
| `/magictags mshow` | Show other players' prefixes again |
| `/magictags range <10-40\|off>` | Change your personal prefix viewing distance |
| `/magictags prefix <text>` | Set your personal prefix |
| `/magictags color <#hex>` | Set your personal prefix color |
| `/magictags clear` | Clear personal prefix settings |

### Admin commands

| Command | Description |
| --- | --- |
| `/magictags list [page]` | List configured prefix rules |
| `/magictags addrule <key> <permission\|group\|any> <access> <text> [color] [size] [priority] [alwaysVisible]` | Add a new rule |
| `/magictags setrule <key> <field> <value>` | Update a rule |
| `/magictags removerule <key>` | Remove a rule |
| `/magictags reload` | Reload config and data |
| `/magictags sync` | Force a full refresh of online players |

## Permissions

| Permission | Purpose |
| --- | --- |
| `magictags.view` | Required only when the config enables permission-gated viewing |
| `magictags.hide` | Allows hiding your own tag |
| `magictags.customprefix` | Allows setting a personal prefix |
| `magictags.customcolor` | Allows setting a personal prefix color |
| `magictags.manage` | Allows rule management and sync actions |
| `magictags.reload` | Allows runtime reload |

Dynamic rule permissions are registered automatically from the config, for example `magictags.vip` or `magictags.staff`.

## Configuration

The plugin creates and normalizes its config automatically. The default values are tuned for public server use.

### Core defaults

```json
{
  "General": {
    "Enabled": true,
    "Update Interval (Seconds)": 0.5,
    "View Distance (Meters)": 30.0,
    "Player Minimum View Distance (Meters)": 10.0,
    "Player Maximum View Distance (Meters)": 40.0,
    "Text Lifetime (Seconds)": 0.75,
    "Text Height Offset": 2.15,
    "Show Tags To Self": false,
    "Require Permission To See Tags": false,
    "Require Admin Flag For Radar Mode": true,
    "Allow Player Range Control": true,
    "Use Chat Prefix": true,
    "Debug Logging": false
  }
}
```

### Prefix styles

- **Default Prefix** is used when no rule matches.
- **Admin Prefix** is shown for admins and is `AlwaysVisible` by default.
- **Prefixes** is a list of dynamic rules using permissions, groups, or either one.

Recommended sizes:
- Default text size: **12**
- Admin text size: **12**
- Custom rule size range: **10-24**

### Example rule

```json
{
  "Key": "vip",
  "Enabled": true,
  "Priority": 10,
  "Type": "Permission",
  "Access": "magictags.vip",
  "Text": "[VIP]",
  "Color": "#ff66cc",
  "Size": 12,
  "AlwaysVisible": false
}
```

### Visibility behavior

- `mhide` only changes what the local player sees.
- Other players still see that user's prefix normally.
- `AlwaysVisible` rules are shown even when a player hides other tags.
- The admin prefix is configured as always visible by default.

## Data file

Player-specific settings are stored in:

`oxide/data/MagicTags_Data.json`

## Installation

1. Put `MagicTags.cs` into `oxide/plugins`.
2. Start the server or run `oxide.reload MagicTags`.
3. Adjust the config if needed.
4. Grant permissions for custom prefix or management commands.
5. Add your own rules using `/magictags addrule` or by editing the config.

## Notes

- This release keeps the localization layer English-only.
- Config files are normalized automatically when the plugin loads.
- A config migration triggers a save and runtime refresh.
- The plugin uses overhead `ddraw.text` rendering, so the visual layer stays separate from player names.

## License

This project is released under the Apache License 2.0. See `LICENSE` for the full text.

<div align="center">
  <sub>Created with ❤️ the INFUNV STUDIO</sub>
</div>