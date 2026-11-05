using System.Collections.Generic;
using System.Linq;
using Godot;

namespace UnpleasantMedia.UnpleasantFences;

/// <summary>The kinds of fence there are, each with its pieces in the plugin's Meshes folder.</summary>
public enum FenceKind
{
    Railing,
    Picket,
    Privacy,
    ChainLink,
    Wall,
    Barrier,
    GuardRail,
}

/// <summary>
/// A fence drawn along points in the editor, as a road is (see FencesPlugin): a post at each point,
/// more between them no further apart than its kind's spacing, and a metre-long piece of fence
/// after another between them, each leaning from one post's foot to the next, so it follows the
/// ground, or the road, sidewalk or parking lot it stands on. A stretch between two points can
/// have a gate in its middle, which swings open as a door does. It is solid to everything.
/// </summary>
[Tool]
[GlobalClass]
[Icon("res://addons/unpleasant_fences/Icons/Fence.svg")]
public partial class Fence : Node3D
{
    /// <summary>
    /// Whether its saved settings have all been put back since it was made: false from when the
    /// scripts are reloaded (it is made anew, already in the tree and built) to the end of that
    /// frame. Settings put back then are not changes, and rebuild nothing: rebuilding everything
    /// after each reload froze the editor for a minute.
    /// </summary>
    private bool _reloadSettled;

    public Fence() => new Callable(this, MethodName.ReloadSettle).CallDeferred();

    private void ReloadSettle() => _reloadSettled = true;

    /// <summary>Whether a setting is being put back as the scripts are reloaded, rather than changed.</summary>
    private bool Reloading => IsInsideTree() && !_reloadSettled;

    private const string Meshes = "res://addons/unpleasant_fences/Meshes";
    private const string Scenes = "res://addons/unpleasant_fences/Scenes";

    /// <summary>A kind's height, how far apart its posts are at most, and how wide its posts are (half).</summary>
    private static readonly Dictionary<FenceKind, (float Height, float Spacing, float HalfPost, float Thick)> Specs = new()
    {
        [FenceKind.Railing] = (1.1f, 2.0f, 0.03f, 0.05f),
        [FenceKind.Picket] = (1.0f, 2.0f, 0.05f, 0.06f),
        [FenceKind.Privacy] = (1.8f, 2.4f, 0.05f, 0.06f),
        [FenceKind.ChainLink] = (2.0f, 3.0f, 0.035f, 0.04f),
        [FenceKind.Wall] = (0.8f, 3.0f, 0.2f, 0.3f),
        [FenceKind.Barrier] = (0.81f, 3.0f, 0f, 0.6f),
        [FenceKind.GuardRail] = (0.75f, 2.0f, 0.05f, 0.55f),
    };

    /// <summary>Kinds with no posts (one block after another), and with no gates.</summary>
    private static readonly HashSet<FenceKind> NoPosts = new() { FenceKind.Barrier };
    private static readonly HashSet<FenceKind> NoGates = new() { FenceKind.Barrier, FenceKind.GuardRail };


    /// <summary>Kinds whose gate is in a material of its own, its fence's material leaving it be.</summary>
    private static readonly HashSet<FenceKind> GatesOwnMaterial = new() { FenceKind.Wall };

    private FenceKind _kind = FenceKind.Picket;
    private Vector3[] _points = System.Array.Empty<Vector3>();
    private int[] _gates = System.Array.Empty<int>();
    private bool _closed;
    private Material? _material;

    [Export]
    public FenceKind Kind { get => _kind; set { _kind = value; Changed(); } }

    /// <summary>The points it runs through, in its own space: where its posts stand (the heights are where they were clicked; it stands on whatever is under them).</summary>
    [Export]
    public Vector3[] Points { get => _points; set { _points = value ?? System.Array.Empty<Vector3>(); Changed(); } }

    /// <summary>Whether it runs from its last point back to its first.</summary>
    [Export]
    public bool Closed { get => _closed; set { _closed = value; Changed(); } }

