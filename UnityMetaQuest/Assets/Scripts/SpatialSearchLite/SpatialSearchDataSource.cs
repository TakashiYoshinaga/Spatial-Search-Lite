using R3;

// 各クラスの間でデータを受け渡す場所。
// 撮影データと検索結果をObservableで流し、受け取る側はSubscribeして処理を始める。
public class SpatialSearchDataSource
{
    Subject<CaptureData> _captureSubject = new Subject<CaptureData>();
    Subject<SearchResult> _resultSubject = new Subject<SearchResult>();
    ReactiveProperty<bool> _connected = new ReactiveProperty<bool>(false);

    // SearchCapture → SearchClient : 撮影した画像と点群
    public Observable<CaptureData> CaptureObservable { get; }

    // SearchClient → SearchResultView : サーバーから返ってきた検索結果
    public Observable<SearchResult> ResultObservable { get; }

    // SearchClient → MainCoordinator : サーバーに接続できているか（購読した時点の値もすぐ届く）
    public Observable<bool> ConnectedObservable { get; }

    public SpatialSearchDataSource()
    {
        CaptureObservable = _captureSubject.AsObservable();
        ResultObservable = _resultSubject.AsObservable();
        ConnectedObservable = _connected.AsObservable();
    }

    public void SetConnected(bool connected)
    {
        _connected.Value = connected;
    }

    public void SetCapture(CaptureData capture)
    {
        _captureSubject.OnNext(capture);
    }

    public void SetResult(SearchResult result)
    {
        _resultSubject.OnNext(result);
    }
}
