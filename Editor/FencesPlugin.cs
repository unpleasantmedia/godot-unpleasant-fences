#if TOOLS
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace UnpleasantMedia.UnpleasantFences.Editor;

/// <summary>
/// Draws fences as roads are drawn, with a FenceGroup (or one of its fences) selected. With
/// Create Fences on, each click on the ground adds a point to a new fence, clicking its first
/// point closes it round, and right-click or Esc finishes it. With Edit Fences on, dragging any
/// fence's point moves it, ctrl-clicking one deletes it, clicking a stretch puts a point there
/// (to drag), and shift-clicking one puts a gate in its middle, or takes it out. Points snap to what
/// the project gives through FenceLinks (say, the corners of buildings and the edges of roads), unless Alt is held.
/// All undoable.
/// </summary>
[Tool]
public partial class FencesPlugin : EditorPlugin
{
    /// <summary>How near on screen a point or stretch is to be picked, in pixels.</summary>
    private const float PickRadius = 10f;

    /// <summary>How near a building's corner or a road's edge a point snaps to it, in metres.</summary>
    private const float SnapReach = 1.0f;

    private HBoxContainer? _toolbar;
    private Button? _createButton;
    private Button? _editButton;
    private OptionButton? _kindMenu;

    private Node3D? _selected;
    /// <summary>The point a new one goes after: its index in the selected fence, or -1 for its end.</summary>
    private int _after = -1;

    /// <summary>Whether the fence being created is carried on from its first point (new points going before it) rather than its last.</summary>
    private bool _fromStart;
    private bool _finished;

    private Vector2 _mouse;
    private Vector3? _hover;
    private bool _snapped;
    private int _dragging = -1;
    private Vector3[]? _dragFrom;
    private Vector2? _rightPressedAt;

    // What points snap to, gathered as drawing starts.
    private List<Vector3>? _corners;
    private List<(Vector3 A, Vector3 B)>? _edges;
    private List<Fence>? _fences;

    private bool Creating => _createButton?.ButtonPressed == true;
    private bool Editing => _editButton?.ButtonPressed == true;
    private bool Drawing => Creating || Editing;

    public override void _EnterTree()
    {
        _toolbar = new HBoxContainer();
        _toolbar.AddChild(new VSeparator());
        _createButton = new Button { Text = "Create Fences", ToggleMode = true, Flat = true, TooltipText =
            "Click the ground to add the points of a new fence; click its first point to close it round; right-click or Esc finishes it. Hold Alt not to snap." };
        // By method name, not a lambda: the editor can then let go of them as it reloads the scripts.
        _createButton.Connect(BaseButton.SignalName.Toggled, new Callable(this, MethodName.CreateToggled));
        Iconify(_createButton, "Fence-Draw");
        _toolbar.AddChild(_createButton);
        _editButton = new Button { Text = "Edit Fences", ToggleMode = true, Flat = true, TooltipText =
            "Drag a fence's point to move it, ctrl-click one to delete it, click a stretch to add a point there, shift-click a stretch for a gate. Hold Alt not to snap." };
        _editButton.Connect(BaseButton.SignalName.Toggled, new Callable(this, MethodName.EditToggled));
        Iconify(_editButton, "Pencil");
        _toolbar.AddChild(_editButton);
        _kindMenu = new OptionButton { TooltipText = "What new fences are. Change a fence's kind later in the Inspector." };
        foreach (var kind in System.Enum.GetNames<FenceKind>())
            _kindMenu.AddItem(kind);
        _kindMenu.Selected = (int)FenceKind.Picket;
        _toolbar.AddChild(_kindMenu);
        ToolButtons.StyleAll(_toolbar);
        _toolbar.Visible = false;
        AddControlToContainer(CustomControlContainer.SpatialEditorMenu, _toolbar);
    }

    public override void _ExitTree()
    {
        if (_toolbar is null)
            return;
        RemoveControlFromContainer(CustomControlContainer.SpatialEditorMenu, _toolbar);
        _toolbar.QueueFree();
        _toolbar = null;
    }

    public override bool _Handles(GodotObject @object) => @object is FenceGroup or Fence;

    public override void _Edit(GodotObject? @object)
    {
        var was = _selected;
        _selected = @object as Node3D;
        if (_selected != was && _selected is Fence)
            _after = -1;
        UpdateOverlays();
    }

    public override void _MakeVisible(bool visible)
    {
        if (_toolbar is not null)
            _toolbar.Visible = visible;
        if (!visible)
            _selected = null;
    }

