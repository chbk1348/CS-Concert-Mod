using System;
using System.Collections.Generic;
using System.IO;
using ColossalFramework;
using HarmonyLib;
using UnityEngine;

namespace ConcertMod.Data
{
    /// <summary>
    /// 공연 중 소리(캔버스 설정 · 소리): 콘서트 DLC 공연(페스티벌 · 빌린 경기장)이 열리는 동안
    /// 공연장 관중 · 공연 소리(ConcertAI.m_crowdAudio)를 1분에 8초만이 아니라 계속 · 더 멀리서 들리게 하고, 게임 배경음악은 줄였다가 끝나면 되돌림.
    /// 아티스트 음악: 모드 폴더 Music/<아티스트 이름>/ 에 넣은 곡을 공연장 자리에서 틀어 줌(곡은 시장이 직접 넣음).
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
            Band band = null;
            ushort stage = 0;
            for (int i = 1; i < em.m_events.m_size && !live; i++)
            {
                ref var e = ref em.m_events.m_buffer[i];
                live = (e.m_flags & (EventData.Flags.Active | EventData.Flags.Deleted)) == EventData.Flags.Active && e.Info?.m_eventAI is ConcertAI;
                if (live) { band = Shows.BandOf((ushort)i, ref e); stage = e.m_building; }
            }
            Live = live;
            Music(live && stage != 0 && band != null && ConcertSetting.Current.ConcertSound ? band.Name : null, stage);
        }

        public static void Clear()
        {
            Live = false;
            duck = 1f;
            StopMusic();
            if (player != null) UnityEngine.Object.Destroy(player.gameObject);
            player = null;
        }

        // ── 아티스트 음악: 모드 폴더 Music/<아티스트 이름>/ 의 .ogg · .wav 를 공연 중 공연장에서 차례로(게임 Unity 5.6 은 mp3 를 못 읽음)

        static AudioSource player;
        static WWW loading;
        static string artist;
        static string[] songs;
        static int next;

        /// <summary>지금 곡을 틀기 시작했나(끝난 곡은 time 이 0 으로 돌아가 '안 틀었음'과 구별이 안 됨).</summary>
        static bool started;

        /// <summary>그 아티스트 곡 파일(이름 순 · 폴더가 없으면 빈 목록).</summary>
        static string[] Songs(string name)
        {
            string root = CSCommonModBase.Folder();
            if (root == null) return new string[0];
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            string dir = Path.Combine(Path.Combine(root, "Music"), name);
            if (!Directory.Exists(dir)) return new string[0];
            var list = new List<string>();
            foreach (var f in Directory.GetFiles(dir))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext == ".ogg" || ext == ".wav") list.Add(f);
            }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            Core.Log.Info($"아티스트 음악 {name}: {list.Count}곡 ({dir})");
            return list.ToArray();
        }

        /// <summary>본 스레드 1초마다: 공연 중인 아티스트 곡을 공연장 자리에서 틀고, 끝나면 다음 곡 · 공연이 끝나면 멈춤.</summary>
        static void Music(string name, ushort stage)
        {
            if (name != artist)
            {
                StopMusic();
                artist = name;
                songs = name != null ? Songs(name) : null;
                next = 0;
            }
            if (songs == null || songs.Length == 0) return;
            if (player == null)
            {
                var go = new GameObject("ConcertMod Music");
                UnityEngine.Object.DontDestroyOnLoad(go);
                player = go.AddComponent<AudioSource>();
                player.spatialBlend = 1f;
                player.rolloffMode = AudioRolloffMode.Linear;
                player.minDistance = 50f;
                player.maxDistance = CrowdDistance;
                player.dopplerLevel = 0f;
            }
            player.transform.position = Singleton<BuildingManager>.instance.m_buildings.m_buffer[stage].m_position;
            var am = Singleton<AudioManager>.instance;
            player.volume = am.MuteAll ? 0f : am.MasterVolume;
            if (loading != null)
            {
                if (!loading.isDone) return;
                var www = loading;
                loading = null;
                AudioClip clip = null;
                if (string.IsNullOrEmpty(www.error))
                    clip = www.GetAudioClipCompressed(false, www.url.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? AudioType.WAV : AudioType.OGGVORBIS);
                else Core.Log.Warn($"아티스트 음악을 읽지 못함: {www.url} ({www.error})");
                www.Dispose();
                if (clip == null) return;
                DropClip();
                player.clip = clip;
                started = false;
            }
            if (player.clip != null && !started)
            {
                if (player.clip.loadState == AudioDataLoadState.Loading) return;
                if (player.clip.loadState == AudioDataLoadState.Loaded)
                {
                    player.Play();
                    started = true;
                    return;
                }
            }
            if (player.isPlaying) return;
            // 곡이 끝났거나(또는 못 읽었거나) 아직 없음 → 다음 곡
            string file = songs[next % songs.Length];
            next++;
            loading = new WWW("file:///" + file.Replace('\\', '/'));
        }

        static void StopMusic()
        {
            loading?.Dispose();
            loading = null;
            artist = null;
            songs = null;
            if (player != null) player.Stop();
            DropClip();
        }

        static void DropClip()
        {
            if (player == null || player.clip == null) return;
            var c = player.clip;
            player.clip = null;
            UnityEngine.Object.Destroy(c);
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
            bool method = TargetMethod() != null, field = AccessTools.Field(typeof(AudioManager), "m_musicVolume") != null;
            if (method && field) return true;
            // 게임 판에는 둘 다 있는데 0.3.0 로그에서 false — 어느 쪽이 없는지 남겨 원인을 좁힘
            Core.Log.Warn($"공연 중 배경음악 줄이기를 쉼(게임 판 확인): AudioManager.LateUpdate {(method ? "있음" : "없음")} · m_musicVolume {(field ? "있음" : "없음")}");
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
