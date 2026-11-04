using System.Linq;
using Godot;

namespace UnpleasantMedia.UnpleasantFences;

/// <summary>
/// Where a level's fences are kept: select it and turn on Draw Fences (see FencesPlugin) to draw
/// a new one, which goes in it.
/// </summary>
[Tool]
[GlobalClass]
[Icon("res://addons/unpleasant_fences/Icons/Fence-Group.svg")]
public partial class FenceGroup : Node3D
{
    /// <summary>Builds all its fences again: after the ground or the roads under them have changed.</summary>
    [ExportToolButton("Rebuild Fences", Icon = "Reload")]
    public Callable RebuildButton => Callable.From(RebuildAll);

    private void RebuildAll()
    {
        foreach (var fence in FindChildren("*", "", true, false).OfType<Fence>())
            fence.Build();
    }
}
