using Oculus.Interaction.Input;
using R3;
using SpatialSearch;
using TMPro;
using UnityEngine;

// 両手で写真を撮るときの枠を作って物体を指し、その物体1つを切り出して複製するクラス。
//
// 流れ
//   構える : 両手の親指と人差し指でL字を作り、左手を枠の左上、右手を右下の角にする（人差し指は横向き、親指は左手が下・右手が上）
//            頭から枠の中心へのレイが深度に当たった位置に点を出す（物体の表面に出すので、物体と同じ奥行きに見える）
//   ロック : 点が2秒止まったら、その位置（ワールド座標）を覚える。リングが緑になり「Lower your hands」と出る
//   撮影   : 両手がカメラ画像から消えたら撮影し、覚えた位置を撮影した画像へ投影して点で探す（手が写り込まないようにするため）
//   結果   : 見つかった物体1つが、撮影した位置に複製される（表示とつかむのは GrabbableSearchResultView）
//
// ロック後、手が下りないまま時間が過ぎたとき、または覚えた位置がカメラ画像から外れたときは取り消す。
// 点の位置がそのまま撮影で使う点なので、見えている点の物体が取れる。
// 点の周りで奥行きが大きく飛んでいる（物体の縁や穴）ときは点を橙にし、ロックしない。
public class FrameCopyController : MonoBehaviour
{
    enum State
    {
        Aiming,  // 枠を探している・点が止まるのを待っている
        Locked,  // 手が下りるのを待っている
        Waiting, // サーバーの結果を待っている
    }

    [Tooltip("手の関節の取得元。空なら起動時にシーンから探す")]
    [SerializeField] Hand _leftHand;
    [SerializeField] Hand _rightHand;

    [Tooltip("点・リング・枠の線に使うマテリアル（頂点カラーで色を付けるもの。Sprites/Default など）")]
    [SerializeField] Material _visualMaterial;

    [Header("Lock")]
    [Tooltip("点がこの角度（度）以内に止まっている時間でロックする")]
    [SerializeField, Min(0.1f)] float _stableAngle = 2f;
    [SerializeField, Min(0.1f)] float _lockSeconds = 2f;

    [Header("Capture")]
    [Tooltip("両手がカメラ画像から消えてから撮影するまでの時間（秒）")]
    [SerializeField, Min(0f)] float _handsGoneSeconds = 0.3f;
    [Tooltip("ロックしてから手が下りるのを待つ時間（秒）。過ぎたら取り消す")]
    [SerializeField, Min(0.5f)] float _lowerHandsTimeout = 3f;
    [Tooltip("サーバーの結果を待つ時間（秒）。過ぎたら次の操作を受け付ける")]
    [SerializeField, Min(1f)] float _resultTimeout = 30f;

    [Header("Look")]
    [Tooltip("点・リングの見た目の大きさ（度）。距離に合わせて大きさを変え、見た目の大きさを一定にする")]
    [SerializeField] float _dotAngle = 0.8f;
    [SerializeField] float _ringAngle = 3f;
    [SerializeField] Color _aimColor = Color.white;
    [SerializeField] Color _edgeColor = new Color(1f, 0.55f, 0f);
    [SerializeField] Color _lockedColor = new Color(0.2f, 1f, 0.3f);

    // 撮影結果の表示に使う名前
    const string Label = "object";

    // 手がカメラ画像に写っているとみなす範囲（画像の外側へ広げる割合）
    const float HandInImageMargin = 0.05f;

    // 指の向きの平滑化の強さ（0〜1、小さいほど滑らか）
    const float RaySmoothing = 0.35f;

    SearchCapture _capture;
    Transform _head;

    State _state = State.Aiming;
    float _stateTime;

    // Aiming
    bool _hasFrame;           // 枠を認識している（途切れても FrameGraceSeconds の間は保つ）
    float _lastFrameTime;
    Vector3 _lastLeftCorner;
    Vector3 _lastRightCorner;
    Vector3 _smoothedDirection;
    bool _hasSmoothedDirection;
    Vector3 _stableDirection;
    float _stableSince = -1f;

