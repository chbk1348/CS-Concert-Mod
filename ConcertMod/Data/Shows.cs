using System;
using System.Collections.Generic;
using ColossalFramework;
using ConcertMod.Core;
using UnityEngine;

namespace ConcertMod.Data
{
    /// <summary>게임 밴드 하나(콘서트 DLC EventInfo): 아티스트 이름 · 밴드 색 · 인기(0 ~ 100) · 기본 입장권.</summary>
    internal sealed class Band
    {
        public EventInfo Info;
        public string Key, Name;
        public Color Color;
        public int Pop, Price;
    }

    /// <summary>게임 공연 하나(EventManager 공연 이벤트) — 화면에 그릴 때 쓸 값만 복사.</summary>
    internal sealed class Show
    {
        public ushort Id, Building;
        public uint StartFrame;
        public Band Band;
        public Color Color;
        public DateTime Start, End;
        public string Venue;
        public bool Preparing, Active, Done, Success, Cancelled;
        public int Price, Arrived, Incoming, Max, Security, Noise, Crime, PopDelta;
        public long Ticket, Reward;
    }

    /// <summary>다음에 열릴 공연(게임이 정한 밴드 · 시작 시각).</summary>
    internal struct NextShow
    {
        public Band Band;
        public DateTime Start;
        /// <summary>게임이 이미 만든 공연이면 그 이벤트 번호(아직 안 만든 칸은 0).</summary>
        public ushort Id;
    }

    /// <summary>
    /// 게임 공연 데이터 읽기(본 스레드, 1초마다): 밴드 목록 · 페스티벌 구역 · 지금 공연 · 다음 공연 2개.
    /// 끝난 공연은 History 가 세이브에 남김(게임은 몇 개만 들고 있음).
    /// </summary>
    internal static class Shows
    {
        public static readonly List<Band> Bands = new();
        public static readonly List<Show> All = new();
        public static readonly List<NextShow> Next = new();
        public static ushort Festival;
        public static string FestivalName = "";
        public static Show Now;

        static readonly Dictionary<string, Band> byKey = new();
        static readonly EventManager.FutureEvent[] future = new EventManager.FutureEvent[2];

        public static bool HasDlc => SteamHelper.IsDLCOwned(SteamHelper.DLC.MusicFestival);

        public static Band BandOf(EventInfo info) => info != null && byKey.TryGetValue(info.name, out var b) ? b : null;

        public static Band BandOf(string key) => key != null && byKey.TryGetValue(key, out var b) ? b : null;

        /// <summary>밴드 목록(도시를 불러올 때 한 번 · 아티스트 이름표도 다시).</summary>
        public static void LoadBands()
        {
            Bands.Clear();
            byKey.Clear();
            var keys = new List<string>();
            for (uint i = 0; i < PrefabCollection<EventInfo>.LoadedCount(); i++)
            {
                var info = PrefabCollection<EventInfo>.GetLoaded(i);
                if (info?.m_eventAI is ConcertAI) keys.Add(info.name);
            }
            Artists.Assign(keys);
            for (uint i = 0; i < PrefabCollection<EventInfo>.LoadedCount(); i++)
            {
                var info = PrefabCollection<EventInfo>.GetLoaded(i);
                if (!(info?.m_eventAI is ConcertAI ai)) continue;
                var b = new Band { Info = info, Key = info.name, Name = Artists.Of(info.name), Color = ai.m_defaultBandColor };
                Bands.Add(b);
                byKey[b.Key] = b;
            }
        }

        public static void Refresh()
        {
            foreach (var b in Bands)
            {
                b.Pop = ConcertAI.GetPopularity(b.Info);
                b.Price = b.Info.m_eventAI.m_ticketPrice + b.Info.m_ticketPriceOffset;
            }
            Festival = FindFestival();
            AutoLevel3();
            FestivalName = Festival != 0 ? Singleton<BuildingManager>.instance.GetBuildingName(Festival, InstanceID.Empty) ?? "" : "";
            ReadEvents();
            Next.Clear();
            // 이미 만들어진(아직 준비 전) 공연이 먼저, 그 뒤 게임이 정할 칸
            foreach (var s in All)
                if (s.Building == Festival && !s.Preparing && !s.Active && !s.Done) Next.Add(new NextShow { Band = s.Band, Start = s.Start, Id = s.Id });
            if (Festival != 0 && Next.Count < future.Length)
            {
                int n = Singleton<EventManager>.instance.GetFutureEvents(Festival, future, future.Length - Next.Count);
                for (int i = 0; i < n; i++)
                {
                    var b = BandOf(future[i].m_info);
                    if (b != null) Next.Add(new NextShow { Band = b, Start = future[i].m_startTime });
                }
            }
        }

