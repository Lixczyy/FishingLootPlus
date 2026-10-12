# Fishing Loot Plus

Fishing Loot Plus is a Rust Oxide/UMod plugin that dramatically expands fishing rewards by mixing vanilla catches with custom loot tables, location-based modifiers, bait-based bonuses, jackpot rolls, and permission-based luck tiers. It is designed for servers that want fishing to be a rewarding progression activity without fully removing the normal fishing loop.

The plugin is built around a simple idea: most catches are still valid fish, but a portion of the result is replaced or augmented with additional loot, with the opportunity for special jackpot rewards.

## Features

- Vanilla fish can be left untouched on a configurable percentage chance.
- Normal catches can award extra loot from a weighted loot pool.
- Jackpot rewards can trigger rare high-value items and weapons.
- Location-specific loot profiles for Ocean, River, Lake, and default water.
- Bait-specific modifiers that affect quantity, jackpot chance, and extra-roll chance.
- Permission-based luck multipliers for VIP or admin-tier fishing bonuses.
- Persistent per-player fishing progression with automatic VIP at 100 catches and God at 500 catches.
- Public `/lb` leaderboard and `/stats` commands for personal and player statistics.
- In-game commands for validation, simulation, testing, and admin forcing.
- Persistent player stats for catches, jackpots, jackpot rate, items, reward drops, and bonus rolls.
- Configurable item display names, custom skin IDs, and weighted loot entries.

## Supported game and framework

- Game: Rust
- Mod framework: Oxide / uMod
- Plugin file: `FishingLootPlus.cs`
- Default config: `FishingLootPlus.json`

## Installation

1. Copy `FishingLootPlus.cs` to your Oxide plugin folder:
   - `oxide/plugins/FishingLootPlus.cs`
2. Start or reload the server so Oxide compiles the plugin.
3. The plugin will create or read its configuration file at:
   - `oxide/config/FishingLootPlus.json`
4. Optional: configure permissions and loot tables in that JSON file.

If the config is invalid or missing, the plugin automatically reloads a generated default configuration and warns in the server console.

## How it works

When a player catches a fish, the plugin checks whether fishing is enabled and whether the player meets any permission requirement. It then evaluates a few conditions:

- A configurable passthrough chance lets the vanilla catch survive untouched.
- The plugin detects the fishing location from nearby terrain topology.
- It detects the bait in the rod inventory.
- It applies any permission-based luck modifiers.
- It rolls for a jackpot and, if successful, adds a jackpot reward.
- It rolls extra normal loot entries based on the configured extra-roll chance.
- The final reward is granted straight to the player inventory.

This preserves the core fishing experience while making it more rewarding and dynamic.

## Configuration overview

The config file contains multiple areas:

### Global settings

These control the plugin behavior across all fish catches:

- `Enabled` — globally enables or disables the plugin.
- `Require fishinglootplus.use permission` — blocks non-permitted users from fishing rewards.
- `Chance (%) to let a vanilla catch through untouched` — percent chance for no custom rewards.
- `Global quantity multiplier` — global scaling factor for awarded item amounts.
- `Base chance (%) for a bonus roll` — base chance to get extra non-jackpot rolls.
- `Max bonus rolls per catch` — maximum extra reward rolls from a single catch.
- `Base jackpot chance (%)` — starting jackpot chance before multipliers.
- `Max jackpot chance (%) after multipliers` — cap for jackpot chance.
- `Jackpot also gives normal loot` — whether jackpot hits also grant normal loot.
- `Announce jackpots to the whole server` — server-wide announcement.
- `Show catch message to the player` — display a per-catch reward message.
- `Jackpot effect prefab (blank = none)` — visual effect spawned at the player.
- `Location detection distance in front of player (m)` — terrain scan distance.
- `Default bait key (used when no bait is found)` — fallback bait entry.
- `Default location key (used when water type is unknown)` — fallback location entry.

### Common loot

The `Common loot (applies everywhere)` list defines the weighted rewards granted from the standard pool.

Each entry contains:

- `Shortname` — item shortname such as `scrap`, `metal.refined`, or `fish.salmon`
- `Skin ID` — optional skin for the item
- `Custom name (blank = default)` — custom display name override
- `Weight (higher = more common)` — weighting relative to other items
- `Min amount` and `Max amount` — range for rewarded quantities

### Jackpot loot

The `Jackpot loot (applies everywhere)` list defines the reward pool for jackpot rolls.

These are usually higher utility or rarer item drops, such as weaponry, explosives, components, or supply items.

### Location profiles

The plugin supports custom loot profiles per detected environment:

- `Ocean`
- `River`
- `Lake`
- `Default`

Each location profile can define:

