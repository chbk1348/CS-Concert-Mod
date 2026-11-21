using System;
using System.Collections.Generic;
using System.IO;
using ColossalFramework;
using HarmonyLib;
using ICities;
using UnityEngine;

namespace ConcertMod.Data
{
    /// <summary>
    /// 시장이 잡은 공연 계획 하나(공연장마다 하나): 밴드 · 1 ~ 3일(회차) · 시작 시각. 게임이 다음 공연을 정할 때(FindNextEventInfo) 그 밴드로,
    /// 공연을 만들 때(ConcertAI.CreateEvent) 회차마다 같은 시각 · 하루 간격으로 시작하게 바꿈. 회차를 다 만들면 계획을 지우고 다음부터는 게임대로.
    /// 목록(Booking.Plans)은 통째로 갈아 끼움(lock 안에서) · 회차 값(Done · LastStart · Due)은 시뮬레이션 스레드가 바꿈.
    /// </summary>
    internal sealed class Plan
    {
        public string Band;
        public ushort Building;
        public int Days, Hour, Done;
        /// <summary>계획을 잡을 때 페스티벌 맨 앞 이벤트가 끝나는 프레임(이 칸 다음 공연에만 적용) · 바로 앞 회차 시작 프레임.</summary>
        public uint Anchor, LastStart;
        /// <summary>공연장이 빌린 경기장: 회차는 대관이 경기 사이마다 하나씩 넣음.</summary>
        public bool Stadium;
        /// <summary>의향서 입장권(이 계획 회차에만 · 0 = 아티스트 값) · 이 계획을 만든 의향서 번호(0 = 직접 예약).</summary>
        public int Price, Letter;
        /// <summary>다음 회차가 늦어도 이때쯤 생겨야 하는 프레임(잡은 때 · 페스티벌 앞 공연이 끝나는 때 · 앞 회차 시작). 0 = 옛 세이브(첫 Tick 이 지금으로).</summary>
        public uint Due;
        /// <summary>풀었음: 목록에서 빠진 뒤 시뮬레이션 스레드가 들고 있던 참조로 더 쓰지 않게.</summary>
        public volatile bool Dropped;
        /// <summary>시뮬레이션 스레드가 자리 정리(준비 전 공연 지우기 · 경기장 넣기 거두기)를 마쳤음: 그 전엔 게임 패치 · 대관이 이 계획을 쓰지 않음(목록엔 바로 있어 의향서 줄은 막음).</summary>
        public volatile bool Ready;
        /// <summary>0.3 판 세이브에서 읽은 계획(의향서 번호가 없어 불러온 뒤 같은 공연장 · 같은 밴드 '잡음' 의향서와 이음 · 세이브엔 안 남김).</summary>
        public bool Legacy;
    }

    internal static class Booking
    {
        static readonly object gate = new();
        static volatile Plan[] plans = new Plan[0];

        /// <summary>지금 계획들(바꿀 때는 새 배열로 갈아 끼움).</summary>
        public static Plan[] Plans => plans;

        /// <summary>방금 다 만든 페스티벌 계획의 마지막 회차 시작 프레임(의향서가 그 회차를 지우고 새 계획을 잡지 않게).</summary>
        public static uint LastPlanStart;

        /// <summary>그 마지막 회차를 만든 의향서 번호(0 = 직접 예약) — 수동 예약이 그 회차를 지울 때 의향서 상태를 맞춤.</summary>
        public static int LastPlanLetter;

        public const int MinDays = 1, MaxDays = 3;

        /// <summary>다음 회차가 Due 뒤 이만큼(게임 날) 지나도 안 생기면 계획을 풂.</summary>
        public const int StallDays = 3;

        public const string SaveKey = "ConcertMod.Booking";

        const int Format = 2;

        static float HourFrames => SimulationManager.DAYTIME_HOUR_TO_FRAME;

        static uint Day => SimulationManager.DAYTIME_FRAMES;

        static uint NowFrame => Singleton<SimulationManager>.instance.m_currentFrameIndex;

        static uint StallFrames => (uint)StallDays * Day;

        /// <summary>이 공연장 계획(없으면 null).</summary>
        public static Plan At(ushort venue)
        {
            if (venue == 0) return null;
            foreach (var p in plans) if (p.Building == venue && !p.Dropped) return p;
            return null;
        }

