using Godot;

namespace ProjectXs.Combat;

/// <summary>
/// Sadece görsel, kısa ömürlü efektler: mermi izi, isabet parıltısı, patlama.
/// Hepsi kendini birkaç onda bir saniye içinde siler. Asset gerektirmez; ileride parçacık efektleriyle değiştirilebilir.
/// </summary>
public static class CombatEffects
{
    private static readonly Color TracerColor = new(1.0f, 0.9f, 0.55f);
    private static readonly Color ImpactColor = new(1.0f, 0.95f, 0.8f);
    private static readonly Color ExplosionColor = new(1.0f, 0.55f, 0.15f);

    public static void Shot(Node3D parent, Vector3 from, Vector3[] ends)
    {
        foreach (var end in ends)
        {
            Tracer(parent, from, end);
            Impact(parent, end);
        }
    }

    public static void Tracer(Node3D parent, Vector3 from, Vector3 to)
    {
        Vector3 direction = to - from;
        float length = direction.Length();
        if (length < 0.05f) return;

        var material = Unlit(TracerColor);
        var mesh = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.02f, 0.02f, length) },
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        parent.AddChild(mesh);

        // BoxMesh'in uzun kenarı Z ekseninde; kutuyu iki noktanın ortasına koyup yönüne çeviriyoruz
        mesh.GlobalTransform = new Transform3D(Basis.LookingAt(direction, SafeUp(direction)), from + direction * 0.5f);
        FadeAndFree(mesh, material, 0.08f);
    }

    public static void Impact(Node3D parent, Vector3 position)
    {
        var material = Unlit(ImpactColor);
        var mesh = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.06f, Height = 0.12f, RadialSegments = 8, Rings = 4 },
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        parent.AddChild(mesh);
        mesh.GlobalPosition = position;
        FadeAndFree(mesh, material, 0.15f);
    }

    public static void Explosion(Node3D parent, Vector3 position, float radius)
    {
        var material = Unlit(ExplosionColor);
        var ball = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 1.0f, Height = 2.0f },
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Scale = Vector3.One * 0.3f,
        };
        parent.AddChild(ball);
        ball.GlobalPosition = position;

        var light = new OmniLight3D
        {
            LightColor = ExplosionColor,
            LightEnergy = 6.0f,
            OmniRange = radius * 2.5f,
        };
        ball.AddChild(light);

        // Top hızla patlama yarıçapına büyür ve sönerken ışık da kararır
        var tween = ball.CreateTween().SetParallel(true);
        tween.TweenProperty(ball, "scale", Vector3.One * radius, 0.25f)
            .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        tween.TweenProperty(material, "albedo_color:a", 0.0f, 0.35f);
        tween.TweenProperty(light, "light_energy", 0.0f, 0.35f);
        tween.Chain().TweenCallback(Callable.From(ball.QueueFree));
    }

    private static StandardMaterial3D Unlit(Color color) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        AlbedoColor = color,
    };

    private static void FadeAndFree(Node node, StandardMaterial3D material, float duration)
    {
        var tween = node.CreateTween();
        tween.TweenProperty(material, "albedo_color:a", 0.0f, duration);
        tween.TweenCallback(Callable.From(node.QueueFree));
    }

    private static Vector3 SafeUp(Vector3 direction)
        => Mathf.Abs(direction.Normalized().Dot(Vector3.Up)) > 0.99f ? Vector3.Right : Vector3.Up;
}