    // One or the other on, never both.
    private void CreateToggled(bool on)
    {
        if (on)
            _editButton?.SetPressedNoSignal(false);
        Reset();
    }

    private void EditToggled(bool on)
    {
        if (on)
            _createButton?.SetPressedNoSignal(false);
        Reset();
    }

    private void Reset()
    {
        _dragging = -1;
        _dragFrom = null;
        _corners = null;
        _edges = null;
        _fences = null;
        // A new fence started with the next click, not one carried on that happened to be selected.
        _finished = true;
        _after = -1;
        _fromStart = false;
        UpdateOverlays();
    }

    public void Select(Node node)
    {
        if (!IsInstanceValid(node) || !node.IsInsideTree())
            return;
        var selection = EditorInterface.Singleton.GetSelection();
        selection.Clear();
        selection.AddNode(node);
        EditorInterface.Singleton.EditNode(node);
    }

    private Fence? SelectedFence => _selected is Fence fence && IsInstanceValid(fence) ? fence : null;

    public override int _Forward3DGuiInput(Camera3D camera, InputEvent @event)
    {
        if (!Drawing || _selected is null || !IsInstanceValid(_selected))
            return (int)AfterGuiInput.Pass;

        switch (@event)
        {
            case InputEventMouseMotion motion:
                _mouse = motion.Position;
                (_hover, _snapped) = Hover(camera, _mouse, !motion.AltPressed);
                if (_dragging >= 0 && SelectedFence is { } dragged && _hover is { } to && _dragging < dragged.Points.Length)
                {
                    var points = dragged.Points.ToArray();
                    points[_dragging] = dragged.ToLocal(to);
                    dragged.Points = points;
                }

                UpdateOverlays();
                return _dragging >= 0 ? (int)AfterGuiInput.Stop : (int)AfterGuiInput.Pass;

            case InputEventMouseButton { ButtonIndex: MouseButton.Left } click:
                return LeftClick(camera, click);

            // The right button still turns the camera: only a click that does not move finishes the fence.
            case InputEventMouseButton { ButtonIndex: MouseButton.Right } rightClick:
                if (rightClick.Pressed)
                    _rightPressedAt = rightClick.Position;
                else if (_rightPressedAt is { } pressedAt && pressedAt.DistanceTo(rightClick.Position) < 4f)
                    Finish();
                if (!rightClick.Pressed)
                    _rightPressedAt = null;
                return (int)AfterGuiInput.Pass;

            case InputEventKey { Keycode: Key.Escape, Pressed: true }:
                Finish();
                return (int)AfterGuiInput.Stop;
        }

        return (int)AfterGuiInput.Pass;
    }

    private void Finish()
    {
        // Its origin moved to its middle, as a step of its own to undo.
        if (!_finished && SelectedFence is { } fence && fence.Middle() is var middle && middle.DistanceTo(fence.GlobalPosition) > 0.5f)
        {
            var undo = EditorInterface.Singleton.GetEditorUndoRedo();
            undo.CreateAction($"Centre {fence.Name}");
            undo.AddDoMethod(fence, Fence.MethodName.MoveOrigin, middle);
            undo.AddUndoMethod(fence, Fence.MethodName.MoveOrigin, fence.GlobalPosition);
            undo.CommitAction();
        }

        _finished = true;
        _after = -1;
        _fromStart = false;
        UpdateOverlays();
    }

    private int LeftClick(Camera3D camera, InputEventMouseButton click)
    {
        var stop = (int)AfterGuiInput.Stop;
        var fence = SelectedFence;

        // Let go of a dragged point: the move kept, undoably.
        if (!click.Pressed)
        {
            if (_dragging >= 0 && fence is not null && _dragFrom is not null)
            {
                var undo = EditorInterface.Singleton.GetEditorUndoRedo();
                undo.CreateAction("Move fence point");
                undo.AddDoProperty(fence, Fence.PropertyName.Points, fence.Points);
                undo.AddUndoProperty(fence, Fence.PropertyName.Points, _dragFrom);
                undo.CommitAction(false);
            }

            var was = _dragging;
            _dragging = -1;
            _dragFrom = null;
            return was >= 0 ? stop : (int)AfterGuiInput.Pass;
        }

        return Creating ? CreateClick(camera, click, fence) : EditClick(camera, click);
    }

