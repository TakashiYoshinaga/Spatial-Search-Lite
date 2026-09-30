using UnityEngine;

// 3つのクラスに同じ DataSource を渡してつなぐ。
//   SearchCapture    : ボタンで撮影 → DataSource.SetCapture
//   SearchClient     : 撮影データをサーバーへ送る → 結果を DataSource.SetResult
//   SearchResultView : 結果の点群を表示
public class MainCoordinator : MonoBehaviour
{
    [SerializeField] SearchCapture _capture;
    [SerializeField] SearchClient _client;
    [SerializeField] SearchResultView _resultView;

    SpatialSearchDataSource _dataSource = new SpatialSearchDataSource();

    void Awake()
    {
        _capture.Initialize(_dataSource);
        _client.Initialize(_dataSource);
        _resultView.Initialize(_dataSource);
    }
}
