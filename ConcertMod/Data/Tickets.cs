using System;
using System.Collections.Generic;
using ColossalFramework;
using ColossalFramework.Math;
using HarmonyLib;
using UnityEngine;

namespace ConcertMod.Data
{
    /// <summary>예매 한 건(방금 예매 줄): 게임 시각 · 실제 주민 이름 · 관광객인지 · 예매처 · 등급 · 매수.</summary>
    internal struct Buy
    {
        public DateTime At;
        public string Name;
        public bool Tourist;
        public int Site, Grade, Count;
    }

    /// <summary>공연 하나의 예매(페스티벌 다음 공연): 오픈 시각 · 정원 · 원하는 사람 · 지금까지 판 매수.</summary>
    internal sealed class Sale
    {
        public Band Band;
        public DateTime Start, Open;
        public int Days, Cap, Want, Sold, Poster;

        /// <summary>단독 예매처(승인한 예매처 단독 제안, 없으면 -1).</summary>
        public int Site = -1;

        public float Fill => Cap > 0 ? Mathf.Clamp01((float)Sold / Cap) : 0f;
        public bool Opened => SimulationManager.instance.m_currentGameTime >= Open;
        public bool SoldOut => Sold >= Cap;
    }

    /// <summary>
    /// 예매(캔버스 예매 · 오픈 전 · 대기줄): 공연 7일 전 20:00 오픈 → 원하는 사람(인기 · 입장권 · 정원) 중 오픈 뒤 시간이 지날수록 팔림(앞 반나절에 몰림).
    /// 정원 = 페스티벌 방문 자리 × 회차. 공연 날 게임이 받는 관객 자리(ConcertAI.GetCapacity)를 판 비율만큼으로 줄여 예매한 만큼만 와요.
    /// 방금 예매는 이 도시의 실제 주민 · 관광객 이름(게임 시민)으로.
    /// </summary>
    internal static class Tickets
    {
        public static readonly string[] Sites = { "티켓온", "무대석", "좌석숲", "바로표" };
        static readonly float[] Shares = { 0.4f, 0.28f, 0.18f, 0.14f };

        /// <summary>오픈: 공연 며칠 전 · 몇 시. 판매 속도(게임 시간): 오픈 뒤 이 시간마다 남은 수요의 63% 가 팔림. 대기줄: 오픈 뒤 이 시간 안 · 원하는 사람이 정원의 이 배수를 넘을 때.</summary>
        public const int OpenHour = 20;

        /// <summary>설정에서 고르는 오픈 날(공연 며칠 전) · 한 사람 최대 매수 후보.</summary>
        public static readonly int[] OpenDayChoices = { 3, 5, 7, 10, 14 }, MaxChoices = { 1, 2, 4, 6, 8 };

        public static int OpenDays => Mathf.Clamp(ConcertSetting.Current.TicketOpenDays, 1, 30);

        static int MaxPerPerson => Mathf.Clamp(ConcertSetting.Current.TicketMax, 1, 10);
        const float TauHours = 10f, QueueHours = 1.5f, QueueRatio = 1.5f;

        public static Sale Current;
        public static readonly List<Buy> Feed = new();

        /// <summary>페스티벌 한 회차 정원(방문 자리 × 5/4, 게임 MonumentAI 와 같은 셈).</summary>
        public static int SessionCap(ushort building)
        {
            if (building == 0) return 0;
            var ai = Singleton<BuildingManager>.instance.m_buildings.m_buffer[building].Info?.m_buildingAI as MonumentAI;
            return ai == null ? 0 : (ai.m_visitPlaceCount0 + ai.m_visitPlaceCount1 + ai.m_visitPlaceCount2) * 5 / 4;
        }

        /// <summary>원하는 사람: 정원 × (0.5 + 인기 비율 × 1.8) × 입장권(기본보다 비싸면 줄고 싸면 늚).</summary>
        static int WantOf(EventInfo info, int cap)
        {
            var ai = info.m_eventAI;
            int pop = ConcertAI.GetPopularity(info);
            float price = ai.m_ticketPrice + info.m_ticketPriceOffset, rel = ai.m_ticketPrice > 0 ? price / ai.m_ticketPrice : 1f;
            float priceF = Mathf.Clamp(1f - (rel - 1f) * 1.2f, 0.4f, 1.6f);
            return Mathf.RoundToInt(cap * (0.5f + pop / 100f * 1.8f) * priceF);
        }

        static DateTime OpenOf(DateTime start) => start.Date.AddDays(-OpenDays).AddHours(OpenHour);

        static int SoldAt(int want, int cap, double hours) => hours <= 0 ? 0 : Math.Min(cap, (int)(want * (1.0 - Math.Exp(-hours / TauHours))));