- `Display name`
- `Quantity multiplier`
- `Jackpot chance multiplier`
- `Extra roll chance bonus (percentage points)`
- `Additional loot (added to common pool)`
- `Additional jackpot loot (added to jackpot pool)`

This lets different map areas produce different reward curves.

### Bait profiles

Bait keys are matched against the bait item shortname in the fishing rod inventory. Examples in the default configuration include:

- `default`
- `worm`
- `grub`
- `fish.anchovy`

Each bait profile can also add extra loot and alter rewards. Premium bait entries can multiply jackpot chance and quantity substantially.

### Luck permissions

The `Luck permissions` section includes permission-based multipliers. By default:

- `fishinglootplus.vip` → quantity x1.25, jackpot multiplier x1.25
- `fishinglootplus.god` → quantity x2.0, jackpot multiplier x3.0

The plugin picks the highest-value matching luck tier for the player.

## Permissions

The plugin registers and uses these permissions:

- `fishinglootplus.use` — basic permission needed if the config requires the use permission.
- `fishinglootplus.admin` — required to use admin commands.
- Any custom permissions defined under `Luck permissions`.

Players are automatically granted `fishinglootplus.vip` after 100 successful catches and
`fishinglootplus.god` after 500 successful catches. These thresholds use the configured VIP and God
luck multipliers and persist across restarts.

If you want players to be able to use the plugin without the use permission, disable the requirement in config.

## Commands

The plugin registers the `/fishloot` command.

### /fishloot reload

Reloads the configuration and validates all loot entries. Invalid shortnames are reported in the server logs and ignored.

### /fishloot toggle

Enables or disables fishing rewards globally.

### /fishloot stats

Displays plugin session stats for administrators:

- total catches handled
- total jackpots
- total items awarded
- jackpot percentage

### /lb

Available to everyone. Displays the top three players by catches and the requesting player's current rank.
The leaderboard also shows each listed player's jackpot rate.

### /stats

Available to everyone. Displays your persistent fishing statistics, effective quantity and jackpot
multipliers, and leaderboard rank.

### /stats <player>

Displays the persistent fishing statistics for an online player or a player already recorded in the
leaderboard. Partial name matches are supported.

### /fishloot list

Lists the configured location keys and bait keys.

### /fishloot info <location> [bait]

Shows effective quantities, jackpot chance, and the top loot entries for a specific location and bait combination.

### /fishloot test <location> [bait] [catches]

Runs a simulation for a number of sample catches to estimate jackpots and likely rewards.

### /fishloot give <player> [jackpot]

Forces a reward roll for a given player. Add `jackpot` to force a jackpot roll.

## Example configuration behavior

The default config creates a very rewarding fishing loop:

- standard catches may award fish, resources, ammo, gadgets, and materials
- extra rolls add more loot from the same pool
- jackpot rewards can include weapons, explosives, and rare gear
- special bait grants stronger multipliers and additional premium loot

This means the plugin is best suited to server owners who want fishing to be a substantial source of progression and economy output.

## Recommended usage

This plugin works best on servers where:

- fishing is a meaningful farming or economy activity
- chests, scrap, and component drops need to be more frequent
- you want a reward system that feels rewarding without replacing vanilla fishing entirely
- you want map-region variation or bait-based progression

For more balanced PvE setups, lower the jackpot chance and quantity multipliers. For a more aggressive economy, raise the standard weights and jackpot values.

## Notes and limitations

- Item shortnames must match valid Rust item definitions.
- Invalid item entries are ignored with warnings during validation.
- The plugin removes the vanilla fish object after custom rewards are generated.
- Item display names and skins are applied when possible, but invalid item definitions are ignored safely.
- The plugin is tuned for Rust’s fishing system and Oxide runtime hooks.

## Files in this repository

- `FishingLootPlus.cs` — main Oxide plugin implementation
- `FishingLootPlus.json` — default config with reward tables and profile settings

## License and author

This project is authored by `Lix` and distributed as a plugin source file for Rust server use. Check your server’s modding and distribution policy before redistributing or modifying it in a public environment.

## Quick reference

```text
Plugin: Fishing Loot Plus
Author: Lix
Version: 1.0.0
Commands: /fishloot reload, toggle, stats, list, info, test, give
Permissions: fishinglootplus.use, fishinglootplus.admin
Main config: oxide/config/FishingLootPlus.json
```

If you want to tune the economy, start with the following knobs:

- `Global quantity multiplier`
- `Base chance (%) for a bonus roll`
- `Base jackpot chance (%)`
- `Max jackpot chance (%) after multipliers`
- `Locations` and `Baits` multipliers
- loot weights in `Common loot` and `Jackpot loot`

These provide the easiest way to balance the plugin for your server.
