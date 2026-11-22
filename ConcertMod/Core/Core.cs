using System;
using System.Collections.Generic;

namespace ConcertMod.Core
{
    /// <summary>output_log.txt에 남김(CSCommon CSCommonLog, 머리 "[Concert Extension]: ").</summary>
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

        /// <summary>자리마다 마지막 기록 시각(초). 시뮬레이션 스레드(AddAction)에서도 불려 lock · 스레드와 상관없는 시계를 씀.</summary>
        static readonly Dictionary<string, double> lastLogged = new();

        static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

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
            double now = clock.Elapsed.TotalSeconds;
            bool first, again = false;
            lock (lastLogged)
            {
                first = !lastLogged.TryGetValue(where, out double last);
                if (!first) again = now - last >= RepeatSeconds;
                if (first || again) lastLogged[where] = now;
            }
            if (first) Log.Error($"{where} 오류(처음 한 번 전체 기록):\n{e}");
            else if (again) Log.Error($"{where} 오류 계속: {e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>Harmony 패치 적용 · 해제(CSCommon CSCommonPatcher: 이 어셈블리의 [HarmonyPatch]를 모두 붙임).</summary>
    public sealed class ConcertPatcher : CSCommonPatcher
    {
    }
}
