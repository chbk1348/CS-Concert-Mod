using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
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
        /// <summary>수명(PlanState 값 · 바꾸기는 Booking.SetState 한 곳 · 본 · 시뮬레이션 스레드가 같은 값을 봄).</summary>
        internal volatile int life;

        public PlanState State => (PlanState)life;

        /// <summary>시뮬레이션 스레드가 자리 정리(준비 전 공연 지우기 · 경기장 넣기 거두기)를 마쳤음: 그 전엔 게임 패치 · 대관이 이 계획을 쓰지 않음(목록엔 바로 있어 의향서 줄은 막음).</summary>
        public bool Ready => State == PlanState.Ready;

        /// <summary>풀었음: 목록에서 빠진 뒤 시뮬레이션 스레드가 들고 있던 참조로 더 쓰지 않게.</summary>
        public bool Dropped => State == PlanState.Dropped;
        /// <summary>0.3 판 세이브에서 읽은 계획(의향서 번호가 없어 불러온 뒤 같은 공연장 · 같은 밴드 '잡음' 의향서와 이음 · 세이브엔 안 남김).</summary>
        public bool Legacy;
        /// <summary>공연 읽기가 실패한 동안 쌓인 프레임 · 그때의 Due(Due 가 바뀌면 버림). 고착 판정에서 빼고 세이브엔 안 남김.</summary>
        public uint Paused, PausedDue;
    }

    /// <summary>계획 수명: 자리 정리 전 → 준비됨 → 회차를 다 만듦(목록에서 빠짐) · 언제든 풂(목록에서 빠짐). 세이브엔 준비됨만 남음(읽은 계획은 Ready).</summary>
    internal enum PlanState { Pending = 0, Ready = 1, Finished = 2, Dropped = 3 }

    /// <summary>페스티벌 하나에서 방금 다 만든 계획의 마지막 회차(시작 프레임 · 만든 의향서 번호, 0 = 직접 예약). 두 값을 함께 바꾸려고 통째로 갈아 끼움.</summary>
    internal sealed class FestivalTail
    {
        public static readonly FestivalTail None = new(0, 0);

        public readonly uint Start;
        public readonly int Letter;

        public FestivalTail(uint start, int letter)
        {
            Start = start;
            Letter = letter;
        }
    }

    internal static class Booking
    {
        static readonly object gate = new();
        static volatile Plan[] plans = new Plan[0];

        /// <summary>지금 계획들(바꿀 때는 새 배열로 갈아 끼움).</summary>
        public static Plan[] Plans => plans;

        /// <summary>
        /// 페스티벌마다 방금 다 만든 계획의 마지막 회차: 시작 프레임(의향서가 그 회차를 지우고 새 계획을 잡지 않게) · 만든 의향서 번호(수동 예약이 그 회차를 지울 때 의향서 상태를 맞춤).
        /// 키 = 페스티벌 건물(0 = 옛 세이브의 하나뿐인 값 · 기본 페스티벌 것으로 봄). 시뮬레이션 스레드(OnCreate)와 본 스레드가 함께 쓰므로 사전은 바꾸지 않고 통째로 갈아 끼움(SwapTails).
        /// </summary>
        static volatile Dictionary<ushort, FestivalTail> tails = new();

        /// <summary>이 페스티벌의 꼬리 회차(없으면 FestivalTail.None).</summary>
        public static FestivalTail TailAt(ushort venue) => TailIn(tails, venue, Shows.Festival);

        /// <summary>사전에서 꼬리 찾기: 그 건물 값, 없고 기본 페스티벌이면 옛 세이브 값(키 0).</summary>
        static FestivalTail TailIn(Dictionary<ushort, FestivalTail> d, ushort venue, ushort festival)
        {
            if (venue == 0) return FestivalTail.None;
            if (d.TryGetValue(venue, out var t)) return t;
            return venue == festival && d.TryGetValue(0, out t) ? t : FestivalTail.None;
        }

        /// <summary>사본에 이 페스티벌 꼬리를 넣음(기본 페스티벌이면 옛 세이브 값 키 0 은 뺌).</summary>
        static Dictionary<ushort, FestivalTail> With(Dictionary<ushort, FestivalTail> d, ushort venue, FestivalTail t, ushort festival)
        {
            var n = new Dictionary<ushort, FestivalTail>(d);
            if (venue == festival) n.Remove(0);
            n[venue] = t;
            return n;
        }

        /// <summary>꼬리 사전 갈아 끼우기: 그새 다른 스레드가 바꿨으면 새 값으로 다시(change 가 null 이면 그만둠).</summary>
        static void SwapTails(Func<Dictionary<ushort, FestivalTail>, Dictionary<ushort, FestivalTail>> change)
        {
            while (true)
            {
                var old = tails;
                var next = change(old);
                if (next == null || Interlocked.CompareExchange(ref tails, next, old) == old) return;
            }
        }

        public const int MinDays = 1, MaxDays = 3;

        /// <summary>다음 회차가 Due 뒤 이만큼(게임 날) 지나도 안 생기면 계획을 풂.</summary>
        public const int StallDays = 3;

        public const string SaveKey = "ConcertMod.Booking";

        const int Format = 2;

        static float HourFrames => SimulationManager.DAYTIME_HOUR_TO_FRAME;

        static uint Day => SimulationManager.DAYTIME_FRAMES;

        static uint NowFrame => Singleton<SimulationManager>.instance.m_currentFrameIndex;

        static uint Offset => Singleton<SimulationManager>.instance.m_dayTimeOffsetFrames;

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
        internal static uint AtHour(uint frame, int hour) => AtHour(frame, hour, Offset);

        /// <summary>AtHour 계산만(게임 값 없이 · 자체 검사용): offset = 게임 하루 시각 어긋남(m_dayTimeOffsetFrames).</summary>
        internal static uint AtHour(uint frame, int hour, uint offset)
        {
            uint tod = (frame + offset) & (Day - 1);
            uint f = frame - tod + (uint)Mathf.RoundToInt(hour * HourFrames);
            return f < frame ? f + Day : f;
        }

        /// <summary>이 칸에 계획을 쓸지: 첫 회차는 잡을 때 그 칸 · 다음 회차는 앞 회차가 같은 밴드일 때.</summary>
        static bool Applies(Plan p, ushort building, EventInfo prev, uint prevExpire, EventInfo band) =>
            band != null && Applies(p, building, prevExpire, p != null && p.Done != 0 && prev == band);

        /// <summary>Applies 판단만(게임 값 없이 · 자체 검사용): sameBand = 앞 회차가 이 계획 밴드인가.</summary>
        internal static bool Applies(Plan p, ushort building, uint prevExpire, bool sameBand) =>
            p != null && p.Ready && !p.Stadium && building == p.Building && p.Done < p.Days &&
            (p.Done == 0 ? prevExpire == p.Anchor : sameBand);

        static EventInfo BandInfo(Plan p) => p == null ? null : Shows.BandOf(p.Band)?.Info;

        /// <summary>회차 시작 프레임: 첫 회차 = 게임이 정한 날의 hour 시, 다음 회차 = 앞 회차 + 하루(앞 회차가 취소돼 그 시각이 지났으면 지금 뒤 hour 시).</summary>
        static uint StartOf(Plan p, uint gameStart, uint now) => StartOf(p, gameStart, now, Offset);

        static uint StartOf(Plan p, uint gameStart, uint now, uint offset)
        {
            if (p.Done == 0) return AtHour(gameStart, p.Hour, offset);
            uint next = p.LastStart + Day;
            return next > now ? next : AtHour(now, p.Hour, offset);
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
                ushort venue = p.Building;
                var tail = new FestivalTail(start, p.Letter);
                SwapTails(old => With(old, venue, tail, Shows.Festival));
                SetState(p, PlanState.Finished);
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
            if (p.Done >= p.Days)
            {
                SetState(p, PlanState.Finished);
                Put(p, null);
            }
            return p.Price;
        }

        /// <summary>
        /// 계획 잡기(본 스레드): 그 공연장 계획을 바로 갈아 끼우고(덮인 계획의 의향서는 철회), 시뮬레이션 스레드에서 이미 만들어진 아직 준비 전 공연은 지워 게임이 계획대로 다시 만들게.
        /// </summary>
        public static void Set(string bandKey, int days, int hour, ushort venue, int price = 0, int letter = 0)
        {
            if (venue == 0) return;
            var old = At(venue);
            var seen = TailAt(venue);
            if (old != null) Drop(old, letter != 0 ? "다른 의향서로 바꿨어요" : "직접 잡은 일정으로 바꿨어요", false);
            // 다 만든 계획의 마지막 준비 전 회차는 아래에서 지워짐 → 그 의향서는 그 회차 앞까지만 열림
            else if (PendingTail(venue, seen, out int tail))
            {
                if (tail != 0) Letters.Stop(tail, "직접 잡은 일정으로 바꿨어요", false, -1);
                // 본 꼬리일 때만 의향서 번호를 비움: 그새 시뮬레이션 스레드가 이 페스티벌에 새 꼬리를 넣었으면 그 값은 그대로
                var cleared = new FestivalTail(seen.Start, 0);
                SwapTails(old => TailIn(old, venue, Shows.Festival) != seen ? null : With(old, venue, cleared, Shows.Festival));
            }
            var p = new Plan
            {
                Band = bandKey,
                Building = venue,
                Days = Mathf.Clamp(days, MinDays, MaxDays),
                Hour = Mathf.Clamp(hour, 0, 23),
                Stadium = !Shows.IsFestival(venue),
                Price = price,
                Letter = letter,
                Due = NowFrame,
            };
            Put(null, p);
            SimulationManager.instance.AddAction(() => Core.Safe.Run("공연 예약: 자리 정리", () =>
            {
                if (p.Dropped) return;
                // 경기장: 자리 잡는 중인 넣기를 거두면 대관이 다음 경기 앞에 이 계획대로 다시 넣음(거둔 뒤에야 Ready → 같은 밴드 자동 넣기가 회차로 세어지지 않게)
                if (p.Stadium)
                {
                    Rental.DropPlanned(venue);
                    SetState(p, PlanState.Ready);
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
                SetState(p, PlanState.Ready);
            }));
        }

        /// <summary>
        /// 이 페스티벌 맨 앞이 다 만든 계획의 마지막 회차인데 아직 준비 전인가(Set 이 지울 공연) · 그 회차를 만든 의향서 번호(0 = 직접 예약).
        /// </summary>
        public static bool PendingTail(ushort venue, out int letter) => PendingTail(venue, TailAt(venue), out letter);

        static bool PendingTail(ushort venue, FestivalTail t, out int letter)
        {
            letter = t.Letter;
            if (!Shows.IsFestival(venue) || t.Start == 0) return false;
            ushort head = Singleton<BuildingManager>.instance.m_buildings.m_buffer[venue].m_eventIndex;
            if (head == 0) return false;
            ref var e = ref Singleton<EventManager>.instance.m_events.m_buffer[head];
            return !Started(e.m_flags) && e.m_startFrame == t.Start;
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
            if (!Shows.IsFestival(venue)) return sim.FrameToTime(Rental.StartAt(Rental.InsertFrom(venue, ai), ai, hour));
            var em = Singleton<EventManager>.instance;
            ushort head = Singleton<BuildingManager>.instance.m_buildings.m_buffer[venue].m_eventIndex;
            if (head != 0 && !Started(em.m_events.m_buffer[head].m_flags)) head = em.m_events.m_buffer[head].m_nextBuildingEvent;
            uint prevExpire = head != 0 ? em.m_events.m_buffer[head].m_expireFrame : 0u;
            return sim.FrameToTime(AtHour(GameStart(ai, prevExpire, sim.m_currentFrameIndex), hour));
        }

        /// <summary>
        /// 계획 수명 바꾸기(여기 한 곳 · 본 · 시뮬레이션 스레드 모두): Pending → Ready, Ready → Finished 는 그 상태일 때만(그새 풀렸으면 그대로 풂) · Dropped 는 언제든. 바꿨으면 true.
        /// </summary>
        internal static bool SetState(Plan p, PlanState to)
        {
            switch (to)
            {
                case PlanState.Ready: return Interlocked.CompareExchange(ref p.life, (int)PlanState.Ready, (int)PlanState.Pending) == (int)PlanState.Pending;
                case PlanState.Finished: return Interlocked.CompareExchange(ref p.life, (int)PlanState.Finished, (int)PlanState.Ready) == (int)PlanState.Ready;
                case PlanState.Dropped: return Interlocked.Exchange(ref p.life, (int)PlanState.Dropped) != (int)PlanState.Dropped;
                default: return false;
            }
        }

        /// <summary>계획 풀기(본 스레드): 목록에서 빼고 · 만든 의향서가 있으면 철회. notify = 알림 토스트(시장이 직접 지울 땐 끔).</summary>
        public static void Drop(Plan p, string why, bool notify)
        {
            SetState(p, PlanState.Dropped);
            Put(p, null);
            Core.Log.Detail($"공연 계획 풂: {Artists.Of(p.Band)} · {p.Done}/{p.Days}회차 · {why}");
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

        /// <summary>지난 Tick 프레임(공연 읽기가 실패한 tick 사이 시간을 셈 · 0 = 아직 없음).</summary>
        static uint lastTick;

        /// <summary>고착 판정 기준: Due + 공연 읽기가 실패한 동안(지금 Due 일 때 쌓인 것만).</summary>
        internal static uint StallDue(Plan p)
        {
            uint due = p.Due;
            return p.PausedDue == due ? due + p.Paused : due;
        }

        /// <summary>공연 읽기가 실패한 tick: 지난 tick(from)부터 지금까지 중 고착 시계가 흐른 부분(Due 뒤)만 고착 판정에서 뺌(Due 가 바뀌었으면 처음부터).</summary>
        internal static void Pause(Plan p, uint from, uint now)
        {
            uint due = p.Due;
            if (p.PausedDue != due)
            {
                p.PausedDue = due;
                p.Paused = 0;
            }
            uint start = Math.Max(from, due);
            if (now > start) p.Paused += now - start;
        }

        /// <summary>본 스레드 1초마다: 공연장이 없어졌거나(철거 · 빌리기 해제 · 페스티벌 구역 교체) 공연장이 아니게 됐거나 오래 회차가 안 생기면 계획을 풀고 알림.</summary>
        /// <param name="checkStall">false = 고착 판정만 건너뜀(공연 읽기가 실패해 대관이 회차를 못 넣은 tick).</param>
        public static void Tick(bool checkStall)
        {
            var buf = Singleton<BuildingManager>.instance.m_buildings.m_buffer;
            uint now = NowFrame;
            uint from = lastTick;
            lastTick = now;
            foreach (var p in plans)
            {
                if (p.Dropped) continue;
                if (p.Due == 0) p.Due = now;
                if (!checkStall && from != 0) Pause(p, from, now);
                ref var b = ref buf[p.Building];
                string why = (b.m_flags & (Building.Flags.Created | Building.Flags.Deleted)) != Building.Flags.Created ? "공연장이 없어졌어요"
                    : p.Stadium ? (Rental.IsRented(p.Building) ? null : "경기장을 더 빌리지 않아요")
                    : b.Info?.m_buildingAI is FestivalAreaAI ? null : "페스티벌 구역이 아니게 됐어요";
                if (why == null && checkStall && Stalled(StallDue(p), now)) why = $"{StallDays}일 넘게 다음 회차가 잡히지 않았어요";
                if (why != null) Drop(p, why, true);
            }
        }

        /// <summary>아티스트 입장권(그 아티스트의 모든 공연에 적용, 출연표 세이브에 남음 · 예약 탭 수동 조정).</summary>
        public static void SetPrice(Band band, int price) => Lineup.SetPrice(band.Key, price - band.Info.m_eventAI.m_ticketPrice);

        /// <summary>도시를 내릴 때 · 읽기 전.</summary>
        public static void Reset()
        {
            lock (gate) plans = new Plan[0];
            tails = new Dictionary<ushort, FestivalTail>();
            lastTick = 0;
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

        public static byte[] Write()
        {
            var d = tails;
            var t = TailIn(d, Shows.Festival, Shows.Festival);
            return Write(plans, t.Start, t.Letter, d);
        }

        /// <summary>쓰기: last · lastLetter = 기본 페스티벌 꼬리(옛 판 모드가 읽는 자리) · 그 뒤에 덧붙임 = 페스티벌마다 꼬리(개수 + 건물 · 시작 · 의향서).</summary>
        static byte[] Write(Plan[] ps, uint last, int lastLetter, Dictionary<ushort, FestivalTail> d)
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
            w.Write(d.Count); // 뒤에 덧붙임: 없으면 옛 값 하나를 기본 페스티벌 것으로
            foreach (var kv in d)
            {
                w.Write(kv.Key);
                w.Write(kv.Value.Start);
                w.Write(kv.Value.Letter);
            }
            w.Flush();
            return ms.ToArray();
        }

        public static void Read(byte[] data)
        {
            Reset();
            var list = Parse(data, out _, out _, out var d);
            tails = d;
            lock (gate) plans = list;
        }

        /// <summary>읽기(전역 값은 안 건드림): 형식 2 = 공연장마다 계획, 형식 1(0.3 판) = 계획 하나 + 뒤에 덧붙인 마지막 회차 · 경기장 표시. 읽은 계획은 자리 정리가 끝난 것으로.</summary>
        static Plan[] Parse(byte[] data, out uint last, out int lastLetter, out Dictionary<ushort, FestivalTail> d)
        {
            last = 0;
            lastLetter = 0;
            d = null;
            using var r = new BinaryReader(new MemoryStream(data));
            int format = r.ReadInt32();
            if (format > Format) throw new InvalidDataException($"공연 예약 형식 {format} 은 이 판보다 새것");
            var list = new List<Plan>();
            if (format >= 2)
            {
                int n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                    list.Add(new Plan { Band = r.ReadString(), Building = r.ReadUInt16(), Days = r.ReadInt32(), Hour = r.ReadInt32(), Done = r.ReadInt32(), Anchor = r.ReadUInt32(), LastStart = r.ReadUInt32(), Stadium = r.ReadBoolean(), Price = r.ReadInt32(), Letter = r.ReadInt32(), Due = r.ReadUInt32(), life = (int)PlanState.Ready });
                last = r.ReadUInt32();
                if (SaveRead.More(r, 4)) lastLetter = r.ReadInt32();
                if (SaveRead.More(r, 4))
                {
                    int m = r.ReadInt32();
                    if (m < 0 || m > ushort.MaxValue) throw new InvalidDataException($"공연 예약 꼬리 회차 수 {m} 이 이상해요");
                    d = new Dictionary<ushort, FestivalTail>();
                    for (int i = 0; i < m; i++) d[r.ReadUInt16()] = new FestivalTail(r.ReadUInt32(), r.ReadInt32());
                }
            }
            else
            {
                Plan p = null;
                if (r.ReadBoolean())
                    p = new Plan { Band = r.ReadString(), Building = r.ReadUInt16(), Days = r.ReadInt32(), Hour = r.ReadInt32(), Done = r.ReadInt32(), Anchor = r.ReadUInt32(), LastStart = r.ReadUInt32(), life = (int)PlanState.Ready, Legacy = true };
                if (SaveRead.More(r, 4)) last = r.ReadUInt32();
                if (SaveRead.More(r, 1) && r.ReadBoolean() && p != null) p.Stadium = true;
                if (p != null) list.Add(p);
            }
            if (d == null)
            {
                // 덧붙임 없는 옛 세이브: 하나뿐인 값을 기본 페스티벌 것으로(키 0)
                d = new Dictionary<ushort, FestivalTail>();
                if (last != 0) d[0] = new FestivalTail(last, lastLetter);
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
            var srcTails = new Dictionary<ushort, FestivalTail> { [12] = new FestivalTail(100, 6), [40] = new FestivalTail(200, 0) };
            var got = Parse(Write(src, 55, 6, srcTails), out uint last, out int lastLetter, out var gotTails);
            var p = got.Length == 2 ? got[0] : null;
            if (p == null || p.Band != "Band A" || p.Building != 12 || p.Days != 3 || p.Hour != 19 || p.Done != 1 || p.Anchor != 9 || p.LastStart != 77 || !p.Stadium
                || p.Price != 3200 || p.Letter != 6 || p.Due != 88 || !p.Ready || p.Legacy || got[1].Band != "Band B" || last != 55 || lastLetter != 6) return "공연 예약을 쓰고 읽은 값이 달라요";
            if (gotTails.Count != 2 || gotTails[12].Start != 100 || gotTails[12].Letter != 6 || gotTails[40].Start != 200 || gotTails[40].Letter != 0) return "페스티벌마다 꼬리 회차를 쓰고 읽은 값이 달라요";
            // 덧붙임 없는 옛 세이브(형식 2): 하나뿐인 값을 기본 페스티벌 것으로 · lastLetter 도 없으면 0 · 값이 0 이면 없음
            if (!OldTail(true, 77, out var oldTails) || oldTails.Count != 1 || TailIn(oldTails, 5, 5).Start != 77 || TailIn(oldTails, 5, 5).Letter != 3 || TailIn(oldTails, 6, 5) != FestivalTail.None)
                return "옛 세이브 꼬리 회차를 기본 페스티벌 것으로 읽지 못해요";
            if (!OldTail(false, 77, out oldTails) || TailIn(oldTails, 5, 5).Start != 77 || TailIn(oldTails, 5, 5).Letter != 0) return "의향서 번호 없는 옛 세이브 꼬리 회차를 읽지 못해요";
            if (!OldTail(true, 0, out oldTails) || oldTails.Count != 0) return "꼬리 회차 없는 옛 세이브에 꼬리가 생겨요";
            // 기본 페스티벌에 새 꼬리를 넣으면 옛 값(키 0)은 빠지고 다른 페스티벌 값은 그대로
            var mixed = With(new Dictionary<ushort, FestivalTail> { [0] = new FestivalTail(77, 3), [8] = new FestivalTail(9, 1) }, 5, new FestivalTail(300, 0), 5);
            if (mixed.ContainsKey(0) || TailIn(mixed, 5, 5).Start != 300 || TailIn(mixed, 8, 5).Start != 9) return "페스티벌 꼬리 회차를 넣을 때 옛 값 · 다른 페스티벌 값이 틀려요";
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(1); w.Write(true); w.Write("Band C"); w.Write((ushort)7); w.Write(2); w.Write(18); w.Write(0); w.Write(5u); w.Write(0u); w.Write(44u); w.Write(true);
                w.Flush();
                got = Parse(ms.ToArray(), out last, out lastLetter, out oldTails);
            }
            p = got.Length == 1 ? got[0] : null;
            if (p == null || p.Band != "Band C" || p.Building != 7 || p.Days != 2 || !p.Stadium || p.Due != 0 || !p.Legacy || p.Letter != 0 || last != 44 || lastLetter != 0
                || TailIn(oldTails, 5, 5).Start != 44) return "0.3 판 공연 예약을 읽지 못해요";
            if (Stalled(100, 100 + StallFrames) || !Stalled(100, 101 + StallFrames) || Stalled(500, 100) || Stalled(100, 100) || Stalled(0, StallFrames) || !Stalled(0, StallFrames + 1)) return "계획 고착 판단이 틀려요";
            var q = new Plan { Done = 1, Hour = 20, LastStart = 1000 };
            if (StartOf(q, 0, 1001, 0) != 1000 + Day) return "다음 회차 시각이 하루 뒤가 아니에요";
            if (StartOf(q, 0, 1000 + Day * 3, 0) < 1000 + Day * 3) return "취소 뒤 회차 시각이 지난 시각이에요";
            // 다음 회차 시각이 바로 지금이면(지난 것으로 봄) 지금 뒤 hour 시
            if (StartOf(q, 0, 1000 + Day, 0) != AtHour(1000 + Day, 20, 0)) return "다음 회차 시각이 지금과 같을 때 셈이 틀려요";
            uint day5 = Day * 5, h = (uint)Mathf.RoundToInt(HourFrames), h10 = (uint)Mathf.RoundToInt(10 * HourFrames), h20 = (uint)Mathf.RoundToInt(20 * HourFrames), h23 = (uint)Mathf.RoundToInt(23 * HourFrames);
            if (StartOf(new Plan { Hour = 20 }, day5 + h10, 0, 0) != day5 + h20) return "첫 회차 시각이 게임이 정한 날 hour 시가 아니에요";
            // AtHour 경계: 같은 날 뒤 시각 · 같은 시각 · 지난 시각(다음 날) · 0시 · 23시 · 하루 시각 어긋남
            if (AtHour(day5 + h10, 20, 0) != day5 + h20) return "같은 날 뒤 시각 셈이 틀려요";
            if (AtHour(day5 + h20, 20, 0) != day5 + h20) return "같은 시각이 다음 날로 넘어가요";
            if (AtHour(day5 + h20, 10, 0) != day5 + Day + h10) return "지난 시각이 다음 날로 넘어가지 않아요";
            if (AtHour(day5, 0, 0) != day5 || AtHour(day5 + h, 0, 0) != day5 + Day || AtHour(0, 0, 0) != 0) return "0시 셈이 틀려요";
            if (AtHour(day5, 23, 0) != day5 + h23 || AtHour(day5 + h23 + 1, 23, 0) != day5 + Day + h23) return "23시 셈이 틀려요";
            uint off = (uint)Mathf.RoundToInt(6 * HourFrames);
            if (AtHour(day5, 10, off) != day5 - off + h10 || AtHour(day5, 0, off) != day5 + Day - off) return "하루 시각 어긋남이 있을 때 셈이 틀려요";
            // Applies 진리표: 준비 · 경기장 아님 · 같은 공연장 · 남은 회차 · 첫 회차는 Anchor 일치, 다음 회차는 앞 회차 같은 밴드
            Plan A(bool ready = true, bool stadium = false, int done = 0) => new() { Building = 12, Days = 2, Done = done, Anchor = 50, life = (int)(ready ? PlanState.Ready : PlanState.Pending), Stadium = stadium };
            if (!Applies(A(), 12, 50, false) || Applies(null, 12, 50, true) || Applies(A(ready: false), 12, 50, true) || Applies(A(stadium: true), 12, 50, true)
                || Applies(A(), 13, 50, true) || Applies(A(), 12, 51, true) || Applies(A(done: 2), 12, 50, true)
                || !Applies(A(done: 1), 12, 0, true) || Applies(A(done: 1), 12, 50, false)) return "계획을 쓸 칸 판단이 틀려요";
            // 공연 읽기가 실패한 동안은 고착 기한에서 뺌: 쌓이고 · Due 가 바뀌면 버림
            var pw = new Plan { Due = 100 };
            if (StallDue(pw) != 100) return "멈춘 적 없는 계획 고착 기준이 Due 가 아니에요";
            Pause(pw, 100, 400);
            Pause(pw, 400, 600);
            if (StallDue(pw) != 600 || Stalled(StallDue(pw), 101 + StallFrames) || !Stalled(StallDue(pw), 601 + StallFrames)) return "공연 읽기가 실패한 동안을 고착 기한에서 빼지 않아요";
            pw.Due = 900;
            if (StallDue(pw) != 900) return "새 회차 뒤에도 지난 멈춤을 고착 기한에서 빼요";
            Pause(pw, 900, 950);
            if (StallDue(pw) != 950) return "새 회차 뒤 멈춤을 처음부터 세지 않아요";
            // Due 가 아직 앞(고착 시계가 안 흐름)이면 쌓지 않고, 간격이 Due 를 가로지르면 Due 뒤만
            pw = new Plan { Due = 1000 };
            Pause(pw, 500, 800);
            Pause(pw, 800, 1000);
            if (StallDue(pw) != 1000) return "Due 전 실패한 동안을 고착 기한에서 빼요";
            Pause(pw, 900, 1100);
            if (StallDue(pw) != 1100) return "Due 를 가로지른 실패 간격에서 Due 뒤만 빼지 않아요";
            // 계획 수명 진리표: 지금 상태 × 바꿀 상태 → 바꿨는지 · 바뀐 뒤 상태
            var lives = new[] { PlanState.Pending, PlanState.Ready, PlanState.Finished, PlanState.Dropped };
            var tos = new[] { PlanState.Ready, PlanState.Finished, PlanState.Dropped };
            // 행 = 지금(Pending · Ready · Finished · Dropped), 열 = 바꿀(Ready · Finished · Dropped)
            var after = new[,]
            {
                { PlanState.Ready, PlanState.Pending, PlanState.Dropped },
                { PlanState.Ready, PlanState.Finished, PlanState.Dropped },
                { PlanState.Finished, PlanState.Finished, PlanState.Dropped },
                { PlanState.Dropped, PlanState.Dropped, PlanState.Dropped },
            };
            for (int i = 0; i < lives.Length; i++)
                for (int j = 0; j < tos.Length; j++)
                {
                    var lp = new Plan { life = (int)lives[i] };
                    bool changed = SetState(lp, tos[j]);
                    if (lp.State != after[i, j] || changed != (lives[i] != after[i, j])) return $"계획 수명 {lives[i]} → {tos[j]} 이 틀려요";
                    if (lp.Ready != (lp.State == PlanState.Ready) || lp.Dropped != (lp.State == PlanState.Dropped)) return "계획 수명 Ready · Dropped 해석이 틀려요";
                }
            return null;
        }

        /// <summary>자체 검사용 덧붙임 없는 옛 형식 2 세이브(계획 없음 · 꼬리 시작 last · withLetter 면 의향서 3)를 읽음.</summary>
        static bool OldTail(bool withLetter, uint last, out Dictionary<ushort, FestivalTail> d)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(2); w.Write(0); w.Write(last);
            if (withLetter) w.Write(3);
            w.Flush();
            Parse(ms.ToArray(), out uint l, out int letter, out d);
            return l == last && letter == (withLetter ? 3 : 0);
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
