// Copyright (c) 2026 Takashi Yoshinaga
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Collections.Generic;
using Meta.XR;
using Meta.XR.BuildingBlocks.AIBlocks;
using SpatialSearch;
using UnityEngine;

// 画像を取得するクラス。
// Capture を呼んだ瞬間のパススルーカメラ画像と深度の点群を DataSource へ流す。
//
// 点群は「深度の各点をカメラ画像のどこに写るか」まで計算しておく（ImageUvs）。
// サーバーからマスクが返ってきたら、そのUVでマスクを引くだけで3D上の位置が分かる。
// 同じUVでカメラ画像を引けば、その点の色も分かる（物体に画像を貼るのに使う）。
public class SearchCapture : MonoBehaviour
{
    static readonly int EnvironmentDepthZBufferParamsId = Shader.PropertyToID("_EnvironmentDepthZBufferParams");

    [SerializeField] PassthroughCameraAccess _cameraAccess;
    [SerializeField] DepthTextureAccess _depthAccess;

    [Header("Depth")]
    [SerializeField, Min(0f)] float _minDepthMeters = 0.1f;
    [SerializeField, Min(0.01f)] float _maxDepthMeters = 5f;

    SpatialSearchDataSource _dataSource;

    // 直近の深度フレーム。コールバックで届いた値をボタンが押されるまで控えておく。
    float[] _depth;
    Matrix4x4 _depthViewProjection;
    Pose _depthPose;
    bool _hasDepth;

