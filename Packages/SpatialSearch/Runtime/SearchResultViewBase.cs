// Copyright (c) 2026 Takashi Yoshinaga
// Licensed under the MIT License. See LICENSE in the repository root.

using R3;
using UnityEngine;

namespace SpatialSearch
{
    // 結果を表示するクラスの共通の入り口。各クラスをつなぐ側は、これを通して DataSource を渡す。
    //   SearchResultView          : 見つかった点をまとめて単色で表示
    //   GrabbableSearchResultView : 物体ごとにカメラ画像の色で表示し、つかめるようにする
    //
    // 表示し終えたら ShownObservable で、表示した結果と物体の数を知らせる。
    // 「見つかった数」などの文字の表示はこのクラスでは行わず、受け取った側（UIを受け持つクラス）が行う。
    public abstract class SearchResultViewBase : MonoBehaviour
    {
        readonly Subject<ShownResult> _shownSubject = new Subject<ShownResult>();

        // 検索結果を表示し終えたとき。失敗した結果（IsSuccess が false）のときも、物体の数 0 で流す。
        public Observable<ShownResult> ShownObservable => _shownSubject;

        public abstract void Initialize(SpatialSearchDataSource dataSource);

        protected void NotifyShown(SearchResult result, int objectCount)
        {
            _shownSubject.OnNext(new ShownResult(result, objectCount));
        }
    }

    // 表示し終えた検索結果と、表示した物体の数（見つからなかったとき・失敗したときは 0）。
    public readonly struct ShownResult
    {
        public readonly SearchResult Result;
        public readonly int ObjectCount;

        public ShownResult(SearchResult result, int objectCount)
        {
            Result = result;
            ObjectCount = objectCount;
        }
    }
}
