# GitHub Copilot Instructions for Visual XML Patches Mod

## Mod Overview and Purpose
Visual XML Patches is a RimWorld mod that provides a visual representation of XML patches loaded in the game. The primary goal of this mod is to assist users in debugging XML issues by displaying the order of patch application, the mod responsible for each patch, and the ability to open the relevant source files. Additionally, it highlights patches that were not applied due to errors.

## Key Features and Systems
- **Patch Visualization**: Displays the order of XML patches applied, along with the mod responsible for each patch.
- **Error Detection**: Highlights patches that failed to apply due to errors.
- **File Navigation**: Allows users to open the relevant source files for each patch.
- **Settings Interface**: Provides a settings window to configure mod options.

## Coding Patterns and Conventions
- **C# Classes and Methods**: The mod is written in C# and follows the standard .NET naming conventions for classes and methods.
- **Harmony Patching**: Utilizes the Harmony library for patching game methods, ensuring compatibility with RimWorld's modding framework.
- **Mod Settings**: Implements a settings window for user configuration using RimWorld's built-in settings system.

## XML Integration
The mod does not directly interact with XML files. Instead, it leverages RimWorld's XML parsing and patching mechanisms to visualize the loaded patches.

## Harmony Patching
The mod uses Harmony to patch specific game methods to inject custom behavior for visualizing XML patches. Key patched methods include:
- `PatchOperation_Apply`: Used to track and visualize the application of XML patches.
- `PatchGroupView`: Manages the display of patch groups in the game interface.

## Suggestions for Copilot
To optimize Copilot's performance and accuracy in this mod, consider the following suggestions:
1. **Code Snippets**: Provide code snippets for common Harmony patches and XML parsing tasks.
2. **Documentation**: Include comments and documentation within the code to explain patching logic and visualization techniques.
3. **Examples**: Share examples of successful XML patch debugging sessions to illustrate the mod's utility.
4. **Best Practices**: Offer guidelines for writing and applying XML patches to ensure compatibility with the mod.

## Example C# Code Structure
csharp
// PatchOperation_Apply.cs
public class PatchOperation_Apply
{
    public void Postfix()
    {
        // Logic to visualize patch application
    }

    public void Prefix()
    {
        // Logic to track patch application
    }
}


csharp
// PatchRecord.cs
public class PatchRecord
{
    public bool HasDetails()
    {
        // Logic to check if patch has detailed information
        return true;
    }
}


csharp
// VisualXMLPatchesMod.cs
public class VisualXMLPatchesMod : Mod
{
    public VisualXMLPatchesMod(ModContentPack content) : base(content) {}

    public override void DoSettingsWindowContents(Rect inRect)
    {
        // Logic for mod settings window
    }

    public override string SettingsCategory()
    {
        return "Visual XML Patches";
    }
}


By following these conventions and suggestions, Copilot can effectively assist in the development and maintenance of the Visual XML Patches mod.

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
- When mentioning SonarQube issues, automatically use the SonarQube MCP service to fetch and address issues instead of making inferred fixes without querying SonarQube first.
- When mentioning the rimworld log, automatically use the Rimworld MCP service to fetch the log.