    public void Initialize(SpatialSearchDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    void OnEnable()
    {
        if (_depthAccess != null) _depthAccess.OnDepthTextureUpdateCPU += OnDepthUpdated;
    }

    void OnDisable()
    {
        if (_depthAccess != null) _depthAccess.OnDepthTextureUpdateCPU -= OnDepthUpdated;
    }

    void Update()
    {
        if (_cameraAccess == null || _depthAccess == null || !_cameraAccess.IsPlaying) return;

        // 深度は要求しないと更新されない。毎フレーム最新を取っておく。
        _depthAccess.RequestDepthSample();
    }

    // 今のカメラ画像と深度を撮影して DataSource へ流す。まだ撮影できない状態なら false。
    public bool Capture(string query)
    {
        if (!CanCapture()) return false;

        var capture = new CaptureData
        {
            Query = query,
            Image = CaptureCameraImage(),
        };
        BuildPoints(capture);

        Debug.Log($"[SearchCapture] query={capture.Query} image={capture.Image.width}x{capture.Image.height} points={capture.Points.Length}");
        _dataSource.SetCapture(capture);
        return true;
    }

    // 今のカメラ画像と深度を撮影し、target（ワールド座標）に写っている物体1つを探すよう DataSource へ流す。
    // target が今のカメラ画像に写っていない、またはまだ撮影できない状態なら false。
    // label は結果の表示に使う名前。
    public bool CaptureAt(Vector3 target, string label)
    {
        if (!CanCapture()) return false;
        if (!TryProjectToImage(target, out var pointUv) || !IsInImage(pointUv, 0f))
        {
            Debug.LogWarning("[SearchCapture] 指した点がカメラ画像に写っていません");
            return false;
        }

        var capture = new CaptureData
        {
            Query = label,
            HasPoint = true,
            PointUv = pointUv,
            Image = CaptureCameraImage(),
        };
        BuildPoints(capture);

        Debug.Log($"[SearchCapture] point uv={capture.PointUv} image={capture.Image.width}x{capture.Image.height} points={capture.Points.Length}");
        _dataSource.SetCapture(capture);
        return true;
    }

    bool CanCapture()
    {
        if (_dataSource == null || _cameraAccess == null || !_cameraAccess.IsPlaying) return false;
        if (!_hasDepth)
        {
            Debug.LogWarning("[SearchCapture] 深度がまだ取れていません");
            return false;
        }
        return true;
    }

    // ワールド座標が、今のカメラ画像のどこに写るか（UV、0〜1、左下原点）。カメラの後ろなら false。
    // 画像の外に写る点は、UVが0〜1の外の値になる（IsInImage で確かめる）。
    public bool TryProjectToImage(Vector3 world, out Vector2 uv)
    {
        if (_cameraAccess == null || !_cameraAccess.IsPlaying)
        {
            uv = Vector2.zero;
            return false;
        }
        return CurrentProjection().TryProject(world, out uv);
    }

    // UVが画像の中にあるか。margin だけ内側（負なら外側）まで広げて判定する。
    public static bool IsInImage(Vector2 uv, float margin)
    {
        return uv.x >= margin && uv.x <= 1f - margin && uv.y >= margin && uv.y <= 1f - margin;
    }

    // 最新の深度に向けてレイを飛ばし、最初に当たった位置を返す。
    // onEdge : 当たった位置のまわりで奥行きが大きく飛んでいる（物体の縁や穴）。そこを指すと狙った物体が取れにくい。
    public bool TryRaycastDepth(Ray ray, out Vector3 hit, out bool onEdge)
    {
        hit = Vector3.zero;
        onEdge = false;
        if (!_hasDepth) return false;

        var zBufferParams = Shader.GetGlobalVector(EnvironmentDepthZBufferParamsId);
        // BuildPoints の逆。BuildPoints では depthLocalToWorld が打ち消し合い、ビュー射影行列がそのままワールド → クリップになる。
        var worldToClip = _depthViewProjection;
        var size = _depthAccess.TextureSize;

        // 少しずつ進めて、レイ上の点が深度の面より奥に入った最初の位置を探す。
        // 進める幅は距離に比例させる（遠いほど深度の1画素も大きいので、細かく進めても意味がない）。
        var previous = _minDepthMeters;
        for (var distance = _minDepthMeters; distance <= _maxDepthMeters; distance += Mathf.Max(RaycastMinStep, distance * RaycastStepRatio))
        {
            if (!SampleRay(ray.GetPoint(distance), worldToClip, zBufferParams, size, out var pointDepth, out var surfaceDepth, out _, out _)) continue;
            if (pointDepth < surfaceDepth)
            {
                previous = distance;
                continue;
            }

            // 前の位置との間を二分して、面に当たる位置を詰める。
            var near = previous;
            var far = distance;
            for (var i = 0; i < 6; i++)
            {
                var middle = (near + far) * 0.5f;
                if (SampleRay(ray.GetPoint(middle), worldToClip, zBufferParams, size, out pointDepth, out surfaceDepth, out _, out _) && pointDepth >= surfaceDepth)
                {
                    far = middle;
                }
                else
                {
                    near = middle;
                }
            }

            hit = ray.GetPoint(far);
            if (SampleRay(hit, worldToClip, zBufferParams, size, out _, out _, out var x, out var y))
            {
                onEdge = IsDepthEdge(x, y, size);
            }
            return true;
        }
        return false;
    }

    // レイを進めるときの幅。距離の RaycastStepRatio 倍（最小 RaycastMinStep m）。
    const float RaycastStepRatio = 0.01f;
    const float RaycastMinStep = 0.005f;

    // 縁と見なす、まわりの奥行きの差（その位置の深度に対する割合）と、調べる範囲（画素）。
    const float EdgeDepthRatio = 0.08f;
    const int EdgeRadius = 3;

    // ワールド座標を深度画像へ投影し、その点の奥行き（pointDepth）と、そこに写っている面の奥行き（surfaceDepth）を返す。
    // 深度画像の外、または面の奥行きが取れていない画素なら false。
    bool SampleRay(Vector3 world, Matrix4x4 worldToClip, Vector4 zBufferParams, int size,
        out float pointDepth, out float surfaceDepth, out int x, out int y)
    {
        pointDepth = surfaceDepth = 0f;
        x = y = 0;

        var clip = worldToClip * new Vector4(world.x, world.y, world.z, 1f);
        if (clip.w <= 1e-5f) return false;

        var ndc = (Vector3)clip / clip.w;
        if (Mathf.Abs(ndc.x) >= 1f || Mathf.Abs(ndc.y) >= 1f) return false;

        // BuildPoints の逆。画素の中心が (x + 0.5) / size * 2 - 1、zは zBufferParams.x / depth - zBufferParams.y。
        x = Mathf.Clamp((int)((ndc.x * 0.5f + 0.5f) * size), 0, size - 1);
        y = Mathf.Clamp((int)((ndc.y * 0.5f + 0.5f) * size), 0, size - 1);
        surfaceDepth = _depth[y * size + x];
        if (!(surfaceDepth >= _minDepthMeters && surfaceDepth <= _maxDepthMeters)) return false; // NaNもここで弾く

        pointDepth = zBufferParams.x / (ndc.z + zBufferParams.y);
        return true;
    }

    // 深度画像の (x, y) のまわりで、奥行きが大きく飛んでいるか（取れていない画素が多い場合も縁とみなす）。
    bool IsDepthEdge(int x, int y, int size)
    {
        var center = _depth[y * size + x];
        var minDepth = center;
        var maxDepth = center;
        var missing = 0;
        for (var dy = -EdgeRadius; dy <= EdgeRadius; dy++)
        {
            for (var dx = -EdgeRadius; dx <= EdgeRadius; dx++)
            {
                var sx = x + dx;
                var sy = y + dy;
                if (sx < 0 || sy < 0 || sx >= size || sy >= size) continue;

                var depth = _depth[sy * size + sx];
                if (!(depth >= _minDepthMeters && depth <= _maxDepthMeters))
                {
                    missing++;
                    continue;
                }
                minDepth = Mathf.Min(minDepth, depth);
                maxDepth = Mathf.Max(maxDepth, depth);
            }
        }

        var window = (EdgeRadius * 2 + 1) * (EdgeRadius * 2 + 1);
        return maxDepth - minDepth > center * EdgeDepthRatio || missing > window / 4;
    }

    // カメラのテクスチャ（GPU上）をCPUから読めるTexture2Dへ写す。
    // 撮影ごとに新しく作る。結果の物体に貼って使い続けるので、次の撮影で使い回して上書きしない。
    Texture2D CaptureCameraImage()
    {
        var source = _cameraAccess.GetTexture();
        var size = _cameraAccess.CurrentResolution;

        // 小さく見えたり遠くに置いたりしたときにちらつかないよう、ミップマップを作って補間する。
        var image = new Texture2D(size.x, size.y, TextureFormat.RGB24, true)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Trilinear,
        };

        var renderTexture = RenderTexture.GetTemporary(size.x, size.y, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var previous = RenderTexture.active;
        Graphics.Blit(source, renderTexture);
        RenderTexture.active = renderTexture;
        image.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
        image.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(renderTexture);

        return image;
    }

    // 深度の各点をワールド座標へ戻し、カメラ画像上のUVも求める。
    // 計算式は Assets/Shaders/ColoredDepthPointCloudCpuURP.shader と同じ。
    void BuildPoints(CaptureData capture)
    {
        var projection = CurrentProjection();

        // --- 深度からワールド座標への復元に使う値 ---
        var zBufferParams = Shader.GetGlobalVector(EnvironmentDepthZBufferParamsId);
        var depthLocalToWorld = Matrix4x4.TRS(_depthPose.position, _depthPose.rotation, Vector3.one);
        var clipToDepthLocal = (_depthViewProjection * depthLocalToWorld).inverse;

        // 深度1画素の幅は深度に比例するので、1m先での右・上への幅を求めておく。
        var textureSize = _depthAccess.TextureSize;
        PixelStepAtOneMeter(clipToDepthLocal, depthLocalToWorld, zBufferParams, textureSize, out var pixelRight, out var pixelUp);

        // 深度の各画素をワールド座標へ戻す。面の向きを求めるとき隣の画素の位置を使うので、先に全部戻しておく。
        var worlds = new Vector3[_depth.Length];
        var hasWorld = new bool[_depth.Length];
        for (var y = 0; y < textureSize; y++)
        {
            for (var x = 0; x < textureSize; x++)
            {
                var index = y * textureSize + x;
                var depth = _depth[index];
                if (!(depth >= _minDepthMeters && depth <= _maxDepthMeters)) continue; // NaNもここで弾く

                // 深度テクスチャ上の位置と深度値 → ワールド座標
                var clip = new Vector4(
                    (x + 0.5f) / textureSize * 2f - 1f,
                    (y + 0.5f) / textureSize * 2f - 1f,
                    zBufferParams.x / depth - zBufferParams.y,
                    1f);
                var local = clipToDepthLocal * clip;
                if (Mathf.Abs(local.w) < 1e-5f) continue;

                worlds[index] = depthLocalToWorld.MultiplyPoint3x4((Vector3)local / local.w);
                hasWorld[index] = true;
            }
        }

        // カメラ画像に写っている画素を点にする。
        var pixelWidthAtOneMeter = Mathf.Max(pixelRight.magnitude, pixelUp.magnitude);
        var points = new List<Vector3>(_depth.Length);
        var uvs = new List<Vector2>(_depth.Length);
        var depths = new List<float>(_depth.Length);
        var normals = new List<Vector3>(_depth.Length);
        for (var y = 0; y < textureSize; y++)
        {
            for (var x = 0; x < textureSize; x++)
            {
                var index = y * textureSize + x;
                if (!hasWorld[index]) continue;

                // ワールド座標 → カメラ画像上のUV
                var world = worlds[index];
                if (!projection.TryProject(world, out var uv)) continue;
                if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f) continue; // 画像に写っていない点

                var depth = _depth[index];
                points.Add(world);
                uvs.Add(uv);
                depths.Add(depth);
                normals.Add(SurfaceNormal(worlds, hasWorld, textureSize, x, y,
                    NormalStep * depth * pixelWidthAtOneMeter * NormalMaxEdgeRatio, projection.CameraPosition - world));
            }
        }

        capture.Points = points.ToArray();
        capture.ImageUvs = uvs.ToArray();
        capture.Depths = depths.ToArray();
        capture.Normals = normals.ToArray();
        capture.PixelRightPerMeter = pixelRight;
        capture.PixelUpPerMeter = pixelUp;
        capture.Projection = projection;
    }

