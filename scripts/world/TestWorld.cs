using Godot;

namespace ProjectXs.World;

/// <summary>
/// Hareketi test etmek için basit bir harita: damalı zemin, rampa ve zıplama blokları.
/// Damalı desen hız hissini görmek için; her kare 1 metre.
/// İleride voxel arazi bunun yerini alacak.
/// </summary>
public static class TestWorld
{
    public static Node3D Build()
    {
        var root = new Node3D { Name = "World" };

        root.AddChild(BuildEnvironment());
        root.AddChild(new DirectionalLight3D
        {
            Name = "Sun",
            RotationDegrees = new Vector3(-50, 30, 0),
            ShadowEnabled = true,
        });

        var blockMat = CheckerMaterial(new Color(0.90f, 0.58f, 0.32f), new Color(0.78f, 0.47f, 0.25f));
        var wallMat = CheckerMaterial(new Color(0.40f, 0.55f, 0.75f), new Color(0.33f, 0.47f, 0.66f));

        // Zemin artık voxel dünya (VoxelWorld). Doğma noktasının çevresi düz ve yüzeyi y = 0'da,
        // bu yüzden aşağıdaki test yapıları eskisi gibi yerde duruyor.

        // Rampalar kama şeklinde: alt ucu zeminle aynı hizada başlar, böylece araçlar kenara takılmaz.
        // Hepsi -Z yönüne (oyuncunun doğduğu yerden ileriye) doğru yükselir; inişler ters yöne bakar.
        AddWedge(root, "Ramp", new Vector3(12, 0, -12), length: 10, height: 3, width: 6, blockMat, climbsForward: true);
        AddWedge(root, "Landing", new Vector3(12, 0, -32), length: 12, height: 3, width: 8, blockMat, climbsForward: false);
        AddWedge(root, "SteepRamp", new Vector3(22, 0, -10), length: 6, height: 3, width: 5, blockMat, climbsForward: true);
        AddWedge(root, "Kicker", new Vector3(-4, 0, -12), length: 4, height: 0.8f, width: 4, blockMat, climbsForward: true);

        // Zıplama blokları: 0.8 m yükseklik, normal zıplamayla çıkılabilir
        for (int i = 0; i < 5; i++)
            AddBox(root, $"Hop{i}", new Vector3(-12, 0.4f + i * 0.5f, -4 - i * 3.5f), new Vector3(2, 0.8f + i, 2), blockMat);

        // Ölçek için büyük bir kule
        AddBox(root, "Tower", new Vector3(0, 4, -25), new Vector3(6, 8, 6), wallMat);

        return root;
    }

    private static WorldEnvironment BuildEnvironment()
    {
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial() },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
        };

        return new WorldEnvironment { Name = "Environment", Environment = environment };
    }

    private static void AddBox(Node3D parent, string name, Vector3 position, Vector3 size, Material material, Vector3? rotationDegrees = null)
    {
        var body = new StaticBody3D
        {
            Name = name,
            Position = position,
            RotationDegrees = rotationDegrees ?? Vector3.Zero,
        };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material });
        parent.AddChild(body);
    }

    /// <summary>
    /// Zemine oturan kama (üçgen prizma) rampa ekler. position, rampanın tabanının ortası.
    /// climbsForward true ise -Z yönüne doğru yükselir, false ise +Z yönüne doğru.
    /// </summary>
    private static void AddWedge(Node3D parent, string name, Vector3 position, float length, float height, float width,
        Material material, bool climbsForward)
    {
        // PrismMesh, LeftToRight = 0 iken dik kenarı -X tarafında olan bir dik üçgen prizma üretir;
        // yani -X yönüne doğru yükselir. Bunu Y ekseni etrafında döndürerek istenen yöne çeviriyoruz.
        var mesh = new PrismMesh { LeftToRight = 0.0f, Size = new Vector3(length, height, width) };

        var body = new StaticBody3D
        {
            Name = name,
            Position = position + new Vector3(0, height * 0.5f, 0),
            RotationDegrees = new Vector3(0, climbsForward ? -90 : 90, 0),
        };
        body.AddChild(new CollisionShape3D { Shape = mesh.CreateConvexShape() });
        body.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = material });
        parent.AddChild(body);
    }

    private static StandardMaterial3D CheckerMaterial(Color a, Color b)
    {
        var image = Image.CreateEmpty(2, 2, false, Image.Format.Rgb8);
        image.SetPixel(0, 0, a);
        image.SetPixel(1, 1, a);
        image.SetPixel(1, 0, b);
        image.SetPixel(0, 1, b);

        return new StandardMaterial3D
        {
            AlbedoTexture = ImageTexture.CreateFromImage(image),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            Uv1Triplanar = true,
            Uv1WorldTriplanar = true,
            Uv1Scale = new Vector3(0.5f, 0.5f, 0.5f),
            Roughness = 0.9f,
        };
    }
}
