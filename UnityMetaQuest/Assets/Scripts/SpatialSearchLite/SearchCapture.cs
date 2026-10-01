using System.Collections.Generic;
using Meta.XR;
using Meta.XR.BuildingBlocks.AIBlocks;
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
        if (_dataSource == null || _cameraAccess == null || !_cameraAccess.IsPlaying) return false;
        if (!_hasDepth)
        {
            Debug.LogWarning("[SearchCapture] 深度がまだ取れていません");
            return false;
        }

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
        // --- カメラ画像への投影に使う値 ---
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
        var projection = new ImageProjection(worldToCamera, intrinsics.FocalLength, intrinsics.PrincipalPoint, cropOrigin, cropSize);

        // --- 深度からワールド座標への復元に使う値 ---
        var zBufferParams = Shader.GetGlobalVector(EnvironmentDepthZBufferParamsId);
        var depthLocalToWorld = Matrix4x4.TRS(_depthPose.position, _depthPose.rotation, Vector3.one);
        var clipToDepthLocal = (_depthViewProjection * depthLocalToWorld).inverse;

        // 深度1画素の幅は深度に比例するので、1m先での右・上への幅を求めておく。
        var textureSize = _depthAccess.TextureSize;
        PixelStepAtOneMeter(clipToDepthLocal, depthLocalToWorld, zBufferParams, textureSize, out var pixelRight, out var pixelUp);

        var points = new List<Vector3>(_depth.Length);
        var uvs = new List<Vector2>(_depth.Length);
        var depths = new List<float>(_depth.Length);

        for (var y = 0; y < textureSize; y++)
        {
            for (var x = 0; x < textureSize; x++)
            {
                var depth = _depth[y * textureSize + x];
                if (!(depth >= _minDepthMeters && depth <= _maxDepthMeters)) continue; // NaNもここで弾く

                // 深度テクスチャ上の位置と深度値 → ワールド座標
                var clip = new Vector4(
                    (x + 0.5f) / textureSize * 2f - 1f,
                    (y + 0.5f) / textureSize * 2f - 1f,
                    zBufferParams.x / depth - zBufferParams.y,
                    1f);
                var local = clipToDepthLocal * clip;
                if (Mathf.Abs(local.w) < 1e-5f) continue;
                var world = depthLocalToWorld.MultiplyPoint3x4((Vector3)local / local.w);

                // ワールド座標 → カメラ画像上のUV
                if (!projection.TryProject(world, out var uv)) continue;
                if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f) continue; // 画像に写っていない点

                points.Add(world);
                uvs.Add(uv);
                depths.Add(depth);
            }
        }

        capture.Points = points.ToArray();
        capture.ImageUvs = uvs.ToArray();
        capture.Depths = depths.ToArray();
        capture.PixelRightPerMeter = pixelRight;
        capture.PixelUpPerMeter = pixelUp;
        capture.Projection = projection;
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
