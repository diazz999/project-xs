using Godot;

namespace ProjectXs.Tools;

public readonly struct RayHit
{
    public Vector3 Position { get; init; }
    public Vector3 Normal { get; init; }
    public GodotObject Collider { get; init; }
}

/// <summary>Kameranın baktığı yöne ışın atar; oyuncunun kendi gövdesini yok sayar. Bütün araçlar bunu kullanır.</summary>
public static class ToolRaycast
{
    public static bool Cast(CollisionObject3D self, Camera3D camera, float range, out RayHit hit)
        => CastRay(self, camera.GlobalPosition, -camera.GlobalBasis.Z, range, out hit);

    /// <summary>İstenen noktadan istenen yöne ışın atar (ör. sapmalı mermiler için).</summary>
    public static bool CastRay(CollisionObject3D self, Vector3 from, Vector3 direction, float range, out RayHit hit)
    {
        Vector3 to = from + direction.Normalized() * range;

        var query = PhysicsRayQueryParameters3D.Create(from, to);
        query.Exclude = new Godot.Collections.Array<Rid> { self.GetRid() };

        var result = self.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (result.Count == 0)
        {
            hit = default;
            return false;
        }

        hit = new RayHit
        {
            Position = result["position"].AsVector3(),
            Normal = result["normal"].AsVector3(),
            Collider = result["collider"].AsGodotObject(),
        };
        return true;
    }
}
