using System.Numerics;
using System.Runtime.InteropServices;

namespace DerpLib.Rendering;

/// <summary>
/// Standard 3D vertex format: position, normal, texcoord.
/// 32 bytes total, matches shader layout and CompiledMesh format.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Vertex3D
{
    public Vector3 Position;   // location 0, offset 0  (12 bytes)
    public Vector3 Normal;     // location 1, offset 12 (12 bytes)
    public Vector2 TexCoord;   // location 2, offset 24 (8 bytes)

    public Vertex3D(Vector3 position, Vector3 normal, Vector2 texCoord)
    {
        Position = position;
        Normal = normal;
        TexCoord = texCoord;
    }

    public Vertex3D(float px, float py, float pz, float nx, float ny, float nz, float u, float v)
    {
        Position = new Vector3(px, py, pz);
        Normal = new Vector3(nx, ny, nz);
        TexCoord = new Vector2(u, v);
    }

    public const int SizeInBytes = 32; // 12 + 12 + 8
}
