# GitHub Copilot Instructions for Visual XML Patches Mod

## Mod Overview and Purpose

**Visual XML Patches** is a RimWorld mod that provides an in‑game visual browser for all active XML `PatchOperation`s (including nested sequence operations). It helps modders inspect, search, group, and open source XML patch files without manually scanning raw XML.

## Current Key Systems

- Patch capture via Harmony prefix on `PatchOperation.Apply` (see `PatchOperation_Apply`).
- Full patch tree pre-scan that maps every (including nested) `PatchOperation` to its owning `ModContentPack` (`VisualXMLPatches.PatchToMod`).
- 1:1 aligned lists: `Patches` and `Mods` grow together in apply order (null entries later inherit grouping from previous non-null mod for nested operations).
- Grouping logic in `VisualXMLPatchesMod` that:
  - Orders groups by load order index.
  - Collapses groups by default unless failures exist.
  - Falls back to previous mod when a sub‑operation has no direct mod mapping.
- Display logic substitutes operations summary (or mods list) when an xpath is missing.
- Reflection field access with caching (`fieldCache`) for fast repeated inspection of private fields like `xpath`, `value`, `attribute`, `operations`, `mods`, `sourceFile`, `neverSucceeded`.
- XML pretty formatting with safe fallbacks for fragments or malformed snippets.
- On-demand expansion of per‑patch details (attribute, mods, operations detail, value).
- Source file open support with root-relative resolution.
- Failure highlighting for patches that never succeeded.

## Target Framework / Language

- .NET Framework 4.8
- C# 12/13 features in use (collection expressions `[]`, range/slice, target-typed `new`). Keep compatibility with the game runtime; avoid APIs newer than .NET 4.8 BCL.

## Important Design Considerations

1. Performance:
   - UI renders every frame while the settings window is open—avoid allocations in hot paths (cache reflection info, avoid repeated LINQ enumerations inside loops where possible).
   - Only re-calc dynamic lists when counts mismatch.
2. Safety:
   - All reflection calls must be guarded with try/catch; never let a reflection failure crash the UI.
   - Null mod references are valid for nested operations; grouping logic must remain null-safe.
3. Ordering & Grouping:
   - Maintain stable order based on application index and load order grouping.
   - When adding new patch metadata, ensure indexing alignment: `Patches[i]` corresponds to `Mods[i]` (may be null) and `Results[i]`.
4. Extensibility:
   - New per‑patch metadata should reuse existing detail block drawing utilities.
   - If adding new reflected fields, extend the centralized helper methods rather than duplicating reflection.
5. XML Handling:
   - Use existing `formatXmlFragment` / `maybeFormatXmlString` helpers; do not introduce external XML libs.
6. UI Behavior:
   - Keep row height constants consistent; if adding icons or columns, recalc total height similarly.
   - Always adjust `totalHeightCalc` when introducing new expandable detail sections.
7. Error State Visualization:
   - Respect existing red highlight pattern for failed patches; reuse `neverSucceeded` logic.
8. Source File Resolution:
   - Preserve cross-platform path normalization when modifying open logic.

## Suggested Future Enhancements

- Optional filtering by success/failure state.
- Export visible patch list to a file (respecting formatted XML blocks).
- Lazy-load XML formatting to reduce initial UI cost.
- Add per‑group collapse/expand all controls.
- Diff view for value changes if original node content can be located.

## Copilot Guidance

When suggesting code:
- Prefer adding helpers near existing private static helper methods in `VisualXMLPatchesMod`.
- Reuse `getFieldCached` for any new private field access.
- Keep public API surface minimal; internal implementation detail is fine.
- Avoid breaking the index alignment contract among `Patches`, `Mods`, and `Results`.
- Use null-safe patterns (`?.`, defensive checks) and swallow reflection errors silently.

## Naming Conventions

- Helper methods: `getX`, `formatX`, `drawX`, `openX`, `resolveX` (camelCase after verb prefix for private static helpers).
- Constants: PascalCase with clear intent (e.g., `HeaderHeight`).
- Collections: plural nouns (`expandedPatches`, `collapsedPerMod`).

## Harmony Usage

- All patches share the Harmony ID: `Mlie.VisualXMLPatches`.
- Additional instrumentation patches should be placed in their own files and kept minimal (avoid game logic mutation—read/observe only).

## Testing & Validation Tips

- Open Mod Settings after activating a large number of mods to ensure scroll performance.
- Validate that nested `PatchOperationSequence` members inherit grouping.
- Induce a failing patch to verify failure highlighting still triggers default group expansion.

By following this updated overview and guidelines, contributors (and Copilot) can confidently extend the Visual XML Patches mod while preserving performance, stability, and clarity.