    /// <summary>A click while creating: the next point of the fence being drawn, or its first point to close it round.</summary>
    private int CreateClick(Camera3D camera, InputEventMouseButton click, Fence? fence)
    {
        // Its other end clicked: closed round.
        var otherEnd = _fromStart ? fence?.Points.Length - 1 : 0;
        if (!_finished && fence is not null && fence.Points.Length > 2 && !fence.Closed && PointUnder(camera, fence, click.Position) == otherEnd)
        {
            SetFence(fence, "Close fence", fence.Points, fence.Gates, true);
            Finish();
            return (int)AfterGuiInput.Stop;
        }

        // Not drawing one: an open fence's end clicked carries that fence on from there.
        if (_finished || fence is null)
        {
            foreach (var other in Fences().Where(f => !f.Closed && f.Points.Length > 0))
            {
                var point = PointUnder(camera, other, click.Position);
                if (point != 0 && point != other.Points.Length - 1)
                    continue;
                Select(other);
                _selected = other;
                _finished = false;
                _after = -1;
                _fromStart = point == 0 && other.Points.Length > 1;
                UpdateOverlays();
                return (int)AfterGuiInput.Stop;
            }
        }

        if (_hover is not { } at)
            return (int)AfterGuiInput.Pass;
        AddPoint(at);
        return (int)AfterGuiInput.Stop;
    }

    /// <summary>A click while editing: on any fence's point or stretch, that fence picked and changed there.</summary>
    private int EditClick(Camera3D camera, InputEventMouseButton click)
    {
        var fences = Fences();
        // The selected fence first, where fences cross.
        if (SelectedFence is { } current)
            fences = fences.Where(f => f != current).Prepend(current).ToList();
        foreach (var fence in fences)
        {
            var point = PointUnder(camera, fence, click.Position);
            var stretch = point < 0 ? StretchUnder(camera, fence, click.Position) : -1;
            if (point < 0 && stretch < 0)
                continue;
            if (fence != _selected)
            {
                Select(fence);
                _selected = fence;
            }

            _finished = true;
            if (point >= 0 && click.CtrlPressed)
            {
                DeletePoint(fence, point);
            }
            else if (point >= 0)
            {
                _dragging = point;
                _dragFrom = fence.Points.ToArray();
                _after = point;
            }
            else if (click.ShiftPressed)
            {
                // A gate there, or none.
                var gates = fence.Gates.Contains(stretch) ? fence.Gates.Where(g => g != stretch).ToArray() : fence.Gates.Append(stretch).Order().ToArray();
                SetFence(fence, fence.Gates.Contains(stretch) ? "Remove fence gate" : "Add fence gate", fence.Points, gates, fence.Closed);
            }
            else if (_hover is { } at)
            {
                // A point put in where it was clicked, and dragged on from there (its move undone on its own).
                var points = fence.Points.ToList();
                points.Insert(stretch + 1, fence.ToLocal(at));
                var gates = fence.Gates.Select(g => g > stretch ? g + 1 : g).ToArray();
                SetFence(fence, "Add fence point", points.ToArray(), gates, fence.Closed);
                _dragFrom = points.ToArray();
                _dragging = stretch + 1;
                _after = stretch + 1;
            }

            UpdateOverlays();
            return (int)AfterGuiInput.Stop;
        }

        return (int)AfterGuiInput.Pass;
    }

    private void AddPoint(Vector3 at)
    {
        var undo = EditorInterface.Singleton.GetEditorUndoRedo();
        var fence = _finished ? null : SelectedFence;
        if (fence is null)
        {
            var group = _selected as FenceGroup ?? _selected?.GetParent() as FenceGroup;
            if (group is null)
                return;
            var root = EditorInterface.Singleton.GetEditedSceneRoot();
            fence = new Fence { Name = "Fence", Kind = (FenceKind)(_kindMenu?.Selected ?? (int)FenceKind.Picket) };
            undo.CreateAction("Add fence");
            undo.AddDoMethod(group, Node.MethodName.AddChild, fence, true);
            undo.AddDoMethod(fence, Node.MethodName.SetOwner, root);
            undo.AddDoProperty(fence, Fence.PropertyName.Points, new[] { group.GlobalTransform.AffineInverse() * at });
            undo.AddDoMethod(this, MethodName.Select, fence);
            undo.AddDoReference(fence);
            undo.AddUndoMethod(group, Node.MethodName.RemoveChild, fence);
            undo.AddUndoMethod(this, MethodName.Select, group);
            undo.CommitAction();
            _after = -1;
            _finished = false;
            _fromStart = false;
            _fences = null;
            GD.Print($"Started {fence.Name}: click on to add points, right-click or Esc to finish it.");
            return;
        }

        // Before its first point when carried on from there; else after the point picked (its
        // stretches after it shifting on one), or at the end.
        var insert = _fromStart ? 0 : _after < 0 || _after >= fence.Points.Length - 1 ? fence.Points.Length : _after + 1;
        var points = fence.Points.ToList();
        points.Insert(insert, fence.ToLocal(at));
        var gates = fence.Gates.Select(g => g >= insert ? g + 1 : g).ToArray();
        SetFence(fence, "Add fence point", points.ToArray(), gates, fence.Closed);
        _after = _fromStart || insert == points.Count - 1 ? -1 : insert;
    }

