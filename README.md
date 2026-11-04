# UnpleasantFences

A fence plugin for Godot 4.7 (.NET version). Draw a `Fence` along points in the 3D view: it places posts and sections that follow what's under them, and is solid. Meshes and materials are included.

## Features

- Seven kinds: Railing, Picket, Privacy, Chain Link, Wall, Barrier and Guard Rail
- Click to add points, drag to move them, ctrl-click to delete, all undoable
- Fences follow the ground, or anything solid under them
- Gates in the middle of a stretch, for every kind but Barrier and Guard Rail
- Points can snap to places your project gives, such as buildings' corners and roads' edges

## Requirements

- The **.NET version** of Godot 4.7
- .NET 8 SDK and C# 12
- A C# project: this plugin can't run in a project without one

## Install

The plugin must end up at exactly `addons/unpleasant_fences`. As a submodule:

```bash
git submodule add https://github.com/unpleasantmedia/godot-unpleasant-fences.git addons/unpleasant_fences
```

Then, in Godot:

1. If the project has no `.csproj` file, choose **Project > Tools > C# > Create C# solution**.
2. Click **Build** (the hammer at the top right).
3. Enable **UnpleasantFences** under **Project > Project Settings > Plugins**.

If you see `Unable to load addon script from path`, the project hasn't been built yet: build it, then enable the plugin again.

## Use

Add a `FenceGroup` to your level and select it. With **Create Fences** on, each click adds a point to a new fence, clicking its first point closes it, and right-click or Esc finishes it. With **Edit Fences** on, drag a point to move it, ctrl-click one to delete it, click a stretch to add a point, and shift-click a stretch to add or remove a gate. Hold Alt to stop points snapping.

Fences stand on whatever solid thing is under them. Connect your ground if you use a terrain, as the editor may not run physics for it.

## Connect your project

Everything here is optional. Set these when your code loads:

```csharp
internal static class FenceHook
{
    [ModuleInitializer]
    internal static void Hook()
    {
        FenceLinks.Ground = static root => new MyGround(root);        // an IFenceGround
        FenceLinks.SnapPoints = static root => MyBuildingCorners(root);
        FenceLinks.SnapLines = static root => MyRoadEdges(root);
        FenceLinks.GateScript = "res://Scripts/MyDoor.cs";            // gates as your own doors
    }
}
```

## Gates

Gates come as scenes in `Scenes/`, with a `FenceGate` script on each leaf. Call `FenceGate.Toggle()` when the player uses one to swing it open or shut. To have gates work as your own doors, set `FenceLinks.GateScript` to your door script, which replaces `FenceGate` on each leaf. It has to be a script for an `AnimatableBody3D`.

## License

MIT
