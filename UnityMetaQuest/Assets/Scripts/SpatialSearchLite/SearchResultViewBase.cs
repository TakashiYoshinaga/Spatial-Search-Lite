using UnityEngine;

// 結果を表示するクラスの共通の入り口。MainCoordinator はこれを通して DataSource を渡す。
//   SearchResultView          : 見つかった点をまとめて単色で表示（SpatialSearchLite シーン）
//   GrabbableSearchResultView : 物体ごとにカメラ画像の色で表示し、コントローラでつかめる（SpatialSearchLiteGrab シーン）
public abstract class SearchResultViewBase : MonoBehaviour
{
    public abstract void Initialize(SpatialSearchDataSource dataSource);
}
