# Overnight production tests

[简体中文](overnight-tests.zh-CN.md) · [Capabilities](capabilities.md) · [Earlier profession item tests](profession-tests.md)

Date: 2026-11-07, Asia/Shanghai. Steam Build **26496118**, game **VERSION 100-Rev12**, Mod **0.2.7 / protocol 2**. The first two printer nights used 0.2.6; reload on day 3 and completion used 0.2.7. The other three scenarios used 0.2.7 throughout. Earlier REV6 evidence remains historical rather than verification of this game build.

All four scenarios produced new items that could be retrieved through current item actions. The path was the official C# MCP client → stdio MCP → authenticated local bridge → native Unity operations. These were separate new saves on normal difficulty, without developer mode or edits to days, money, items, or saves.

## Observed outcomes

| Scenario | Setup and calls | Actual result after normal nights |
| --- | --- | --- |
| 3D printer, slot 12 | Open the printer; `item_move` four filament rolls into Input, keeping the native chip, battery, and modules; close shop, `sleep`, and confirm settlement | Two rolls consumed over the first two nights; reloading day 3 exposed progress 48/50. After the third night, day 4 Output contained one new Liberator printed pistol, item ID 176, which was moved into player inventory |
| Hydroponics, slot 13 | Place one filler-grass seed in Seed Input of the old hydroponic system, using its initial battery and 2000 ml bottle | On day 2, after one night, Crop Output contained four filler-grass plants, one small plant, and one seed, IDs 85–90; water fell to 1548 ml. The output remained on day 3 and one plant was retrieved |
| Rat husbandry, slot 14 | Place four mature male/female rats and six small nutrient fruits in the cage; settle normally; use one immune serum dose on a sick rat | Pregnancy displayed 1/3 and 2/3 on days 2 and 3. Day 4 contained seven new age-zero pups, IDs 74–80; one was retrieved from the cage. Native status reported overcrowding, and one pup appeared in a visible container outside the cage; birth count does not mean every pup fitted inside |
| Aquaculture, slot 15 | Fill each of two RAS units with 6000 ml; keep roe/feed in a nursery and starting adults separately; use normal sleep | Day 2: six fertilized eggs became one group of six fry, ID 66. Pellet-fed growth was 75%. Day 5: six separate juvenile fish, IDs 72–77. Two native knife operations on newly grown fish ID 77 produced a fish fillet and a tiny fillet, IDs 78 and 79; the tiny fillet was retrieved |

Each night used `open_shop`, `next_customer` when needed, `close_shop` with its native confirmation, `sleep`, and the settlement confirmation. Days, costs, inventory, and output were read again after dispatch. No trade transaction was tested in these production runs.

## Fixes and regression checks

- **Missing production and biological state:** added `InventoryItem.statusText` from selected normal native hover sections. Real-game checks covered printer progress; animal sex, age, health, hunger and pregnancy; roe/fry development; water; cage condition; battery charge; and remaining tool uses. It applies to rendered player-owned items, is disabled in developer mode, and excludes unrevealed genes, true appraisals, and unopened item lists. Text is capped at 8192 characters and preserves localization and possible rich-text markup. Empty-string and `null` behavior is explained in the [guide](usage-guide.md).
- **Missing settlement checkbox state:** native `TabletCheckbox` controls now expose `kind=checkbox`, `isOn`, and the service label/cost, while execution still uses the native button callback. Electricity was observed on → off → on before settlement.
- **Observable serum result:** remaining uses decreased from 2/10 to 1/10; the next day showed immunity 52.5 versus base 30. This does not claim an immediate cure of the disease.

One test expectation was corrected: on day 3 the fry still reported two days remaining, together with a native 75% growth-rate explanation. No game behavior was changed or accelerated. Continuing normal nights produced six individual fish on day 5.

## Evidence, checks, and saves

The [call and observation index](validation/2026-11-07-overnight.json) includes submitted arguments, outcomes, key state, source filenames, and SHA-256 hashes. Screenshots show [newborn rats](validation/2026-11-07-rat-birth.jpg) and [processing of newly grown fish](validation/2026-11-07-fish-output.jpg). Full local recordings and backups remain in ignored `.cache/overnight-tests/`; game assemblies, decompiled materials, saves, and connection tokens are not distributed.

There were 200 action submissions: 192 dispatched and eight stale-snapshot rejections. Rejected calls were followed by fresh observation and a new UUID. Of 46 live-state assertions, 45 passed; one was the growth-rate expectation above, followed by successful output checks. These counts do not mean 200 distinct gameplay features passed.

The complete build/protocol suite passed **153 checks**, and the published server passed **21 separate offline help checks**. Neither substitutes for the real-game evidence.

All 17 pre-existing `save_*.es3` files, including user saves and previous profession test slots, match their pre-test backup hashes. New slots are 12–15; save indexes and global metadata may change normally. The game ended at the main menu. Normal overnight autosaves occurred; final daytime retrieval and fish-processing state was not additionally verified to persist through another reload.

Coverage is limited to these seeds, rats, tilapia, printing recipe, and equipment configurations. It does not establish all crops/species/modules, complete pup raising, a second aquaculture breeding generation, a full trading day, or compatibility with other builds, resolutions, and mod combinations.