    // 今のカメラの位置・向きで、ワールド座標をカメラ画像へ投影するもの。
    ImageProjection CurrentProjection()
    {
        // CurrentResolution はセンサー画像の中央を切り抜いたものなので、その切り抜き範囲を求める。
        var intrinsics = _cameraAccess.Intrinsics;
        var sensor = (Vector2)intrinsics.SensorResolution;
        var image = (Vector2)_cameraAccess.CurrentResolution;
        var scale = new Vector2(image.x / sensor.x, image.y / sensor.y);
        scale /= Mathf.Max(scale.x, scale.y);
        var cropSize = Vector2.Scale(sensor, scale);
        var cropOrigin = (sensor - cropSize) * 0.5f;

        var cameraPose = _cameraAccess.GetCameraPose();
        var worldToCamera = Matrix4x4.TRS(cameraPose.position, cameraPose.rotation, Vector3.one).inverse;
        return new ImageProjection(worldToCamera, intrinsics.FocalLength, intrinsics.PrincipalPoint, cropOrigin, cropSize);
    }

    // 面の向き（法線）を求めるときに、上下左右へ何画素離れた画素と比べるか。
    // 深度にはノイズがあるので、隣の画素どうしだと向きがばらつく。少し離れた画素で差を取って落ち着かせる。
    const int NormalStep = 3;

