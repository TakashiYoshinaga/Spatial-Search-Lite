// Copyright (c) 2026 Takashi Yoshinaga
// Licensed under the MIT License. See LICENSE in the repository root.

Shader "SpatialSearch/URP/Colored Points"
{
    Properties
    {
        _CameraTexture ("Camera Image", 2D) = "white" {}
        _DepthTolerance ("Depth Tolerance", Float) = 2.0
        _FadeStart ("Fade Start (cos of angle)", Range(-1, 1)) = 0.6
        _FadeEnd ("Fade End (cos of angle)", Range(-1, 1)) = -0.2
        [HideInInspector] _FrontOS ("Front Direction (Object Space)", Vector) = (0, 0, 1, 0)
        _TintColor ("Tint Color (A = strength)", Color) = (1, 1, 1, 0)
    }

    // 見つかった物体を、点ごとの四角形（深度1画素ぶん）に撮影したカメラ画像を貼って描く。
    // 頂点のUV（uv0）が、その四隅がカメラ画像のどこに写っていたか（SearchResultMeshBuilder が作る）。
    // 四角形の中は画素ごとに画像を引くので、点ごとに1色を割り当てるのと違い、粗いドットにならない。
    //
    // 頂点の位置は点の中心で、uv1 が中心から四隅への「画面の右・上へのずらし量」（m）。
    // 視点から見た空間でずらすので、四角形は常に視点に正対する（ビルボード）。
    // 四隅とUVの対応は撮影したままなので、撮影した向きから見るとぴったり合い、傾けると合わなくなる。
    //
    // そこで、物体の正面から離れて見るほど物体を透明にする。
    //   物体の正面の向き（点群から求めた面の向き）と、点から今の視点への向き の内積（正面から見れば1、真横なら0、裏側なら負）が
    //   _FadeStart 以上なら不透明、_FadeEnd 以下なら完全に透明で、その間はなめらかに変える。
    //   正面の向きは、物体のローカル座標で _FrontOS に入れる（GrabbableSearchResultView）。
    //   ローカル座標なので、物体をつかんで回すと正面も一緒に回る。
    //   撮影したカメラの方向ではなく面の向きを使うのは、斜めから撮った物（机の上の本など）でも、
    //   面を自分に向けた姿勢を基準に、上下左右どちらへ傾けても同じように透けるようにするため。
    //
    // 板は重なるので、2パスで描く。
    //   1パス目 : 奥行きだけを、板の大きさ × _DepthTolerance ぶん奥へずらして書く
    //   2パス目 : 色を奥行きを書かずに、透明度をつけて描く。手前の面の板はどれも奥行きテストに通り、頂点の順に重なる
    // 1パスで奥行きを書くと、重なった板のどちらが手前になるかが頭のわずかな動きで入れ替わり、色がちらつく。
    // 2パスにすると重なりの色が毎フレーム同じになり、物体の後ろにある板はきちんと隠れる。
    // 奥行きは透明な板にも書くが、完全に透明になった板は描かない（後ろの物を隠さない）。
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_CameraTexture);
        SAMPLER(sampler_CameraTexture);

        CBUFFER_START(UnityPerMaterial)
        float _DepthTolerance;
        float _FadeStart;
        float _FadeEnd;
        float4 _FrontOS;
        half4 _TintColor;
        CBUFFER_END

        // これより薄い板は描かない
        static const float MinAlpha = 0.004;

        struct Attributes
        {
            float3 positionOS : POSITION; // 点の中心
            float2 uv : TEXCOORD0;        // この四隅のカメラ画像上のUV
            float2 corner : TEXCOORD1;    // 中心から四隅への、画面の右・上へのずらし量（オブジェクト空間のm）
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        // オブジェクトの拡大率。両手で拡大縮小したとき、四角形の大きさも一緒に変える。
        float ObjectScale()
        {
            return length(GetObjectToWorldMatrix()._m00_m10_m20);
        }

        // 点の中心を、視点から見た空間へ移す。目ごとのビュー行列なので、原点がその目の位置になる。
        float3 CenterPositionVS(Attributes input)
        {
            return TransformWorldToView(TransformObjectToWorld(input.positionOS));
        }

        // 物体の正面に対して、今の視点がどちら側にあるか。1なら正面から、0なら真横、負なら裏側から見ている。
        // その内積から、不透明度（1〜0）を決める。
        float ViewAlpha(float3 centerVS)
        {
            float3 frontVS = TransformWorldToViewDir(TransformObjectToWorldDir(_FrontOS.xyz));
            float3 toEyeVS = normalize(-centerVS);
            float facing = dot(frontVS, toEyeVS);

            float t = saturate((facing - _FadeEnd) / max(_FadeStart - _FadeEnd, 1e-4));
            return t * t * (3.0 - 2.0 * t);
        }
        ENDHLSL

        // ---- 1パス目: 奥行きだけを少し奥へずらして書く ----
        Pass
        {
            Name "PointDepth"
            Tags { "LightMode" = "SRPDefaultUnlit" } // URPは UniversalForward より先にこのパスを描く

            ColorMask 0
            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);

                Varyings output;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float objectScale = ObjectScale();
                float3 centerVS = CenterPositionVS(input);

                float3 positionVS = centerVS;
                positionVS.xy += input.corner * objectScale;

                // 目から見て奥へずらす。この範囲にある板は、2パス目で同じ面の板として色が描かれる。
                float sizeWS = max(abs(input.corner.x), abs(input.corner.y)) * 2.0 * objectScale;
                positionVS += normalize(positionVS) * sizeWS * _DepthTolerance;

                // 完全に透明な板は、4頂点とも同じ点にして面積をなくす（描かれない）。
                bool visible = ViewAlpha(centerVS) > MinAlpha;
                output.positionCS = visible ? TransformWViewToHClip(positionVS) : float4(0.0, 0.0, 0.0, 1.0);

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // ---- 2パス目: 色を描く ----
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float alpha : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);

                Varyings output;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 centerVS = CenterPositionVS(input);
                float alpha = ViewAlpha(centerVS);

                float3 positionVS = centerVS;
                positionVS.xy += input.corner * ObjectScale();

                output.positionCS = alpha > MinAlpha ? TransformWViewToHClip(positionVS) : float4(0.0, 0.0, 0.0, 1.0);
                output.uv = input.uv;
                output.alpha = alpha;

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // テクスチャはsRGBで作ってあるので、Linearの色空間ではサンプルした時点でLinearになる。
                half3 color = SAMPLE_TEXTURE2D(_CameraTexture, sampler_CameraTexture, input.uv).rgb;

                // コントローラが近づいた・つかんだときの色付け（InteractableColorVisual が _TintColor を変える）。
                color = lerp(color, _TintColor.rgb, _TintColor.a);

                return half4(color, input.alpha);
            }
            ENDHLSL
        }
    }
}