    /// <summary>Turned round to face the other way: a guard rail's rail on the left as drawn rather than the right.</summary>
    [Export]
    public bool Flip { get => _flip; set { _flip = value; Changed(); } }

    private bool _flip;

    private bool _terrainOnly;

    private bool _onPoints;

    /// <summary>Whether it stands at its points' own heights (and straight between them), not on whatever is under them: a road's barriers, put there by the road.</summary>
    [Export]
    public bool OnPoints { get => _onPoints; set { _onPoints = value; Changed(); } }

    /// <summary>Whether it stands on the terrain alone, through anything over it (a plot's fence: never up on a sidewalk, a neighbour's fence or a building's eaves).</summary>
    [Export]
    public bool TerrainOnly { get => _terrainOnly; set { _terrainOnly = value; Changed(); } }

    /// <summary>The stretches with a gate in their middle: each the index of the point it starts at.</summary>
    [Export]
    public int[] Gates { get => _gates; set { _gates = value ?? System.Array.Empty<int>(); Changed(); } }

    private int[] _gaps = System.Array.Empty<int>();

    /// <summary>The stretches left open (a drive's way through it): each the index of the point it starts at; a post stands either side.</summary>
    [Export]
    public int[] Gaps { get => _gaps; set { _gaps = value ?? System.Array.Empty<int>(); Changed(); } }

    /// <summary>What it is made of; empty keeps its kind's own (a chain-link fence's mesh keeps its own always).</summary>
    [Export]
    public Material? Material { get => _material; set { _material = value; Changed(); } }

    /// <summary>Builds it again: after the ground or the roads under it have changed.</summary>
    [ExportToolButton("Rebuild", Icon = "Reload")]
    public Callable RebuildButton => Callable.From(Build);

    private bool _queued;

    public override void _Ready()
    {
        // Once, a moment after it comes in: by then the roads and lots under it have their
        // collision, to stand on (built straight away, it stood on the bare ground, and had to
        // build again).
        GetTree().CreateTimer(0.5).Connect(SceneTreeTimer.SignalName.Timeout, new Callable(this, MethodName.QueueBuild));
    }

    /// <summary>A setting changed: built again, but not as the scripts are reloaded.</summary>
    private void Changed()
    {
        if (!Reloading)
            QueueBuild();
    }

    /// <summary>Builds it at the end of the frame, once, however many of its settings change in it.</summary>
    public void QueueBuild()
    {
        if (_queued || !IsInsideTree())
            return;
        _queued = true;
        Callable.From(Build).CallDeferred();
    }

    /// <summary>The middle of the box round its points, in the world; its own place if it has none.</summary>
    public Vector3 Middle()
    {
        if (_points.Length == 0)
            return GlobalPosition;
        var box = new Aabb(ToGlobal(_points[0]), Vector3.Zero);
        foreach (var point in _points)
            box = box.Expand(ToGlobal(point));
        return box.GetCenter();
    }

    /// <summary>Puts its origin here, its points (and anything put on it) staying where they are.</summary>
    public void MoveOrigin(Vector3 to)
    {
        var points = _points.Select(ToGlobal).ToArray();
        var children = GetChildren().OfType<Node3D>().Select(c => (c, c.GlobalTransform)).ToList();
        GlobalPosition = to;
        foreach (var (child, kept) in children)
            child.GlobalTransform = kept;
        Points = [.. points.Select(ToLocal)];
    }

    /// <summary>How many stretches it has (from each point to the next, and back to the first if closed).</summary>
    public int Stretches => _points.Length < 2 ? 0 : _closed && _points.Length > 2 ? _points.Length : _points.Length - 1;

    /// <summary>How tall its kind is.</summary>
    public float Height => Specs[_kind].Height;

