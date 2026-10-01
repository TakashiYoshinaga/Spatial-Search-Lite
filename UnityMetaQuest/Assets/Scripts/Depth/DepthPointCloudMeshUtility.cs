using UnityEngine;
using UnityEngine.Rendering;

public static class DepthPointCloudMeshUtility
{
    public static Mesh BuildPointMesh(Mesh currentMesh, int textureSize)
    {
        if (currentMesh != null)
        {
            Object.Destroy(currentMesh);
        }

        var pointCount = textureSize * textureSize;
        var positions = new Vector3[pointCount];
        var uvs = new Vector2[pointCount];
        var indices = new int[pointCount];
        var inverseTextureSize = 1f / textureSize;

        var index = 0;
        for (var y = 0; y < textureSize; y++)
        {
            var v = (y + 0.5f) * inverseTextureSize;
            for (var x = 0; x < textureSize; x++)
            {
                positions[index] = Vector3.zero;
                uvs[index] = new Vector2((x + 0.5f) * inverseTextureSize, v);
                indices[index] = index;
                index++;
            }
        }

        var mesh = new Mesh
        {
            name = $"DepthPointCloud_{textureSize}"
        };
        mesh.indexFormat = IndexFormat.UInt32;
        mesh.vertices = positions;
        mesh.uv = uvs;
        mesh.SetIndices(indices, MeshTopology.Points, 0, calculateBounds: false);
        mesh.UploadMeshData(false);

        return mesh;
    }

    // One quad (4 vertices, 2 triangles) per depth pixel, for shaders that texture the footprint of each pixel.
    // Vertex index = pixel index * 4 + corner, and uv is the depth-texture uv of that corner (the pixel's edges, not its center),
    // so a shader reads the pixel's depth from (vertexID >> 2) and unprojects every corner on its own.
    public static Mesh BuildQuadMesh(Mesh currentMesh, int textureSize)
    {
        if (currentMesh != null)
        {
            Object.Destroy(currentMesh);
        }

        var pointCount = textureSize * textureSize;
        var positions = new Vector3[pointCount * 4];
        var uvs = new Vector2[pointCount * 4];
        var indices = new int[pointCount * 6];
        var inverseTextureSize = 1f / textureSize;

        var point = 0;
        for (var y = 0; y < textureSize; y++)
        {
            var v0 = y * inverseTextureSize;
            var v1 = (y + 1) * inverseTextureSize;
            for (var x = 0; x < textureSize; x++)
            {
                var u0 = x * inverseTextureSize;
                var u1 = (x + 1) * inverseTextureSize;

                var vertex = point * 4;
                uvs[vertex] = new Vector2(u0, v0);
                uvs[vertex + 1] = new Vector2(u1, v0);
                uvs[vertex + 2] = new Vector2(u0, v1);
                uvs[vertex + 3] = new Vector2(u1, v1);

                var index = point * 6;
                indices[index] = vertex;
                indices[index + 1] = vertex + 2;
                indices[index + 2] = vertex + 1;
                indices[index + 3] = vertex + 1;
                indices[index + 4] = vertex + 2;
                indices[index + 5] = vertex + 3;
                point++;
            }
        }

        var mesh = new Mesh
        {
            name = $"DepthQuadCloud_{textureSize}"
        };
        mesh.indexFormat = IndexFormat.UInt32;
        mesh.vertices = positions;
        mesh.uv = uvs;
        mesh.SetIndices(indices, MeshTopology.Triangles, 0, calculateBounds: false);
        mesh.UploadMeshData(false);

        return mesh;
    }

    public static void UpdateBounds(Mesh mesh, float maxDepthMeters)
    {
        if (mesh == null)
        {
            return;
        }

        var extent = Mathf.Max(1f, maxDepthMeters);
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * extent * 4f);
    }
}