    private void DeletePoint(Fence fence, int index)
    {
        var points = fence.Points.Where((_, i) => i != index).ToArray();
        // Its gates on the stretches after it shift back one; the one from it goes.
        var gates = fence.Gates.Where(g => g != index).Select(g => g > index ? g - 1 : g).ToArray();
        SetFence(fence, "Delete fence point", points, gates, fence.Closed && points.Length > 2);
        _after = -1;
    }

    private static void SetFence(Fence fence, string action, Vector3[] points, int[] gates, bool closed)
    {
        var undo = EditorInterface.Singleton.GetEditorUndoRedo();
        undo.CreateAction(action);
        undo.AddDoProperty(fence, Fence.PropertyName.Points, points);
        undo.AddDoProperty(fence, Fence.PropertyName.Gates, gates);
        undo.AddDoProperty(fence, Fence.PropertyName.Closed, closed);
        undo.AddUndoProperty(fence, Fence.PropertyName.Points, fence.Points);
        undo.AddUndoProperty(fence, Fence.PropertyName.Gates, fence.Gates);
        undo.AddUndoProperty(fence, Fence.PropertyName.Closed, fence.Closed);
        undo.CommitAction();
    }

    // ------------------------------------------------------------------ where

    /// <summary>Every fence in the scene.</summary>
    private List<Fence> Fences()
    {
        _fences ??= EditorInterface.Singleton.GetEditedSceneRoot()?.FindChildren("*", "", true, false).OfType<Fence>().ToList() ?? new List<Fence>();
        _fences.RemoveAll(f => !IsInstanceValid(f) || !f.IsInsideTree());
        return _fences;
    }

    private static int PointUnder(Camera3D camera, Fence fence, Vector2 mouse)
    {
        var best = -1;
        var bestDistance = PickRadius;
        for (var i = 0; i < fence.Points.Length; i++)
        {
            var at = fence.ToGlobal(fence.Points[i]);
            if (camera.IsPositionBehind(at))
                continue;
            var distance = camera.UnprojectPosition(at).DistanceTo(mouse);
            if (distance < bestDistance)
                (best, bestDistance) = (i, distance);
        }

        return best;
    }

    private static int StretchUnder(Camera3D camera, Fence fence, Vector2 mouse)
    {
        var best = -1;
        var bestDistance = PickRadius;
        for (var s = 0; s < fence.Stretches; s++)
        {
            var a = fence.ToGlobal(fence.Points[s]);
            var b = fence.ToGlobal(fence.Points[(s + 1) % fence.Points.Length]);
            if (camera.IsPositionBehind(a) || camera.IsPositionBehind(b))
                continue;
            var distance = Geometry2D.GetClosestPointToSegment(mouse, camera.UnprojectPosition(a), camera.UnprojectPosition(b)).DistanceTo(mouse);
            if (distance < bestDistance)
                (best, bestDistance) = (s, distance);
        }

        return best;
    }

