// Copyright (c) 2026 Takashi Yoshinaga
// Licensed under the MIT License. See LICENSE in the repository root.

using UnityEngine;

// ウィンドウを頭の正面に置き、頭の動きにゆっくり追従させる。
// 表示した瞬間は正面へ直接置くので、どちらを向いていても目の前に出る。
public class HeadFollow : MonoBehaviour
{
    [SerializeField, Min(0.1f)] float _distance = 0.6f;
    [Tooltip("目の高さからのずれ。マイナスで少し下に置く")]
    [SerializeField] float _heightOffset = -0.1f;
    [Tooltip("大きいほど素早く追いつく")]
    [SerializeField, Min(0.1f)] float _followSpeed = 4f;

    Transform _head;

    void OnEnable()
    {
        _head = Camera.main != null ? Camera.main.transform : null;
        if (_head == null) return;

        GetTargetPose(out var position, out var rotation);
        transform.SetPositionAndRotation(position, rotation);
    }

    void LateUpdate()
    {
        if (_head == null) return;

        GetTargetPose(out var position, out var rotation);
        var t = 1f - Mathf.Exp(-_followSpeed * Time.deltaTime);
        transform.SetPositionAndRotation(
            Vector3.Lerp(transform.position, position, t),
            Quaternion.Slerp(transform.rotation, rotation, t));
    }

    void GetTargetPose(out Vector3 position, out Quaternion rotation)
    {
        // 上下を向いてもウィンドウが床や天井へ行かないよう、水平方向の正面に置く。
        var forward = Vector3.ProjectOnPlane(_head.forward, Vector3.up);
        if (forward.sqrMagnitude < 1e-4f) forward = Vector3.ProjectOnPlane(_head.up, Vector3.up);
        forward.Normalize();

        position = _head.position + forward * _distance + Vector3.up * _heightOffset;
        // Canvasは+Z方向を向いた面が見えるので、頭から見て奥向きにする。
        rotation = Quaternion.LookRotation(position - _head.position, Vector3.up);
    }
}
