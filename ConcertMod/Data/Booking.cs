using System;
using System.IO;
using ColossalFramework;
using HarmonyLib;
using ICities;
using UnityEngine;

namespace ConcertMod.Data
{
    /// <summary>
    /// 시장이 잡은 다음 공연: 밴드 · 1 ~ 3일(회차) · 시작 시각. 게임이 다음 공연을 정할 때(FindNextEventInfo) 그 밴드로,
    /// 공연을 만들 때(ConcertAI.CreateEvent) 회차마다 같은 시각 · 하루 간격으로 시작하게 바꿈. 회차를 다 만들면 계획을 지우고 다음부터는 게임대로.
    /// 값은 시뮬레이션 스레드에서만 바꿈(Set · Clear 는 AddAction 으로).
    /// </summary>
    internal sealed class Plan
    {
        public string Band;
        public ushort Building;
        public int Days, Hour, Done;
        /// <summary>계획을 잡을 때 페스티벌 맨 앞 이벤트가 끝나는 프레임(이 칸 다음 공연에만 적용) · 바로 앞 회차 시작 프레임.</summary>
        public uint Anchor, LastStart;
        /// <summary>기본 공연장이 빌린 경기장(페스티벌 구역 없음): 회차는 대관이 경기 사이 빈 틈마다 하나씩 넣음.</summary>
        public bool Stadium;
    }

    internal static class Booking
    {
        public static Plan Current;

        /// <summary>방금 다 만든 계획의 마지막 회차 시작 프레임(의향서가 그 회차를 지우고 새 계획을 잡지 않게).</summary>
        public static uint LastPlanStart;

        public const int MinDays = 1, MaxDays = 3;

        public const string SaveKey = "ConcertMod.Booking";

        const int Format = 1;

        static float HourFrames => SimulationManager.DAYTIME_HOUR_TO_FRAME;

        static uint Day => SimulationManager.DAYTIME_FRAMES;

        /// <summary>게임이 정한 시작 프레임과 같은 날(지나면 다음 날) hour 시.</summary>
        internal static uint AtHour(uint frame, int hour)
        {
            uint tod = (frame + Singleton<SimulationManager>.instance.m_dayTimeOffsetFrames) & (Day - 1);
            uint f = frame - tod + (uint)Mathf.RoundToInt(hour * HourFrames);
            return f < frame ? f + Day : f;
        }

        /// <summary>이 칸에 계획을 쓸지: 첫 회차는 잡을 때 그 칸 · 다음 회차는 앞 회차가 같은 밴드일 때.</summary>
        static bool Applies(Plan p, ushort building, EventInfo prev, uint prevExpire, EventInfo band) =>
            p != null && !p.Stadium && band != null && building == p.Building && p.Done < p.Days &&
            (p.Done == 0 ? prevExpire == p.Anchor : prev == band);

        static EventInfo BandInfo(Plan p) => p == null ? null : Shows.BandOf(p.Band)?.Info;

        /// <summary>회차 시작 프레임: 첫 회차 = 게임이 정한 날의 hour 시, 다음 회차 = 앞 회차 + 하루.</summary>
        static uint StartOf(Plan p, uint gameStart) => p.Done == 0 ? AtHour(gameStart, p.Hour) : p.LastStart + Day;

        /// <summary>FindNextEventInfo 뒤: 계획 칸이면 밴드 · 시작 프레임을 바꿈(화면 다음 공연 미리 보기도 같은 길로).</summary>
        public static void OnFindNext(ushort building, EventInfo prev, uint prevExpire, ref EventInfo next, ref uint nextStart, ref bool result)
        {
            var p = Current;
            var band = BandInfo(p);
            if (!Applies(p, building, prev, prevExpire, band)) return;
            uint game = result && next != null ? nextStart : band.m_eventAI.CalculateStartFrame(prevExpire, false);
            next = band;
            nextStart = StartOf(p, game);
            result = true;
        }

        /// <summary>ConcertAI.CreateEvent 뒤: 계획 회차면 시작 · 끝 프레임을 맞추고 회차를 셈.</summary>
        public static void OnCreate(ushort eventId, ref EventData data)
        {
            var p = Current;
            var band = BandInfo(p);
            var em = Singleton<EventManager>.instance;
            EventInfo prev = data.m_nextBuildingEvent != 0 ? em.m_events.m_buffer[data.m_nextBuildingEvent].Info : null;
            uint prevExpire = data.m_nextBuildingEvent != 0 ? em.m_events.m_buffer[data.m_nextBuildingEvent].m_expireFrame : 0u;
            if (data.Info != band || !Applies(p, data.m_building, prev, prevExpire, band)) return;
            var ai = band.m_eventAI;
            uint start = StartOf(p, data.m_startFrame);
            bool last = p.Done + 1 >= p.Days;
            data.m_startFrame = start;
            // 마지막 회차만 게임의 다음 공연 간격, 그 앞 회차는 공연 · 철수 뒤 바로 다음 회차를 만들게
            data.m_expireFrame = last ? ai.CalculateExpireFrame(start) : start + (uint)Mathf.RoundToInt((ai.m_eventDuration + ai.m_disorganizeDuration) * HourFrames);
            p.LastStart = start;
            p.Done++;
            Lineup.Bind(eventId, start, Shows.BandOf(p.Band)?.Key);
            Core.Log.Detail($"공연 예약: {Artists.Of(p.Band)} {p.Done}/{p.Days}회차 · 이벤트 {eventId} · {Singleton<SimulationManager>.instance.FrameToTime(start):MM-dd HH:mm}");
            if (p.Done >= p.Days)
            {
                LastPlanStart = start;
                Current = null;
            }
        }