        /// <summary>목록 갈아 끼우기: old 를 빼고(add 와 같은 공연장 계획도 빼고) add 를 넣음.</summary>
        static void Put(Plan old, Plan add)
        {
            lock (gate)
            {
                var list = new List<Plan>(plans.Length + 1);
                foreach (var p in plans) if (p != old && (add == null || p.Building != add.Building)) list.Add(p);
                if (add != null) list.Add(add);
                plans = list.ToArray();
            }
        }

        /// <summary>게임이 정한 시작 프레임과 같은 날(지나면 다음 날) hour 시.</summary>
        internal static uint AtHour(uint frame, int hour)
        {
            uint tod = (frame + Singleton<SimulationManager>.instance.m_dayTimeOffsetFrames) & (Day - 1);
            uint f = frame - tod + (uint)Mathf.RoundToInt(hour * HourFrames);
            return f < frame ? f + Day : f;
        }

        /// <summary>이 칸에 계획을 쓸지: 첫 회차는 잡을 때 그 칸 · 다음 회차는 앞 회차가 같은 밴드일 때.</summary>
        static bool Applies(Plan p, ushort building, EventInfo prev, uint prevExpire, EventInfo band) =>
            p != null && p.Ready && !p.Stadium && band != null && building == p.Building && p.Done < p.Days &&
            (p.Done == 0 ? prevExpire == p.Anchor : prev == band);

        static EventInfo BandInfo(Plan p) => p == null ? null : Shows.BandOf(p.Band)?.Info;

        /// <summary>회차 시작 프레임: 첫 회차 = 게임이 정한 날의 hour 시, 다음 회차 = 앞 회차 + 하루(앞 회차가 취소돼 그 시각이 지났으면 지금 뒤 hour 시).</summary>
        static uint StartOf(Plan p, uint gameStart, uint now)
        {
            if (p.Done == 0) return AtHour(gameStart, p.Hour);
            uint next = p.LastStart + Day;
            return next > now ? next : AtHour(now, p.Hour);
        }

        /// <summary>게임이 다음 공연 시작을 셈하는 값(CreateEvent 의 CalculateStartFrame(앞 끝, 지금 기준)과 같은 날): 앞 공연이 이미 끝났으면 지금부터.</summary>
        static uint GameStart(EventAI ai, uint prevExpire, uint now) => ai.CalculateStartFrame(Math.Max(prevExpire, now), false);

        /// <summary>FindNextEventInfo 뒤: 계획 칸이면 밴드 · 시작 프레임을 바꿈(화면 다음 공연 미리 보기도 같은 길로).</summary>
        public static void OnFindNext(ushort building, EventInfo prev, uint prevExpire, ref EventInfo next, ref uint nextStart, ref bool result)
        {
            var p = At(building);
            var band = BandInfo(p);
            if (!Applies(p, building, prev, prevExpire, band)) return;
            uint now = NowFrame;
            next = band;
            nextStart = StartOf(p, GameStart(band.m_eventAI, prevExpire, now), now);
            result = true;
        }

        /// <summary>ConcertAI.CreateEvent 뒤: 계획 회차면 시작 · 끝 프레임을 맞추고 회차를 셈.</summary>
        public static void OnCreate(ushort eventId, ref EventData data)
        {
            var p = At(data.m_building);
            var band = BandInfo(p);
            var em = Singleton<EventManager>.instance;
            EventInfo prev = data.m_nextBuildingEvent != 0 ? em.m_events.m_buffer[data.m_nextBuildingEvent].Info : null;
            uint prevExpire = data.m_nextBuildingEvent != 0 ? em.m_events.m_buffer[data.m_nextBuildingEvent].m_expireFrame : 0u;
            if (data.Info != band || !Applies(p, data.m_building, prev, prevExpire, band)) return;
            var ai = band.m_eventAI;
            uint start = StartOf(p, data.m_startFrame, NowFrame);
            bool last = p.Done + 1 >= p.Days;
            data.m_startFrame = start;
            // 마지막 회차만 게임의 다음 공연 간격, 그 앞 회차는 공연 · 철수 뒤 바로 다음 회차를 만들게
            data.m_expireFrame = last ? ai.CalculateExpireFrame(start) : start + (uint)Mathf.RoundToInt((ai.m_eventDuration + ai.m_disorganizeDuration) * HourFrames);
            p.LastStart = start;
            p.Due = start;
            p.Done++;
            Lineup.Bind(eventId, start, Shows.BandOf(p.Band)?.Key, p.Price);
            Core.Log.Detail($"공연 예약: {Artists.Of(p.Band)} {p.Done}/{p.Days}회차 · 이벤트 {eventId} · {Singleton<SimulationManager>.instance.FrameToTime(start):MM-dd HH:mm}");
            if (p.Done >= p.Days)
            {
                LastPlanStart = start;
                LastPlanLetter = p.Letter;
                Put(p, null);
            }
        }