    // Locked
    Vector3 _lockedPoint;
    float _handsGoneSince = -1f;

    // 表示
    LineRenderer _dot;
    LineRenderer _ring;
    LineRenderer _frame;
    TextMeshPro _message;
    float _messageUntil;

    const int RingSegments = 48;

    public void Initialize(SpatialSearchDataSource dataSource, SearchCapture capture)
    {
        _capture = capture;

        // 結果が出たら次の操作を受け付ける。
        dataSource.ResultObservable
            .Subscribe(result =>
            {
                if (_state != State.Waiting) return;
                ShowMessage(result.IsSuccess ? "Copied" : "Failed", 1.5f);
                SetState(State.Aiming);
            })
            .AddTo(this);
    }

    void Start()
    {
        if (_leftHand == null) _leftHand = FindHand(Handedness.Left);
        if (_rightHand == null) _rightHand = FindHand(Handedness.Right);
        if (_leftHand == null || _rightHand == null)
        {
            Debug.LogError("[FrameCopyController] 手（Hand）が見つかりません。インタラクションリグがシーンにあるか確認してください", this);
        }

        CreateVisuals();
        HideAll();
    }

    // リグの中から、指定した側の手のデータ（Hand）を探す。つかんだときに指の形が変わる SyntheticHand は使わない。
    static Hand FindHand(Handedness handedness)
    {
        foreach (var hand in FindObjectsByType<Hand>(FindObjectsSortMode.None))
        {
            if (hand.GetType() == typeof(Hand) && hand.Handedness == handedness) return hand;
        }
        return null;
    }

    void Update()
    {
        if (_capture == null || _leftHand == null || _rightHand == null) return;
        if (_head == null)
        {
            if (Camera.main == null) return;
            _head = Camera.main.transform;
        }

        HideAll();
        switch (_state)
        {
            case State.Aiming: UpdateAiming(); break;
            case State.Locked: UpdateLocked(); break;
            case State.Waiting: UpdateWaiting(); break;
        }
        UpdateMessage();
    }

    void UpdateAiming()
    {
        // 認識している間は、少しゆるい閾値で枠を保つ（境目の形で出たり消えたりしないように）。
        // 手の追跡が一瞬揺れて外れても、FrameGraceSeconds の間は最後の枠を使い、確定までの時間も数え続ける。
        Vector3 leftCorner, rightCorner;
        if (TryGetFrame(_hasFrame ? FrameThresholds.Keep : FrameThresholds.Acquire, out leftCorner, out rightCorner))
        {
            _hasFrame = true;
            _lastFrameTime = Time.time;
            _lastLeftCorner = leftCorner;
            _lastRightCorner = rightCorner;
        }
        else if (_hasFrame && Time.time - _lastFrameTime <= FrameGraceSeconds)
        {
            leftCorner = _lastLeftCorner;
            rightCorner = _lastRightCorner;
        }
        else
        {
            ResetAim();
            return;
        }
        DrawFrame(leftCorner, rightCorner);

        // 頭から枠の中心へのレイ。手の揺れで点が震えないよう、向きを平滑化する。
        var direction = ((leftCorner + rightCorner) * 0.5f - _head.position).normalized;
        _smoothedDirection = _hasSmoothedDirection ? Vector3.Slerp(_smoothedDirection, direction, RaySmoothing) : direction;
        _hasSmoothedDirection = true;

        if (!_capture.TryRaycastDepth(new Ray(_head.position, _smoothedDirection), out var hit, out var onEdge))
        {
            _stableSince = -1f;
            return;
        }

        // 点が止まっている時間を数える。縁や穴を指しているときはロックしない。
        if (onEdge || _stableSince < 0f || Vector3.Angle(_stableDirection, _smoothedDirection) > _stableAngle)
        {
            _stableDirection = _smoothedDirection;
            _stableSince = onEdge ? -1f : Time.time;
        }
        var progress = _stableSince < 0f ? 0f : (Time.time - _stableSince) / _lockSeconds;

        DrawDot(hit, onEdge ? _edgeColor : _aimColor);
        DrawRing(hit, Mathf.Clamp01(progress), _aimColor);

        if (progress >= 1f)
        {
            _lockedPoint = hit;
            _handsGoneSince = -1f;
            SetState(State.Locked);
        }
    }

