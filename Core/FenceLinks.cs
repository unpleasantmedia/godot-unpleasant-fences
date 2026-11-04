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
    /// The path of a script to use on gates' leaves in place of <see cref="FenceGate"/>, for gates that
    /// work as a project's own doors do. It has to be an AnimatableBody3D's, with an OpenAngle.
    /// </summary>
    public static string? GateScript { get; set; }
}
