using System;
using System.Collections.Generic;
using System.IO;
using ColossalFramework;
using HarmonyLib;
using ICities;
using UnityEngine;

namespace ConcertMod.Data
{
    /// <summary>경기장 공연 한 번: 경기장 · 밴드 · 시작 · 정리 끝 프레임 · 만든 이벤트 · 앞 경기(일정 기준) · 상태(0 넣는 중 · 1 만들어짐).</summary>
    internal sealed class Rent
    {
        public ushort Stadium, Event, AnchorPrev;
        public string Band;
        public uint Start, End, AnchorExpire;
        public byte State;
    }

    /// <summary>지난 경기장 공연(화면 기록): 경기장 · 밴드 · 시작 프레임.</summary>
    internal struct RentLog
    {
        public ushort Stadium;
        public string Band;
        public uint Start;
    }

    /// <summary>
    /// 공연장 대관(캔버스 예약 · 공연장): 빌리기를 켠 경기장(Match Day · 대학 경기장)에서 다음 경기가 아직 준비 전이고
    /// 그 준비 전까지 공연 하나(준비 · 공연 · 정리)가 들어갈 틈이 있으면, 준비 전 다음 경기를 지우고 그 자리에 공연을 넣음.
    /// 공연이 끝나 게임이 다음 경기를 만들 때 앞 경기를 일정 기준으로 다시 고르고(같은 경기 · 같은 시각) 공연 이벤트는 경기장 기록에서 뺌
    /// → 경기 일정 · 전적 · 상금 · 지난 경기 목록은 빌리기 전과 같음. 값은 시뮬레이션 스레드에서만 바꿈(화면은 lock 안에서 읽음).
    /// </summary>
    internal static class Rental
    {
        public const string SaveKey = "ConcertMod.Rental";
        const int Format = 3, KeepLog = 12;

        /// <summary>시장이 고른 기본 공연장(페스티벌 구역이 없을 때 · 빌린 경기장 중 하나). 빌리지 않았으면 빌린 첫 경기장.</summary>
        static ushort preferred;

        /// <summary>공연이 끝나고 다음 경기 준비까지 남길 여유(게임 시간).</summary>
        const float MarginHours = 2f;

        public static readonly List<ushort> Rented = new();
        public static readonly List<Rent> Active = new();
        public static readonly List<RentLog> Log = new();

        /// <summary>빌린 경기장마다 이번 단계에서 공연을 못 넣은 까닭(화면 안내 · 넣었거나 기다릴 것 없으면 없음).</summary>
        static readonly Dictionary<ushort, string> waitWhy = new();

        public static string WaitReason(ushort b) { lock (gate) return waitWhy.TryGetValue(b, out var s) ? s : null; }
        static readonly object gate = new();

        /// <summary>경기장(경기 이벤트를 받는 건물) — 본 스레드에서 1초마다 다시 찾음.</summary>
        public static readonly List<ushort> Venues = new();

        /// <summary>경기장: 경기 이벤트를 받는 MonumentAI(Match Day · 대학 경기장) 중 관객 자리가 있는 것 — 경기 플래그만 붙은 다른 애셋(택시 회사 등)은 뺌.</summary>
        public static bool IsStadium(BuildingInfo info) =>
            info != null && info.m_buildingAI is MonumentAI ai && (ai.m_supportEvents & (EventManager.EventType.Football | EventManager.EventType.VarsitySportsMatch)) != 0
            && ai.m_visitPlaceCount0 + ai.m_visitPlaceCount1 + ai.m_visitPlaceCount2 > 0;

        static uint Frames(float hours) => (uint)Mathf.RoundToInt(hours * SimulationManager.DAYTIME_HOUR_TO_FRAME);

        /// <summary>본 스레드 1초마다: 경기장 목록 · 시뮬레이션 스레드에 대관 단계.</summary>
        /// <summary>페스티벌 다음 공연 밴드(본 스레드가 적고 시뮬레이션 스레드가 읽음).</summary>
        static volatile string festivalNext;

