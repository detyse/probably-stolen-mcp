# Profession item tests in the real game

[简体中文](profession-tests.zh-CN.md) · [User guide](usage-guide.md) · [Capability record](capabilities.md)

Tested on **2026-11-07**, Steam Build **26337366 / VERSION 100-REV6**, Probably Automated **0.2.6 / protocol 2**. Calls used the official MCP client, the stdio server, and the installed game Mod. Early menu/gunsmith captures used a 0.2.5 development candidate; subsequent regressions used 0.2.6 candidates. The [call and state evidence](validation/2026-11-07-professions.json) distinguishes versions, fixes, corrected expectations, and limits.

## Results

All **9 currently startable professions** reached day 1 through the native menu. All 12 visible profession cards were selectable for viewing. The game marks **Scrapper, Street-food vendor, and Organ dealer** as coming soon and prevents continuing with them.

| Profession | Verified result | Limit of this run |
| --- | --- | --- |
| Gunsmith | Disassembly produced materials; a sight and magazine attached to compatible slots; filament entered the printer's Input; a suppressor was rotated into the showcase; undo-sort restored the layout | No overnight print completion; not every weapon or attachment |
| Chemist | Manual opened; blender accepted coffee beans and a container, consumed the input and changed the container's visible contents feature; container retrieved; heater switched to High and Off | Not every recipe; measuring-cylinder transfer dispatched without independent volume measurement |
| Water merchant | Small bottle printed; testing changed a strip to Used and displayed results; purification changed the barrel's visible feature from rusty to basic water; pouring into a small bottle enabled further pouring | Not every water quality or consumable |
| Hydroponic farmer | Seeds inserted, removed, reinserted; draining a full bottle made it a legal water-pack target; water pack and nutrient tablet consumed; fixed damaged modules had no move action | No overnight growth, maturity, or harvest |
| Rat farmer | Animals and food entered a cage; cage sorted and animal retrieved; first blade use removed the animal from serum targets, second produced raw meat | Serum dispatched, but immunity and remaining uses were not independently quantified; no breeding cycle |
| Aquaculturist | Fish, fertilized roe, and feed entered the tank; after filling, the tank could pour to the drain; sorting, retrieval, and two-step filleting produced fish meat | No hatching, growth, or breeding cycle |
| Fence | Items stored in the stash; unload skipped pinned contents; closing hid contents from observation; reopening, unpinning, and unloading worked | No trading or inspection test of concealment effects |
| Apprentice | Food stored and unloaded; toothpaste placed with rotation; overlapping placement rejected without inventory mutation; item entered the trashcan | Insertion does not establish permanent disposal or overnight collection |
| Destitute | Player inventory was empty; no fabricated tool targets; empty trashcan opening/unloading worked; fixed shop fixtures remained observable | No initial profession items to manipulate |

Menu tests also covered selecting/removing the Ex-con perk, its incompatibility with Model Citizen, difficulty cards, the endless-mode checkbox, and the disabled forced-skip-intro checkbox. This does not establish every perk combination or difficulty.

## Fixes

- **Missing profession/difficulty/perk cards:** these reuse the upgrade pointer component. The adapter now identifies their native menu context, selected and locked state, and invokes native pointer callbacks. Perks come from the menu catalog without accidentally constructing shop state.
- **Missing in-game Back action:** `navigate_back` uses the native escape resolver when allowed. It closes the top window/selection before opening the pause menu and can resume from pause.
- **Observation failure after returning to the main menu:** detect the actual menu controller and verify Unity window handlers before invoking window methods. Repeated cross-profession returns passed.
- **Unnamed machine inventories:** `containers[].label` reads the visible panel heading or slot tooltip. Input, Output, Seeds, Tank, Animals, and Modules were checked. Icon-only panels may still have an empty label; use the parent item, window, and legal candidates as well.

Game item rules were not rewritten. Used test strips remaining as items, full bottles rejecting refills, and two-step animal processing were corrections to test expectations.

## Evidence and saves

The record includes 203 action submissions: 200 dispatched, 2 rejected by validity checks, and 1 early-candidate exception. The exception came from the developing perk lookup and was fixed and retested. These counts do not mean 203 distinct features passed. There are also 75 passing state assertions and 4 explained, corrected test expectations. Actual completion is checked separately from dispatch.

The original slots 0 and 1 and their five daily backups—seven files—retained identical SHA-256 hashes. Tests created slots 2–11 and ended at the main menu. Developer tools stayed disabled; game/save values were not edited. Full transcripts and backups remain in the ignored local `.cache/profession-tests/` directory.

This session tested profession items and new-game/return flows. It **did not repeat the complete open → trade → close → sleep workflow**. Overnight production, every item, other game builds, and all resolutions remain outside this run.

The final build passed all **152 automated checks** with no warnings or errors. The published MCP server also preserved machine labels through both get_inventory and get_entity in the real game.

Follow-up: the overnight limitations above describe this REV6 run. The separate [0.2.7 / Rev12 overnight report](overnight-tests.md) records later production outcomes and fixes.
