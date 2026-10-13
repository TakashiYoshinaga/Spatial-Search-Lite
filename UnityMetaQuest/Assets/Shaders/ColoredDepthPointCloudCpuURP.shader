// Copyright (c) 2026 Takashi Yoshinaga
// Licensed under the MIT License. See LICENSE in the repository root.

Shader "TryAR/URP/Colored Depth Point Cloud CPU"
{
    Properties
    {
        _CameraTexture ("Camera Texture", 2D) = "black" {}
        _PointAlpha ("Point Alpha", Range(0, 1)) = 0.9
        _DepthRange ("Depth Range", Vector) = (0.1, 5.0, 0.0, 0.0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            StructuredBuffer<float> _LinearDepthBuffer;
            float4 _EnvironmentDepthZBufferParams;
            TEXTURE2D(_CameraTexture);
            SAMPLER(sampler_CameraTexture);

            CBUFFER_START(UnityPerMaterial)
            float _PointAlpha;
            float4 _DepthRange;
            float4x4 _InverseLocalReprojection;
            float4x4 _ColorCameraWorldToLocal;
            float4 _ColorFocalLength;
            float4 _ColorPrincipalPoint;
            float4 _ColorSensorCropRect;
            CBUFFER_END

            // Each depth pixel is drawn as a quad covering its footprint (see DepthPointCloudMeshUtility.BuildQuadMesh).
            // Vertex index = pixel index * 4 + corner, and uv is the depth-texture uv of that corner.
            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                // Corner position in color sensor pixels, still multiplied by its camera-space depth: (sensor.xy * z, z).
                // The rasterizer interpolates it perspective-correctly across the quad and Frag divides by z, which gives
                // the exact camera uv of every pixel in the quad and not just the corners.
                float3 cameraProjective : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            bool IsFiniteValue(float value)
            {
                return value == value && abs(value) < 1e8;
            }

            bool IsFiniteFloat3(float3 value)
            {
                return IsFiniteValue(value.x) && IsFiniteValue(value.y) && IsFiniteValue(value.z);
            }

            // All 4 corners share the depth of their pixel, so the quad is a flat patch facing the depth camera.
            // Neighboring quads then tile the depth image without gaps when seen from the depth camera.
            bool TryReconstructLocalPosition(float2 uv, float linearDepth, out float3 localPosition)
            {
                localPosition = 0.0;

                if (linearDepth < _DepthRange.x || linearDepth > _DepthRange.y || !IsFiniteValue(linearDepth))
                {
                    return false;
                }

                float ndcDepth = (_EnvironmentDepthZBufferParams.x / linearDepth) - _EnvironmentDepthZBufferParams.y;
                if (!IsFiniteValue(ndcDepth))
                {
                    return false;
                }

                float4 clipPosition = float4(uv * 2.0 - 1.0, ndcDepth, 1.0);
                float4 localPositionH = mul(_InverseLocalReprojection, clipPosition);
                if (abs(localPositionH.w) < 1e-5)
                {
                    return false;
                }

                localPosition = localPositionH.xyz / localPositionH.w;
                return IsFiniteFloat3(localPosition);
            }

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);

                Varyings output;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float linearDepth = _LinearDepthBuffer[input.vertexID >> 2];

                float3 localPosition;
                bool isValid = TryReconstructLocalPosition(input.uv, linearDepth, localPosition);

                float3 cameraProjective = 0.0;
                if (isValid)
                {
                    float3 worldPosition = TransformObjectToWorld(localPosition);
                    float3 cameraPosition = mul(_ColorCameraWorldToLocal, float4(worldPosition, 1.0)).xyz;
                    cameraProjective = float3(
                        cameraPosition.xy * _ColorFocalLength.xy + _ColorPrincipalPoint.xy * cameraPosition.z,
                        cameraPosition.z);
                }

                // The 4 corners of an invalid pixel all collapse to one point, so the quad has no area and is never rasterized.
                output.cameraProjective = cameraProjective;
                output.positionCS = isValid
                    ? TransformObjectToHClip(localPosition)
                    : float4(0.0, 0.0, 0.0, 1.0);

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Behind the color camera.
                clip(input.cameraProjective.z - 1e-5);

                float2 sensorPoint = input.cameraProjective.xy / input.cameraProjective.z;
                float2 cameraUv = (sensorPoint - _ColorSensorCropRect.xy) / _ColorSensorCropRect.zw;

                // Outside the color image. Cut per pixel, so the edge of the image is not quantized to depth pixels.
                clip(min(cameraUv, 1.0 - cameraUv));

                half4 cameraColor = SAMPLE_TEXTURE2D(_CameraTexture, sampler_CameraTexture, cameraUv);
                cameraColor.a *= _PointAlpha;
                return cameraColor;
            }
            ENDHLSL
        }
    }
}
