# Agent Onboarding

This repo targets Unity **6000.2.10f1**, URP 3D, Input System (see `Assets/InputSystem_Actions.inputactions`). Work inside `Assets/` so Unity tracks GUIDs.

## Repo map
- `Assets/` — gameplay, UI, shaders, scenes. Key folders: `Map/`, `Resources/Map`, `UI/Layouts`, `UI/Scripts`, `Shaders/`, `Settings/`, `Sprites/` (includes `za.json`).
- `Packages/`, `ProjectSettings/`, `UserSettings/` — Unity-managed; avoid manual edits.
- `Library/`, `Temp/`, `Logs/` — generated caches; keep out of source control.

## MCP workflow (Unity must be running)
1. **Inspect before edit**: Use `access_mcp_resource` with Unity resources (e.g., `unity://editor/state`, `unity://editor/selection`) for project state, `manage_scene get_hierarchy` for scene info, `manage_gameobject find` for GameObject queries.
2. **Modify safely**: prefer `script_apply_edits` for structured C# edits (methods/classes), `manage_script` for basic script operations, `manage_asset` for UXML/USS/materials, `manage_prefabs` for prefab work. Use `apply_text_edits` only for precise character-based tweaks with exact coordinates.
3. **Validate**: run `validate_script` for C# validation, check `read_console` for errors/warnings, use `run_tests` for Edit/Play Mode test suites.
4. **Respect Assets**: never hand-edit `.meta` files, keep all assets in `Assets/`, rely on MCP tools to preserve GUIDs and Unity references.
5. **Set active instance**: Use `set_active_instance` if multiple Unity instances are running to target the correct project.

## Common tasks
- **Fix compile errors**: `read_console` → inspect file with `access_mcp_resource` → edit via `script_apply_edits` → re-check console.
- **Assembly definitions**: edit `.asmdef` JSON with standard file tools; add Unity package references explicitly.
- **GameObjects**: `manage_scene get_hierarchy` to locate objects, `manage_gameobject create/modify/delete` for GameObject operations, use component-specific actions (`add_component`, `remove_component`, `set_component_property`).
- **Scripts**: Use `create_script` for new C# files, `script_apply_edits` for structured method/class changes, `apply_text_edits` for precise line/character edits, `validate_script` to check syntax.
- **Assets**: `manage_asset` for import/create/modify operations, `get_sha` for file integrity checks, search with `manage_asset search`.
- **Prefabs**: `manage_prefabs create/modify` for prefab operations, edit in isolation mode via prefab stage.
- **Testing**: `run_tests EditMode` or `run_tests PlayMode`, access test lists via `mcpforunity://tests` resources.
- **UI Toolkit**: UXML in `Assets/UI/Layouts`, USS in `Assets/UI/Styles`, controllers under `Assets/UI/Scripts`. UIDocuments must reference layouts + PanelSettings.
- **Editor state**: Query `unity://editor/state` for play mode status, `unity://editor/selection` for current selection, `unity://editor/active-tool` for transform tools.

## MCP Tools Reference

