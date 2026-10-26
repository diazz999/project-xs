using Godot;

namespace ProjectXs.Combat;

/// <summary>
/// Roketin herkesin ekranında gördüğü görüntüsü. Sadece düz bir çizgide ilerler; gerçek roketi sunucu hesaplar
/// ve patladığında bu görüntüyü siler. Roketler düz gittiği için görüntü ile gerçeği hep aynı yerdedir.
/// </summary>
public partial class RocketVisual : Node3D
{
    private const float MaxLifetime = 8.0f;

    public Vector3 Velocity { get; set; }

    private float _life = MaxLifetime;

    public override void _Ready()
    {
        // Silindirin uzun ekseni Y; -90° çevirince -Z'ye, yani gittiği yöne bakar
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.08f, Height = 0.5f, RadialSegments = 8 },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.35f, 0.3f) },
            RotationDegrees = new Vector3(-90, 0, 0),
        });
        AddChild(new OmniLight3D
        {
            LightColor = new Color(1.0f, 0.6f, 0.2f),
            LightEnergy = 2.0f,
            OmniRange = 3.0f,
        });

        if (Velocity.LengthSquared() > 0.001f)
        {
            Vector3 up = Mathf.Abs(Velocity.Normalized().Dot(Vector3.Up)) > 0.99f ? Vector3.Right : Vector3.Up;
            LookAt(GlobalPosition + Velocity, up);
        }
    }

    public override void _Process(double delta)
    {
        GlobalPosition += Velocity * (float)delta;

        _life -= (float)delta;
        if (_life <= 0)
            QueueFree();
    }
}
