# Stage 2 — data tables pipeline

`data/` holds the Luban schema + seed data for ProjectF.

## Files

| File | Purpose |
|---|---|
| `luban.conf` | Luban project config (csv schema defines, c/s/e groups, topModule `ProjectF.Tables`) |
| `__tables__.csv` | Table registry — one row per table (`full_name`, `value_type`, `input`, `index`, `mode`) |
| `__beans__.csv` | Hand-written enum beans (`ItemCategory`, `TaskType`) — data files only carry an int id |
| `*.csv` | The 11 game tables (item, fish, bait, rod, pond, recipe, recipe_material, seed, shop, task, level_exp) |

## Generating

```bash
pwsh tools/gen.ps1
```

Output of one run:
- C# → `src/ProjectF.Tables/Gen/` (compiled into ProjectF.Tables, netstandard2.1)
- Binary → `UnityProject/Assets/StreamingAssets/Tables/` (loaded by the Unity client)

`Luban.dll` is expected at `tools/luban/Luban.dll` (gitignored). gen.ps1 prints the
download URL if it is missing.

## Schema conventions

- Data files use the Luban csv format: `##var` field-name row, `##type` type row,
  then data rows. **Every data row starts with an empty marker column** (a leading
  comma) — field names begin at column B.
- **Luban v5.1.0 csv rules (learned the hard way, enforced by the pipeline):**
  - NO comment rows (`##,...`) inside data files — the csv title parser rejects them.
  - NO commas inside cells (a quoted `"1,2,3"` cell is split by the naive csv reader).
    Multi-value lists therefore live in separate relational tables (see `pond_fish.csv`),
    not in comma-joined string columns.
  - Schema-define files (like `Defines/__beans__.xml`) must be referenced per-file in
    `luban.conf` (`"Defines/__beans__.xml"`), not as a bare directory entry.
  - Files live as UTF-8 **with BOM**.
- Enums (`ItemCategory`, `TaskType`) are defined in `Defines/__beans__.xml` and used
  directly in `##type` rows (e.g. `ItemCategory`); the binary stores their int values.
- Table value types are inferred from the data file header
  (`read_schema_from_file=true`), except the enums above.
- `pond_fish.csv` maps ponds to `fish.id` values (NOT `item.id` — fish.item_id is the
  inventory item you receive; the two ranges coincide at 3001-3008 today but are
  separate concepts).