    void UpdateLocked()
    {
        DrawDot(_lockedPoint, _lockedColor);
        DrawRing(_lockedPoint, 1f, _lockedColor);
        DrawLabel(_lockedPoint, "Lower your hands");

        if (Time.time - _stateTime > _lowerHandsTimeout)
        {
            Cancel("Canceled");
            return;
        }
        if (!_capture.TryProjectToImage(_lockedPoint, out var uv) || !SearchCapture.IsInImage(uv, 0f))
        {
            Cancel("Lost the target");
            return;
        }

        if (IsHandInImage(_leftHand) || IsHandInImage(_rightHand))
        {
            _handsGoneSince = -1f;
            return;
        }
        if (_handsGoneSince < 0f) _handsGoneSince = Time.time;
        if (Time.time - _handsGoneSince < _handsGoneSeconds) return;

        if (_capture.CaptureAt(_lockedPoint, Label))
        {
            SetState(State.Waiting);
        }
        else
        {
            Cancel("Could not capture");
        }
    }

    void UpdateWaiting()
    {
        DrawDot(_lockedPoint, _lockedColor);
        DrawLabel(_lockedPoint, "Copying...");

        if (Time.time - _stateTime > _resultTimeout)
        {
            Cancel("No response");
        }
    }

    void Cancel(string message)
    {
        ShowMessage(message, 1.5f);
        SetState(State.Aiming);
    }

    void SetState(State state)
    {
        _state = state;
        _stateTime = Time.time;
        ResetAim();
    }

    void ResetAim()
    {
        _hasFrame = false;
        _hasSmoothedDirection = false;
        _stableSince = -1f;
    }

    // ---- 手の形 ----

    // 両手がL字で、左手が枠の左上、右手が右下の角になっているとき true。角の位置（ワールド）を返す。
    //   左手 : 人差し指は右向き（枠の上辺）、親指は下向き（左辺）
    //   右手 : 人差し指は左向き（枠の下辺）、親指は上向き（右辺）
    //
    // 位置や向きは「視線の座標」で測る。頭から枠の中心への向きを前とするので、正面から外れた所（横や下）で枠を作っても同じ厳しさになる。
    bool TryGetFrame(FrameThresholds t, out Vector3 leftCorner, out Vector3 rightCorner)
    {
        rightCorner = Vector3.zero;
        if (!TryGetLCorner(_leftHand, t, out leftCorner, out var leftIndex, out var leftThumb) ||
            !TryGetLCorner(_rightHand, t, out rightCorner, out var rightIndex, out var rightThumb))
        {
            return false;
        }

        var view = new ViewBasis(_head, (leftCorner + rightCorner) * 0.5f);
        var left = view.ToLocal(leftCorner);
        var right = view.ToLocal(rightCorner);
        if (left.z < t.MinDistance || right.z < t.MinDistance || left.z > t.MaxDistance || right.z > t.MaxDistance) return false;
        if (right.x - left.x < MinFrameWidth) return false;                       // 左手が左、右手が右
        if (left.y - right.y < MinFrameHeight) return false;                      // 左手が上、右手が下
        if (Mathf.Abs(right.z - left.z) > t.MaxDepthGap) return false;            // 枠が視線の方を向いている
        if (view.Flat(leftIndex).x < t.MinFingerAlignment) return false;          // 左手の人差し指が右向き
        if (view.Flat(leftThumb).y > -t.MinFingerAlignment) return false;         // 左手の親指が下向き
        if (view.Flat(rightIndex).x > -t.MinFingerAlignment) return false;        // 右手の人差し指が左向き
        if (view.Flat(rightThumb).y < t.MinFingerAlignment) return false;         // 右手の親指が上向き
        return true;
    }