        /// <summary>이 세이브에서 경기장 자동 대관을 이미 했나(세이브에 남음 — 시장이 끈 뒤 다시 빌리지 않게).</summary>
        static volatile bool autoRented;

        public static void Tick()
        {
            festivalNext = Shows.NextAt(Shows.Festival)?.Band.Key;
            Venues.Clear();
            var buf = Singleton<BuildingManager>.instance.m_buildings.m_buffer;
            for (int i = 1; i < buf.Length; i++)
                if ((buf[i].m_flags & (Building.Flags.Created | Building.Flags.Deleted)) == Building.Flags.Created && buf[i].m_parentBuilding == 0 && IsStadium(buf[i].Info))
                    Venues.Add((ushort)i);
            // 페스티벌 구역 · 빌린 경기장이 없으면 경기 사이 틈이 있는 첫 경기장을 자동으로 빌려 기본 공연장으로(세이브마다 한 번 — 시장이 끄면 그대로 둠)
            // 경기 기록이 없어 아직 모르는 경기장은 기록이 쌓일 때까지 다시 봄
            if (!autoRented && Shows.Festival == 0 && Default() == 0)
                foreach (var v in Venues)
                {
                    if (Room(v) != RoomFree) continue;
                    autoRented = true;
                    SetRented(v, true);
                    break;
                }
            if (Rented.Count > 0) SimulationManager.instance.AddAction(Step);
        }

        public static bool IsRented(ushort b) { lock (gate) return Rented.Contains(b); }

        public static ushort[] RentedSnapshot() { lock (gate) return Rented.ToArray(); }

        /// <summary>페스티벌 구역이 없을 때 다음 공연 · 예매 · 의향서가 가는 경기장(고른 곳 → 빌린 첫 곳 → 없으면 0).</summary>
        public static ushort Default()
        {
            lock (gate) return Rented.Contains(preferred) ? preferred : Rented.Count > 0 ? Rented[0] : (ushort)0;
        }

        public static void SetDefault(ushort b) => SimulationManager.instance.AddAction(() => { lock (gate) preferred = b; });

        /// <summary>아직 만들지 않은 넣기(자리 잡는 중)를 거둠 — 예약 계획을 새로 잡을 때(시뮬레이션 스레드).</summary>
        public static void DropPlanned(ushort b)
        {
            lock (gate) Active.RemoveAll(r => r.Stadium == b && r.State == 0);
        }

        /// <summary>화면용 지난 경기장 공연 사본(시뮬레이션 스레드가 바꾸는 중에도 안전).</summary>
        public static RentLog[] LogSnapshot() { lock (gate) return Log.ToArray(); }

        public static Rent ActiveAt(ushort b)
        {
            lock (gate) foreach (var r in Active) if (r.Stadium == b) return r;
            return null;
        }

        /// <summary>빌리기 켜고 끄기(화면 → 시뮬레이션 스레드). 끄면 아직 만들지 않은 공연만 거둠(이미 만든 공연은 그대로 열림).</summary>
        public static void SetRented(ushort b, bool on) => SimulationManager.instance.AddAction(() =>
        {
            lock (gate)
            {
                Rented.Remove(b);
                if (on) Rented.Add(b);
                else
                {
                    Active.RemoveAll(r => r.Stadium == b && r.State == 0); // 이 경기장 예약 계획은 Booking.Tick 이 풀고 의향서를 철회
                }
            }
        });

        /// <summary>대관에 쓸 밴드: 인기 1위(페스티벌 다음 공연 밴드 · 다른 경기장 공연 밴드는 뺌).</summary>
        static Band PickBand()
        {
            string busy = festivalNext;
            Band best = null;
            foreach (var b in Shows.Bands)
            {
                if (b.Key == busy) continue;
                bool used = false;
                foreach (var r in Active) if (r.Band == b.Key) used = true;
                if (used) continue;
                if (best == null || b.Pop > best.Pop) best = b;
            }
            return best;
        }

