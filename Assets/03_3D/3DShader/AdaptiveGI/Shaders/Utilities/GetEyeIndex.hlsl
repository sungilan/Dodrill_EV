void GetEyeIndex_float(out float EyeIndex)
{
#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
    EyeIndex = unity_StereoEyeIndex;
#else
    EyeIndex = 0;
#endif
}

void GetEyeIndex_half(out half EyeIndex)
{
#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
    EyeIndex = unity_StereoEyeIndex;
#else
    EyeIndex = 0;
#endif
}