        /// <summary>지금 예매 중이거나 다음에 열릴 페스티벌 공연(본 스레드, Shows.Refresh 뒤).</summary>
        public static void Refresh()
        {
            Current = null;
            if (Shows.Venue == 0 || Shows.Next.Count == 0) { Feed.Clear(); return; }
            var n = Shows.Next[0];
            var plan = Booking.Current;
            int days = plan != null && plan.Band == n.Band.Key ? plan.Days - Math.Max(0, plan.Done - 1) : 1;
            int cap = SessionCap(Shows.Venue) * Math.Max(1, days);
            var now = SimulationManager.instance.m_currentGameTime;
            var s = new Sale { Band = n.Band, Start = n.Start, Days = Math.Max(1, days), Open = OpenOf(n.Start), Cap = cap, Want = WantOf(n.Band.Info, cap) };
            s.Sold = SoldAt(s.Want, s.Cap, (now - s.Open).TotalHours);
            s.Site = Letters.ExclusiveSite(n.Band.Key);
            s.Poster = (int)((uint)(n.Band.Key.GetHashCode() ^ n.Start.Date.GetHashCode()) % 10u);
            Current = s;
            MakeFeed(s, now);
        }

        /// <summary>대기줄: 오픈 직후 · 원하는 사람이 많을 때. 기다리는 사람 수(없으면 0).</summary>
        public static int Queue(Sale s)
        {
            if (s == null || !s.Opened || s.SoldOut) return 0;
            double h = (SimulationManager.instance.m_currentGameTime - s.Open).TotalHours;
            if (h > QueueHours || s.Want < s.Cap * QueueRatio) return 0;
            return Math.Max(0, (int)((s.Want - s.Sold) * (1.0 - h / QueueHours)));
        }

        /// <summary>매진까지 남은 게임 시간(시간, 못 팔면 -1).</summary>
        public static double HoursToSellOut(Sale s)
        {
            if (s == null || s.Want <= s.Cap) return -1;
            double t = -TauHours * Math.Log(1.0 - (double)s.Cap / s.Want);
            return Math.Max(0, t - (SimulationManager.instance.m_currentGameTime - s.Open).TotalHours);
        }

        /// <summary>예매처별 판 매수(나눔 비율대로).</summary>
        public static int SiteSold(Sale s, int site) => s.Site >= 0 ? (site == s.Site ? s.Sold : 0) : Mathf.RoundToInt(s.Sold * Shares[site]);

        /// <summary>방금 예매 6줄: 게임 분마다 같은 무작위(다시 그려도 같음) · 실제 시민 중에서.</summary>
        static void MakeFeed(Sale s, DateTime now)
        {
            Feed.Clear();
            if (!s.Opened || s.SoldOut) return;
            var cm = Singleton<CitizenManager>.instance;
            var buf = cm.m_citizens.m_buffer;
            long minute = now.Ticks / TimeSpan.TicksPerMinute;
            for (int k = 0; k < 6; k++)
            {
                var rng = new Randomizer((ulong)(minute - k * 3) ^ (ulong)s.Start.Ticks);
                uint id = 0;
                for (int tries = 0; tries < 30 && id == 0; tries++)
                {
                    uint c = (uint)rng.Int32((uint)buf.Length);
                    if ((buf[c].m_flags & Citizen.Flags.Created) != 0 && !buf[c].Dead) id = c;
                }
                if (id == 0) continue;
                Feed.Add(new Buy
                {
                    At = now.AddMinutes(-k * 3),
                    Name = cm.GetCitizenName(id) ?? "",
                    Tourist = (buf[id].m_flags & Citizen.Flags.Tourist) != 0,
                    Site = s.Site >= 0 ? s.Site : PickSite(rng.Int32(100u)),
                    Grade = rng.Int32(4u),
                    Count = 1 + rng.Int32((uint)MaxPerPerson),
                });
            }
        }

        static int PickSite(int roll)
        {
            float acc = 0f;
            for (int i = 0; i < Shares.Length; i++) { acc += Shares[i] * 100f; if (roll < acc) return i; }
            return Shares.Length - 1;
        }

        /// <summary>시뮬레이션 스레드: 이 공연 이벤트가 받을 관객 비율(예매한 만큼, 예매를 안 쓰면 1).</summary>
        public static float FillFor(ushort eventId, ref EventData data)
        {
            if (!ConcertSetting.Current.Ticketing || data.m_building == 0 || data.m_building != Shows.Venue) return 1f;
            var info = data.Info;
            if (info == null) return 1f;
            int cap = Math.Max(1, SessionCap(data.m_building));
            int want = WantOf(info, cap);
            var start = data.StartTime;
            return Mathf.Clamp01((float)SoldAt(want, cap, (start - OpenOf(start)).TotalHours) / cap);
        }

        public static void Clear()
        {
            Current = null;
            Feed.Clear();
        }
    }

    /// <summary>공연 관객 자리: 예매한 비율만큼(예매 못 한 사람은 오지 않음).</summary>
    [HarmonyPatch(typeof(ConcertAI), nameof(ConcertAI.GetCapacity))]
    internal static class CapacityPatch
    {
        static void Postfix(ushort eventID, ref EventData data, ref int __result)
        {
            try
            {
                float f = Tickets.FillFor(eventID, ref data);
                if (f < 1f) __result = Mathf.Max(1, Mathf.RoundToInt(__result * f));
            }
            catch (Exception e)
            {
                Core.Safe.Report("예매: 관객 자리", e);
            }
        }
    }
}
