Shader "SpatialSearch/URP/Colored Points"
{
    Properties
    {
        _CameraTexture ("Camera Image", 2D) = "white" {}
        _DepthTolerance ("Depth Tolerance", Float) = 2.0
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
    // 板は重なるので、2パスで描く。
    //   1パス目 : 奥行きだけを、板の大きさ × _DepthTolerance ぶん奥へずらして書く
    //   2パス目 : 色を奥行きを書かずに描く。手前の面の板はどれも奥行きテストに通り、頂点の順に上書きされる
    // 1パスで奥行きを書くと、重なった板のどちらが手前になるかが頭のわずかな動きで入れ替わり、色がちらつく。
    // 2パスにすると重なりの色が毎フレーム同じになり、物体の後ろにある板はきちんと隠れる。
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

        TEXTURE2D(_CameraTexture);
        SAMPLER(sampler_CameraTexture);

        CBUFFER_START(UnityPerMaterial)
        float _DepthTolerance;
        half4 _TintColor;
        CBUFFER_END

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

        // 点の中心を視点から見た空間へ移し、四隅のぶんだけ画面の右・上へずらす。
        // 目ごとのビュー行列で計算するので、両目それぞれに正対する。
        float3 BillboardPositionVS(Attributes input, float objectScale)
        {
            float3 positionVS = TransformWorldToView(TransformObjectToWorld(input.positionOS));
            positionVS.xy += input.corner * objectScale;
            return positionVS;
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
                float3 positionVS = BillboardPositionVS(input, objectScale);

                // 目から見て奥へずらす。この範囲にある板は、2パス目で同じ面の板として色が描かれる。
                float sizeWS = max(abs(input.corner.x), abs(input.corner.y)) * 2.0 * objectScale;
                positionVS += normalize(positionVS) * sizeWS * _DepthTolerance;
                output.positionCS = TransformWViewToHClip(positionVS);

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
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);

                Varyings output;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformWViewToHClip(BillboardPositionVS(input, ObjectScale()));
                output.uv = input.uv;

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // テクスチャはsRGBで作ってあるので、Linearの色空間ではサンプルした時点でLinearになる。
                half3 color = SAMPLE_TEXTURE2D(_CameraTexture, sampler_CameraTexture, input.uv).rgb;

                // コントローラが近づいた・つかんだときの色付け（InteractableColorVisual が _TintColor を変える）。
                color = lerp(color, _TintColor.rgb, _TintColor.a);

                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
