using System;
using System.Collections.Generic;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

// 結果を表示するクラス（つかめる版）。
// 見つかった物体ごとに _objectTemplate を複製し、マスクに写っていた点を、撮影したカメラ画像を貼った四角形で表示する。
// 四角形は点ごとにあり、シェーダーが常にこちらを向ける（ビルボード）。
// つかむための設定（Grabbable / GrabInteractable など）はひな形のプレハブに付けてあるので、
// このクラスは四角形のメッシュを作り、つかめる範囲（BoxCollider）を物体に合わせるだけ。
public class GrabbableSearchResultView : SearchResultViewBase
{
    static readonly int CameraTextureId = Shader.PropertyToID("_CameraTexture");

    [Tooltip("物体1つぶんのひな形（Assets/Prefabs/GrabbableSearchResult.prefab）")]
    [SerializeField] MeshFilter _objectTemplate;

    [Tooltip("つかめる範囲の箱を決めるとき、各軸の両端から外す点の割合。マスクの縁で拾った背景の点で箱が広がりすぎないようにする")]
    [SerializeField, Range(0f, 0.2f)] float _outlierRatio = 0.05f;

    [Tooltip("つかめる範囲の箱の最小の大きさ（m）。小さな物でもつかみやすくする")]
    [SerializeField, Min(0.01f)] float _minColliderSize = 0.05f;

    [Tooltip("検索中・結果の文字表示（任意）。日本語フォントが無いので表示文は英語")]
    [SerializeField] TMP_Text _statusText;

    readonly List<GameObject> _objects = new List<GameObject>();
    readonly List<Mesh> _meshes = new List<Mesh>();
    Material _resultMaterial;
    SearchResult _shownResult;

    public override void Initialize(SpatialSearchDataSource dataSource)
    {
        if (_objectTemplate == null)
        {
            Debug.LogError("[GrabbableSearchResultView] Object Template が設定されていません", this);
        }

        dataSource.CaptureObservable
            .Subscribe(capture => SetStatus($"Searching \"{capture.Query}\"..."))
            .AddTo(this);

        dataSource.ResultObservable
            .Subscribe(Show)
            .AddTo(this);
    }

    void Show(SearchResult result)
    {
        ReleaseShownResult();
        _shownResult = result;

        var query = result.Capture.Query;
        if (!result.IsSuccess)
        {
            SetStatus($"Error: {result.Error}");
            return;
        }

        // 物体はどれも同じカメラ画像を貼る。ひな形のマテリアルは共有なので、画像を渡すために複製して使う。
        var capture = result.Capture;
        _resultMaterial = new Material(_objectTemplate.GetComponent<MeshRenderer>().sharedMaterial);
        _resultMaterial.SetTexture(CameraTextureId, capture.Image);

        // 物体ごとに点を集める。複数のマスクに写った点は、先に見つかった物体へ入れる。
        var used = new bool[capture.Points.Length];
        foreach (var detection in result.Detections)
        {
            var pointIndices = CollectPointsInMask(capture, detection.Mask, used);
            if (pointIndices.Count == 0) continue;

            CreateObject($"{query} {_objects.Count + 1}", capture, pointIndices);
        }

        Debug.Log($"[GrabbableSearchResultView] query={query} detections={result.Detections.Count} objects={_objects.Count}");
        SetStatus(_objects.Count == 0 ? $"\"{query}\" was not found" : $"Found {_objects.Count} \"{query}\"");
    }

    // マスクの白い部分に写っていた点の番号（capture.Points の添字）を集める。
    static List<int> CollectPointsInMask(CaptureData capture, Texture2D mask, bool[] used)
    {
        var indices = new List<int>();
        var pixels = mask.GetPixels32(); // 左下から右へ、下の行から順に並ぶ
        for (var i = 0; i < capture.Points.Length; i++)
        {
            if (used[i]) continue;

            var uv = capture.ImageUvs[i];
            var x = Mathf.Min((int)(uv.x * mask.width), mask.width - 1);
            var y = Mathf.Min((int)(uv.y * mask.height), mask.height - 1);
            if (pixels[y * mask.width + x].r < 128) continue;

            used[i] = true;
            indices.Add(i);
        }
        return indices;
    }

