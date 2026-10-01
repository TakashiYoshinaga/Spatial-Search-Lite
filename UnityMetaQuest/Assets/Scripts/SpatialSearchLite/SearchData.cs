using System;
using System.Collections.Generic;
using UnityEngine;

// 撮影した瞬間のデータ。
// Points[i] を画像へ投影した位置が ImageUvs[i]（0〜1、左下原点）で、
// サーバーから返ってきたマスクを ImageUvs で引けば、その点が物体上にあるかが分かる。
// 点は深度画像の1画素なので、その画素が写している範囲を四角形で表せる。Depths[i] は点の深度（m）、
// PixelRightPerMeter / PixelUpPerMeter は深度1画素ぶんの右・上への幅を、1m先で測ったワールドのベクトル（深度に比例する）。
// Points[i] ± 半画素ぶんが四隅の位置で、四隅をそれぞれ Projection でカメラ画像へ投影すれば、四隅のUVが分かる。
//
// Normals[i] は Points[i] での面の向き（長さ1、撮影したカメラの側を向く）。隣の画素から求めるので、
// 物体の縁など隣が使えない点は Vector3.zero。物体の「正面」を決めるのに使う。
//
// Image は撮影ごとに作る（次の撮影で上書きしない）。結果の物体に貼って使うので、
// 表示し終えたクラスが Destroy する。結果にならなかった撮影は SearchClient が Destroy する。
public class CaptureData
{
    public string Query;
    public Texture2D Image;
    public Vector3[] Points;
    public Vector2[] ImageUvs;
    public float[] Depths;
    public Vector3[] Normals;
    public Vector3 PixelRightPerMeter;
    public Vector3 PixelUpPerMeter;
    public ImageProjection Projection;
}

// ワールド座標が、撮影したカメラ画像のどこに写るか（UV、0〜1、左下原点）を求める。
// 撮影した瞬間のカメラの位置・向きと内部パラメータを持っている。
public class ImageProjection
{
    readonly Matrix4x4 _worldToCamera;
    readonly Vector2 _focalLength;
    readonly Vector2 _principalPoint;
    readonly Vector2 _cropOrigin;
    readonly Vector2 _cropSize;

    // 撮影した瞬間のカメラの位置（ワールド）。この位置から見た向きが、貼った画像と合う向きになる。
    public Vector3 CameraPosition { get; }

    // cropOrigin / cropSize : センサー画像のうち、カメラ画像として切り出された範囲（センサーのピクセル）
    public ImageProjection(Matrix4x4 worldToCamera, Vector2 focalLength, Vector2 principalPoint, Vector2 cropOrigin, Vector2 cropSize)
    {
        _worldToCamera = worldToCamera;
        CameraPosition = worldToCamera.inverse.MultiplyPoint3x4(Vector3.zero);
        _focalLength = focalLength;
        _principalPoint = principalPoint;
        _cropOrigin = cropOrigin;
        _cropSize = cropSize;
    }

    // カメラの後ろにある点は false。画像の外に写る点は、UVが0〜1の外の値になる。
    public bool TryProject(Vector3 world, out Vector2 uv)
    {
        var inCamera = _worldToCamera.MultiplyPoint3x4(world);
        if (inCamera.z <= 1e-5f)
        {
            uv = Vector2.zero;
            return false;
        }

        var sensorX = inCamera.x / inCamera.z * _focalLength.x + _principalPoint.x;
        var sensorY = inCamera.y / inCamera.z * _focalLength.y + _principalPoint.y;
        uv = new Vector2((sensorX - _cropOrigin.x) / _cropSize.x, (sensorY - _cropOrigin.y) / _cropSize.y);
        return true;
    }
}

// 検索結果。どの撮影に対する結果かを分かるよう Capture を持たせる。
public class SearchResult
{
    public CaptureData Capture;
    public List<Detection> Detections = new List<Detection>();
    public string Error;

    public bool IsSuccess => string.IsNullOrEmpty(Error);
}

// SAM3が見つけた物体1つぶん。Maskは送った画像と同じサイズで、白い部分が物体。
public class Detection
{
    public float Score;
    public Texture2D Mask;
}

// ---- 以下はサーバーとやり取りするJSONの形（JsonUtility用） ----

[Serializable]
public class SearchRequestMessage
{
    public string type = "search";
    public string query;
    public string image; // PNGのbase64
}

[Serializable]
public class SearchResponseMessage
{
    public string type; // "result" または "error"
    public string query;
    public string message; // type が "error" のときの理由
    public DetectionMessage[] detections;
}

[Serializable]
public class DetectionMessage
{
    public float score;
    public string mask; // PNGのbase64
}
