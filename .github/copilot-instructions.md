# GitHub Copilot Instructions for Visual XML Patches

## Mod Overview and Purpose

**Mod Name:** Visual XML Patches  
**Author:** Mlie  
**PackageId:** Mlie.VisualXMLPatches  

Visual XML Patches is a mod for RimWorld designed to enhance the XML debugging process. It provides a visual representation of XML patches loaded into the game, showing the order of application, the originating mod, and allowing direct access to the relevant files. Errors in patch application are prominently flagged, making it easier to identify and resolve issues. While primarily created for personal use, this tool is intended to benefit any mod developer through more efficient XML debugging, especially when used alongside the Unified XML Export mod.

## Key Features and Systems

- **Visual Patch Order:** Displays the sequence in which XML patches are applied in the game.
- **Source Identification:** Highlights which mod each patch originates from.
- **Error Detection:** Marks unapplied patches in red if an error occurs.
- **File Access:** Provides functionality to directly open the XML file where a patch is defined.
- **Integration with Unified XML Export:** When used together, both mods can significantly streamline the XML debugging process.

## Coding Patterns and Conventions

- **Namespace Organization:** Keep all classes within the `VisualXMLPatches` namespace.
- **Class Structure:** Critical classes such as `PatchOperation_Apply`, `PatchRecord`, and various helper classes are used to manage the patch display and operations.
- **Method Naming:** Methods adhere to descriptive naming conventions, reflecting their purpose (e.g., `addRowWrapBreaks`, `BuildPatchRowTooltip`).
- **Extension Methods:** Use extension methods where appropriate to enhance readability and functionality.

## XML Integration

- Although the primary logic of this mod is handled in C#, there should be preparedness for potential XML configuration files in future development.
- Consider XML-loading patterns, ensuring efficient parsing and error handling mechanisms are in place for further integrations.

## Harmony Patching

- **Dependency:** The mod relies on `brrainz.harmony` for runtime patching.
- **Patch Definitions:** Utilize harmony to apply postfixes and prefixes, such as those in `PatchOperation_Apply`, to modify and enhance game behavior without altering the original source code.

## Suggestions for Copilot

- **Real-Time Code Suggestions:** Utilize Copilot to propose code formats adhering to established conventions, like method signatures or class definition skeletons.
- **Error Handling Routines:** Leverage Copilot to suggest improvements or optimizations in try-catch blocks to handle XML parsing errors.
- **Documentation Assistance:** Allow Copilot to assist in generating comprehensive comments and inline documentation, enhancing code maintainability.
- **Code Reusability:** Employ Copilot’s pattern-matching capabilities to identify opportunities for method extraction, optimizing code reuse within similar classes.

By adhering to these detailed guidelines, Copilot can effectively contribute to the development, maintenance, and enhancement of the Visual XML Patches mod. This structured approach ensures the mod's stability, usability, and future expandability as a valuable tool in the RimWorld modding community.

## Project Solution Guidelines
- Relevant mod XML files are included as Solution Items under the solution folder named XML, these can be read and modified from within the solution.
- Use these in-solution XML files as the primary files for reference and modification.
- The `.github/copilot-instructions.md` file is included in the solution under the `.github` solution folder, so it should be read/modified from within the solution instead of using paths outside the solution. Update this file once only, as it and the parent-path solution reference point to the same file in this workspace.
- When making functional changes in this mod, ensure the documented features stay in sync with implementation; use the in-solution `.github` copy as the primary file.
- In the solution is also a project called Assembly-CSharp, containing a read-only version of the decompiled game source, for reference and debugging purposes.
- For any new documentation, update this copilot-instructions.md file rather than creating separate documentation files.


## Hard rules (must follow)
- Do NOT run commands that modify the repo (no git commit, git apply, dotnet format) unless explicitly asked.
- Prefer minimal reads: read only the smallest code region needed (around the suspicious lines).