    // 四角形を持った物体を1つ作る。物体の中心に置き、つかめる範囲の箱を物体に合わせる。
    void CreateObject(string objectName, CaptureData capture, List<int> pointIndices)
    {
        // つかめる範囲は点の中心で決める。
        var centers = new List<Vector3>(pointIndices.Count);
        foreach (var point in pointIndices) centers.Add(capture.Points[point]);
        var bounds = TrimmedBounds(centers, _outlierRatio);

        var filter = Instantiate(_objectTemplate, bounds.center, Quaternion.identity, transform);
        filter.name = objectName;
        filter.GetComponent<MeshRenderer>().sharedMaterial = _resultMaterial;

        // 点ごとの四角形にする。頂点にはカメラ画像のUV（uv0）と、中心から四隅へのずらし量（uv1）を持たせる。
        var positions = new List<Vector3>(pointIndices.Count * 4);
        var uvs = new List<Vector2>(pointIndices.Count * 4);
        var cornerSizes = new List<Vector2>(pointIndices.Count * 4);
        var triangles = new List<int>(pointIndices.Count * 6);
        SearchResultMeshBuilder.Build(capture, pointIndices, positions, uvs, cornerSizes, triangles);

        // 頂点の位置と四隅のずらし量はワールドの値なので、作った物体のローカルの値へ直す。
        // こうしておくと、両手で拡大縮小したときに四角形の大きさも一緒に変わる。
        var worldToLocal = filter.transform.worldToLocalMatrix;
        var worldToLocalScale = worldToLocal.MultiplyVector(Vector3.right).magnitude;
        var maxCornerSize = 0f;
        for (var i = 0; i < positions.Count; i++)
        {
            positions[i] = worldToLocal.MultiplyPoint3x4(positions[i]);
            cornerSizes[i] *= worldToLocalScale;
            maxCornerSize = Mathf.Max(maxCornerSize, Mathf.Abs(cornerSizes[i].x), Mathf.Abs(cornerSizes[i].y));
        }

        var mesh = new Mesh { name = objectName, indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(positions);
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, cornerSizes);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();

        // 頂点は点の中心にあるので、四角形がはみ出ても画面の端で消えないよう、範囲を四角形の大きさぶん広げる。
        var meshBounds = mesh.bounds;
        meshBounds.Expand(maxCornerSize * 4f);
        mesh.bounds = meshBounds;
        filter.sharedMesh = mesh;

        if (filter.TryGetComponent<BoxCollider>(out var box))
        {
            var size = Vector3.Max(bounds.size, Vector3.one * _minColliderSize);
            box.center = Vector3.zero;
            box.size = Abs(worldToLocal.MultiplyVector(size));
        }

        _objects.Add(filter.gameObject);
        _meshes.Add(mesh);
    }

    // 各軸の両端から ratio ぶんの点を外した範囲。マスクの縁で拾った背景の点に引きずられないようにする。
    static Bounds TrimmedBounds(List<Vector3> positions, float ratio)
    {
        var count = positions.Count;
        var xs = new float[count];
        var ys = new float[count];
        var zs = new float[count];
        for (var i = 0; i < count; i++)
        {
            xs[i] = positions[i].x;
            ys[i] = positions[i].y;
            zs[i] = positions[i].z;
        }
        Array.Sort(xs);
        Array.Sort(ys);
        Array.Sort(zs);

        var low = Mathf.Min((int)(count * ratio), count - 1);
        var high = Mathf.Max(count - 1 - low, low);

        var bounds = new Bounds();
        bounds.SetMinMax(new Vector3(xs[low], ys[low], zs[low]), new Vector3(xs[high], ys[high], zs[high]));
        return bounds;
    }

    static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    void SetStatus(string message)
    {
        if (_statusText != null) _statusText.text = message;
    }

    void ReleaseShownResult()
    {
        foreach (var go in _objects)
        {
            if (go != null) Destroy(go);
        }
        _objects.Clear();

        foreach (var mesh in _meshes) Destroy(mesh);
        _meshes.Clear();

        if (_resultMaterial != null) Destroy(_resultMaterial);
        _resultMaterial = null;

        if (_shownResult == null) return;
        foreach (var detection in _shownResult.Detections)
        {
            Destroy(detection.Mask);
        }
        Destroy(_shownResult.Capture.Image);
        _shownResult = null;
    }

    void OnDestroy()
    {
        ReleaseShownResult();
    }
}