        /// <summary>대관이 경기장 공연을 만들었을 때(시뮬레이션 스레드): 경기장 계획 회차를 셈.</summary>
        public static void OnStadiumShow(ushort stadium, string band, uint start)
        {
            var p = Current;
            if (p == null || !p.Stadium || p.Building != stadium || p.Band != band) return;
            p.LastStart = start;
            p.Done++;
            if (p.Done >= p.Days)
            {
                LastPlanStart = start;
                Current = null;
            }
        }

        /// <summary>계획 잡기(본 스레드에서 부름 → 시뮬레이션 스레드). 이미 만들어진 아직 준비 전 공연은 지워 게임이 계획대로 다시 만들게.</summary>
        public static void Set(string bandKey, int days, int hour, ushort venue = 0)
        {
            ushort festival = venue != 0 ? venue : Shows.Venue;
            if (festival == 0) return;
            bool stadium = festival != Shows.Festival;
            SimulationManager.instance.AddAction(() =>
            {
                if (stadium)
                {
                    // 경기장: 자리 잡는 중인 넣기를 거두면 대관이 다음 빈 틈에 이 계획대로 다시 넣음
                    Rental.DropPlanned(festival);
                    Current = new Plan { Band = bandKey, Building = festival, Days = Mathf.Clamp(days, MinDays, MaxDays), Hour = Mathf.Clamp(hour, 0, 23), Stadium = true };
                    return;
                }
                var em = Singleton<EventManager>.instance;
                ref var building = ref Singleton<BuildingManager>.instance.m_buildings.m_buffer[festival];
                ushort head = building.m_eventIndex;
                if (head != 0)
                {
                    var f = em.m_events.m_buffer[head].m_flags;
                    if ((f & (EventData.Flags.Preparing | EventData.Flags.Active | EventData.Flags.Completed | EventData.Flags.Cancelled)) == 0)
                    {
                        em.ReleaseEvent(head);
                        head = building.m_eventIndex;
                    }
                }
                Current = new Plan
                {
                    Band = bandKey,
                    Building = festival,
                    Days = Mathf.Clamp(days, MinDays, MaxDays),
                    Hour = Mathf.Clamp(hour, 0, 23),
                    Anchor = head != 0 ? em.m_events.m_buffer[head].m_expireFrame : 0u,
                };
            });
        }

        /// <summary>계획 지우기(게임이 고른 밴드로). 이미 만들어진 준비 전 공연은 그대로.</summary>
        public static void Clear() => SimulationManager.instance.AddAction(() => Current = null);

        /// <summary>아티스트 입장권(그 아티스트의 모든 공연에 적용, 출연표 세이브에 남음).</summary>
        public static void SetPrice(Band band, int price) => Lineup.SetPrice(band.Key, price - band.Info.m_eventAI.m_ticketPrice);

        public static byte[] Write()
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(Format);
            var p = Current;
            w.Write(p != null);
            if (p != null)
            {
                w.Write(p.Band ?? "");
                w.Write(p.Building);
                w.Write(p.Days);
                w.Write(p.Hour);
                w.Write(p.Done);
                w.Write(p.Anchor);
                w.Write(p.LastStart);
            }
            w.Write(LastPlanStart); // 뒤에 덧붙임: 옛 판은 여기서 끝나 읽지 않음
            w.Write(p != null && p.Stadium);
            w.Flush();
            return ms.ToArray();
        }

        public static void Read(byte[] data)
        {
            Current = null;
            LastPlanStart = 0;
            using var r = new BinaryReader(new MemoryStream(data));
            int format = r.ReadInt32();
            if (format > Format) throw new InvalidDataException($"공연 예약 형식 {format} 은 이 판보다 새것");
            if (r.ReadBoolean())
                Current = new Plan { Band = r.ReadString(), Building = r.ReadUInt16(), Days = r.ReadInt32(), Hour = r.ReadInt32(), Done = r.ReadInt32(), Anchor = r.ReadUInt32(), LastStart = r.ReadUInt32() };
            if (r.BaseStream.Length - r.BaseStream.Position >= 4) LastPlanStart = r.ReadUInt32();
            if (r.BaseStream.Length - r.BaseStream.Position >= 1 && r.ReadBoolean() && Current != null) Current.Stadium = true;
        }

        /// <summary>자체 검사: 쓰고 다시 읽어 같은지. 틀리면 설명, 맞으면 null.</summary>
        public static string SelfCheck()
        {
            var keep = Current;
            uint keepLast = LastPlanStart;
            try
            {
                Current = new Plan { Band = "Band A", Building = 12, Days = 3, Hour = 19, Done = 1, Anchor = 9, LastStart = 77, Stadium = true };
                LastPlanStart = 55;
                Read(Write());
                var p = Current;
                return p == null || p.Band != "Band A" || p.Building != 12 || p.Days != 3 || p.Hour != 19 || p.Done != 1 || p.Anchor != 9 || p.LastStart != 77 || !p.Stadium || LastPlanStart != 55 ? "공연 예약을 쓰고 읽은 값이 달라요" : null;
            }
            finally
            {
                Current = keep;
                LastPlanStart = keepLast;
            }
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
            Booking.Current = null;
            Booking.LastPlanStart = 0;
            try
            {
                var data = serializableDataManager.LoadData(Booking.SaveKey);
                if (data != null && data.Length > 0) Booking.Read(data);
            }
            catch (Exception e)
            {
                Core.Log.Warn("공연 예약을 읽지 못해 예약 없이 시작: " + e.Message);
                Booking.Current = null;
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