        /// <summary>대관이 경기장 공연을 만들었을 때(시뮬레이션 스레드): 경기장 계획 회차를 셈. 그 회차 입장권(의향서 값, 계획 회차가 아니면 0)을 돌려줌.</summary>
        public static int OnStadiumShow(ushort stadium, string band, uint start)
        {
            var p = At(stadium);
            if (p == null || !p.Ready || !p.Stadium || p.Band != band) return 0;
            p.LastStart = start;
            p.Due = start;
            p.Done++;
            if (p.Done >= p.Days) Put(p, null);
            return p.Price;
        }

        /// <summary>
        /// 계획 잡기(본 스레드): 그 공연장 계획을 바로 갈아 끼우고(덮인 계획의 의향서는 철회), 시뮬레이션 스레드에서 이미 만들어진 아직 준비 전 공연은 지워 게임이 계획대로 다시 만들게.
        /// </summary>
        public static void Set(string bandKey, int days, int hour, ushort venue, int price = 0, int letter = 0)
        {
            if (venue == 0) return;
            var old = At(venue);
            if (old != null) Drop(old, letter != 0 ? "다른 의향서로 바꿨어요" : "직접 잡은 일정으로 바꿨어요", false);
            // 다 만든 계획의 마지막 준비 전 회차는 아래에서 지워짐 → 그 의향서는 그 회차 앞까지만 열림
            else if (PendingTail(venue, out int tail))
            {
                if (tail != 0) Letters.Stop(tail, "직접 잡은 일정으로 바꿨어요", false, -1);
                LastPlanLetter = 0;
            }
            var p = new Plan
            {
                Band = bandKey,
                Building = venue,
                Days = Mathf.Clamp(days, MinDays, MaxDays),
                Hour = Mathf.Clamp(hour, 0, 23),
                Stadium = venue != Shows.Festival,
                Price = price,
                Letter = letter,
                Due = NowFrame,
            };
            Put(null, p);
            SimulationManager.instance.AddAction(() =>
            {
                if (p.Dropped) return;
                // 경기장: 자리 잡는 중인 넣기를 거두면 대관이 다음 경기 앞에 이 계획대로 다시 넣음(거둔 뒤에야 Ready → 같은 밴드 자동 넣기가 회차로 세어지지 않게)
                if (p.Stadium)
                {
                    Rental.DropPlanned(venue);
                    p.Ready = true;
                    return;
                }
                var em = Singleton<EventManager>.instance;
                ref var building = ref Singleton<BuildingManager>.instance.m_buildings.m_buffer[venue];
                ushort head = building.m_eventIndex;
                if (head != 0 && !Started(em.m_events.m_buffer[head].m_flags))
                {
                    em.ReleaseEvent(head);
                    head = building.m_eventIndex;
                }
                p.Anchor = head != 0 ? em.m_events.m_buffer[head].m_expireFrame : 0u;
                p.Due = Math.Max(p.Due, p.Anchor);
                p.Ready = true;
            });
        }

        /// <summary>
        /// 이 페스티벌 맨 앞이 다 만든 계획의 마지막 회차인데 아직 준비 전인가(Set 이 지울 공연) · 그 회차를 만든 의향서 번호(0 = 직접 예약).
        /// </summary>
        public static bool PendingTail(ushort venue, out int letter)
        {
            letter = LastPlanLetter;
            if (venue == 0 || venue != Shows.Festival || LastPlanStart == 0) return false;
            ushort head = Singleton<BuildingManager>.instance.m_buildings.m_buffer[venue].m_eventIndex;
            if (head == 0) return false;
            ref var e = ref Singleton<EventManager>.instance.m_events.m_buffer[head];
            return !Started(e.m_flags) && e.m_startFrame == LastPlanStart;
        }

        static bool Started(EventData.Flags f) => (f & (EventData.Flags.Preparing | EventData.Flags.Active | EventData.Flags.Completed | EventData.Flags.Cancelled)) != 0;

