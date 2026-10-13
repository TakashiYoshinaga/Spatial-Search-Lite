// Copyright (c) 2026 Takashi Yoshinaga
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Collections.Generic;
using UnityEngine;

namespace SpatialSearch
{
    // 見つかった物体の点を、点ごとの四角形（ビルボード）にするための頂点データを作る。
    //
    // 点1つは深度画像の1画素なので、その画素が写している範囲を四角形で表す。
    // 四角形は撮影した深度カメラの方を向いた平らな板で、四隅は画素の中心から右・上へ半画素ぶんずらした位置（奥行きは中心と同じ）。
    // 四隅をそれぞれカメラ画像へ投影してUVにしておけば、四角形の中にカメラ画像の画素そのものを貼れる。
    // 点ごとに1色を割り当てるのと違い、四角形の中がカメラ画像の細かさで埋まるので、粗いドットに見えない。
    //
    // 四角形は、シェーダーが毎フレーム視点の方へ向ける（ビルボード）。そのため頂点の位置は四隅ではなく点の中心にして、
    // 四隅のずらし量（画面の右・上へ何mか）を別に持たせる。四隅とUVの対応は撮影したときのまま変わらないので、
    // 撮影した向きから見ると板の大きさも貼った画像もぴったり合うが、傾けると合わなくなる（画像の断片が向きを変えずに追従する）。
    //
    // Assets/Shaders/ColoredDepthPointCloudCpuURP.shader（12-Colored-AR）と同じ考え方。
    // あちらは毎フレームGPUで四隅を求めるが、こちらは撮影した位置のまま動かさないので、撮影時の値からCPUで求める。
    public static class SearchResultMeshBuilder
    {
        // pointIndices : 物体に含める点の番号（capture.Points の添字）
        //
        // pointIndices[k] の四角形は、頂点 4k〜4k+3（左下・右下・左上・右上）と、2つの三角形になる。
        // positions   : 頂点の位置（ワールド座標）。4頂点とも点の中心
        // uvs         : 頂点のUV（その四隅がカメラ画像のどこに写るか）
        // cornerSizes : 点の中心から四隅への、画面の右・上へのずらし量（m）。左下なら (-幅の半分, -高さの半分)
        // triangles   : 頂点の番号を3つずつ足す
        public static void Build(CaptureData capture, IReadOnlyList<int> pointIndices,
            List<Vector3> positions, List<Vector2> uvs, List<Vector2> cornerSizes, List<int> triangles)
        {
            foreach (var point in pointIndices)
            {
                var center = capture.Points[point];
                var halfRight = capture.PixelRightPerMeter * (0.5f * capture.Depths[point]);
                var halfUp = capture.PixelUpPerMeter * (0.5f * capture.Depths[point]);

                var first = positions.Count;
                for (var corner = 0; corner < 4; corner++)
                {
                    var signX = (corner & 1) == 0 ? -1f : 1f;
                    var signY = (corner & 2) == 0 ? -1f : 1f;

                    // 撮影したときの四隅の位置を画像へ投影してUVにする。
                    // 画像の端では四隅が画像の外に出ることがある。UVは0〜1の外でもよい（テクスチャは端を引き伸ばす）。
                    // カメラの後ろに回る四隅は投影できないので、中心のUVで代える。
                    var cornerPosition = center + halfRight * signX + halfUp * signY;
                    var hasUv = capture.Projection.TryProject(cornerPosition, out var uv);

                    positions.Add(center);
                    uvs.Add(hasUv ? uv : capture.ImageUvs[point]);
                    cornerSizes.Add(new Vector2(signX * halfRight.magnitude, signY * halfUp.magnitude));
                }

                triangles.Add(first);
                triangles.Add(first + 2);
                triangles.Add(first + 1);
                triangles.Add(first + 1);
                triangles.Add(first + 2);
                triangles.Add(first + 3);
            }
        }
    }
}