    public void Build()
    {
        // Its gizmo, for clicking it in the editor, round what it builds now.
        UpdateGizmos();
        _queued = false;
        if (!IsInsideTree())
            return;
        // All it built before, its internal children: the list of them is lost when the scripts are
        // reloaded, the children are not.
        foreach (var node in Built().ToList())
        {
            RemoveChild(node);
            node.QueueFree();
        }

        if (Stretches == 0)
            return;

        var spec = Specs[_kind];
        var ground = FenceLinks.Ground?.Invoke(EditorOrSceneRoot());
        var posts = new List<Transform3D>();
        var sections = new List<Transform3D>();
        var boxes = new List<(Transform3D Place, Vector3 Size)>();
        var gates = new List<Transform3D>();

        // Where the posts stand: each point, and between them; on the ground or whatever is under them.
        Vector3 Foot(Vector3 local)
        {
            var at = ToGlobal(local);
            return _onPoints ? at : at with { Y = GroundAt(at, ground) };
        }

        var feet = _points.Select(Foot).ToArray();
        var gateSet = _gates.ToHashSet();
        var gapSet = _gaps.ToHashSet();
        for (var s = 0; s < Stretches; s++)
        {
            var a = feet[s];
            var b = feet[(s + 1) % feet.Length];
            var flat = (b - a) with { Y = 0f };
            var length = flat.Length();
            if (length < 0.05f)
                continue;
            var way = flat / length;

            // Left open: nothing along it, but a post at its far end for the fence to go on from (and
            // at its near end, if it is the first stretch: any other's stands there already).
            if (gapSet.Contains(s))
            {
                var facingOpen = _flip ? -way : way;
                if (s == 0)
                    posts.Add(new Transform3D(new Basis(facingOpen, Vector3.Up, facingOpen.Cross(Vector3.Up)), a));
                if (!(s == Stretches - 1 && _closed && _points.Length > 2))
                    posts.Add(new Transform3D(new Basis(facingOpen, Vector3.Up, facingOpen.Cross(Vector3.Up)), b));
                continue;
            }

            // The runs of fence along it: all of it, or either side of a gate in its middle.
            var gateSpan = 1f + spec.HalfPost * 2f;
            var runs = new List<(float From, float To)>();
            if (gateSet.Contains(s) && !NoGates.Contains(_kind) && length >= gateSpan + 0.1f)
            {
                var from = (length - gateSpan) / 2f;
                runs.Add((0f, from));
                runs.Add((from + gateSpan, length));
                var hinge = Foot(ToLocal(a.Lerp(b, (from + spec.HalfPost) / length)));
                gates.Add(new Transform3D(new Basis(way, Vector3.Up, way.Cross(Vector3.Up)), hinge));
            }
            else
            {
                runs.Add((0f, length));
            }

            foreach (var (from, to) in runs)
            {
                var count = Mathf.Max(1, Mathf.CeilToInt((to - from) / spec.Spacing - 0.01f));
                var feetAlong = Enumerable.Range(0, count + 1)
                    .Select(k => k == 0 && from == 0f ? a : k == count && to == length ? b : Foot(ToLocal(a.Lerp(b, Mathf.Lerp(from, to, (float)k / count) / length))))
                    .ToList();
                for (var k = 0; k < count; k++)
                {
                    var p = feetAlong[k];
                    var q = feetAlong[k + 1];
                    // The metres of it between two posts, each leaning from one's foot to the other's: a metre piece stretched a little to fit.
                    var span = ((q - p) with { Y = 0f }).Length();
                    var metres = Mathf.Max(1, Mathf.RoundToInt(span));
                    var across = way.Cross(Vector3.Up);
                    for (var m = 0; m < metres; m++)
                    {
                        var start = p.Lerp(q, (float)m / metres);
                        var end = p.Lerp(q, (float)(m + 1) / metres);
                        // Turned round: drawn from its far end back, so its right is the other side.
                        sections.Add(_flip
                            ? new Transform3D(new Basis(start - end, Vector3.Up, -across), end)
                            : new Transform3D(new Basis(end - start, Vector3.Up, across), start));
                    }

                    var rise = q - p;
                    var middle = (p + q) / 2f + Vector3.Up * (spec.Height / 2f);
                    var along = rise.Normalized();
                    var up = across.Cross(along).Normalized();
                    boxes.Add((new Transform3D(new Basis(along, up, across), middle), new Vector3(rise.Length(), spec.Height, spec.Thick)));
                }

                for (var k = 0; k <= count; k++)
                {
                    // A post at each end, but where one already stands (the last point of the stretch before, or a closed fence's first).
                    if (k == 0 && from == 0f && s > 0 || k == count && to == length && s == Stretches - 1 && _closed && _points.Length > 2)
                        continue;
                    var facing = _flip ? -way : way;
                    posts.Add(new Transform3D(new Basis(facing, Vector3.Up, facing.Cross(Vector3.Up)), feetAlong[k]));
                }
            }
        }

        // Each post solid, as each length of fence between them is.
        if (NoPosts.Contains(_kind))
            posts.Clear();
        foreach (var post in posts)
            boxes.Add((post.Translated(Vector3.Up * (spec.Height / 2f)), new Vector3(spec.HalfPost * 2f, spec.Height, spec.HalfPost * 2f)));

        var name = _kind.ToString();
        Instances($"Fence-{name}-Post", posts, _material);
        Instances($"Fence-{name}-Section", sections, _material);
        if (_kind == FenceKind.ChainLink)
            Instances($"Fence-{name}-Mesh", sections, null);

        // All its boxes as one shape: a node and shape for each was most of the time it took to build.
        var body = AddBuilt(new StaticBody3D { Name = "Body" });
        if (boxes.Count > 0)
        {
            var inverse = GlobalTransform.AffineInverse();
            var faces = new Vector3[boxes.Count * BoxFaces.Length];
            var f = 0;
            foreach (var (place, size) in boxes)
            {
                var local = inverse * place;
                foreach (var corner in BoxFaces)
                    faces[f++] = local * (corner * size);
            }

            body.AddChild(new CollisionShape3D { Shape = new ConcavePolygonShape3D { Data = faces, BackfaceCollision = true } });
        }

        if (gates.Count > 0 && ResourceLoader.Exists($"{Scenes}/Fence-{name}-Gate.tscn"))
        {
            var scene = GD.Load<PackedScene>($"{Scenes}/Fence-{name}-Gate.tscn");
            foreach (var place in gates)
            {
                var gate = scene.Instantiate<Node3D>();
                if (FenceLinks.GateScript is { } script)
                {
                    foreach (var leaf in gate.FindChildren("*", nameof(AnimatableBody3D), true, false).OfType<FenceGate>().ToList())
                    {
                        var (angle, path) = (leaf.OpenAngle, gate.GetPathTo(leaf));
                        leaf.SetScript(GD.Load<Script>(script));
                        // Found again: changing its script leaves the one above no longer usable.
                        gate.GetNode(path).Set("OpenAngle", angle);
                    }
                }

                gate.Transform = GlobalTransform.AffineInverse() * place;
                AddBuilt(gate);
                if (_material is not null && !GatesOwnMaterial.Contains(_kind))
                {
                    foreach (var mesh in gate.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
                    {
                        if (!mesh.GetPath().ToString().Contains("/Mesh"))
                            mesh.MaterialOverride = _material;
                    }
                }
            }
        }
    }

    /// <summary>A unit box's twelve triangles, for its corners to be scaled by a box's size (solid from either side).</summary>
    private static readonly Vector3[] BoxFaces = MakeBoxFaces();

    private static Vector3[] MakeBoxFaces()
    {
        var faces = new List<Vector3>();
        // Each side: its outward axis, and two axes across it.
        foreach (var (normal, u, v) in new[]
                 {
                     (Vector3.Right, Vector3.Up, Vector3.Back), (Vector3.Left, Vector3.Back, Vector3.Up),
                     (Vector3.Up, Vector3.Back, Vector3.Right), (Vector3.Down, Vector3.Right, Vector3.Back),
                     (Vector3.Back, Vector3.Right, Vector3.Up), (Vector3.Forward, Vector3.Up, Vector3.Right),
                 })
        {
            Vector3 At(float a, float b) => (normal + u * a + v * b) / 2f;
            faces.AddRange([At(-1, -1), At(1, -1), At(1, 1), At(-1, -1), At(1, 1), At(-1, 1)]);
        }

        return [.. faces];
    }

    /// <summary>Its solid parts (its body and its gates'), for rays to pass through it.</summary>
    public IEnumerable<Rid> Bodies() =>
        Built().SelectMany(n => n.FindChildren("*", "", true, false).Prepend(n)).OfType<CollisionObject3D>().Select(c => c.GetRid());

    /// <summary>What it has built: its internal children (kept through script reloads, unlike a list of them).</summary>
    private IEnumerable<Node> Built()
    {
        var own = GetChildren().ToHashSet();
        return GetChildren(true).Where(c => !own.Contains(c));
    }

    private T AddBuilt<T>(T node) where T : Node
    {
        AddChild(node, false, InternalMode.Back);
        // Not drawn from far off (see FenceDrawDistance).
        FenceDrawDistance.LimitSmall(node, _kind is FenceKind.Barrier or FenceKind.GuardRail ? FenceDrawDistance.Barriers : FenceDrawDistance.Fences);
        return node;
    }

    /// <summary>Many of one piece, at these places (in world space), drawn together.</summary>
    private void Instances(string piece, List<Transform3D> places, Material? material)
    {
        if (places.Count == 0 || PieceMesh(piece) is not { } mesh)
            return;
        var inverse = GlobalTransform.AffineInverse();
        // A square of the ground at a time, so a long fence is not drawn (or left out) all at once.
        foreach (var chunk in FenceDrawDistance.Chunks(places))
        {
            var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = chunk.Count };
            for (var i = 0; i < chunk.Count; i++)
                multi.SetInstanceTransform(i, inverse * chunk[i]);
            AddBuilt(new MultiMeshInstance3D { Name = piece, Multimesh = multi, MaterialOverride = material });
        }
    }

