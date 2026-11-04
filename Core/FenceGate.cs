using System.Linq;
using Godot;

namespace UnpleasantMedia.UnpleasantFences;

/// <summary>
/// A gate's leaf, its origin on the hinge. <see cref="Toggle"/> swings it open or shut, with any other
/// leaf beside it. A project calls that when the player uses it, or has its own script used in
/// its place (see <see cref="FenceLinks.GateScript"/>).
/// </summary>
[Tool]
[GlobalClass]
[Icon("res://addons/unpleasant_fences/Icons/Fence-Gate.svg")]
public partial class FenceGate : AnimatableBody3D
{
    /// <summary>How long it takes to swing open or shut, in seconds.</summary>
    private const float SwingSeconds = 0.35f;

    private float _shut;
    private Tween? _swing;

    /// <summary>How far it swings open about its hinge, in degrees.</summary>
    [Export(PropertyHint.Range, "-180,180,1,suffix:\u00b0")]
    public float OpenAngle { get; set; } = 90f;

    public bool IsOpen { get; private set; }

    public override void _Ready() => _shut = RotationDegrees.Y;

    /// <summary>Opens it if it is shut, shuts it if it is open.</summary>
    public void Toggle()
    {
        var open = !IsOpen;
        foreach (var leaf in GetParent().GetChildren().OfType<FenceGate>())
            leaf.Swing(open);
    }

    private void Swing(bool open)
    {
        IsOpen = open;
        _swing?.Kill();
        _swing = CreateTween();
        _swing.TweenProperty(this, "rotation_degrees:y", _shut + (open ? OpenAngle : 0f), SwingSeconds)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.Out);
    }
}