        /// <summary>시뮬레이션 스레드: 빌린 경기장마다 틈이 있으면 공연을 넣음 · 끝내 못 만든 넣기는 거둠.</summary>
        static void Step()
        {
            var em = Singleton<EventManager>.instance;
            var bm = Singleton<BuildingManager>.instance;
            uint now = Singleton<SimulationManager>.instance.m_currentFrameIndex;
            lock (gate)
            {
                Rented.RemoveAll(b => !IsStadium(bm.m_buildings.m_buffer[b].Info));
                // 끝내 못 만든 넣기 · 다른 길로 사라진 공연(건물 철거 등)은 거둠
                Active.RemoveAll(r => r.State == 0 ? now > r.Start : em.m_events.m_buffer[r.Event].Info != Shows.BandOf(r.Band)?.Info || (em.m_events.m_buffer[r.Event].m_flags & EventData.Flags.Deleted) != 0);
                // 다음 경기 없이 정리까지 끝난 공연(경기가 없는 경기장): 기록만 남기고 거둠(공연은 게임 기록대로 History 가 적음) → 다음 회차를 넣을 수 있게
                for (int i = Active.Count - 1; i >= 0; i--)
                {
                    var done = Active[i];
                    if (done.State != 1 || !em.m_events.m_buffer[done.Event].IsComplete) continue;
                    Active.RemoveAt(i);
                    Log.Insert(0, new RentLog { Stadium = done.Stadium, Band = done.Band, Start = done.Start });
                    if (Log.Count > KeepLog) Log.RemoveAt(Log.Count - 1);
                }
                waitWhy.Clear();
                foreach (ushort s in Rented)
                {
                    if (ActiveAtLocked(s) != null) continue;
                    // 이 경기장에 잡은 예약 계획이면 그 밴드 · 시각(회차마다 다음 경기 앞에 하나씩)
                    var plan = Booking.At(s);
                    bool planned = plan != null && plan.Ready && plan.Stadium && plan.Done < plan.Days;
                    if (!planned)
                    {
                        int room = Room(s);
                        if (room == RoomTight) { waitWhy[s] = "경기를 쉬지 않아 자동 공연은 넣지 않아요"; continue; }
                        if (room == RoomUnknown) { waitWhy[s] = "경기 기록이 쌓이면 자동 공연을 봐요"; continue; }
                    }
                    ushort head = bm.m_buildings.m_buffer[s].m_eventIndex;
                    // 확정한 공연인데 다음 경기가 없음(경기가 없거나 끝난 경기만): 밀 경기 없이 바로 넣음 → 게임이 다음 이벤트를 정할 때(CheckEvents · FindNextEventInfo) 이 공연을 만듦
                    if (planned && (head == 0 || (em.m_events.m_buffer[head].m_flags & (EventData.Flags.Completed | EventData.Flags.Cancelled)) != 0))
                    {
                        if (Shows.BandOf(plan.Band)?.Info?.m_eventAI is not ConcertAI pai) { waitWhy[s] = "공연할 밴드가 없어요"; continue; }
                        uint ps = StartAt(now, pai, plan.Hour);
                        Active.Add(new Rent { Stadium = s, Band = plan.Band, Start = ps, End = ps + Frames(pai.m_eventDuration + pai.m_disorganizeDuration), AnchorPrev = head, AnchorExpire = head != 0 ? em.m_events.m_buffer[head].m_expireFrame : 0u });
                        Core.Log.Info($"경기장 확정 공연(다음 경기 없음): {bm.GetBuildingName(s, InstanceID.Empty)} · {Artists.Of(plan.Band)} · {Singleton<SimulationManager>.instance.FrameToTime(ps):MM-dd HH:mm}");
                        continue;
                    }
                    if (head == 0) { waitWhy[s] = "게임이 다음 경기를 잡으면 그 앞 틈을 봐요"; continue; }
                    ref var match = ref em.m_events.m_buffer[head];
                    if (match.m_flags != EventData.Flags.Created) { waitWhy[s] = "경기 중이에요 · 끝나고 다음 경기 앞 틈을 봐요"; continue; }
                    if (match.Info?.m_eventAI is not SportMatchAI mai) continue;
                    var band = planned ? Shows.BandOf(plan.Band) : PickBand();
                    if (band?.Info?.m_eventAI is not ConcertAI cai) { waitWhy[s] = "공연할 밴드가 없어요"; continue; }
                    uint start = StartAt(now, cai, planned ? plan.Hour : Mathf.RoundToInt(cai.m_startTime));
                    uint end = start + Frames(cai.m_eventDuration + cai.m_disorganizeDuration);
                    // 확정한 공연(의향서 · 예약 계획)은 틈을 기다리지 않음: 다음 경기를 지우면 게임이 공연 뒤에 다시 잡음(경기가 공연 뒤로 밀림)
                    if (!planned && end + Frames(MarginHours) > match.m_startFrame - Frames(mai.m_prepareDuration))
                    {
                        waitWhy[s] = $"다음 경기({Singleton<SimulationManager>.instance.FrameToTime(match.m_startFrame):M월 d일})까지 틈이 짧아 기다려요";
                        continue;
                    }
                    ushort prev = match.m_nextBuildingEvent;
                    var r = new Rent { Stadium = s, Band = band.Key, Start = start, End = end, AnchorPrev = prev, AnchorExpire = prev != 0 ? em.m_events.m_buffer[prev].m_expireFrame : 0u };
                    Active.Add(r);
                    em.ReleaseEvent(head);
                    (planned ? (Action<string>)Core.Log.Info : Core.Log.Detail)($"경기장 {(planned ? "확정 공연" : "대관")}: {bm.GetBuildingName(s, InstanceID.Empty)} · {Artists.Of(band.Key)} · {Singleton<SimulationManager>.instance.FrameToTime(start):MM-dd HH:mm} (다음 경기 {Singleton<SimulationManager>.instance.FrameToTime(match.m_startFrame):MM-dd HH:mm})");
                }
            }
        }

