using System.Collections.Generic;
using R3;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpatialSearch
{
    // 結果を表示するクラス。
    // マスクの白い部分に写っていた点だけを集め、ワールド空間に点群として描く。
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class SearchResultView : SearchResultViewBase
    {
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int PointSizeId = Shader.PropertyToID("_PointSize");

        [SerializeField] Material _pointMaterial;
        [SerializeField] Color _color = new Color(1f, 0f, 1f, 1f);
        [SerializeField, Min(1f)] float _pointSize = 6f;

        MeshRenderer _meshRenderer;
        Mesh _mesh;
        Material _material;
        SearchResult _shownResult;

        public override void Initialize(SpatialSearchDataSource dataSource)
        {
            _mesh = new Mesh { name = "SearchResultPoints", indexFormat = IndexFormat.UInt32 };
            GetComponent<MeshFilter>().sharedMesh = _mesh;

            _material = new Material(_pointMaterial);
            _material.SetColor(ColorId, _color);
            _material.SetFloat(PointSizeId, _pointSize);

            _meshRenderer = GetComponent<MeshRenderer>();
            _meshRenderer.sharedMaterial = _material;
            _meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _meshRenderer.enabled = false;

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
                _meshRenderer.enabled = false;
                NotifyShown(result, 0);
                return;
            }

            var positions = CollectPointsInMasks(result);
            Debug.Log($"[SearchResultView] query={query} detections={result.Detections.Count} points={positions.Count}");

            if (positions.Count == 0)
            {
                _meshRenderer.enabled = false;
                NotifyShown(result, 0);
                return;
            }

            // 点はワールド座標なので、このオブジェクトのローカル座標へ直してから描く。
            // こうしておけば、親（Logic など）の位置やスケールに関係なく正しい場所に出る。
            var worldToLocal = transform.worldToLocalMatrix;
            for (var i = 0; i < positions.Count; i++) positions[i] = worldToLocal.MultiplyPoint3x4(positions[i]);

            var indices = new int[positions.Count];
            for (var i = 0; i < indices.Length; i++) indices[i] = i;

            _mesh.Clear();
            _mesh.SetVertices(positions);
            _mesh.SetIndices(indices, MeshTopology.Points, 0);
            _mesh.RecalculateBounds();
            _meshRenderer.enabled = true;

            NotifyShown(result, result.Detections.Count);
        }

        // どれかのマスクの白い部分に写っていた点を集める。
        static List<Vector3> CollectPointsInMasks(SearchResult result)
        {
            var capture = result.Capture;
            var positions = new List<Vector3>();
            var hit = new bool[capture.Points.Length];

            foreach (var detection in result.Detections)
            {
                var mask = detection.Mask;
                var pixels = mask.GetPixels32(); // 左下から右へ、下の行から順に並ぶ
                for (var i = 0; i < capture.Points.Length; i++)
                {
                    if (hit[i]) continue;

                    var uv = capture.ImageUvs[i];
                    var x = Mathf.Min((int)(uv.x * mask.width), mask.width - 1);
                    var y = Mathf.Min((int)(uv.y * mask.height), mask.height - 1);
                    if (pixels[y * mask.width + x].r < 128) continue;

                    hit[i] = true;
                    positions.Add(capture.Points[i]);
                }
            }
            return positions;
        }

        void ReleaseShownResult()
        {
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
            if (_mesh != null) Destroy(_mesh);
            if (_material != null) Destroy(_material);
        }
    }
}
