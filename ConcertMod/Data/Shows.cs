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

    /// <summary>다음에 열릴 공연(게임이 정한 밴드 · 시작 시각 · 공연장 · 그 공연 입장권).</summary>
    internal struct NextShow
    {
        public Band Band;
        public DateTime Start;
        /// <summary>게임이 이미 만든 공연이면 그 이벤트 번호(아직 안 만든 칸은 0).</summary>
        public ushort Id, Building;
        public string Venue;
        public int Price;
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

        /// <summary>공연장: 페스티벌 구역, 없으면 기본 공연장(빌린 경기장) · 이름. 다음 공연 · 예매 · 의향서가 여기로.</summary>
        public static ushort Venue;
        public static string VenueName = "";

        /// <summary>공연장이 빌린 경기장(페스티벌 구역 없음).</summary>
        public static bool StadiumVenue => Venue != 0 && Venue != Festival;
        public static Show Now;

        static readonly Dictionary<string, Band> byKey = new();
        static readonly EventManager.FutureEvent[] future = new EventManager.FutureEvent[2];

        public static bool HasDlc => SteamHelper.IsDLCOwned(SteamHelper.DLC.MusicFestival);

        /// <summary>아티스트 이름(옛 세이브의 밴드 프리팹 이름도 그때 붙던 아티스트로) → 밴드.</summary>
        public static Band BandOf(string key) => key != null && byKey.TryGetValue(Artists.Of(key), out var b) ? b : null;

        /// <summary>공연 이벤트의 아티스트(출연표 · 출연표 전 공연은 그 프리팹에 붙던 아티스트).</summary>
        public static Band BandOf(ushort id, ref EventData d) => BandOf(Lineup.ArtistOf(id, d.m_startFrame) ?? d.Info?.name);

        /// <summary>아직 안 만든 다음 공연(게임이 고른 자리 프리팹)의 아티스트: 예약 계획이 그 자리면 계획 아티스트, 아니면 그 자리 차례.</summary>
        internal static Band Predict(EventInfo slot, ushort building)
        {
            var p = Booking.At(building);
            var planned = p != null && !p.Stadium && p.Done < p.Days ? BandOf(p.Band) : null;
            return planned != null && planned.Info == slot ? planned : Lineup.Pick(slot);
        }

        /// <summary>이 공연장의 다음 공연(없으면 null).</summary>
        public static NextShow? NextAt(ushort venue)
        {
            if (venue != 0) foreach (var n in Next) if (n.Building == venue) return n;
            return null;
        }

        /// <summary>아직 안 만든 칸 입장권: 그 공연장 계획 밴드면 계획(의향서) 값, 아니면 아티스트 값.</summary>
        static int PriceFor(Band b, ushort building)
        {
            var p = Booking.At(building);
            return p != null && p.Price > 0 && p.Band == b.Key ? p.Price : b.Price;
        }

        /// <summary>같은 공연장 · 같은 시작은 한 번만(게임 미리 보기가 계획 회차를 두 번 돌려줄 때).</summary>
        static void AddNext(Band b, DateTime start, ushort id, ushort building, int price)
        {
            foreach (var n in Next) if (n.Building == building && n.Start == start) return;
            Next.Add(new NextShow { Band = b, Start = start, Id = id, Building = building, Price = price, Venue = Singleton<BuildingManager>.instance.GetBuildingName(building, InstanceID.Empty) ?? "" });
        }

        /// <summary>밴드 목록(도시를 불러올 때 한 번): 게임 밴드 프리팹은 공연 자리, 무대에 오르는 건 아티스트 Lineup.Count 팀.</summary>
        public static void LoadBands()
        {
            Bands.Clear();
            byKey.Clear();
            var slots = new List<EventInfo>();
            for (uint i = 0; i < PrefabCollection<EventInfo>.LoadedCount(); i++)
            {
                var info = PrefabCollection<EventInfo>.GetLoaded(i);
                if (info?.m_eventAI is ConcertAI) slots.Add(info);
            }
            slots.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            var keys = new List<string>();
            foreach (var s in slots) keys.Add(s.name);
            Artists.Assign(keys); // 옛 세이브: 프리팹 이름 → 그때 붙던 아티스트
            History.Rekey();
            lock (Letters.All) foreach (var l in Letters.All) l.Band = Artists.Of(l.Band);
            foreach (var p in Booking.Plans) p.Band = Artists.Of(p.Band);
            Booking.LinkOld();
            if (slots.Count == 0) return;
            int n = Math.Min(Lineup.Count, Artists.Names.Length);
            for (int i = 0; i < n; i++)
            {
                var slot = slots[i % slots.Count];
                var b = new Band { Info = slot, Key = Artists.Names[i], Name = Artists.Names[i], Color = ((ConcertAI)slot.m_eventAI).m_defaultBandColor };
                Bands.Add(b);
                byKey[b.Key] = b;
                // 출연표 없는 세이브: 앞 팀들은 그때 쓰던 밴드 프리팹 인기 · 입장권을 넘겨받음
                if (!Lineup.Loaded && i < slots.Count) Lineup.Seed(b.Key, slot.m_popularityOffset, slot.m_ticketPriceOffset);
            }
            Lineup.MarkLoaded();
        }

        public static void Refresh()
        {
            foreach (var b in Bands)
            {
                b.Pop = Lineup.Pop(b.Key);
                b.Price = b.Info.m_eventAI.m_ticketPrice + Lineup.PriceOffset(b.Key);
            }
            Festival = FindFestival();
            AutoLevel3();
            Venue = Festival != 0 ? Festival : Rental.Default();
            VenueName = Venue != 0 ? Singleton<BuildingManager>.instance.GetBuildingName(Venue, InstanceID.Empty) ?? "" : "";
            ReadEvents();
            Lineup.Sync();
            Next.Clear();
            // 공연장: 페스티벌 구역(또는 기본 경기장) + 빌린 경기장 모두(페스티벌이 있어도 경기장 계획 · 공연이 보이게)
            var places = new List<ushort>();
            if (Venue != 0) places.Add(Venue);
            foreach (var b in Rental.RentedSnapshot()) if (!places.Contains(b)) places.Add(b);
            // 이미 만들어진(아직 준비 전) 공연이 먼저, 그 뒤 게임이 정할 칸
            foreach (var s in All)
                if (places.Contains(s.Building) && !s.Preparing && !s.Active && !s.Done)
                {
                    int bound = Lineup.PriceOf(s.Id, s.StartFrame);
                    AddNext(s.Band, s.Start, s.Id, s.Building, bound > 0 ? bound : s.Band.Price);
                }
            // 경기장: 대관이 자리 잡는 중인 공연(아직 게임 이벤트 전)
            foreach (var b in places)
            {
                if (b == Festival) continue;
                var rent = Rental.ActiveAt(b);
                if (rent != null && rent.State == 0 && BandOf(rent.Band) is Band rb) AddNext(rb, SimulationManager.instance.FrameToTime(rent.Start), 0, b, PriceFor(rb, b));
                // 잡은 계획인데 아직 넣지 못함(경기 중 등): 대관이 넣을 때로 셈한 시각
                else if (rent == null && Booking.At(b) is Plan sp && sp.Stadium && BandOf(sp.Band) is Band pb) AddNext(pb, Booking.Expect(b, pb, sp.Hour), 0, b, PriceFor(pb, b));
            }
            int have = 0;
            foreach (var n in Next) if (n.Building == Festival) have++;
            if (Festival != 0 && have < future.Length)
            {
                int n = Singleton<EventManager>.instance.GetFutureEvents(Festival, future, future.Length - have);
                for (int i = 0; i < n; i++)
                {
                    var b = Predict(future[i].m_info, Festival);
                    if (b != null) AddNext(b, future[i].m_startTime, 0, Festival, PriceFor(b, Festival));
                }
            }
            Next.Sort((a, b) => a.Start.CompareTo(b.Start));
        }

        /// <summary>
        /// 공연 이벤트 하나를 화면 값으로 복사(본 스레드 ReadEvents · 시뮬레이션 스레드 대관이 지우기 직전). 공연이 아니거나 아티스트가 없으면 null.
        /// </summary>
        internal static Show Capture(ushort id, ref EventData d)
        {
            if (!(d.Info?.m_eventAI is ConcertAI ai)) return null;
            ai.CountVisitors(id, ref d, out int arrived, out int incoming, out int max);
            int noise = 0;
            float range = 0f;
            ai.ModifyNoise(id, ref d, ref noise, ref range);
            var s = new Show
            {
                Id = id,
                Building = d.m_building,
                StartFrame = d.m_startFrame,
                Band = BandOf(id, ref d),
                Color = ai.m_defaultBandColor, // m_color 는 페스티벌 테마 색이라 밴드 색은 AI 값(아티스트는 자리 프리팹 색)
                Start = d.StartTime,
                End = Singleton<SimulationManager>.instance.FrameToTime(d.m_expireFrame),
                Venue = d.m_building != 0 ? Singleton<BuildingManager>.instance.GetBuildingName(d.m_building, InstanceID.Empty) ?? "" : "",
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
            return s.Band == null ? null : s;
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
            for (int i = 1; i < em.m_events.m_size; i++)
            {
                ref var d = ref em.m_events.m_buffer[i];
                if ((d.m_flags & EventData.Flags.Created) == 0 || (d.m_flags & EventData.Flags.Deleted) != 0) continue;
                var s = Capture((ushort)i, ref d);
                if (s == null) continue;
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
            Venue = 0;
            VenueName = "";
            Now = null;
        }
    }
}
