using R3;

// 各クラスの間でデータを受け渡す場所。
// 撮影データと検索結果をObservableで流し、受け取る側はSubscribeして処理を始める。
public class SpatialSearchDataSource
{
    Subject<CaptureData> _captureSubject = new Subject<CaptureData>();
    Subject<SearchResult> _resultSubject = new Subject<SearchResult>();

    // SearchCapture → SearchClient : 撮影した画像と点群
    public Observable<CaptureData> CaptureObservable { get; }

    // SearchClient → SearchResultView : サーバーから返ってきた検索結果
    public Observable<SearchResult> ResultObservable { get; }

    public SpatialSearchDataSource()
    {
        CaptureObservable = _captureSubject.AsObservable();
        ResultObservable = _resultSubject.AsObservable();
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