    /// <summary>Where the mouse is on whatever is solid under it (a road, a lot, the ground), snapped to a building's corner or a road's edge nearby if asked.</summary>
    private (Vector3? At, bool Snapped) Hover(Camera3D camera, Vector2 mouse, bool snap)
    {
        var from = camera.ProjectRayOrigin(mouse);
        var way = camera.ProjectRayNormal(mouse);
        Vector3? hit = null;
        var query = PhysicsRayQueryParameters3D.Create(from, from + way * camera.Far);
        // Through fences, onto what they stand on.
        var root = EditorInterface.Singleton.GetEditedSceneRoot();
        query.Exclude = new Godot.Collections.Array<Rid>(Fences().SelectMany(f => f.Bodies()));
        var result = camera.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (result.Count > 0)
            hit = result["position"].AsVector3();
        if (FenceLinks.Ground?.Invoke(root) is { } ground && ground.Intersect(from, way) is { } onGround
            && (hit is null || from.DistanceTo(onGround) < from.DistanceTo(hit.Value)))
            hit = onGround;
        if (hit is not { } at || !snap)
            return (hit, false);

        // A building's corner first, then a road's edge.
        _corners ??= root is not null && FenceLinks.SnapPoints is { } points ? [.. points(root)] : [];
        var corner = _corners.Where(c => Flat(c - at).Length() < SnapReach).OrderBy(c => Flat(c - at).Length()).Cast<Vector3?>().FirstOrDefault();
        if (corner is { } c)
            return (c with { Y = at.Y }, true);
        _edges ??= root is not null && FenceLinks.SnapLines is { } lines ? [.. lines(root)] : [];
        Vector3? best = null;
        var bestDistance = SnapReach;
        foreach (var (a, b) in _edges)
        {
            var on = Geometry3D.GetClosestPointToSegment(at, a, b);
            var distance = Flat(on - at).Length();
            if (distance < bestDistance)
                (best, bestDistance) = (on, distance);
        }

        return best is { } edge ? (edge, true) : (at, false);
    }

    private static Vector3 Flat(Vector3 v) => v with { Y = 0f };

    /// <summary>Shows one of the plugin's icons in place of a button's text.</summary>
    private static void Iconify(Button button, string icon)
    {
        button.Icon = GD.Load<Texture2D>($"res://addons/unpleasant_fences/Icons/{icon}.svg");
        button.AddThemeConstantOverride("icon_max_width", 20);
        button.TooltipText = $"{button.Text}: {button.TooltipText}";
        button.Text = "";
    }

    // ------------------------------------------------------------------ drawing

    public override void _Forward3DDrawOverViewport(Control overlay)
    {
        if (!Drawing || EditorInterface.Singleton.GetEditorViewport3D(0)?.GetCamera3D() is not { } camera)
            return;
        var fence = SelectedFence;
        Vector2? Screen(Vector3 at) => camera.IsPositionBehind(at) ? null : camera.UnprojectPosition(at);
        // The other fences faintly, to be clicked to edit.
        foreach (var other in Fences().Where(f => f != fence))
        {
            for (var s = 0; s < other.Stretches; s++)
            {
                if (Screen(other.ToGlobal(other.Points[s])) is { } a && Screen(other.ToGlobal(other.Points[(s + 1) % other.Points.Length])) is { } b)
                    overlay.DrawLine(a, b, new Color(1f, 1f, 1f, 0.35f), 1.5f);
            }

            foreach (var point in other.Points)
            {
                if (Screen(other.ToGlobal(point)) is { } p)
                    overlay.DrawCircle(p, 3f, new Color(1f, 1f, 1f, 0.5f));
            }
        }

        if (fence is not null)
        {
            for (var s = 0; s < fence.Stretches; s++)
            {
                if (Screen(fence.ToGlobal(fence.Points[s])) is { } a && Screen(fence.ToGlobal(fence.Points[(s + 1) % fence.Points.Length])) is { } b)
                    overlay.DrawLine(a, b, fence.Gates.Contains(s) ? new Color(1f, 0.6f, 0.2f) : new Color(1f, 1f, 1f, 0.8f), 2f);
            }

            for (var i = 0; i < fence.Points.Length; i++)
            {
                if (Screen(fence.ToGlobal(fence.Points[i])) is not { } p)
                    continue;
                var picked = i == (_fromStart ? 0 : _after < 0 ? fence.Points.Length - 1 : _after) && !_finished;
                overlay.DrawCircle(p, picked ? 7f : 5f, picked ? new Color(0.3f, 0.8f, 1f) : Colors.White);
                overlay.DrawArc(p, picked ? 7f : 5f, 0f, Mathf.Tau, 16, Colors.Black, 1f);
            }
        }

        if (_dragging >= 0 || _hover is not { } hover || Screen(hover) is not { } h)
            return;
        // Where a click would put the next point, from the one it would follow.
        if (Creating && fence is not null && !_finished && fence.Points.Length > 0)
        {
            var from = fence.Points[_fromStart ? 0 : _after < 0 ? fence.Points.Length - 1 : _after];
            if (Screen(fence.ToGlobal(from)) is { } f)
                overlay.DrawDashedLine(f, h, new Color(1f, 1f, 1f, 0.6f), 2f, 6f);
        }

        overlay.DrawCircle(h, 4f, _snapped ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 1f, 1f, 0.6f));
    }
}
#endif