    // 比べる画素までの距離の上限（画素の間隔の何倍まで）。物体の縁のように奥行きが大きく違う画素は、同じ面ではないので使わない。
    const float NormalMaxEdgeRatio = 4f;

    // 点での面の向き（長さ1）。上下左右の画素の位置の差から求め、toCamera の側（撮影したカメラの側）を向ける。
    // 画像の端や、物体の縁で比べる画素が使えないときは Vector3.zero。
    // maxDistance : 比べる画素までの距離の上限（m）
    static Vector3 SurfaceNormal(Vector3[] worlds, bool[] hasWorld, int size, int x, int y, float maxDistance, Vector3 toCamera)
    {
        if (x < NormalStep || y < NormalStep || x + NormalStep >= size || y + NormalStep >= size) return Vector3.zero;

        var center = y * size + x;
        var left = center - NormalStep;
        var right = center + NormalStep;
        var down = center - NormalStep * size;
        var up = center + NormalStep * size;
        if (!(hasWorld[left] && hasWorld[right] && hasWorld[down] && hasWorld[up])) return Vector3.zero;

        var origin = worlds[center];
        var maxSqr = maxDistance * maxDistance;
        if ((worlds[left] - origin).sqrMagnitude > maxSqr || (worlds[right] - origin).sqrMagnitude > maxSqr ||
            (worlds[down] - origin).sqrMagnitude > maxSqr || (worlds[up] - origin).sqrMagnitude > maxSqr)
        {
            return Vector3.zero;
        }

        var normal = Vector3.Cross(worlds[right] - worlds[left], worlds[up] - worlds[down]);
        if (normal.sqrMagnitude < 1e-12f) return Vector3.zero;

        normal.Normalize();
        return Vector3.Dot(normal, toCamera) < 0f ? -normal : normal;
    }