    // 視線の座標。頭の位置を原点に、頭から target への向きを前（z）、頭の上を視線に直交させた向きを上（y）、右を x とする。
    readonly struct ViewBasis
    {
        readonly Vector3 _origin;
        readonly Vector3 _right;
        readonly Vector3 _up;
        readonly Vector3 _forward;

        public ViewBasis(Transform head, Vector3 target)
        {
            _origin = head.position;
            _forward = (target - head.position).normalized;
            _up = Vector3.ProjectOnPlane(head.up, _forward).normalized;
            _right = Vector3.Cross(_up, _forward);
        }

        public Vector3 ToLocal(Vector3 world)
        {
            var offset = world - _origin;
            return new Vector3(Vector3.Dot(offset, _right), Vector3.Dot(offset, _up), Vector3.Dot(offset, _forward));
        }

        public Vector3 ToWorld(Vector3 local)
        {
            return _origin + _right * local.x + _up * local.y + _forward * local.z;
        }

        // 向き（ワールド）を視線に垂直な面に写した向き（長さ1。x が右、y が上）
        public Vector2 Flat(Vector3 direction)
        {
            var flat = new Vector2(Vector3.Dot(direction, _right), Vector3.Dot(direction, _up));
            return flat.sqrMagnitude > 1e-8f ? flat.normalized : Vector2.zero;
        }
    }

    // 枠の判定に使う閾値。認識し始めるとき（Acquire）と、認識している枠を保つとき（Keep、少しゆるい）の2組。
    readonly struct FrameThresholds
    {
        public readonly float MinDistance;         // 頭から枠の角までの奥行き（m）
        public readonly float MaxDistance;
        public readonly float MaxDepthGap;         // 左右の角の奥行きの差の上限（m）
        public readonly float MinFingerAlignment;  // 指が決めた向き（右・左・上・下）からずれてよい角度の cos
        public readonly float MinIndexStraightness;
        public readonly float MinThumbStraightness;
        public readonly float MaxMiddleReach;      // 伸ばすと1.8〜1.9、軽く曲げて1.4〜1.5
        public readonly float MinLAngle;           // 親指と人差し指の角度（度）
        public readonly float MaxLAngle;

        FrameThresholds(float minDistance, float maxDistance, float maxDepthGap, float maxFingerAngle,
            float minIndexStraightness, float minThumbStraightness, float maxMiddleReach, float minLAngle, float maxLAngle)
        {
            MinDistance = minDistance;
            MaxDistance = maxDistance;
            MaxDepthGap = maxDepthGap;
            MinFingerAlignment = Mathf.Cos(maxFingerAngle * Mathf.Deg2Rad);
            MinIndexStraightness = minIndexStraightness;
            MinThumbStraightness = minThumbStraightness;
            MaxMiddleReach = maxMiddleReach;
            MinLAngle = minLAngle;
            MaxLAngle = maxLAngle;
        }

        public static readonly FrameThresholds Acquire = new FrameThresholds(0.15f, 0.8f, 0.2f, 45f, 0.9f, 0.85f, 1.7f, 45f, 135f);
        public static readonly FrameThresholds Keep = new FrameThresholds(0.12f, 0.9f, 0.25f, 55f, 0.85f, 0.8f, 1.8f, 35f, 145f);
    }

    // 枠の最小の幅・高さ（m）。左右・上下の位置関係そのものなので、保つときもゆるめない。
    const float MinFrameWidth = 0.05f;
    const float MinFrameHeight = 0.03f;

    // 枠の判定から外れても、この時間（秒）以内に戻れば枠を保つ。手の追跡が一瞬揺れたときに、確定までの時間をやり直さないため。
    const float FrameGraceSeconds = 0.3f;