        /// <summary>
        /// 화면 예상 첫 회차 시작(실제 계산과 같은 길): 페스티벌 = Set 이 지울 준비 전 공연을 빼고 그 앞 공연이 끝난 뒤 게임이 정할 날의 hour 시,
        /// 경기장 = 대관이 넣을 때(지금 · 경기 중이면 그 경기 뒤)부터 Rental.StartAt.
        /// </summary>
        public static DateTime Expect(ushort venue, Band band, int hour)
        {
            var sim = Singleton<SimulationManager>.instance;
            if (venue == 0 || band?.Info?.m_eventAI is not ConcertAI ai) return sim.m_currentGameTime;
            if (venue != Shows.Festival) return sim.FrameToTime(Rental.StartAt(Rental.InsertFrom(venue, ai), ai, hour));
            var em = Singleton<EventManager>.instance;
            ushort head = Singleton<BuildingManager>.instance.m_buildings.m_buffer[venue].m_eventIndex;
            if (head != 0 && !Started(em.m_events.m_buffer[head].m_flags)) head = em.m_events.m_buffer[head].m_nextBuildingEvent;
            uint prevExpire = head != 0 ? em.m_events.m_buffer[head].m_expireFrame : 0u;
            return sim.FrameToTime(AtHour(GameStart(ai, prevExpire, sim.m_currentFrameIndex), hour));
        }

        /// <summary>계획 풀기(본 스레드): 목록에서 빼고 · 만든 의향서가 있으면 철회. notify = 알림 토스트(시장이 직접 지울 땐 끔).</summary>
        public static void Drop(Plan p, string why, bool notify)
        {
            p.Dropped = true;
            Put(p, null);
            Core.Log.Info($"공연 계획 풂: {Artists.Of(p.Band)} · {p.Done}/{p.Days}회차 · {why}");
            if (p.Letter != 0) Letters.Stop(p.Letter, why, notify, p.Done);
            else if (notify) CSCommonUX.Toast.Show($"{Artists.Of(p.Band)} 공연 계획을 풀었어요 · {why}");
        }

        /// <summary>이 공연장 계획 지우기(게임이 고른 밴드로). 이미 만들어진 준비 전 공연은 그대로.</summary>
        public static void Clear(ushort venue)
        {
            var p = At(venue);
            if (p != null) Drop(p, "게임이 고른 밴드로 돌렸어요", false);
        }

        /// <summary>Due 뒤 StallDays 넘게 지났나.</summary>
        internal static bool Stalled(uint due, uint now) => now > due && now - due > StallFrames;

        /// <summary>본 스레드 1초마다: 공연장이 없어졌거나(철거 · 빌리기 해제 · 페스티벌 구역 교체) 공연장이 아니게 됐거나 오래 회차가 안 생기면 계획을 풀고 알림.</summary>
        public static void Tick()
        {
            var buf = Singleton<BuildingManager>.instance.m_buildings.m_buffer;
            uint now = NowFrame;
            foreach (var p in plans)
            {
                if (p.Dropped) continue;
                if (p.Due == 0) p.Due = now;
                ref var b = ref buf[p.Building];
                string why = (b.m_flags & (Building.Flags.Created | Building.Flags.Deleted)) != Building.Flags.Created ? "공연장이 없어졌어요"
                    : p.Stadium ? (Rental.IsRented(p.Building) ? null : "경기장을 더 빌리지 않아요")
                    : b.Info?.m_buildingAI is FestivalAreaAI ? null : "페스티벌 구역이 아니게 됐어요";
                if (why == null && Stalled(p.Due, now)) why = $"{StallDays}일 넘게 다음 회차가 잡히지 않았어요";
                if (why != null) Drop(p, why, true);
            }
        }

        /// <summary>아티스트 입장권(그 아티스트의 모든 공연에 적용, 출연표 세이브에 남음 · 예약 탭 수동 조정).</summary>
        public static void SetPrice(Band band, int price) => Lineup.SetPrice(band.Key, price - band.Info.m_eventAI.m_ticketPrice);

        /// <summary>도시를 내릴 때 · 읽기 전.</summary>
        public static void Reset()
        {
            lock (gate) plans = new Plan[0];
            LastPlanStart = 0;
            LastPlanLetter = 0;
        }

