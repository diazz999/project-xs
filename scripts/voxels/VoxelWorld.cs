using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using ProjectXs.Core;
using ProjectXs.Player;

namespace ProjectXs.Voxels;

/// <summary>Dünyayı üreten bütün değerler. Host bunları katılan oyunculara gönderir, onlar da aynı dünyayı üretir.</summary>
public readonly record struct WorldParams(
    int Seed, int ChunksX, int ChunksY, int ChunksZ, int MinY,
    float FlatRadius, float BlendWidth, float HillHeight, float NoiseFrequency)
{
    public int[] Ints => new[] { Seed, ChunksX, ChunksY, ChunksZ, MinY };
    public float[] Floats => new[] { FlatRadius, BlendWidth, HillHeight, NoiseFrequency };

    public static WorldParams From(int[] ints, float[] floats)
        => new(ints[0], ints[1], ints[2], ints[3], ints[4], floats[0], floats[1], floats[2], floats[3]);
}

/// <summary>
/// Minecraft tarzı blok dünya.
///
/// Dünya 16x16x16 blokluk chunk'lara bölünür; her chunk görünen yüzlerinden bir mesh ve bir çarpışma şekli üretir.
/// Bloklar arasında kalan, görünmeyen yüzler hiç çizilmez.
///
/// Ağ modeli: dünya bir tohumdan (seed) üretildiği için her bilgisayar aynı dünyayı kendisi üretir; ağ üzerinden
/// sadece değişiklikler (kırılan ve konan bloklar) gider. Değişiklikleri sunucu onaylar ve herkese yayar,
/// sonradan katılan oyuncuya da o ana kadarki bütün değişiklikleri tek seferde gönderir.
/// </summary>
public partial class VoxelWorld : Node3D
{
    public const int ChunkSize = 16;
    private const string SettingsPath = "res://data/voxel_world_default.tres";

    public static VoxelWorld Instance { get; private set; }

    /// <summary>Blok koyarken oyuncuların içine blok konmasın diye kontrol edilir.</summary>
    public Node3D PlayersRoot { get; set; }

    private sealed class ChunkNode
    {
        public StaticBody3D Body;
        public MeshInstance3D Mesh;
        public CollisionShape3D Shape;
    }

    private sealed class ChunkArrays
    {
        public readonly List<Vector3> Vertices = new();
        public readonly List<Vector3> Normals = new();
        public readonly List<Color> Colors = new();
        public readonly List<int> Indices = new();
        public readonly List<Vector3> CollisionFaces = new();
        public bool IsEmpty => Vertices.Count == 0;
    }