    // 親指と人差し指を伸ばし、中指を曲げた「L字」のとき true。
    // L字の角（親指と人差し指の付け根の間）と、人差し指・親指の向き（ワールド）を返す。
    static bool TryGetLCorner(Hand hand, FrameThresholds t, out Vector3 corner, out Vector3 indexDirection, out Vector3 thumbDirection)
    {
        corner = Vector3.zero;
        indexDirection = Vector3.zero;
        thumbDirection = Vector3.zero;
        if (!hand.IsTrackedDataValid) return false;
        if (!hand.GetJointPose(HandJointId.HandWristRoot, out var wrist) ||
            !hand.GetJointPose(HandJointId.HandThumb2, out var thumb2) ||
            !hand.GetJointPose(HandJointId.HandThumb3, out var thumb3) ||
            !hand.GetJointPose(HandJointId.HandThumbTip, out var thumbTip) ||
            !hand.GetJointPose(HandJointId.HandIndex1, out var index1) ||
            !hand.GetJointPose(HandJointId.HandIndex2, out var index2) ||
            !hand.GetJointPose(HandJointId.HandIndex3, out var index3) ||
            !hand.GetJointPose(HandJointId.HandIndexTip, out var indexTip) ||
            !hand.GetJointPose(HandJointId.HandMiddle1, out var middle1) ||
            !hand.GetJointPose(HandJointId.HandMiddleTip, out var middleTip))
        {
            return false;
        }

        var indexStraight = Straightness(index1.position, index2.position, index3.position, indexTip.position);
        var thumbStraight = Straightness(thumb2.position, thumb3.position, thumbTip.position);
        if (indexStraight < t.MinIndexStraightness || thumbStraight < t.MinThumbStraightness) return false;

        // 中指が曲がっている（指先が手首からあまり遠くない）。手を開いただけのときにL字と見なさないため。
        var middleReach = Vector3.Distance(middleTip.position, wrist.position) / Vector3.Distance(middle1.position, wrist.position);
        if (middleReach > t.MaxMiddleReach) return false;

        // 親指と人差し指がおおよそ直角。
        indexDirection = indexTip.position - index1.position;
        thumbDirection = thumbTip.position - thumb2.position;
        var angle = Vector3.Angle(thumbDirection, indexDirection);
        if (angle < t.MinLAngle || angle > t.MaxLAngle) return false;

        corner = (thumb2.position + index1.position) * 0.5f;
        return true;
    }

    // 指の伸び具合。付け根から指先までの直線距離と、関節をたどった長さの比（1でまっすぐ）。
    static float Straightness(params Vector3[] joints)
    {
        var length = 0f;
        for (var i = 1; i < joints.Length; i++) length += Vector3.Distance(joints[i - 1], joints[i]);
        return length > 1e-5f ? Vector3.Distance(joints[0], joints[joints.Length - 1]) / length : 0f;
    }

    // 手の関節のどれかがカメラ画像（の少し外側まで）に写っているか。追跡できていない手は写っていないとみなす。
    bool IsHandInImage(Hand hand)
    {
        if (!hand.IsTrackedDataValid) return false;
        for (var joint = HandJointId.HandStart; joint < HandJointId.HandEnd; joint++)
        {
            if (!hand.GetJointPose(joint, out var pose)) continue;
            if (_capture.TryProjectToImage(pose.position, out var uv) && SearchCapture.IsInImage(uv, -HandInImageMargin)) return true;
        }
        return false;
    }

    // ---- 表示 ----

    void CreateVisuals()
    {
        _dot = CreateLine("Dot", false);
        _dot.numCapVertices = 8; // 短い線を太く描き、丸い端で円に見せる
        _ring = CreateLine("Ring", false);
        _frame = CreateLine("Frame", true);

        var label = new GameObject("Message");
        label.transform.SetParent(transform, false);
        _message = label.AddComponent<TextMeshPro>();
        _message.alignment = TextAlignmentOptions.Center;
        _message.fontSize = 5f; // 3Dの文字は1ポイントがおよそ0.1。localScale を掛けた大きさになる
        _message.textWrappingMode = TextWrappingModes.NoWrap;
        _message.rectTransform.sizeDelta = new Vector2(20f, 2f);
    }

    LineRenderer CreateLine(string objectName, bool loop)
    {
        var go = new GameObject(objectName);
        go.transform.SetParent(transform, false);
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = _visualMaterial;
        line.useWorldSpace = true;
        line.loop = loop;
        line.alignment = LineAlignment.View;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        return line;
    }

