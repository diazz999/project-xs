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

        var floorMat = CheckerMaterial(new Color(0.56f, 0.58f, 0.62f), new Color(0.46f, 0.48f, 0.52f));
        var blockMat = CheckerMaterial(new Color(0.90f, 0.58f, 0.32f), new Color(0.78f, 0.47f, 0.25f));
        var wallMat = CheckerMaterial(new Color(0.40f, 0.55f, 0.75f), new Color(0.33f, 0.47f, 0.66f));

        AddBox(root, "Ground", new Vector3(0, -0.5f, 0), new Vector3(120, 1, 120), floorMat);

        // Rampalar: kayarak hız kazanmayı ve iniş açısını test etmek için
        AddBox(root, "Ramp", new Vector3(12, 1.6f, -10), new Vector3(5, 0.5f, 10), blockMat, new Vector3(-20, 0, 0));
        AddBox(root, "SteepRamp", new Vector3(20, 2.4f, -10), new Vector3(5, 0.5f, 10), blockMat, new Vector3(-35, 0, 0));

        // Zıplama blokları: 0.8 m yükseklik, normal zıplamayla çıkılabilir
        for (int i = 0; i < 5; i++)
            AddBox(root, $"Hop{i}", new Vector3(-12, 0.4f + i * 0.5f, -4 - i * 3.5f), new Vector3(2, 0.8f + i, 2), blockMat);

        // Ölçek için büyük bir kule ve duvarlar
        AddBox(root, "Tower", new Vector3(0, 4, -25), new Vector3(6, 8, 6), wallMat);
        AddBox(root, "WallNorth", new Vector3(0, 2, -60), new Vector3(120, 4, 1), wallMat);
        AddBox(root, "WallSouth", new Vector3(0, 2, 60), new Vector3(120, 4, 1), wallMat);
        AddBox(root, "WallEast", new Vector3(60, 2, 0), new Vector3(1, 4, 120), wallMat);
        AddBox(root, "WallWest", new Vector3(-60, 2, 0), new Vector3(1, 4, 120), wallMat);

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
