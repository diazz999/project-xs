using System.Collections.Generic;
using Godot;

namespace ProjectXs.Save;

/// <summary>
/// Bir kayıt dosyasının içeriği; JSON olarak yazılır, yani istersen bir metin editöründe açıp okuyabilirsin.
/// Oyuncular kaydedilmez; dünya (değişen bloklar), objeler ve aralarındaki bağlantılar kaydedilir.
/// </summary>
public sealed class SaveFile
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public string Name { get; set; } = "";
    public long SavedAtUnix { get; set; }

    public int[] WorldInts { get; set; } = System.Array.Empty<int>();
    public float[] WorldFloats { get; set; } = System.Array.Empty<float>();
    /// <summary>Üretilmiş dünyaya göre değişen bloklar: her blok için x, y, z, tür.</summary>
    public int[] BlockEdits { get; set; } = System.Array.Empty<int>();

    public List<PropSave> Props { get; set; } = new();
    public List<ConstraintSave> Constraints { get; set; } = new();
}

public sealed class PropSave
{
    /// <summary>Kayıt anındaki node adı; sadece bağlantıların hangi objelere ait olduğunu eşlemek için.</summary>
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public float[] Position { get; set; } = System.Array.Empty<float>();
    public float[] Rotation { get; set; } = System.Array.Empty<float>();
    public bool Frozen { get; set; }
}

public sealed class ConstraintSave
{
    /// <summary>Weld, Axis ya da Wheel.</summary>
    public string Kind { get; set; } = "";
    public string A { get; set; } = "";
    /// <summary>Boşsa A dünyaya bağlıdır.</summary>
    public string B { get; set; } = "";
    /// <summary>Eklemin dünyadaki konumu ve yönü: üç eksen sütunu ve merkez (12 sayı).</summary>
    public float[] Transform { get; set; } = System.Array.Empty<float>();
    public bool Motor { get; set; }
}

/// <summary>Godot tiplerini JSON'a uygun sayı dizilerine ve geri çevirir.</summary>
public static class SaveConvert
{
    public static float[] From(Vector3 v) => new[] { v.X, v.Y, v.Z };
    public static float[] From(Quaternion q) => new[] { q.X, q.Y, q.Z, q.W };

    public static float[] From(Transform3D t) => new[]
    {
        t.Basis.Column0.X, t.Basis.Column0.Y, t.Basis.Column0.Z,
        t.Basis.Column1.X, t.Basis.Column1.Y, t.Basis.Column1.Z,
        t.Basis.Column2.X, t.Basis.Column2.Y, t.Basis.Column2.Z,
        t.Origin.X, t.Origin.Y, t.Origin.Z,
    };

    public static Vector3 ToVector3(float[] a)
        => a is { Length: >= 3 } ? new Vector3(a[0], a[1], a[2]) : Vector3.Zero;

    public static Quaternion ToQuaternion(float[] a)
        => a is { Length: >= 4 } ? new Quaternion(a[0], a[1], a[2], a[3]).Normalized() : Quaternion.Identity;

    public static Transform3D ToTransform(float[] a)
    {
        if (a is not { Length: >= 12 }) return Transform3D.Identity;

        var basis = new Basis(
            new Vector3(a[0], a[1], a[2]),
            new Vector3(a[3], a[4], a[5]),
            new Vector3(a[6], a[7], a[8]));
        return new Transform3D(basis, new Vector3(a[9], a[10], a[11]));
    }
}
