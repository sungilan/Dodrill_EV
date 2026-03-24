using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.XR.Management;

namespace XRAirpotrSecurity
{
    public enum PlatformType
    {
        PC,
        Mobile,
        VR,
    }

    public class Utils
    {

        public static string FormatToKorean(string isoString)
        {
            if (string.IsNullOrEmpty(isoString))
                return "";

            try
            {
                // ISO 8601 파싱
                DateTimeOffset dto = DateTimeOffset.Parse(isoString, null, DateTimeStyles.RoundtripKind);

                // 한국 시간 기준으로 변환 (원래 +09:00이면 이미 KST)
                DateTime localTime = dto.LocalDateTime;

                // "tt"는 AM/PM → 오전/오후
                string formatted = localTime.ToString("yyyy년 M월 d일 tt h시 mm분", CultureInfo.CreateSpecificCulture("ko-KR"));
                return formatted;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("날짜 변환 실패: " + ex.Message);
                return isoString;
            }
        }

        public static PlatformType GetPlatformType()


        {
#if UNITY_EDITOR || UNITY_STANDALONE
            // Editor or Standalone build
#if ENABLE_VR || ENABLE_XR_MODULE
            if (XRGeneralSettings.Instance != null &&
                XRGeneralSettings.Instance.Manager != null &&
                XRGeneralSettings.Instance.Manager.isInitializationComplete)
            {
                return PlatformType.VR;
            }
#endif
            return PlatformType.PC;

#elif UNITY_ANDROID || UNITY_IOS
#if ENABLE_VR || ENABLE_XR_MODULE
        if (XRGeneralSettings.Instance != null &&
            XRGeneralSettings.Instance.Manager != null &&
            XRGeneralSettings.Instance.Manager.isInitializationComplete
            )
        {
            return PlatformType.VR;
        }
#endif
        return PlatformType.Mobile;

#else
        // fallback for unknown platforms
        return PlatformType.PC;
#endif
        }
        /// <summary>
        /// float 초 단위를 "MM:SS.ss" 형태로 변환
        /// </summary>
        public static string ToClockFormat(float time)
        {
            int minutes = Mathf.FloorToInt(time / 60f);
            float seconds = time % 60f;
            return $"{minutes:00}:{seconds:00.00}";
        }
    }
}