        /// <summary>경기장 공연 시작(Step 이 넣을 때와 같은 셈): 넣는 때 + 준비 시간 뒤 그 날(지나면 다음 날) hour 시.</summary>
        public static uint StartAt(uint from, ConcertAI cai, int hour) => Booking.AtHour(from + Frames(cai.m_prepareDuration), hour);

        /// <summary>화면 예상: 대관이 이 경기장에 다음 공연을 넣을 때 — 지금, 넣은 공연이 있으면 그 공연이 끝날 때, 경기 중이면 그 경기가 끝날 때(게임이 다음 경기를 만드는 때).</summary>
        public static uint InsertFrom(ushort b, ConcertAI cai)
        {
            uint now = Singleton<SimulationManager>.instance.m_currentFrameIndex;
            var r = ActiveAt(b);
            if (r != null) return Math.Max(now, r.State == 1 ? r.Start + Frames(cai.m_eventDuration) : r.Start);
            ushort head = Singleton<BuildingManager>.instance.m_buildings.m_buffer[b].m_eventIndex;
            if (head == 0) return now;
            ref var e = ref Singleton<EventManager>.instance.m_events.m_buffer[head];
            if (e.m_flags == EventData.Flags.Created || (e.m_flags & (EventData.Flags.Completed | EventData.Flags.Cancelled)) != 0 || e.Info == null) return now;
            return Math.Max(now, e.m_startFrame + Frames(e.Info.m_eventAI.m_eventDuration));
        }

        /// <summary>앞 경기가 끝난 때(idleFrom)부터 다음 경기 준비 시작(nextPrep)까지 공연 한 번(need)이 들어가는지.</summary>
        internal static bool Fits(uint idleFrom, uint nextPrep, uint need) => nextPrep > idleFrom && nextPrep - idleFrom >= need;

        public const int RoomUnknown = -1, RoomTight = 0, RoomFree = 1;