### Script Management
- `create_script` — Create new C# scripts at specified paths with optional namespace/type
- `script_apply_edits` — Structured edits for methods/classes (preferred for C# changes)
- `apply_text_edits` — Precise character-position edits (use sparingly, requires exact coordinates)
- `validate_script` — Check C# syntax and compilation errors, get diagnostics
- `get_sha` — Get SHA256 hash and metadata for scripts (useful for integrity checks)
- `manage_script` — Legacy compatibility for basic script CRUD operations
- `delete_script` — Remove scripts by URI or path

### Scene & GameObject Management
- `manage_scene` — Create, load, save scenes; get hierarchy and build settings
- `manage_gameobject` — Full CRUD for GameObjects, components, properties
- `manage_prefabs` — Create, modify, delete prefabs; edit in isolation mode

### Asset Management
- `manage_asset` — Import, create, modify, delete assets; search and get metadata
- `manage_shader` — Create, read, update, delete shader files

### Editor Control
- `manage_editor` — Control play/pause/stop, get state, manage tools, tags, layers
- `set_active_instance` — Switch between multiple Unity editor instances
- `read_console` — Get Unity console messages, filter by type, clear console
- `run_tests` — Execute EditMode or PlayMode test suites with timeout options

### Key Resources (use with `access_mcp_resource`)
- `unity://editor/state` — Current play mode, compilation status, active scene
- `unity://editor/selection` — Currently selected objects and their properties
- `unity://editor/active-tool` — Transform tools (Move, Rotate, Scale) and settings
- `unity://editor/windows` — All open editor windows with positions and focus
- `unity://project/info` — Project root, Unity version, platform info
- `unity://project/tags` — All defined tags in TagManager
- `unity://project/layers` — All layers with indices (0-31)
- `unity://editor/prefab-stage` — Current prefab editing context
- `mcpforunity://tests` — Available test suites and individual tests

## Best practices
- Keep edits incremental; test in small slices with validation after each change.
- Use Input System-friendly APIs (already enabled project-wide).
- Avoid touching generated folders or auto assets (meshes under `Map/Meshes`, etc.).
- **Script editing**: Prefer `script_apply_edits` for method/class operations (safer, structured), use `apply_text_edits` only for precise character-position edits when you know exact coordinates.
- **Resource access**: Use `access_mcp_resource` to read Unity resources before making changes, check editor state frequently.
- **Multi-instance**: If working with multiple Unity projects, use `set_active_instance` to target the correct editor.
- **Validation workflow**: Always use `validate_script` after C# changes, monitor `read_console` for warnings/errors.
- **Asset integrity**: Use `get_sha` to verify script checksums before applying edits, especially for collaborative work.
- Document new workflows directly in this file to keep onboarding tight.
- If a `FEATURE.md` exists in the repo, follow its implementation plan closely when adding or changing gameplay features.

## Commit conventions
- Format: `type: summary`. Common types: `feat`, `fix`, `docs`, `chore`, `tool`, `refactor`, `test`, `style`.
- One logical change per commit; reference assets/scripts only when helpful.

## UI & map quick notes
- `UIManager` (Assets/UI/Scripts/Core) swaps Player/UI action maps, handles pause (ESC via "UI/Cancel").
- `GameHUD` shows timer, provinces visited, province details; hook new HUD data through this class.
- `RegionMapController` manages runtime meshes, hover/select events, and color/emission via `MaterialPropertyBlock` (useful for night-light effects).
- GIS importer (`Zarus/Map/Rebuild Region Assets`) rebuilds `RegionDatabase.asset` + meshes from `Assets/Sprites/za.json`—run it after GIS changes rather than editing generated assets.
- Start menu lives in `Assets/Scenes/Start.unity` with layout `UI/Layouts/Screens/StartMenu.uxml` + controller `StartMenuController`. End/game-over menu mirrors this in `Assets/Scenes/End.unity` using `EndMenuController`.
- Build order is Start → Main → End; use `UIManager.ReturnToMenu()`, `.RestartGame()`, or `.ShowEndScreen()` to hop between scenes at runtime (ESC pause "Quit to Menu" already calls ReturnToMenu).
- Shared settings overlay uses `Resources/UI/Layouts/Screens/SettingsPanel.uxml` + `SettingsPanelView`; instantiate it inside any layout via `SettingsPanelView.Create(hostElement, template)` (see StartMenu & PauseMenu for reference).
- **UI Toolkit pitfalls:** Unity 6 still crashes when inline style attributes reference USS variables (`style="margin-top: var(--spacing-md)"`). Always apply theme spacing via USS classes instead. All overlay/World/Game HUD panel settings must use `ZarusOverlayTheme.tss` so variables resolve consistently; mixing themes breaks styling and can hide UIDocuments entirely.
