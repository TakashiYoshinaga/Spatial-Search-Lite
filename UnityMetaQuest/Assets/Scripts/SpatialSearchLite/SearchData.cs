using System;
using System.Collections.Generic;
using UnityEngine;

// 撮影した瞬間のデータ。
// Points[i] を画像へ投影した位置が ImageUvs[i]（0〜1、左下原点）で、
// サーバーから返ってきたマスクを ImageUvs で引けば、その点が物体上にあるかが分かる。
public class CaptureData
{
    public string Query;
    public Texture2D Image;
    public Vector3[] Points;
    public Vector2[] ImageUvs;
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
