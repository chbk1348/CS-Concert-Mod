using System;
using System.Collections.Generic;
using UnityEngine;

namespace ConcertMod.Core
{
    /// <summary>output_log.txt에 남김(CSCommon CSCommonLog, 머리 "[콘서트 확장판]: ").</summary>
    internal static class Log
    {
        public static void Info(string message) => CSCommonLog.Info(message);

        /// <summary>설정 '자세한 로그'가 켜졌을 때만 남김.</summary>
        public static void Detail(string message)
        {
            if (ConcertSetting.Current.VerboseLog) CSCommonLog.Info(message);
        }

        public static void Warn(string message) => CSCommonLog.Warn(message);

        public static void Error(string message) => CSCommonLog.Error(message);
    }

    /// <summary>매 프레임 처리 감싸기: 처음 한 번은 전체 로그, 그 뒤엔 같은 자리 오류를 1분에 한 번만. 오류가 나도 다른 처리는 계속 돎.</summary>
    internal static class Safe
    {
        const float RepeatSeconds = 60f;

        static readonly Dictionary<string, float> lastLogged = new();

        public static void Run(string where, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Report(where, e);
            }
        }

        public static void Report(string where, Exception e)
        {
            float now = Time.realtimeSinceStartup;
            if (!lastLogged.TryGetValue(where, out float last))
            {
                lastLogged[where] = now;
                Log.Error($"{where} 오류(처음 한 번 전체 기록):\n{e}");
            }
            else if (now - last >= RepeatSeconds)
            {
                lastLogged[where] = now;
                Log.Error($"{where} 오류 계속: {e.GetType().Name}: {e.Message}");
            }
        }
    }

    /// <summary>Harmony 패치 적용 · 해제(CSCommon CSCommonPatcher: 이 어셈블리의 [HarmonyPatch]를 모두 붙임).</summary>
    public sealed class ConcertPatcher : CSCommonPatcher
    {
    }
}
