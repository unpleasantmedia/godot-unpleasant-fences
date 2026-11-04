using System;
using System.Collections.Generic;
using Godot;

namespace UnpleasantMedia.UnpleasantFences;

/// <summary>
/// The ground fences stand on. The plugin knows no terrain itself: a project connects its own
/// through <see cref="FenceLinks.Ground"/>. Without one, fences stand on whatever solid thing is under them.
/// </summary>
public interface IFenceGround
{
    /// <summary>How high the ground is at a place, or NaN where there is none.</summary>
    float HeightAt(Vector3 at);

    /// <summary>Where a ray first meets the ground, if it does.</summary>
    Vector3? Intersect(Vector3 from, Vector3 direction);
}

/// <summary>What a project tells the plugin.</summary>
public static class FenceLinks
{
    /// <summary>Gives the ground in a level, or null if there is none.</summary>
    public static Func<Node?, IFenceGround?>? Ground { get; set; }

    /// <summary>Gives the points in a level that new fence points snap to first, such as buildings' corners.</summary>
    public static Func<Node, IReadOnlyList<Vector3>>? SnapPoints { get; set; }

    /// <summary>Gives the lines in a level that new fence points snap to next, such as roads' edges.</summary>
    public static Func<Node, IReadOnlyList<(Vector3 A, Vector3 B)>>? SnapLines { get; set; }

    /// <summary>
    /// The folder of the gates' scenes, each named Fence-(kind)-Gate.tscn, as a gate's leaf needs a
    /// script of the project's own to swing. Without one, a gate is a gap in the fence.
    /// </summary>
    public static string? GateFolder { get; set; }
}
