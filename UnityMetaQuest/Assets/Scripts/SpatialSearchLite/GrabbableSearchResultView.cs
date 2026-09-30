using System;
using System.Collections.Generic;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

// 結果を表示するクラス（つかめる版）。
// 見つかった物体ごとに _objectTemplate を複製し、マスクに写っていた点をカメラ画像の色で描く。
// つかむための設定（Grabbable / GrabInteractable など）はひな形のプレハブに付けてあるので、
// このクラスは点群のメッシュを作り、つかめる範囲（BoxCollider）を点群に合わせるだけ。
public class GrabbableSearchResultView : SearchResultViewBase
{
    [Tooltip("物体1つぶんの点群のひな形（Assets/Prefabs/GrabbableSearchResult.prefab）")]
    [SerializeField] MeshFilter _objectTemplate;

    [Tooltip("つかめる範囲の箱を決めるとき、各軸の両端から外す点の割合。マスクの縁で拾った背景の点で箱が広がりすぎないようにする")]
    [SerializeField, Range(0f, 0.2f)] float _outlierRatio = 0.05f;

    [Tooltip("つかめる範囲の箱の最小の大きさ（m）。小さな物でもつかみやすくする")]
    [SerializeField, Min(0.01f)] float _minColliderSize = 0.05f;

    [Tooltip("検索中・結果の文字表示（任意）。日本語フォントが無いので表示文は英語")]
    [SerializeField] TMP_Text _statusText;

    readonly List<GameObject> _objects = new List<GameObject>();
    readonly List<Mesh> _meshes = new List<Mesh>();
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

        // 物体ごとに点を集める。複数のマスクに写った点は、先に見つかった物体へ入れる。
        var capture = result.Capture;
        var used = new bool[capture.Points.Length];
        foreach (var detection in result.Detections)
        {
            var positions = new List<Vector3>();
            var colors = new List<Color32>();
            var sizes = new List<Vector2>();
            CollectPointsInMask(capture, detection.Mask, used, positions, colors, sizes);
            if (positions.Count == 0) continue;

            CreateObject($"{query} {_objects.Count + 1}", positions, colors, sizes);
        }

        Debug.Log($"[GrabbableSearchResultView] query={query} detections={result.Detections.Count} objects={_objects.Count}");
        SetStatus(_objects.Count == 0 ? $"\"{query}\" was not found" : $"Found {_objects.Count} \"{query}\"");
    }

    // マスクの白い部分に写っていた点と、その色・大きさを集める。
    // 大きさはシェーダーへ UV の x で渡すので Vector2 にしておく。
    static void CollectPointsInMask(CaptureData capture, Texture2D mask, bool[] used,
        List<Vector3> positions, List<Color32> colors, List<Vector2> sizes)
    {
        var pixels = mask.GetPixels32(); // 左下から右へ、下の行から順に並ぶ
        for (var i = 0; i < capture.Points.Length; i++)
        {
            if (used[i]) continue;

            var uv = capture.ImageUvs[i];
            var x = Mathf.Min((int)(uv.x * mask.width), mask.width - 1);
            var y = Mathf.Min((int)(uv.y * mask.height), mask.height - 1);
            if (pixels[y * mask.width + x].r < 128) continue;

            used[i] = true;
            positions.Add(capture.Points[i]);
            colors.Add(capture.Colors[i]);
            sizes.Add(new Vector2(capture.PointSizes[i], 0f));
        }
    }

    // 点群を持った物体を1つ作る。点群の中心に置き、つかめる範囲の箱を点群に合わせる。
    void CreateObject(string objectName, List<Vector3> positions, List<Color32> colors, List<Vector2> sizes)
    {
        var bounds = TrimmedBounds(positions, _outlierRatio);

        var filter = Instantiate(_objectTemplate, bounds.center, Quaternion.identity, transform);
        filter.name = objectName;

        // 点の位置と大きさはワールドの値なので、作った物体のローカルの値へ直す。
        // こうしておくと、両手で拡大縮小したときに点の大きさも一緒に変わる。
        var worldToLocal = filter.transform.worldToLocalMatrix;
        var worldToLocalScale = worldToLocal.MultiplyVector(Vector3.right).magnitude;
        for (var i = 0; i < positions.Count; i++)
        {
            positions[i] = worldToLocal.MultiplyPoint3x4(positions[i]);
            sizes[i] *= worldToLocalScale;
        }

        var indices = new int[positions.Count];
        for (var i = 0; i < indices.Length; i++) indices[i] = i;

        var mesh = new Mesh { name = objectName, indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(positions);
        mesh.SetColors(colors);
        mesh.SetUVs(0, sizes);
        mesh.SetIndices(indices, MeshTopology.Points, 0);
        mesh.RecalculateBounds();
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

        if (_shownResult == null) return;
        foreach (var detection in _shownResult.Detections)
        {
            Destroy(detection.Mask);
        }
        _shownResult = null;
    }

    void OnDestroy()
    {
        ReleaseShownResult();
    }
}