        /// <summary>
        /// 자동 공연이 들어갈 틈: 게임이 남겨 둔 지난 경기 사이 틈 중 하나라도 공연 한 번(준비 · 공연 · 정리 · 여유)이 들어가면 Free(원정 연전 틈 포함),
        /// 모두 짧으면 Tight(다른 모드가 홈 경기를 매일 이어 여는 곳 등), 이어진 지난 경기 기록이 없으면 Unknown(쌓일 때까지 자동 대관 · 자동 빌리기를 미룸).
        /// 일정을 미리 셈하지 않고 실제로 열린 경기 간격만 봄(다른 모드가 CreateEvent 에서 일정을 바꿔도 맞음).
        /// </summary>
        public static int Room(ushort b)
        {
            if (Shows.Bands.Count == 0 || Shows.Bands[0].Info?.m_eventAI is not ConcertAI cai) return RoomUnknown;
            uint need = Frames(cai.m_prepareDuration + cai.m_eventDuration + cai.m_disorganizeDuration + MarginHours);
            var buf = Singleton<EventManager>.instance.m_events.m_buffer;
            ushort e = Singleton<BuildingManager>.instance.m_buildings.m_buffer[b].m_eventIndex;
            int gaps = 0;
            for (int guard = 0; e != 0 && guard < 8; guard++)
            {
                ushort older = buf[e].m_nextBuildingEvent;
                if (older == 0) break;
                var newer = buf[e].Info?.m_eventAI;
                var prev = buf[older].Info?.m_eventAI;
                if (newer is SportMatchAI && prev is SportMatchAI && (buf[older].m_flags & EventData.Flags.Cancelled) == 0)
                {
                    if (Fits(buf[older].m_startFrame + Frames(prev.m_eventDuration), buf[e].m_startFrame - Frames(newer.m_prepareDuration), need)) return RoomFree;
                    gaps++;
                }
                e = older;
            }
            return gaps > 0 ? RoomTight : RoomUnknown;
        }

        static Rent ActiveAtLocked(ushort b)
        {
            foreach (var r in Active) if (r.Stadium == b) return r;
            return null;
        }

        /// <summary>FindNextEventInfo 뒤(경기장): 넣는 중이면 공연 밴드 · 시작, 공연 뒤면 앞 경기 기준으로 원래 다음 경기.</summary>
        public static void OnFindNext(PlayerBuildingAI ai, ushort building, EventInfo prev, uint prevExpire, ref EventInfo next, ref uint nextStart, ref bool result)
        {
            Rent r;
            lock (gate) r = ActiveAtLocked(building);
            if (r == null) return;
            if (r.State == 0 && prevExpire == r.AnchorExpire)
            {
                var band = Shows.BandOf(r.Band)?.Info;
                if (band == null) return;
                next = band;
                nextStart = r.Start;
                result = true;
            }
            else if (r.State == 1 && prev?.m_eventAI is ConcertAI && prevExpire == r.End)
            {
                // 같은 메서드를 앞 경기로 다시 부름(이번엔 위 두 갈래에 걸리지 않음) → 빌리기 전과 같은 다음 경기
                var em = Singleton<EventManager>.instance;
                var anchor = r.AnchorPrev != 0 ? em.m_events.m_buffer[r.AnchorPrev].Info : null;
                result = ai.FindNextEventInfo(building, anchor, r.AnchorExpire, out next, out nextStart);
            }
        }

        /// <summary>ConcertAI.CreateEvent 뒤: 넣는 중인 경기장 공연이면 시작 · 끝 프레임을 맞춤.</summary>
        public static void OnConcertCreate(ushort eventId, ref EventData data)
        {
            lock (gate)
            {
                var r = ActiveAtLocked(data.m_building);
                if (r == null || r.State != 0 || data.Info == null || data.Info != Shows.BandOf(r.Band)?.Info) return;
                data.m_startFrame = r.Start;
                data.m_expireFrame = r.End;
                int price = Booking.OnStadiumShow(r.Stadium, r.Band, r.Start);
                Lineup.Bind(eventId, r.Start, Shows.BandOf(r.Band)?.Key, price);
                r.Event = eventId;
                r.State = 1;
            }
        }

