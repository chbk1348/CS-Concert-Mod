using System;
using System.Collections.Generic;
using ColossalFramework;
using ColossalFramework.UI;
using HarmonyLib;

namespace ConcertMod.Data
{
    /// <summary>
    /// 게임 축제 정보 창(FestivalPanel)의 밴드 이름 · 인기를 모드 아티스트로. 이 창은 ConcertAI.GetBandName 을 거치지 않고
    /// 밴드 프리팹 이름(EventInfo.name) · 프리팹 인기(ConcertAI.GetPopularity)를 바로 읽어 BandNamePatch 가 닿지 않음.
    /// 표 줄 = 그 자리(밴드 프리팹)의 다음 차례 아티스트(Lineup.Pick), 지금 · 다음 공연 = 그 공연의 아티스트.
    /// </summary>
    internal static class FestivalPanelPatch
    {
        static readonly AccessTools.FieldRef<FestivalPanel, UITemplateList<UIPanel>> bands = AccessTools.FieldRefAccess<FestivalPanel, UITemplateList<UIPanel>>("m_bands");
        static readonly AccessTools.FieldRef<FestivalPanel, UILabel> nowPlaying = AccessTools.FieldRefAccess<FestivalPanel, UILabel>("m_nowPlayingBand");

        /// <summary>아티스트 · 인기 표(창을 열 때 · 갱신할 때마다).</summary>
        [HarmonyPatch(typeof(FestivalPanel), "RefreshBands")]
        static class RefreshBandsPatch
        {
            static void Postfix(FestivalPanel __instance) => Core.Safe.Run("축제 창: 아티스트 표", () =>
            {
                var list = bands(__instance);
                if (list == null) return;
                foreach (UIPanel row in list.items)
                {
                    if (!(row.objectUserData is uint index)) continue;
                    var b = Lineup.Pick(PrefabCollection<EventInfo>.GetLoaded(index));
                    if (b == null) continue;
                    row.Find<UILabel>("Name").text = b.Name;
                    row.Find<UILabel>("Popularity").text = Lineup.Pop(b.Key).ToString();
                }
            });
        }

        /// <summary>'지금 공연 중' 이름.</summary>
        [HarmonyPatch(typeof(FestivalPanel), "UpdateBindings")]
        static class NowPlayingPatch
        {
            static void Postfix(FestivalPanel __instance) => Core.Safe.Run("축제 창: 지금 공연", () =>
            {
                var label = nowPlaying(__instance);
                if (label == null || string.IsNullOrEmpty(label.text)) return;
                ushort building = WorldInfoPanel.GetCurrentInstanceID().Building;
                if (building == 0) return;
                ushort id = Singleton<BuildingManager>.instance.m_buildings.m_buffer[building].m_eventIndex;
                if (id == 0) return;
                var b = Shows.BandOf(id, ref Singleton<EventManager>.instance.m_events.m_buffer[id]);
                if (b != null) label.text = b.Name;
            });
        }

        /// <summary>지금 공연 칸(이벤트 값만 넘어와 번호를 찾음 — 같은 프리팹 · 같은 시작 틀).</summary>
        [HarmonyPatch(typeof(FestivalPanel), "RefreshCurrentConcert")]
        static class CurrentPatch
        {
            static void Postfix(UIPanel panel, EventData concert) => Core.Safe.Run("축제 창: 지금 공연 칸", () =>
            {
                var em = Singleton<EventManager>.instance;
                for (ushort id = 1; id < em.m_events.m_size; id++)
                {
                    ref EventData d = ref em.m_events.m_buffer[id];
                    if (d.m_startFrame != concert.m_startFrame || d.Info != concert.Info) continue;
                    var b = Shows.BandOf(id, ref d);
                    if (b != null) panel.Find<UILabel>("Name").text = b.Name;
                    return;
                }
            });
        }

        /// <summary>다음 공연 칸(아직 안 만든 공연 — 모드가 미리 고른 아티스트).</summary>
        [HarmonyPatch(typeof(FestivalPanel), "RefreshFutureConcert")]
        static class FuturePatch
        {
            static void Postfix(UIPanel panel, EventManager.FutureEvent concert) => Core.Safe.Run("축제 창: 다음 공연 칸", () =>
            {
                var b = Shows.Predict(concert.m_info, Shows.Festival);
                if (b != null) panel.Find<UILabel>("Name").text = b.Name;
            });
        }
    }
}