    // 深度画像の1画素が、1m先でワールドの右・上へどれだけの幅になるか（ベクトル）。
    // 同じ奥行きなら画素の位置に関係なく同じで、奥行きに比例して大きくなる。
    static void PixelStepAtOneMeter(Matrix4x4 clipToDepthLocal, Matrix4x4 depthLocalToWorld, Vector4 zBufferParams, int textureSize,
        out Vector3 right, out Vector3 up)
    {
        var clipZ = zBufferParams.x - zBufferParams.y; // 深度1mのときのクリップ座標のz
        var step = 2f / textureSize;
        var center = Unproject(clipToDepthLocal, new Vector4(0f, 0f, clipZ, 1f));
        right = depthLocalToWorld.MultiplyVector(Unproject(clipToDepthLocal, new Vector4(step, 0f, clipZ, 1f)) - center);
        up = depthLocalToWorld.MultiplyVector(Unproject(clipToDepthLocal, new Vector4(0f, step, clipZ, 1f)) - center);
    }

    static Vector3 Unproject(Matrix4x4 clipToLocal, Vector4 clip)
    {
        var local = clipToLocal * clip;
        return (Vector3)local / local.w;
    }

    void OnDepthUpdated(DepthTextureAccess.DepthFrameData frame)
    {
        if (frame.ViewProjectionMatrix == null || frame.ViewProjectionMatrix.Length == 0) return;

        var pointCount = _depthAccess.TextureSize * _depthAccess.TextureSize;
        if (!frame.DepthTexturePixels.IsCreated || frame.DepthTexturePixels.Length < pointCount * 2) return;

        // 深度は左目・右目の順に詰まっている。カメラと同じ側の目を使う。
        var eye = _cameraAccess.CameraPosition == PassthroughCameraAccess.CameraPositionType.Right ? 1 : 0;
        eye = Mathf.Min(eye, frame.ViewProjectionMatrix.Length - 1);

        if (_depth == null || _depth.Length != pointCount) _depth = new float[pointCount];
        frame.DepthTexturePixels.GetSubArray(eye * pointCount, pointCount).CopyTo(_depth);

        _depthViewProjection = frame.ViewProjectionMatrix[eye];
        _depthPose = frame.CameraPose;
        _hasDepth = true;
    }
}