        /// <summary>
        /// EventAI.CreateEvent 앞(모든 이벤트): 경기장 공연 다음에 만드는 경기면 앞 이벤트를 공연 앞 경기로 바꾸고 공연을 경기장 기록에서 뺌
        /// (게임이 시작 시각 · 전적 · 상금 누계를 앞 경기에서 이어받게).
        /// </summary>
        public static void OnEventCreate(EventAI ai, ref EventData data)
        {
            if (data.m_nextBuildingEvent == 0 || ai is ConcertAI) return;
            lock (gate)
            {
                var r = ActiveAtLocked(data.m_building);
                var em = Singleton<EventManager>.instance;
                if (r == null || r.State != 1 || r.Event != data.m_nextBuildingEvent)
                {
                    // 거둔 뒤 생긴 경기(다음 경기 없던 경기장): 앞 공연들을 정상 흐름처럼 기록 넘김 · 지우고 그 앞 경기를 앞 이벤트로(공연의 상금 · 전적 · 입장권 · 색을 이어받지 않게)
                    if (ai is SportMatchAI && Rented.Contains(data.m_building))
                        for (int guard = 0; guard < 8 && data.m_nextBuildingEvent != 0; guard++)
                        {
                            ushort c = data.m_nextBuildingEvent;
                            ref var old = ref em.m_events.m_buffer[c];
                            if (old.Info?.m_eventAI is not ConcertAI) break;
                            data.m_nextBuildingEvent = old.m_nextBuildingEvent;
                            var gone = Shows.Capture(c, ref old);
                            if (gone != null)
                            {
                                gone.Done = true;
                                History.Released(gone);
                            }
                            em.ReleaseEvent(c);
                        }
                    return;
                }
                ushort concert = r.Event;
                ref var done = ref em.m_events.m_buffer[concert];
                data.m_nextBuildingEvent = done.m_nextBuildingEvent;
                // 게임은 공연 정리 중(끝나기 전)에도 다음 경기를 만듦 → 지우기 전에 결과를 넘겨 기록 · 토스트 · 인기 반영이 빠지지 않게
                var show = Shows.Capture(concert, ref done);
                if (show != null)
                {
                    show.Done = true;
                    History.Released(show);
                }
                em.ReleaseEvent(concert);
                Active.Remove(r);
                Log.Insert(0, new RentLog { Stadium = r.Stadium, Band = r.Band, Start = r.Start });
                if (Log.Count > KeepLog) Log.RemoveAt(Log.Count - 1);
            }
        }

        public static void Clear()
        {
            lock (gate)
            {
                Rented.Clear();
                Active.Clear();
                Log.Clear();
                preferred = 0;
            }
            Venues.Clear();
            autoRented = false;
        }

        /// <summary>세이브 내용(쓰고 읽기 · 자체 검사가 전역 값을 건드리지 않게 따로 묶음).</summary>
        sealed class Saved
        {
            public List<ushort> Rented = new();
            public List<Rent> Active = new();
            public List<RentLog> Log = new();
            public ushort Preferred;
            public bool AutoRented;
        }

        public static byte[] Write()
        {
            lock (gate) return Write(new Saved { Rented = Rented, Active = Active, Log = Log, Preferred = preferred, AutoRented = autoRented });
        }

        static byte[] Write(Saved v)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(Format);
            w.Write(v.Rented.Count);
            foreach (var b in v.Rented) w.Write(b);
            w.Write(v.Active.Count);
            foreach (var r in v.Active)
            {
                w.Write(r.Stadium);
                w.Write(r.Band ?? "");
                w.Write(r.Start);
                w.Write(r.End);
                w.Write(r.Event);
                w.Write(r.AnchorPrev);
                w.Write(r.AnchorExpire);
                w.Write(r.State);
            }
            w.Write(v.Log.Count);
            foreach (var l in v.Log)
            {
                w.Write(l.Stadium);
                w.Write(l.Band ?? "");
                w.Write(l.Start);
            }
            w.Write(v.Preferred);
            w.Write(v.AutoRented);
            w.Flush();
            return ms.ToArray();
        }

        public static void Read(byte[] data)
        {
            Clear();
            var v = Parse(data);
            lock (gate)
            {
                Rented.AddRange(v.Rented);
                Active.AddRange(v.Active);
                Log.AddRange(v.Log);
                preferred = v.Preferred;
            }
            autoRented = v.AutoRented;
        }