    void HideAll()
    {
        _dot.enabled = false;
        _ring.enabled = false;
        _frame.enabled = false;
        _message.enabled = false;
    }

    // 見た目の大きさが angle 度になる、position での長さ（m）
    float SizeAt(Vector3 position, float angle)
    {
        return Vector3.Distance(_head.position, position) * Mathf.Tan(angle * Mathf.Deg2Rad);
    }

    // 物体の表面に埋もれないよう、頭の方へ少し寄せた位置
    Vector3 TowardHead(Vector3 position)
    {
        return position + (_head.position - position).normalized * 0.01f;
    }

    void DrawDot(Vector3 position, Color color)
    {
        var size = SizeAt(position, _dotAngle);
        var center = TowardHead(position);
        _dot.positionCount = 2;
        _dot.SetPosition(0, center - _head.right * size * 0.01f);
        _dot.SetPosition(1, center + _head.right * size * 0.01f);
        _dot.widthMultiplier = size;
        _dot.startColor = _dot.endColor = color;
        _dot.enabled = true;
    }

    // 点の周りの円。progress（0〜1）の割合だけ、上から時計回りに描く。
    void DrawRing(Vector3 position, float progress, Color color)
    {
        var count = Mathf.RoundToInt(RingSegments * progress);
        if (count < 2) return;

        var radius = SizeAt(position, _ringAngle) * 0.5f;
        var center = TowardHead(position);
        _ring.positionCount = count + 1;
        for (var i = 0; i <= count; i++)
        {
            var angle = (float)i / RingSegments * Mathf.PI * 2f;
            _ring.SetPosition(i, center + (_head.up * Mathf.Cos(angle) + _head.right * Mathf.Sin(angle)) * radius);
        }
        _ring.widthMultiplier = radius * 0.15f;
        _ring.startColor = _ring.endColor = color;
        _ring.enabled = true;
    }

    // 左右のL字の角を対角とする、視線に向いた四角形（判定と同じ視線の座標で描く）
    void DrawFrame(Vector3 leftCorner, Vector3 rightCorner)
    {
        var view = new ViewBasis(_head, (leftCorner + rightCorner) * 0.5f);
        var left = view.ToLocal(leftCorner);
        var right = view.ToLocal(rightCorner);
        var z = (left.z + right.z) * 0.5f;
        _frame.positionCount = 4;
        _frame.SetPosition(0, view.ToWorld(new Vector3(left.x, left.y, z)));
        _frame.SetPosition(1, view.ToWorld(new Vector3(right.x, left.y, z)));
        _frame.SetPosition(2, view.ToWorld(new Vector3(right.x, right.y, z)));
        _frame.SetPosition(3, view.ToWorld(new Vector3(left.x, right.y, z)));
        _frame.widthMultiplier = 0.002f;
        _frame.startColor = _frame.endColor = new Color(1f, 1f, 1f, 0.6f);
        _frame.enabled = true;
    }

    // 点の下に文字を出す
    void DrawLabel(Vector3 position, string text)
    {
        var size = SizeAt(position, _ringAngle);
        _message.transform.position = TowardHead(position) - _head.up * size * 1.2f;
        _message.transform.rotation = Quaternion.LookRotation(_message.transform.position - _head.position, _head.up);
        _message.transform.localScale = Vector3.one * size;
        _message.text = text;
        _message.enabled = true;
    }

    // 取り消しや完了を、頭の前に少しの間だけ出す
    void ShowMessage(string text, float seconds)
    {
        _message.text = text;
        _messageUntil = Time.time + seconds;
    }

    void UpdateMessage()
    {
        if (_message.enabled || Time.time > _messageUntil) return;

        var position = _head.position + _head.forward * 0.8f - _head.up * 0.1f;
        _message.transform.position = position;
        _message.transform.rotation = Quaternion.LookRotation(position - _head.position, _head.up);
        _message.transform.localScale = Vector3.one * 0.06f;
        _message.enabled = true;
    }
}