        /// <summary>지난번 3단계로 올리려 한 페스티벌 구역(같은 건물은 한 번만 시도 — 올릴 단계가 없는 애셋이면 다시 안 봄).</summary>
        static ushort leveled;

        /// <summary>페스티벌 구역을 지으면 바로 3단계로(게임 정보 창 업그레이드와 같은 UpgradeBuilding · 해금 조건 · 비용 없이).</summary>
        static void AutoLevel3()
        {
            ushort b = Festival;
            if (b == 0 || b == leveled) return;
            leveled = b;
            Singleton<SimulationManager>.instance.AddAction(() => Safe.Run("페스티벌 3단계", () =>
            {
                var bm = Singleton<BuildingManager>.instance;
                for (int i = 0; i < 2; i++)
                {
                    var info = bm.m_buildings.m_buffer[b].Info;
                    if (info == null || info.m_class.m_level >= ItemClass.Level.Level3 || !bm.UpgradeBuilding(b, false)) break;
                    Log.Info("페스티벌 구역 " + b + " → " + bm.m_buildings.m_buffer[b].Info?.m_class.m_level);
                }
            }));
        }

        static ushort FindFestival()
        {
            var bm = Singleton<BuildingManager>.instance;
            var buf = bm.m_buildings.m_buffer;
            if (Festival != 0 && (buf[Festival].m_flags & Building.Flags.Created) != 0 && buf[Festival].Info?.m_buildingAI is FestivalAreaAI) return Festival;
            for (int i = 1; i < buf.Length; i++)
                if ((buf[i].m_flags & Building.Flags.Created) != 0 && buf[i].Info?.m_buildingAI is FestivalAreaAI) return (ushort)i;
            return 0;
        }

        static void ReadEvents()
        {
            All.Clear();
            Now = null;
            var em = Singleton<EventManager>.instance;
            var sim = Singleton<SimulationManager>.instance;
            var bm = Singleton<BuildingManager>.instance;
            for (int i = 1; i < em.m_events.m_size; i++)
            {
                ref var d = ref em.m_events.m_buffer[i];
                if ((d.m_flags & EventData.Flags.Created) == 0 || (d.m_flags & EventData.Flags.Deleted) != 0) continue;
                var info = d.Info;
                if (!(info?.m_eventAI is ConcertAI ai)) continue;
                ushort id = (ushort)i;
                ai.CountVisitors(id, ref d, out int arrived, out int incoming, out int max);
                int noise = 0;
                float range = 0f;
                ai.ModifyNoise(id, ref d, ref noise, ref range);
                var s = new Show
                {
                    Id = id,
                    Building = d.m_building,
                    StartFrame = d.m_startFrame,
                    Band = BandOf(info),
                    Color = ai.m_defaultBandColor, // m_color 는 페스티벌 테마 색이라 밴드 색은 AI 값
                    Start = d.StartTime,
                    End = sim.FrameToTime(d.m_expireFrame),
                    Venue = d.m_building != 0 ? bm.GetBuildingName(d.m_building, InstanceID.Empty) ?? "" : "",
                    Preparing = (d.m_flags & EventData.Flags.Preparing) != 0,
                    Active = (d.m_flags & EventData.Flags.Active) != 0,
                    Done = d.IsComplete,
                    Success = (d.m_flags & EventData.Flags.Success) != 0,
                    Cancelled = (d.m_flags & EventData.Flags.Cancelled) != 0,
                    Price = ai.GetTicketPrice(id, ref d),
                    Arrived = arrived,
                    Incoming = incoming,
                    Max = max,
                    Security = ai.GetSecurityBudget(id, ref d),
                    Noise = noise,
                    Crime = ai.GetCrimeAccumulation(id, ref d, 0),
                    PopDelta = d.m_popularityDelta,
                    Ticket = (long)d.m_ticketMoney,
                    Reward = (long)d.m_rewardMoney,
                };
                if (s.Band == null) continue;
                All.Add(s);
                if (s.Active && (Now == null || !Now.Active)) Now = s;
                else if (s.Preparing && Now == null) Now = s;
            }
        }

        /// <summary>도시를 내릴 때.</summary>
        public static void Clear()
        {
            Bands.Clear();
            byKey.Clear();
            All.Clear();
            Next.Clear();
            Festival = 0;
            FestivalName = "";
            Now = null;
        }
    }
}