        /// <summary>
        /// 0.3 판 세이브: 의향서 번호 없이 남은 계획을 같은 공연장(또는 공연장을 안 적은) · 같은 밴드의 가장 새 '잡음' 의향서와 이음(LoadBands 에서 아티스트 이름을 맞춘 뒤).
        /// 그때 의향서가 바꾼 아티스트 입장권은 시장이 바꾼 값과 가릴 수 없어 그대로 둠.
        /// </summary>
        public static void LinkOld()
        {
            foreach (var p in plans)
            {
                if (!p.Legacy || p.Letter != 0) continue;
                p.Legacy = false;
                lock (Letters.All)
                    foreach (var l in Letters.All) // 새것부터
                        if (l.State == LetterState.Booked && l.Band == p.Band && (l.Venue == p.Building || l.Venue == 0))
                        {
                            p.Letter = l.Id;
                            Core.Log.Info($"옛 세이브 공연 계획을 의향서와 이음: {Artists.Of(p.Band)} · 의향서 {l.Id}");
                            break;
                        }
            }
        }

        public static byte[] Write() => Write(plans, LastPlanStart, LastPlanLetter);

        static byte[] Write(Plan[] ps, uint last, int lastLetter)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(Format);
            w.Write(ps.Length);
            foreach (var p in ps)
            {
                w.Write(p.Band ?? "");
                w.Write(p.Building);
                w.Write(p.Days);
                w.Write(p.Hour);
                w.Write(p.Done);
                w.Write(p.Anchor);
                w.Write(p.LastStart);
                w.Write(p.Stadium);
                w.Write(p.Price);
                w.Write(p.Letter);
                w.Write(p.Due);
            }
            w.Write(last);
            w.Write(lastLetter); // 뒤에 덧붙임: 없으면 0
            w.Flush();
            return ms.ToArray();
        }

        public static void Read(byte[] data)
        {
            Reset();
            var list = Parse(data, out uint last, out int lastLetter);
            LastPlanStart = last;
            LastPlanLetter = lastLetter;
            lock (gate) plans = list;
        }

        /// <summary>읽기(전역 값은 안 건드림): 형식 2 = 공연장마다 계획, 형식 1(0.3 판) = 계획 하나 + 뒤에 덧붙인 마지막 회차 · 경기장 표시. 읽은 계획은 자리 정리가 끝난 것으로.</summary>
        static Plan[] Parse(byte[] data, out uint last, out int lastLetter)
        {
            last = 0;
            lastLetter = 0;
            using var r = new BinaryReader(new MemoryStream(data));
            int format = r.ReadInt32();
            if (format > Format) throw new InvalidDataException($"공연 예약 형식 {format} 은 이 판보다 새것");
            var list = new List<Plan>();
            if (format >= 2)
            {
                int n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                    list.Add(new Plan { Band = r.ReadString(), Building = r.ReadUInt16(), Days = r.ReadInt32(), Hour = r.ReadInt32(), Done = r.ReadInt32(), Anchor = r.ReadUInt32(), LastStart = r.ReadUInt32(), Stadium = r.ReadBoolean(), Price = r.ReadInt32(), Letter = r.ReadInt32(), Due = r.ReadUInt32(), Ready = true });
                last = r.ReadUInt32();
                if (r.BaseStream.Length - r.BaseStream.Position >= 4) lastLetter = r.ReadInt32();
            }
            else
            {
                Plan p = null;
                if (r.ReadBoolean())
                    p = new Plan { Band = r.ReadString(), Building = r.ReadUInt16(), Days = r.ReadInt32(), Hour = r.ReadInt32(), Done = r.ReadInt32(), Anchor = r.ReadUInt32(), LastStart = r.ReadUInt32(), Ready = true, Legacy = true };
                if (r.BaseStream.Length - r.BaseStream.Position >= 4) last = r.ReadUInt32();
                if (r.BaseStream.Length - r.BaseStream.Position >= 1 && r.ReadBoolean() && p != null) p.Stadium = true;
                if (p != null) list.Add(p);
            }
            return list.ToArray();
        }

