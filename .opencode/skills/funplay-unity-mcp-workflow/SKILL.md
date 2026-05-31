---
name: funplay-unity-mcp-workflow
description: Efficient workflow for using Unity MCP to edit, import, compile, inspect, and test Unity projects. Use when working in a Unity project and needing to verify code, prefabs, UI, Play Mode behavior, screenshots, scene hierarchy, console logs, domain reloads, or MCP connection issues.
---

# Unity MCP Workflow

## Operating Loop

1. **Establish context.**
   - Confirm the Unity project root and active scene.
   - Check that Unity MCP is reachable before assuming Editor state.
   - Inspect hierarchy, prefab paths, selected objects, and component references through MCP.
   - Verify the real Unity object path via MCP before editing.
2. **Choose the edit surface.**
   - Edit source files with normal repo tools, then trigger Unity recompilation.
   - Edit scene objects through Unity APIs, mark scene dirty, save.
   - Edit prefab assets with `PrefabUtility.LoadPrefabContents`, `SaveAsPrefabAsset`, `UnloadPrefabContents`.
   - For open scene instances, update both the visible instance and the prefab asset.
3. **Execute changes.**
   - **Do not use `execute_code` with the MCP server.** C# execute calls are unreliable and bypass proper compilation.
   - Use null guards for every object lookup; return explicit missing-path messages.
   - Return concise before/after values.
   - Save only the assets or scenes intentionally modified.
   - **Do NOT modify GameObject hierarchy in scenes or prefabs through code or MCP tools** (add/remove/restructure children). It is fragile. Instead, give the user step-by-step manual instructions and move on to code-side changes.
4. **Validate.**
   - Read back changed objects through MCP.
   - For file edits: `request_recompile` → `wait_for_compilation` → inspect console/compilation errors.
   - For runtime: enter Play Mode or inspect live objects.
   - Report what was verified and what still needs device, store, network, or manual validation.

## Tool Exposure

- With the default `core` profile, rely on focused workflow tools: `execute_code`, recompilation, Play Mode control, hierarchy, console logs, screenshots, input simulation, and performance inspection.
- With the default `full` profile, prefer specific MCP tools for simple scene, asset, GameObject, component, prefab, camera, UI, package, animation, file, or visual-feedback operations.
- If a named tool is unavailable, adapt to the exposed list and report which tool is missing.

## MCP Call Pattern

If native MCP tools are not directly available, probe the local HTTP endpoint:

```bash
curl -sS -m 1 -X POST http://127.0.0.1:8765/mcp \
  -H 'Content-Type: application/json' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
```



## Unity C# Patterns

Use fully qualified types if `using` statements are unreliable:

```csharp
var root = UnityEngine.GameObject.Find("PracticeInGameUiRoot");
var rect = root.GetComponent<UnityEngine.RectTransform>();
```

Use Unity null semantics for `UnityEngine.Object` references.

**Prefab edits:**

```csharp
var path = "Assets/MyGame/UI/Prefabs/PF_PracticeInGameUiRoot.prefab";
var prefab = UnityEditor.PrefabUtility.LoadPrefabContents(path);
try
{
    var target = prefab.transform.Find("SafeArea/SwingCancelZone");
    if (target == null) return "SwingCancelZone not found in prefab";
    var rect = target.GetComponent<UnityEngine.RectTransform>();
    var before = rect.anchoredPosition;
    rect.anchoredPosition = new UnityEngine.Vector2(-76f, 448f);
    UnityEditor.EditorUtility.SetDirty(rect);
    UnityEditor.PrefabUtility.SaveAsPrefabAsset(prefab, path);
    UnityEditor.AssetDatabase.SaveAssets();
    return "Prefab saved: pos " + before + " -> " + rect.anchoredPosition;
}
finally { UnityEditor.PrefabUtility.UnloadPrefabContents(prefab); }
```

**Scene edits:**

```csharp
var obj = UnityEngine.GameObject.Find("PracticeInGameUiRoot/SafeArea/SwingCancelZone");
if (obj == null) return "Scene object not found";
var rect = obj.GetComponent<UnityEngine.RectTransform>();
var before = rect.sizeDelta;
UnityEditor.Undo.RecordObject(rect, "Update cancel zone");
rect.sizeDelta = new UnityEngine.Vector2(220f, 116f);
UnityEditor.EditorUtility.SetDirty(rect);
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(obj.scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(obj.scene);
return "Scene saved: size " + before + " -> " + rect.sizeDelta;
```

## Recompile And Reload

After external C# or asset file edits:

1. If Unity is in Play Mode, call `exit_play_mode` first — `request_recompile` is rejected during play.
2. Call `request_recompile`, then `wait_for_compilation`.
3. Read console or compilation errors before continuing.
4. If a domain reload drops the request, call `get_reload_recovery_status` when available, re-scan MCP endpoint, then continue from `wait_for_compilation`.

Do not treat a disconnected request as a successful compile.

After `enter_play_mode`, the HTTP server is briefly unreachable. Poll a cheap endpoint (`tools/list` or `get_reload_recovery_status`) until it responds — do not assume the connection survives the transition.

## Verification

Use readback snippets that print exact values, not only `success`:

```csharp
var all = UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Transform>();
UnityEngine.Transform target = null;
for (int i = 0; i < all.Length; i++)
{
    if (all[i].name == "SwingCancelZone") { target = all[i]; break; }
}
if (target == null) return "SwingCancelZone not found";
var rect = target.GetComponent<UnityEngine.RectTransform>();
return "path=" + target.name + "; pos=" + rect.anchoredPosition + "; size=" + rect.sizeDelta;
```

- For UI: verify hierarchy, sprite references, anchors, sorting order, active state, text fit, button listeners. A populated `Content` hierarchy does not prove visible UI.
- For gameplay/network: verify object identity, ownership, live instance existence, transform values, animation state, visibility, and client-side filters.

## Mobile VR Performance (Oculus/Meta Quest)

When writing Unity C# code for mobile VR, ensure it is strictly performance-optimized:

1. **Zero Runtime Allocations** — No `new`, `Instantiate()`, `Destroy()`, or `AddComponent` during gameplay loops or frequent events. Use Object Pooling instead.
2. **Allocation-Free Queries** — Avoid methods that return arrays (e.g., use `GetComponentsInChildren(List<T>)` instead of `GetComponentsInChildren<T>()` to prevent GC spikes).
3. **Independent Hierarchy** — Do not parent or unparent objects to tracked XR Controller/Camera nodes at runtime. Sync transforms manually in world space to avoid tracking stalls.

## Failure Handling

- If MCP is unreachable, say so and fall back to safe filesystem inspection or code edits. Do not claim runtime verification without Unity readback.
- If an object lookup fails, inspect hierarchy and prefab contents instead of inventing a path.
- If multiple matching objects exist, print their paths and choose the one matching the current scene or user-visible UI.
- If compile errors appear after a change, fix them before Play Mode validation.
- When Unity and text files disagree on serialized scene/prefab state, trust Unity readback.
