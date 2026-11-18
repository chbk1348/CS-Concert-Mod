using System;
using ColossalFramework;
using HarmonyLib;
using UnityEngine;

namespace ConcertMod.Data
{
    /// <summary>
    /// 공연 중 소리(캔버스 설정 · 소리): 콘서트 DLC 공연(페스티벌 · 빌린 경기장)이 열리는 동안
    /// 공연장 관중 · 공연 소리(ConcertAI.m_crowdAudio)를 1분에 8초만이 아니라 계속 · 더 멀리서 들리게 하고, 게임 배경음악은 줄였다가 끝나면 되돌림.
    /// 콘서트 DLC 에는 따로 배경음악 파일이 없어(게임 Files\Audio) 배경음악을 바꾸는 대신 공연 소리가 앞에 나오게 함.
    /// </summary>
    internal static class Sound
    {
        /// <summary>공연 소리가 들리는 거리(게임 300) · 공연 중 배경음악 크기(원래의 비율) · 줄고 느는 빠르기(초당).</summary>
        const float CrowdDistance = 600f, MusicDuck = 0.15f, FadePerSecond = 0.5f;

        /// <summary>지금 열리는 콘서트가 있는지(본 스레드 1초마다).</summary>
        public static bool Live { get; private set; }

        static float duck = 1f;

        public static void Tick()
        {
            var em = Singleton<EventManager>.instance;
            bool live = false;
            for (int i = 1; i < em.m_events.m_size && !live; i++)
            {
                ref var e = ref em.m_events.m_buffer[i];
                live = (e.m_flags & (EventData.Flags.Active | EventData.Flags.Deleted)) == EventData.Flags.Active && e.Info?.m_eventAI is ConcertAI;
            }
            Live = live;
        }

        public static void Clear()
        {
            Live = false;
            duck = 1f;
        }

        public static void OnAudioState(ConcertAI ai, ref EventData data, ref float maxDistance, ref AudioInfo clip)
        {
            if (!ConcertSetting.Current.ConcertSound || (data.m_flags & EventData.Flags.Active) == 0 || ai.m_crowdAudio == null) return;
            clip = ai.m_crowdAudio;
            maxDistance = Mathf.Max(maxDistance, CrowdDistance);
        }

        /// <summary>배경음악 크기 필드(패치가 걸린 뒤 처음 쓸 때 찾음 — 없으면 MusicDuckPatch.Prepare 가 패치를 걸지 않음).</summary>
        static AccessTools.FieldRef<AudioManager, float> musicVolume;

        /// <summary>AudioManager.LateUpdate 뒤: 공연 중이면 배경음악 크기를 천천히 줄이고, 끝나면 천천히 되돌림.</summary>
        public static void OnLateUpdate(AudioManager am)
        {
            float target = ConcertSetting.Current.ConcertMusic && Live ? MusicDuck : 1f;
            duck = Mathf.MoveTowards(duck, target, Time.deltaTime * FadePerSecond);
            if (duck >= 1f) return;
            musicVolume ??= AccessTools.FieldRefAccess<AudioManager, float>("m_musicVolume");
            musicVolume(am) *= duck;
        }
    }

    [HarmonyPatch(typeof(ConcertAI), nameof(ConcertAI.GetAudioState))]
    internal static class ConcertAudioPatch
    {
        static void Postfix(ConcertAI __instance, ref EventData data, ref float maxDistance, ref AudioInfo clip)
        {
            try
            {
                Sound.OnAudioState(__instance, ref data, ref maxDistance, ref clip);
            }
            catch (Exception e)
            {
                Core.Safe.Report("공연 소리", e);
            }
        }
    }

    /// <summary>게임 배경음악 크기(AudioManager.LateUpdate 가 매 프레임 다시 셈). 게임 판에 메서드가 없으면 이 패치만 건너뜀.</summary>
    [HarmonyPatch]
    [CSCommonOptionalPatch]
    internal static class MusicDuckPatch
    {
        static System.Reflection.MethodBase TargetMethod() => AccessTools.Method(typeof(AudioManager), "LateUpdate");

        static bool Prepare()
        {
            if (TargetMethod() != null && AccessTools.Field(typeof(AudioManager), "m_musicVolume") != null) return true;
            Core.Log.Warn("AudioManager.LateUpdate · m_musicVolume 를 찾지 못해 공연 중 배경음악 줄이기를 쉼(게임 판 확인)");
            return false;
        }

        static void Postfix(AudioManager __instance)
        {
            try
            {
                Sound.OnLateUpdate(__instance);
            }
            catch (Exception e)
            {
                Core.Safe.Report("공연 중 배경음악", e);
            }
        }
    }
}
