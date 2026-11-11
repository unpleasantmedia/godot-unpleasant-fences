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
    /// <summary>How often its progress is printed, in milliseconds.</summary>
    private const ulong ProgressEvery = 5000;

    /// <summary>Builds all its fences again: after the ground or the roads under them have changed.</summary>
    [ExportToolButton("Rebuild Fences", Icon = "Reload")]
    public Callable RebuildButton => Callable.From(RebuildAll);

    /// <summary>
    /// Builds every fence in it again: a few each frame (they stand on what is solid, which only the
    /// main thread may ask), so the editor keeps up, its progress in the Output every few seconds.
    /// </summary>
    private void RebuildAll()
    {
        if (_rebuilding is not null)
        {
            GD.PushWarning($"Rebuild Fences ({Name}): already under way; wait for it to finish.");
            return;
        }

        var fences = FindChildren("*", "", true, false).OfType<Fence>().ToList();
        var next = 0;
        var shownAt = Time.GetTicksMsec();
        var started = Time.GetTicksMsec();
        var tree = GetTree();
        GD.Print($"Rebuild Fences ({Name})...");
        _rebuilding = () =>
        {
            var frame = Time.GetTicksMsec();
            while (next < fences.Count && Time.GetTicksMsec() - frame < 25)
            {
                var fence = fences[next++];
                if (IsInstanceValid(fence))
                    fence.Build();
            }

            var percent = fences.Count == 0 ? 100 : next * 100 / fences.Count;
            if (next < fences.Count)
            {
                if (Time.GetTicksMsec() - shownAt >= ProgressEvery)
                {
                    shownAt = Time.GetTicksMsec();
                    GD.Print($"Rebuild Fences ({Name})... {percent}%");
                }

                return;
            }

            tree.ProcessFrame -= _rebuilding;
            _rebuilding = null;
            GD.Print($"Rebuild Fences ({Name}): rebuilt {fences.Count} fences ({(Time.GetTicksMsec() - started) / 1000.0:0.0} s).");
        };
        tree.ProcessFrame += _rebuilding;
    }

    private System.Action? _rebuilding;
}
