# Visual XML Patches performance refactor notes

This refactor is intentionally focused on the settings window hot path. The mod can display tens of thousands of applied XML patches, and Unity IMGUI may call the settings draw method multiple times per frame for layout, repaint, and input events. The old implementation performed discovery, reflection, search filtering, grouping, sorting, XML formatting, and height calculation directly in that draw method. That made an idle open window expensive and made typing in the search field trigger repeated full scans.

## Main design change

The settings window now uses cached view models:

- `PatchRecord` stores stable metadata for one applied patch.
- `PatchGroupView` stores the current filtered grouping by owning mod.
- Dirty flags decide when patch metadata, filtered rows, or grouped rows must be rebuilt.

The draw method should now mostly draw already prepared data. If future changes add new fields, prefer adding them to the cached record/group layer rather than reflecting or formatting them directly inside `DoSettingsWindowContents`.

## Search changes

Search now uses a cached `SearchText` string per patch record and `IndexOf(..., StringComparison.OrdinalIgnoreCase)`. This replaces repeated `ToLowerInvariant().Contains(...)` calls because lowercasing every searchable field allocates many temporary strings.

Search is also debounced by 250 ms. The text box updates immediately, but filtering waits briefly until the user pauses typing. Clearing the search applies immediately because returning to the full list should feel instant.

Patch values are deliberately excluded from default search. Extracting values can require XML parsing and pretty-printing. A future explicit "include values in search" option could build a separate value index, but ordinary search should stay cheap.

## XML value formatting changes

Patch values are now formatted lazily. The refactor only checks whether a patch has a value field while building `PatchRecord`; it does not stringify or pretty-print the value. The actual formatted text is computed only when the user expands a row, then cached on that record.

This is the most important removal from the old implementation: the previous code could format XML values while searching or calculating layout even when rows were collapsed and the user never inspected those values.

## Removed or avoided work

The old draw method used LINQ grouping/sorting/counting on every draw call. That was replaced with explicit cached groups because grouping is data preparation, not rendering.

The old height calculation measured `mods` and verbose `operations` detail text, but the row expansion UI only displayed `attribute` and `value`. That unused detail-height work was removed rather than cached. The compact operations summary is still retained because it is useful for search and for display when a patch has no xpath.

The scroll view now skips drawing rows outside the visible viewport. It still advances the logical Y position for every row so the scrollbar remains correct, but off-screen rows do not create labels, buttons, highlights, or tooltips.

## Harmony capture changes

`PatchOperation.Apply` is now captured with both a prefix and postfix. The prefix records the application order before Verse applies the patch. The postfix records the bool result on the same row. This avoids later inferring success from private state during the UI pass. The settings UI still has a defensive fallback if captured results and patches ever become misaligned.

## What not to do lightly

Do not replace the ordered patch list with a `HashSet`. Patch order is part of what the mod shows. Sets are useful for membership state such as expanded rows, not for the primary patch collection.

Do not move work back into `DoSettingsWindowContents` unless it is strictly drawing. If a future feature needs reflection, grouping, sorting, or XML formatting, add it behind a cache or lazy path.

Do not add a separate .NET helper process unless profiling proves in-process cached search is still insufficient. The current bottleneck was Unity/RimWorld-side UI recomputation, not modern .NET regex or text-processing throughput.
