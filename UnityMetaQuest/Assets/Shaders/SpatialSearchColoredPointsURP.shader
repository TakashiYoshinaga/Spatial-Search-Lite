Shader "SpatialSearch/URP/Colored Points"
{
    Properties
    {
        _PointScale ("Point Scale", Float) = 1.5
        _MinPixelSize ("Min Pixel Size", Float) = 2.0
        _MaxPixelSize ("Max Pixel Size", Float) = 32.0
        _DepthTolerance ("Depth Tolerance", Float) = 2.0
        _TintColor ("Tint Color (A = strength)", Color) = (1, 1, 1, 0)
    }

    // 点群は2パスで描く。
    //   1パス目 : 奥行きだけを、点の大きさ × _DepthTolerance ぶん奥へずらして書く
    //   2パス目 : 色を奥行きを書かずに描く。手前の面の点はどれも奥行きテストに通り、頂点の順に上書きされる
    // 1パスで奥行きを書くと、重なった点のどちらが手前になるかが頭のわずかな動きで入れ替わり、色がちらつく。
    // 2パスにすると重なりの色が毎フレーム同じになり、物体の後ろにある点はきちんと隠れる。
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

        CBUFFER_START(UnityPerMaterial)
        float _PointScale;
        float _MinPixelSize;
        float _MaxPixelSize;
        float _DepthTolerance;
        half4 _TintColor;
        CBUFFER_END

        struct Attributes
        {
            float3 positionOS : POSITION;
            half4 color : COLOR;
            float2 size : TEXCOORD0; // x = 撮影したときの点の間隔（オブジェクト空間のm）
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        // 点の大きさ（ワールド空間のm）= 撮影したときの点の間隔 × _PointScale（1より大きくして隙間を埋める）
        float PointSizeWS(Attributes input)
        {
            float objectScale = length(GetObjectToWorldMatrix()._m00_m10_m20);
            return input.size.x * objectScale * _PointScale;
        }

        // 点の大きさを画面上のピクセル数へ直す。どの距離から見ても、顔に近づけても隙間ができない。
        // マルチビュー描画では目ごとに変えてよいのは位置だけなので、左目の行列で計算して両目で同じ値にする。
        float PointPixelSize(float3 positionWS, float sizeWS)
        {
        #if defined(USING_STEREO_MATRICES)
            float w = mul(unity_StereoMatrixVP[0], float4(positionWS, 1.0)).w;
            float focal = abs(unity_StereoMatrixP[0]._m11);
        #else
            float w = mul(UNITY_MATRIX_VP, float4(positionWS, 1.0)).w;
            float focal = abs(UNITY_MATRIX_P._m11);
        #endif
            float pixels = sizeWS * focal * _ScaledScreenParams.y * 0.5 / max(w, 1e-4);
            return clamp(pixels, _MinPixelSize, _MaxPixelSize);
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
                float pointSize : PSIZE;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);

                Varyings output;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS);
                float sizeWS = PointSizeWS(input);
                output.pointSize = PointPixelSize(positionWS, sizeWS);

                // 目から見て奥へずらす。この範囲にある点は、2パス目で同じ面の点として色が描かれる。
                float3 viewDirection = normalize(positionWS - GetCameraPositionWS());
                output.positionCS = TransformWorldToHClip(positionWS + viewDirection * sizeWS * _DepthTolerance);

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
                half4 color : COLOR;
                float pointSize : PSIZE;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);

                Varyings output;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.pointSize = PointPixelSize(positionWS, PointSizeWS(input));

                // 頂点カラーにはカメラ画像の色が sRGB のまま入っているので、Linear へ直す。
                half3 color = input.color.rgb;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                color = SRGBToLinear(color);
                #endif

                // コントローラが近づいた・つかんだときの色付け（InteractableColorVisual が _TintColor を変える）。
                output.color = half4(lerp(color, _TintColor.rgb, _TintColor.a), 1.0);

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return input.color;
            }
            ENDHLSL
        }
    }
}