        static Saved Parse(byte[] data)
        {
            var v = new Saved();
            using var r = new BinaryReader(new MemoryStream(data));
            int format = r.ReadInt32();
            if (format > Format) throw new InvalidDataException($"대관 형식 {format} 은 이 판보다 새것");
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++) v.Rented.Add(r.ReadUInt16());
            n = r.ReadInt32();
            for (int i = 0; i < n; i++)
                v.Active.Add(new Rent { Stadium = r.ReadUInt16(), Band = r.ReadString(), Start = r.ReadUInt32(), End = r.ReadUInt32(), Event = r.ReadUInt16(), AnchorPrev = r.ReadUInt16(), AnchorExpire = r.ReadUInt32(), State = r.ReadByte() });
            n = r.ReadInt32();
            for (int i = 0; i < n; i++) v.Log.Add(new RentLog { Stadium = r.ReadUInt16(), Band = r.ReadString(), Start = r.ReadUInt32() });
            if (format >= 2) v.Preferred = r.ReadUInt16();
            if (format >= 3) v.AutoRented = r.ReadBoolean();
            return v;
        }

        /// <summary>자체 검사(지역 값만 — 실제 대관 목록은 안 건드려 시뮬레이션 스레드가 가짜 넣기를 쓰지 않음): 쓰고 다시 읽어 같은지 · 경기 사이 틈 판단. 틀리면 설명, 맞으면 null.</summary>
        public static string SelfCheck()
        {
            if (!Fits(100, 300, 200) || Fits(100, 299, 200) || Fits(300, 100, 1)) return "경기 사이 틈 판단이 틀려요";
            var src = new Saved { Preferred = 31, AutoRented = true };
            src.Rented.Add(31);
            src.Active.Add(new Rent { Stadium = 31, Band = "Band A", Start = 100, End = 200, Event = 7, AnchorPrev = 5, AnchorExpire = 90, State = 1 });
            src.Log.Add(new RentLog { Stadium = 31, Band = "Band B", Start = 50 });
            var v = Parse(Write(src));
            var a = v.Active.Count == 1 ? v.Active[0] : null;
            return v.Rented.Count != 1 || v.Rented[0] != 31 || a == null || a.Band != "Band A" || a.Start != 100 || a.End != 200 || a.Event != 7 || a.AnchorPrev != 5 || a.AnchorExpire != 90 || a.State != 1
                || v.Log.Count != 1 || v.Log[0].Band != "Band B" || v.Log[0].Start != 50 || v.Preferred != 31 || !v.AutoRented ? "대관을 쓰고 읽은 값이 달라요" : null;
        }
    }

    /// <summary>모든 이벤트를 만들 때(경기 포함) 앞: 경기장 공연 다음 경기면 앞 이벤트를 공연 앞 경기로.</summary>
    [HarmonyPatch(typeof(EventAI), nameof(EventAI.CreateEvent))]
    internal static class EventCreatePatch
    {
        static void Prefix(EventAI __instance, ref EventData data)
        {
            try
            {
                Rental.OnEventCreate(__instance, ref data);
            }
            catch (Exception e)
            {
                Core.Safe.Report("공연장 대관: 다음 경기", e);
            }
        }
    }

    /// <summary>세이브에 대관(빌린 경기장 · 넣은 공연 · 기록)을 넣고 꺼냄.</summary>
    public sealed class RentalSave : SerializableDataExtensionBase
    {
        public override void OnLoadData()
        {
            Rental.Clear();
            try
            {
                var data = serializableDataManager.LoadData(Rental.SaveKey);
                if (data != null && data.Length > 0) Rental.Read(data);
            }
            catch (Exception e)
            {
                Core.Log.Warn("대관을 읽지 못해 빌린 경기장 없이 시작: " + e.Message);
                Rental.Clear();
            }
        }

        public override void OnSaveData()
        {
            try
            {
                serializableDataManager.SaveData(Rental.SaveKey, Rental.Write());
            }
            catch (Exception e)
            {
                Core.Log.Error("대관 저장 실패: " + e);
            }
        }
    }
}