    // Altı yön: +X, -X, +Y, -Y, +Z, -Z
    private const int TopFace = 2;
    private static readonly Vector3I[] FaceNormals =
    {
        new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1),
    };

    private static readonly Vector3[][] FaceCorners = BuildFaceCorners();

    private VoxelWorldSettings _settings;
    private WorldParams _params;
    private byte[] _blocks = Array.Empty<byte>();
    private int _sizeX, _sizeY, _sizeZ;    // Blok cinsinden boyut
    private int _chunksX, _chunksY, _chunksZ;
    private Vector3I _origin;               // (0,0,0) bloğunun dünyadaki köşesi

    private readonly Dictionary<Vector3I, ChunkNode> _chunks = new();
    private readonly HashSet<Vector3I> _dirtyChunks = new();
    private readonly Dictionary<int, byte> _modifications = new(); // Blok indeksi -> yeni tür
    private StandardMaterial3D _material;

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    public override void _Ready()
    {
        _settings = SettingsLoader.Load<VoxelWorldSettings>(SettingsPath);
        _material = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            VertexColorIsSrgb = true,
            Roughness = 1.0f,
        };

        Generate(_settings.ToParams());
        Multiplayer.PeerConnected += OnPeerConnected;
    }

    // Değişen chunk'ları kare sonunda toplu olarak yeniden çiziyoruz; bir patlama 30 blok kırsa bile her chunk bir kez çizilir.
    public override void _Process(double delta)
    {
        if (_dirtyChunks.Count == 0) return;

        foreach (var chunk in _dirtyChunks)
            ApplyChunk(chunk, BuildChunkArrays(chunk));
        _dirtyChunks.Clear();
    }

    // --- Sorgular ---

    public bool InBounds(Vector3I block)
        => block.X >= 0 && block.Y >= 0 && block.Z >= 0 && block.X < _sizeX && block.Y < _sizeY && block.Z < _sizeZ;

    /// <summary>Dünyadaki bir noktanın hangi bloğun içinde olduğunu bulur.</summary>
    public bool TryWorldToBlock(Vector3 position, out Vector3I block)
    {
        block = new Vector3I(
            Mathf.FloorToInt(position.X) - _origin.X,
            Mathf.FloorToInt(position.Y) - _origin.Y,
            Mathf.FloorToInt(position.Z) - _origin.Z);
        return InBounds(block);
    }

    public BlockType GetBlock(Vector3I block)
        => InBounds(block) ? (BlockType)_blocks[Index(block.X, block.Y, block.Z)] : BlockType.Air;

    public Vector3 BlockCenter(Vector3I block)
        => new(block.X + _origin.X + 0.5f, block.Y + _origin.Y + 0.5f, block.Z + _origin.Z + 0.5f);

    /// <summary>Bir ışının çarptığı şey voxel dünyanın bir parçası mı?</summary>
    public static bool IsVoxelBody(GodotObject collider)
        => collider is StaticBody3D body && body.HasMeta("voxel");

    // --- Değişiklik istekleri ---

    public void RequestSetBlock(Vector3I block, BlockType type)
        => RpcId(1, MethodName.ServerSetBlock, block.X, block.Y, block.Z, (int)type);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ServerSetBlock(int x, int y, int z, int type)
    {
        if (!Multiplayer.IsServer() || !BlockCatalog.IsValid(type)) return;

        var block = new Vector3I(x, y, z);
        if (!InBounds(block)) return;

        var newType = (BlockType)type;
        var current = GetBlock(block);

        if (newType == BlockType.Air)
        {
            if (current == BlockType.Air || !BlockCatalog.Get(current).Breakable) return;
        }
        else
        {
            if (current != BlockType.Air || !BlockCatalog.Get(newType).Placeable || OverlapsPlayer(block)) return;
        }

        Broadcast(new List<(Vector3I, BlockType)> { (block, newType) });
    }

    /// <summary>Sunucuda, bir patlamanın yarıçapındaki kırılabilir blokları yok eder.</summary>
    public void ServerExplode(Vector3 center, float radius)
    {
        if (!Multiplayer.IsServer() || radius <= 0) return;

        var edits = new List<(Vector3I, BlockType)>();
        int r = Mathf.CeilToInt(radius);
        TryWorldToBlock(center, out var middle);

        for (int y = middle.Y - r; y <= middle.Y + r; y++)
        for (int z = middle.Z - r; z <= middle.Z + r; z++)
        for (int x = middle.X - r; x <= middle.X + r; x++)
        {
            var block = new Vector3I(x, y, z);
            if (!InBounds(block) || BlockCenter(block).DistanceTo(center) > radius) continue;

            var type = GetBlock(block);
            if (type != BlockType.Air && BlockCatalog.Get(type).Breakable)
                edits.Add((block, BlockType.Air));
        }

        if (edits.Count > 0)
            Broadcast(edits);
    }

    /// <summary>Oturum bitince dünyayı ilk haline döndürür.</summary>
    public void ResetWorld()
    {
        var parameters = _settings.ToParams();
        if (parameters != _params || _modifications.Count > 0)
            Generate(parameters);
    }

    private void Broadcast(List<(Vector3I Block, BlockType Type)> edits)
        => Rpc(MethodName.ClientApplyEdits, Pack(edits));

    private bool OverlapsPlayer(Vector3I block)
    {
        if (PlayersRoot == null) return false;

        var blockBox = new Aabb(BlockCenter(block) - Vector3.One * 0.5f, Vector3.One);
        foreach (Node node in PlayersRoot.GetChildren())
        {
            if (node is not PlayerController player || player.Dead || player.IsSeated) continue;

            var playerBox = new Aabb(player.GlobalPosition + new Vector3(-0.4f, 0, -0.4f), new Vector3(0.8f, 1.8f, 0.8f));
            if (blockBox.Intersects(playerBox))
                return true;
        }
        return false;
    }

    // --- Herkeste çalışan RPC'ler ---

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ClientApplyEdits(int[] edits)
    {
        for (int i = 0; i + 3 < edits.Length; i += 4)
        {
            var block = new Vector3I(edits[i], edits[i + 1], edits[i + 2]);
            if (InBounds(block) && BlockCatalog.IsValid(edits[i + 3]))
                SetBlockInternal(block, (BlockType)edits[i + 3]);
        }
    }

    /// <summary>Sunucu, yeni katılan oyuncuya dünyanın ayarlarını ve o ana kadarki bütün değişiklikleri gönderir.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ClientSyncWorld(int[] ints, float[] floats, int[] edits)
    {
        var parameters = WorldParams.From(ints, floats);
        if (parameters != _params || _modifications.Count > 0)
            Generate(parameters);

        ClientApplyEdits(edits);
    }

    private void OnPeerConnected(long id)
    {
        if (!Multiplayer.IsServer()) return;

        var edits = new List<(Vector3I, BlockType)>(_modifications.Count);
        foreach (var (index, type) in _modifications)
            edits.Add((BlockFromIndex(index), (BlockType)type));

        RpcId(id, MethodName.ClientSyncWorld, _params.Ints, _params.Floats, Pack(edits));
    }

    private static int[] Pack(List<(Vector3I Block, BlockType Type)> edits)
    {
        var packed = new int[edits.Count * 4];
        for (int i = 0; i < edits.Count; i++)
        {
            packed[i * 4] = edits[i].Block.X;
            packed[i * 4 + 1] = edits[i].Block.Y;
            packed[i * 4 + 2] = edits[i].Block.Z;
            packed[i * 4 + 3] = (int)edits[i].Type;
        }
        return packed;
    }

    private void SetBlockInternal(Vector3I block, BlockType type)
    {
        int index = Index(block.X, block.Y, block.Z);
        _blocks[index] = (byte)type;
        _modifications[index] = (byte)type;

        // Bloğun chunk'ı ve blok chunk sınırındaysa komşu chunk da yeniden çizilmeli (aradaki yüz değişti)
        var chunk = new Vector3I(block.X / ChunkSize, block.Y / ChunkSize, block.Z / ChunkSize);
        _dirtyChunks.Add(chunk);

        var local = new Vector3I(block.X % ChunkSize, block.Y % ChunkSize, block.Z % ChunkSize);
        if (local.X == 0) MarkDirty(chunk + Vector3I.Left);
        if (local.X == ChunkSize - 1) MarkDirty(chunk + Vector3I.Right);
        if (local.Y == 0) MarkDirty(chunk + Vector3I.Down);
        if (local.Y == ChunkSize - 1) MarkDirty(chunk + Vector3I.Up);
        if (local.Z == 0) MarkDirty(chunk + Vector3I.Forward);
        if (local.Z == ChunkSize - 1) MarkDirty(chunk + Vector3I.Back);
    }

    private void MarkDirty(Vector3I chunk)
    {
        if (chunk.X >= 0 && chunk.Y >= 0 && chunk.Z >= 0 && chunk.X < _chunksX && chunk.Y < _chunksY && chunk.Z < _chunksZ)
            _dirtyChunks.Add(chunk);
    }

    // --- Üretim ---

    private void Generate(WorldParams parameters)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();

        foreach (var chunk in _chunks.Values)
            chunk.Body.QueueFree();
        _chunks.Clear();
        _dirtyChunks.Clear();
        _modifications.Clear();

        _params = parameters;
        _chunksX = Mathf.Max(parameters.ChunksX, 1);
        _chunksY = Mathf.Max(parameters.ChunksY, 1);
        _chunksZ = Mathf.Max(parameters.ChunksZ, 1);
        _sizeX = _chunksX * ChunkSize;
        _sizeY = _chunksY * ChunkSize;
        _sizeZ = _chunksZ * ChunkSize;
        _origin = new Vector3I(-_sizeX / 2, parameters.MinY, -_sizeZ / 2);
        _blocks = new byte[_sizeX * _sizeY * _sizeZ];

        // Yükseklik haritası: ortada düz bir alan, dışarı doğru gürültüyle tepeler ve vadiler
        var noise = new FastNoiseLite
        {
            Seed = parameters.Seed,
            NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
            Frequency = parameters.NoiseFrequency,
            FractalType = FastNoiseLite.FractalTypeEnum.Fbm,
            FractalOctaves = 4,
        };

        var heights = new int[_sizeX * _sizeZ];
        for (int z = 0; z < _sizeZ; z++)
        for (int x = 0; x < _sizeX; x++)
        {
            float worldX = x + _origin.X + 0.5f;
            float worldZ = z + _origin.Z + 0.5f;
            float distance = Mathf.Sqrt(worldX * worldX + worldZ * worldZ);
            float blend = Mathf.SmoothStep(parameters.FlatRadius, parameters.FlatRadius + parameters.BlendWidth, distance);
            float hills = noise.GetNoise2D(worldX, worldZ) * parameters.HillHeight;

            int height = Mathf.RoundToInt(Mathf.Lerp(0.0f, hills, blend));
            heights[z * _sizeX + x] = Mathf.Clamp(height, _origin.Y + 2, _origin.Y + _sizeY - 2);
        }

        // Blokları doldurmak ve chunk mesh'lerini hesaplamak saf C# işi; birden çok çekirdekte paralel yapıyoruz
        Parallel.For(0, _sizeZ, z =>
        {
            for (int x = 0; x < _sizeX; x++)
            {
                int height = heights[z * _sizeX + x];
                for (int y = 0; y < _sizeY; y++)
                    _blocks[Index(x, y, z)] = (byte)GenerateBlock(y, height);
            }
        });

        int chunkCount = _chunksX * _chunksY * _chunksZ;
        var allArrays = new ChunkArrays[chunkCount];
        Parallel.For(0, chunkCount, i => allArrays[i] = BuildChunkArrays(ChunkFromIndex(i)));

        // Godot node'ları ana iş parçacığında oluşturulmalı
        for (int i = 0; i < chunkCount; i++)
            ApplyChunk(ChunkFromIndex(i), allArrays[i]);

        GD.Print($"[Voxel] Dünya üretildi: {_sizeX}x{_sizeY}x{_sizeZ} blok, {_chunks.Count} dolu chunk, {watch.ElapsedMilliseconds} ms");
    }

    private BlockType GenerateBlock(int y, int surfaceHeight)
    {
        int worldY = y + _origin.Y;

        if (y == 0) return BlockType.Bedrock;            // En alt katman kırılamaz
        if (worldY >= surfaceHeight) return BlockType.Air;
        if (worldY == surfaceHeight - 1)                  // Yüzey: alçak yerlerde kum, diğer yerlerde çimen
            return surfaceHeight <= -3 ? BlockType.Sand : BlockType.Grass;
        if (worldY >= surfaceHeight - 4) return BlockType.Dirt;
        return BlockType.Stone;
    }

    // --- Mesh ---

    private ChunkArrays BuildChunkArrays(Vector3I chunk)
    {
        var arrays = new ChunkArrays();
        int startX = chunk.X * ChunkSize, startY = chunk.Y * ChunkSize, startZ = chunk.Z * ChunkSize;

        for (int y = 0; y < ChunkSize; y++)
        for (int z = 0; z < ChunkSize; z++)
        for (int x = 0; x < ChunkSize; x++)
        {
            int bx = startX + x, by = startY + y, bz = startZ + z;
            byte type = _blocks[Index(bx, by, bz)];
            if (type == 0) continue;

            var definition = BlockCatalog.Get((BlockType)type);
            float shade = Shade(bx, by, bz);
            var local = new Vector3(x, y, z);

            for (int face = 0; face < 6; face++)
            {
                var normal = FaceNormals[face];
                if (IsSolid(bx + normal.X, by + normal.Y, bz + normal.Z)) continue; // Görünmeyen yüz

                Color color = (face == TopFace ? definition.TopColor : definition.Color) * shade;
                color.A = 1.0f;
                AddFace(arrays, face, local, color);
            }
        }

        return arrays;
    }

    private static void AddFace(ChunkArrays arrays, int face, Vector3 position, Color color)
    {
        var corners = FaceCorners[face];
        var normal = (Vector3)FaceNormals[face];
        int start = arrays.Vertices.Count;

        for (int i = 0; i < 4; i++)
        {
            arrays.Vertices.Add(position + corners[i]);
            arrays.Normals.Add(normal);
            arrays.Colors.Add(color);
        }

        arrays.Indices.Add(start);
        arrays.Indices.Add(start + 1);
        arrays.Indices.Add(start + 2);
        arrays.Indices.Add(start);
        arrays.Indices.Add(start + 2);
        arrays.Indices.Add(start + 3);

        arrays.CollisionFaces.Add(position + corners[0]);
        arrays.CollisionFaces.Add(position + corners[1]);
        arrays.CollisionFaces.Add(position + corners[2]);
        arrays.CollisionFaces.Add(position + corners[0]);
        arrays.CollisionFaces.Add(position + corners[2]);
        arrays.CollisionFaces.Add(position + corners[3]);
    }

    private void ApplyChunk(Vector3I chunk, ChunkArrays arrays)
    {
        _chunks.TryGetValue(chunk, out var node);

        if (arrays.IsEmpty)
        {
            if (node != null)
            {
                node.Body.QueueFree();
                _chunks.Remove(chunk);
            }
            return;
        }

        if (node == null)
        {
            node = new ChunkNode
            {
                Body = new StaticBody3D
                {
                    Name = $"Chunk_{chunk.X}_{chunk.Y}_{chunk.Z}",
                    Position = new Vector3(
                        _origin.X + chunk.X * ChunkSize,
                        _origin.Y + chunk.Y * ChunkSize,
                        _origin.Z + chunk.Z * ChunkSize),
                },
                Mesh = new MeshInstance3D { MaterialOverride = _material },
                Shape = new CollisionShape3D(),
            };
            node.Body.SetMeta("voxel", true);
            node.Body.AddChild(node.Mesh);
            node.Body.AddChild(node.Shape);
            AddChild(node.Body);
            _chunks[chunk] = node;
        }

        var surface = new Godot.Collections.Array();
        surface.Resize((int)Mesh.ArrayType.Max);
        surface[(int)Mesh.ArrayType.Vertex] = arrays.Vertices.ToArray();
        surface[(int)Mesh.ArrayType.Normal] = arrays.Normals.ToArray();
        surface[(int)Mesh.ArrayType.Color] = arrays.Colors.ToArray();
        surface[(int)Mesh.ArrayType.Index] = arrays.Indices.ToArray();

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, surface);
        node.Mesh.Mesh = mesh;

        var shape = new ConcavePolygonShape3D { BackfaceCollision = true };
        shape.SetFaces(arrays.CollisionFaces.ToArray());
        node.Shape.Shape = shape;
    }

    // --- Yardımcılar ---

    private int Index(int x, int y, int z) => (y * _sizeZ + z) * _sizeX + x;

    private Vector3I BlockFromIndex(int index)
        => new(index % _sizeX, index / (_sizeX * _sizeZ), (index / _sizeX) % _sizeZ);

    private Vector3I ChunkFromIndex(int i)
        => new(i % _chunksX, (i / _chunksX) % _chunksY, i / (_chunksX * _chunksY));

    private bool IsSolid(int x, int y, int z)
    {
        if (y < 0) return true;                                       // Dünyanın altı dolu sayılır, alt yüz çizilmez
        if (y >= _sizeY || x < 0 || z < 0 || x >= _sizeX || z >= _sizeZ) return false;
        return _blocks[Index(x, y, z)] != 0;
    }

    // Her bloğa konumuna göre sabit, hafif bir parlaklık farkı: düz yüzeyler daha canlı görünür
    private static float Shade(int x, int y, int z)
    {
        uint hash = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791);
        return 0.93f + (hash % 1000) / 1000.0f * 0.1f;
    }

    // Godot'da ön yüzler saat yönünde sıralanır. Köşe listelerini yazarken hata yapmamak için
    // sırayı burada normale göre kontrol edip gerekirse çeviriyoruz.
    private static Vector3[][] BuildFaceCorners()
    {
        var corners = new[]
        {
            new Vector3[] { new(1, 0, 0), new(1, 1, 0), new(1, 1, 1), new(1, 0, 1) }, // +X
            new Vector3[] { new(0, 0, 1), new(0, 1, 1), new(0, 1, 0), new(0, 0, 0) }, // -X
            new Vector3[] { new(0, 1, 0), new(0, 1, 1), new(1, 1, 1), new(1, 1, 0) }, // +Y
            new Vector3[] { new(0, 0, 1), new(0, 0, 0), new(1, 0, 0), new(1, 0, 1) }, // -Y
            new Vector3[] { new(1, 0, 1), new(1, 1, 1), new(0, 1, 1), new(0, 0, 1) }, // +Z
            new Vector3[] { new(0, 0, 0), new(0, 1, 0), new(1, 1, 0), new(1, 0, 0) }, // -Z
        };

        for (int face = 0; face < 6; face++)
        {
            var c = corners[face];
            Vector3 normal = FaceNormals[face];
            Vector3 cross = (c[1] - c[0]).Cross(c[2] - c[0]);
            if (cross.Dot(normal) > 0)
                corners[face] = new[] { c[0], c[3], c[2], c[1] }; // Saat yönünün tersiydi, çevir
        }

        return corners;
    }
}