        /// <summary>
        /// 자체 검사(지역 값만 — 실제 계획 목록은 안 건드려 시뮬레이션 스레드 패치가 가짜 계획을 쓰지 않음): 쓰고 다시 읽어 같은지 · 0.3 판(계획 하나) 읽기 · 고착 판단 · 취소 뒤 회차 시각.
        /// 틀리면 설명, 맞으면 null.
        /// </summary>
        public static string SelfCheck()
        {
            var src = new[]
            {
                new Plan { Band = "Band A", Building = 12, Days = 3, Hour = 19, Done = 1, Anchor = 9, LastStart = 77, Stadium = true, Price = 3200, Letter = 6, Due = 88 },
                new Plan { Band = "Band B", Building = 40, Days = 1, Hour = 20 },
            };
            var got = Parse(Write(src, 55, 6), out uint last, out int lastLetter);
            var p = got.Length == 2 ? got[0] : null;
            if (p == null || p.Band != "Band A" || p.Building != 12 || p.Days != 3 || p.Hour != 19 || p.Done != 1 || p.Anchor != 9 || p.LastStart != 77 || !p.Stadium
                || p.Price != 3200 || p.Letter != 6 || p.Due != 88 || !p.Ready || p.Legacy || got[1].Band != "Band B" || last != 55 || lastLetter != 6) return "공연 예약을 쓰고 읽은 값이 달라요";
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(1); w.Write(true); w.Write("Band C"); w.Write((ushort)7); w.Write(2); w.Write(18); w.Write(0); w.Write(5u); w.Write(0u); w.Write(44u); w.Write(true);
                w.Flush();
                got = Parse(ms.ToArray(), out last, out lastLetter);
            }
            p = got.Length == 1 ? got[0] : null;
            if (p == null || p.Band != "Band C" || p.Building != 7 || p.Days != 2 || !p.Stadium || p.Due != 0 || !p.Legacy || p.Letter != 0 || last != 44 || lastLetter != 0) return "0.3 판 공연 예약을 읽지 못해요";
            if (Stalled(100, 100 + StallFrames) || !Stalled(100, 101 + StallFrames) || Stalled(500, 100)) return "계획 고착 판단이 틀려요";
            var q = new Plan { Done = 1, Hour = 20, LastStart = 1000 };
            if (StartOf(q, 0, 1001) != 1000 + Day) return "다음 회차 시각이 하루 뒤가 아니에요";
            if (StartOf(q, 0, 1000 + Day * 3) < 1000 + Day * 3) return "취소 뒤 회차 시각이 지난 시각이에요";
            return null;
        }
    }

    /// <summary>게임이 다음 공연을 정할 때(페스티벌 건물): 계획 칸이면 그 밴드 · 시각.</summary>
    [HarmonyPatch(typeof(PlayerBuildingAI), nameof(PlayerBuildingAI.FindNextEventInfo))]
    internal static class FindNextPatch
    {
        static void Postfix(PlayerBuildingAI __instance, ushort buildingID, EventInfo prevEvent, uint prevExpireFrame, ref EventInfo nextEvent, ref uint nextStartFrame, ref bool __result)
        {
            try
            {
                Rental.OnFindNext(__instance, buildingID, prevEvent, prevExpireFrame, ref nextEvent, ref nextStartFrame, ref __result);
                Booking.OnFindNext(buildingID, prevEvent, prevExpireFrame, ref nextEvent, ref nextStartFrame, ref __result);
            }
            catch (Exception e)
            {
                Core.Safe.Report("공연 예약: 다음 공연", e);
            }
        }
    }

    /// <summary>공연을 만들 때: 계획 회차면 시작 · 끝 프레임.</summary>
    [HarmonyPatch(typeof(ConcertAI), nameof(ConcertAI.CreateEvent))]
    internal static class CreateConcertPatch
    {
        static void Postfix(ushort eventID, ref EventData data)
        {
            try
            {
                Rental.OnConcertCreate(eventID, ref data);
                Booking.OnCreate(eventID, ref data);
                Lineup.OnCreate(eventID, ref data); // 예약 · 대관이 아티스트를 안 붙인 공연은 그 자리 차례 아티스트
            }
            catch (Exception e)
            {
                Core.Safe.Report("공연 예약: 공연 만들기", e);
            }
        }
    }

    /// <summary>세이브에 예약 계획을 넣고 꺼냄.</summary>
    public sealed class BookingSave : SerializableDataExtensionBase
    {
        public override void OnLoadData()
        {
            Booking.Reset();
            try
            {
                var data = serializableDataManager.LoadData(Booking.SaveKey);
                if (data != null && data.Length > 0) Booking.Read(data);
            }
            catch (Exception e)
            {
                Core.Log.Warn("공연 예약을 읽지 못해 예약 없이 시작: " + e.Message);
                Booking.Reset();
            }
        }

        public override void OnSaveData()
        {
            try
            {
                serializableDataManager.SaveData(Booking.SaveKey, Booking.Write());
            }
            catch (Exception e)
            {
                Core.Log.Error("공연 예약 저장 실패: " + e);
            }
        }
    }
}