    private static readonly Dictionary<string, Mesh?> PieceMeshes = new();

    /// <summary>A piece's mesh, from its model (null until it has been imported).</summary>
    private static Mesh? PieceMesh(string piece)
    {
        if (PieceMeshes.TryGetValue(piece, out var known) && known is not null)
            return known;
        var path = $"{Meshes}/{piece}.glb";
        Mesh? mesh = null;
        if (ResourceLoader.Exists(path) && GD.Load<PackedScene>(path)?.Instantiate() is { } model)
        {
            mesh = model.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().FirstOrDefault()?.Mesh;
            model.Free();
        }

        PieceMeshes[piece] = mesh;
        return mesh;
    }

    private Node? EditorOrSceneRoot()
    {
#if TOOLS
        if (Engine.IsEditorHint())
            return EditorInterface.Singleton.GetEditedSceneRoot();
#endif
        // The level it is in: the current scene if it is in that, else the topmost node over it (a
        // level loaded by the menu builds before it becomes the current scene).
        if (GetTree().CurrentScene is { } current && (current == this || current.IsAncestorOf(this)))
            return current;
        Node top = this;
        while (top.GetParent() is { } parent && parent != GetTree().Root)
            top = parent;
        return top;
    }

    /// <summary>
    /// How high whatever is under a place stands: the ground, or a road, sidewalk, parking lot or
    /// anything else solid over it within a few metres of where it was clicked.
    /// </summary>
    private float GroundAt(Vector3 at, IFenceGround? ground)
    {
        var height = ground is not null && ground.HeightAt(at) is var h && !float.IsNaN(h) ? h : at.Y;
        var space = GetWorld3D()?.DirectSpaceState;
        if (space is null || _terrainOnly)
            return height;
        var top = Mathf.Max(height, at.Y) + 3f;
        var query = PhysicsRayQueryParameters3D.Create(at with { Y = top }, at with { Y = Mathf.Min(height, at.Y) - 3f });
        query.Exclude = new Godot.Collections.Array<Rid>(Bodies());
        var hit = space.IntersectRay(query);
        return hit.Count > 0 && hit["position"].AsVector3().Y > height - 0.05f ? hit["position"].AsVector3().Y : height;
    }
}
