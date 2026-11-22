using System;
using System.Collections.Generic;
using System.IO;
using ColossalFramework;
using ColossalFramework.Math;
using ICities;
using UnityEngine;

namespace ConcertMod.Data
{
    /// <summary>세이브에 byte 로 남음: 번호를 바꾸지 말 것.</summary>
    internal enum LetterState : byte { Waiting = 0, Approved = 1, Booked = 2, Rejected = 3, Withdrawn = 4 }

    /// <summary>공연개최의향서 한 통: 밴드 · 기획사(또는 예매처 단독 제안) · 바라는 기간 · 시각 · 입장권 · 도착 · 철회 날 · 상태.</summary>
    internal sealed class Letter
    {
        public int Id, Agency, Site = -1, Days, Hour, Price;
        public string Band;
        public DateTime Arrived, Deadline, Booked;
        public LetterState State;
        public bool Held;
        /// <summary>희망 공연장(페스티벌 구역 또는 빌린 경기장, 0 = 그때 기본 공연장).</summary>
        public ushort Venue;
        /// <summary>철회한 까닭(없으면 답이 없어 철회).</summary>
        public string Why;
    }

    /// <summary>
    /// 의향서(캔버스 예약 · 의향서): 며칠마다 아티스트 기획사가 의향서를 보냄(밴드는 이 도시 게임 밴드, 기획사 이름은 가상 · 지역마다 문서 모양이 다름).
    /// 시장이 승인하면 그 공연장 다음 빈 공연 칸에 Booking 으로 잡음(그 공연장에 잡힌 계획이 있으면 끝난 뒤 · 다른 공연장 의향서는 따로) · 입장권은 그 계획 회차에만.
    /// 10일 안에 답이 없으면 철회, 보류는 한 번 10일 더. 승인하고 ApproveDays 안에 못 잡거나 잡은 계획이 풀리면 철회.
    /// 예매처 단독 제안은 승인하면 그 공연 표를 그 사이트에서만 팔아요. 모든 값은 본 스레드에서만 바꿈.
    /// </summary>
    internal static partial class Letters
    {
        public const string SaveKey = "ConcertMod.Letters";
        const int Format = 3;

        /// <summary>답 기다리는 날 · 보류로 더하는 날 · 동시에 기다리는 의향서 최대 · 남길 지난 의향서 · 다음 의향서까지 최소 · 최대 날 · 단독 제안 확률(%) · 단독 예매가 그 공연에 붙어 있는 날.</summary>
        public const int WaitDays = 10, HoldDays = 10, ApproveDays = 14, MaxWaiting = 4, Keep = 12, GapMin = 3, GapMax = 6, ExclusivePct = 20, ExclusiveDays = 30;

        /// <summary>가상 기획사(지역마다 셋: 한국 0 ~ 2 · 일본 3 ~ 5 · 해외 6 ~ 8): 이름 · 영문 · 문서 종류(0 공문 · 1 서한 · 2 레터헤드 · 3 메모).</summary>
        public static readonly string[] AgencyName = { "하늘별 엔터테인먼트", "별빛 뮤직", "소리숲 레이블", "未来レコーズ", "月光ミュージック", "Paper Moon", "Northlight Music", "Harbor & Vine Live", "Static Bloom" };
        public static readonly string[] AgencyEn = { "HANEULBYEOL ENT.", "BYEOLBIT MUSIC", "SORISUP", "MIRAI RECORDS", "GEKKO MUSIC", "PAPER MOON", "NORTHLIGHT MUSIC GROUP", "HARBOR & VINE LIVE", "STATIC BLOOM" };
        static readonly int[] AgencyKind = { 0, 0, 3, 1, 1, 3, 2, 2, 3 };
        static readonly string[] Signers = { "J. Calloway", "M. Hartley", "R. Okafor" };

        public static readonly List<Letter> All = new();
        public static DateTime NextAt;
        static int nextId = 1;

        public static int Waiting
        {
            get { int n = 0; foreach (var l in All) if (l.State == LetterState.Waiting) n++; return n; }
        }

        /// <summary>문서 종류: 단독 제안은 예매처 공문(0).</summary>
        public static int KindOf(Letter l) => l.Site >= 0 ? 0 : AgencyKind[Mathf.Clamp(l.Agency, 0, AgencyKind.Length - 1)];

        public static string FromOf(Letter l) => l.Site >= 0 ? Tickets.Sites[l.Site] : AgencyName[Mathf.Clamp(l.Agency, 0, AgencyName.Length - 1)];

        /// <summary>아티스트 지역(0 한국 · 1 일본 · 2 해외 · Artists 명단 지역 경계).</summary>
        static int Region(string key)
        {
            int i = Array.IndexOf(Artists.Names, Artists.Of(key));
            return i < 0 ? 2 : i < Artists.JapanFrom ? 0 : i < Artists.WorldFrom ? 1 : 2;
        }

        /// <summary>1초마다(본 스레드): 철회 · 승인된 의향서를 빈 칸에 잡기 · 새 의향서.</summary>
        public static void Tick()
        {
            lock (All) TickLocked();
        }

        static void TickLocked()
        {
            if (Shows.Venue == 0 || Shows.Bands.Count == 0) return;
            var now = SimulationManager.instance.m_currentGameTime;
            if (NextAt == default) NextAt = now.Date.AddDays(1);
            Expire(All, now);
            // 승인한 의향서는 오래된 것부터: 공연장마다 앞 계획이 다 열리면 그 의향서의 공연장에 잡음(막힌 공연장만 건너뜀)
            WaitWhy.Clear();
            bool logDay = now.Date != waitLogged;
            var lined = new HashSet<ushort>();
            for (int i = All.Count - 1; i >= 0; i--)
            {
                var l = All[i];
                if (l.State != LetterState.Approved) continue;
                if (Shows.BandOf(l.Band) == null) { Withdraw(l, "이 도시에 그 아티스트가 없어요", true); continue; }
                ushort venue = VenueOf(l);
                bool behind = !lined.Add(venue);
                string why = behind ? $"{Name(venue)}에 먼저 승인한 의향서가 잡히면 이어서 잡아요" : Blocked(venue);
                // 승인 기한: 그 공연장 줄 맨 앞(앞 의향서 · 계획 목록의 앞 계획이 없음)이 된 때부터 셈 · 다시 막히면 처음부터
                l.Deadline = Deadline(l.Deadline, !behind && Booking.At(venue) == null, now);
                if (l.Deadline != default && now >= l.Deadline) { Withdraw(l, $"차례가 되고 {ApproveDays}일 안에 잡지 못했어요", true); continue; }
                if (why != null)
                {
                    WaitWhy[l.Id] = why;
                    if (logDay) Core.Log.Info($"의향서 승인 대기: {Artists.Of(l.Band)} · {why}");
                    continue;
                }
                Booking.Set(l.Band, l.Days, l.Hour, venue, l.Price, l.Id);
                l.Booked = now;
                SetState(l, LetterState.Booked, null, false);
            }
            if (logDay) waitLogged = now.Date;
            if (now >= NextAt)
            {
                var rng = new Randomizer((ulong)now.Ticks);
                if (Waiting < MaxWaiting) Make(rng, now);
                NextAt = now.Date.AddDays(GapMin + rng.Int32((uint)(GapMax - GapMin + 1)));
            }
            Trim();
        }

        static DateTime waitLogged;

        /// <summary>답을 기다리다 기한이 지난 의향서는 철회.</summary>
        internal static void Expire(List<Letter> letters, DateTime now)
        {
            foreach (var l in letters)
                if (l.State == LetterState.Waiting && now >= l.Deadline) SetState(l, LetterState.Withdrawn, null, false); // 까닭 · 로그 · 알림 없음
        }

        /// <summary>승인 기한: 줄 맨 앞이 아니면 비움(셈 안 함) · 막 맨 앞이 됐으면 지금 + ApproveDays · 이미 세는 중이면 그대로.</summary>
        internal static DateTime Deadline(DateTime deadline, bool front, DateTime now) => !front ? default : deadline == default ? now.AddDays(ApproveDays) : deadline;

        /// <summary>승인한 의향서가 기다리는 까닭(의향서 번호 → 한 줄, Tick 마다 다시 셈).</summary>
        public static readonly Dictionary<int, string> WaitWhy = new();

        /// <summary>이 공연장에 새 계획을 못 잡는 까닭(잡아도 되면 null).</summary>
        static string Blocked(ushort venue)
        {
            if (venue == 0) return "공연장이 없어요";
            var p = Booking.At(venue);
            if (p != null) return $"{Name(venue)}에 잡힌 {Artists.Of(p.Band)} {p.Done}/{p.Days}회차가 끝나면 잡아요";
            return FreeSlot(venue) ? null : $"{Name(venue)} 앞 공연 마지막 회차가 준비를 시작하면 잡아요";
        }

        /// <summary>
        /// 계획이 풀리거나 남은 회차가 지워짐(본 스레드): 연 회차가 있으면 '잡음'으로 두고 일부만 열린 까닭을 남김, 없으면 철회.
        /// opened = 만든 회차 수(-1 = 의향서 일수 - 1 · 다 만든 계획의 마지막 회차를 지울 때).
        /// </summary>
        public static void Stop(int id, string why, bool notify, int opened)
        {
            lock (All)
                foreach (var l in All)
                {
                    if (l.Id != id) continue;
                    if (!KeepsBooked(l, ref opened)) { Withdraw(l, why, notify); return; }
                    l.Why = $"{l.Days}회차 중 {opened}회차만 잡혔어요 · {why}";
                    Core.Log.Info($"의향서 일부만 잡힘: {Artists.Of(l.Band)} · {l.Why}");
                    if (notify) CSCommonUX.Toast.Show($"{Artists.Of(l.Band)} 공연이 {opened}회차까지만 잡혔어요 · {why}");
                    return;
                }
        }

        /// <summary>Stop 판단: opened 를 맞추고(-1 = 일수 - 1) 연 회차가 있는 '잡음' 의향서면 true(그대로 둠), 아니면 철회.</summary>
        internal static bool KeepsBooked(Letter l, ref int opened)
        {
            if (opened < 0) opened = l.Days - 1;
            return opened > 0 && l.State == LetterState.Booked;
        }

        /// <summary>의향서 철회(본 스레드): 승인 · 잡힘 상태에서 까닭과 함께. notify = 알림 토스트.</summary>
        static void Withdraw(Letter l, string why, bool notify)
        {
            if (l.State != LetterState.Approved && l.State != LetterState.Booked) return;
            SetState(l, LetterState.Withdrawn, why, notify);
        }

        /// <summary>
        /// 의향서 상태 바꾸기는 모두 여기로(본 스레드) — 어느 상태에서 바꿀 수 있는지는 부르는 쪽이 봄. why 가 있으면 까닭으로 남김.
        /// 철회(까닭 있음) = 로그 · notify 면 알림, 승인 = 승인 기한 비움 · 로그, 잡음 = 확정 로그. 답 기한 지난 철회(까닭 없음) · 거절은 상태만.
        /// </summary>
        static void SetState(Letter l, LetterState to, string why, bool notify)
        {
            l.State = to;
            if (why != null) l.Why = why;
            switch (to)
            {
                case LetterState.Withdrawn when why != null:
                    Core.Log.Info($"의향서 철회: {Artists.Of(l.Band)} · {why}");
                    if (notify) CSCommonUX.Toast.Show($"{Artists.Of(l.Band)} 의향서를 철회했어요 · {why}");
                    break;
                case LetterState.Approved:
                    l.Deadline = default; // 승인 기한은 그 공연장 줄 맨 앞이 되면 Tick 이 셈
                    Core.Log.Info($"의향서 승인: {Artists.Of(l.Band)} · {Name(VenueOf(l))}");
                    break;
                case LetterState.Booked:
                    Core.Log.Info($"의향서 확정: {Artists.Of(l.Band)} · {Name(VenueOf(l))} · {l.Days}일 {l.Hour:00}:00");
                    break;
            }
        }

        static string Name(ushort b) => b != 0 ? Singleton<BuildingManager>.instance.GetBuildingName(b, InstanceID.Empty) ?? b.ToString() : "공연장 없음";

        /// <summary>의향서 공연장: 적힌 곳이 아직 페스티벌 구역이거나 빌린 경기장이면 그곳, 아니면 지금 기본 공연장.</summary>
        public static ushort VenueOf(Letter l) =>
            l.Venue != 0 && (Shows.IsFestival(l.Venue) || Rental.IsRented(l.Venue)) ? l.Venue : Shows.Venue;

        /// <summary>새 계획을 잡아도 되는지: 페스티벌 맨 앞 이벤트가 방금 만든 계획 마지막 회차(아직 준비 전)가 아닐 때.</summary>
        static bool FreeSlot(ushort venue)
        {
            if (!Shows.IsFestival(venue)) return true; // 경기장 계획은 대관이 빈 틈마다 넣어 덮어쓸 회차가 없음
            ushort head = Singleton<BuildingManager>.instance.m_buildings.m_buffer[venue].m_eventIndex;
            if (head == 0) return true;
            ref var e = ref Singleton<EventManager>.instance.m_events.m_buffer[head];
            bool started = (e.m_flags & (EventData.Flags.Preparing | EventData.Flags.Active | EventData.Flags.Completed | EventData.Flags.Cancelled)) != 0;
            return started || e.m_startFrame != Booking.TailAt(venue).Start;
        }

        static void Make(Randomizer rng, DateTime now)
        {
            var pool = new List<Band>();
            foreach (var b in Shows.Bands)
            {
                bool busy = false;
                foreach (var l in All) if (l.Band == b.Key && (l.State == LetterState.Waiting || l.State == LetterState.Approved)) busy = true;
                if (!busy) pool.Add(b);
            }
            if (pool.Count == 0) return;
            var band = pool[rng.Int32((uint)pool.Count)];
            int region = Region(band.Key);
            int baseP = band.Info.m_eventAI.m_ticketPrice;
            var l2 = new Letter
            {
                Id = nextId++,
                Band = band.Key,
                Agency = region * 3 + rng.Int32(3u),
                Days = band.Pop >= 60 ? 2 + rng.Int32(2u) : 1 + rng.Int32(2u),
                Hour = 18 + rng.Int32(4u),
                Price = Mathf.RoundToInt(baseP * (0.9f + 0.1f * rng.Int32(5u)) / 100f) * 100,
                Arrived = now,
                Deadline = now.AddDays(WaitDays),
            };
            if (rng.Int32(100u) < ExclusivePct) l2.Site = rng.Int32((uint)Tickets.Sites.Length);
            // 희망 공연장: 페스티벌 구역 · 빌린 경기장 중 하나
            var venues = new List<ushort>();
            venues.AddRange(Shows.Festivals);
            venues.AddRange(Rental.RentedSnapshot());
            l2.Venue = venues.Count > 0 ? venues[rng.Int32((uint)venues.Count)] : Shows.Venue;
            All.Insert(0, l2);
            Core.Log.Detail($"의향서 도착: {Artists.Of(l2.Band)} · {FromOf(l2)} · {Name(l2.Venue)} · {l2.Days}일 {l2.Hour:00}:00 · {l2.Price}");
        }

        /// <summary>지난 의향서(답한 것 · 철회)는 Keep 통까지만.</summary>
        static void Trim()
        {
            int kept = 0;
            for (int i = 0; i < All.Count; i++)
            {
                var s = All[i].State;
                if (s == LetterState.Waiting || s == LetterState.Approved) continue;
                if (++kept > Keep) All.RemoveAt(i--);
            }
        }

        public static void Approve(Letter l)
        {
            if (l.State != LetterState.Waiting) return;
            SetState(l, LetterState.Approved, null, false);
        }

        public static void Reject(Letter l) { if (l.State == LetterState.Waiting) SetState(l, LetterState.Rejected, null, false); }

        public static void Hold(Letter l)
        {
            if (l.State != LetterState.Waiting || l.Held) return;
            l.Held = true;
            l.Deadline = l.Deadline.AddDays(HoldDays);
        }

        /// <summary>이 밴드 공연의 단독 예매처(승인한 단독 제안이 ExclusiveDays 안이면 사이트 번호, 아니면 -1).</summary>
        public static int ExclusiveSite(string band)
        {
            var now = SimulationManager.instance.m_currentGameTime;
            foreach (var l in All)
                if (l.State == LetterState.Booked && l.Site >= 0 && l.Band == band && (now - l.Booked).TotalDays < ExclusiveDays) return l.Site;
            return -1;
        }

        public static byte[] Write()
        {
            List<Letter> copy;
            lock (All) copy = new List<Letter>(All);
            return Write(copy, nextId, NextAt);
        }

        static byte[] Write(List<Letter> copy, int id, DateTime at)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(Format);
            w.Write(id);
            w.Write(at.Ticks);
            w.Write(copy.Count);
            foreach (var l in copy)
            {
                w.Write(l.Id);
                w.Write(l.Band ?? "");
                w.Write(l.Agency);
                w.Write(l.Site);
                w.Write(l.Days);
                w.Write(l.Hour);
                w.Write(l.Price);
                w.Write(l.Arrived.Ticks);
                w.Write(l.Deadline.Ticks);
                w.Write(l.Booked.Ticks);
                w.Write((byte)l.State);
                w.Write(l.Held);
                w.Write(l.Venue);
                w.Write(l.Why ?? "");
            }
            w.Flush();
            return ms.ToArray();
        }

        public static void Read(byte[] data)
        {
            Clear();
            var list = Parse(data, out int id, out var at);
            nextId = id;
            NextAt = at;
            All.AddRange(list);
        }

        /// <summary>읽기(전역 값은 안 건드림): 형식 2 공연장 · 형식 3 철회 까닭 더함.</summary>
        static List<Letter> Parse(byte[] data, out int id, out DateTime at)
        {
            var list = new List<Letter>();
            using var r = new BinaryReader(new MemoryStream(data));
            int format = r.ReadInt32();
            if (format > Format) throw new InvalidDataException($"의향서 형식 {format} 은 이 판보다 새것");
            id = r.ReadInt32();
            at = new DateTime(r.ReadInt64());
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                var l = new Letter
                {
                    Id = r.ReadInt32(),
                    Band = r.ReadString(),
                    Agency = r.ReadInt32(),
                    Site = r.ReadInt32(),
                    Days = r.ReadInt32(),
                    Hour = r.ReadInt32(),
                    Price = r.ReadInt32(),
                    Arrived = new DateTime(r.ReadInt64()),
                    Deadline = new DateTime(r.ReadInt64()),
                    Booked = new DateTime(r.ReadInt64()),
                    State = (LetterState)r.ReadByte(),
                    Held = r.ReadBoolean(),
                };
                if (format >= 2) l.Venue = r.ReadUInt16();
                if (format >= 3) l.Why = r.ReadString();
                if (l.Why == "") l.Why = null;
                if (format < 3 && l.State == LetterState.Approved) l.Deadline = default; // 승인 기한은 줄 맨 앞이 되면 Tick 이 셈
                list.Add(l);
            }
            return list;
        }

        public static void Clear()
        {
            All.Clear();
            NextAt = default;
            nextId = 1;
            waitLogged = default;
        }

        /// <summary>자체 검사(지역 값만 — 실제 우편함은 안 건드림): 쓰고 다시 읽어 같은지 · 옛 형식 · 상태 번호 · 기한 · 보류 · 계획 풀림 판단. 틀리면 설명, 맞으면 null.</summary>
        public static string SelfCheck()
        {
            var src = new List<Letter> { new Letter { Id = 6, Band = "Band A", Agency = 4, Site = 2, Days = 3, Hour = 20, Price = 3200, Arrived = new DateTime(2026, 7, 17), Deadline = new DateTime(2026, 7, 27), State = LetterState.Booked, Held = true, Venue = 4321, Why = "까닭" } };
            var got = Parse(Write(src, 7, new DateTime(2026, 7, 20)), out int id, out var at);
            var l = got.Count == 1 ? got[0] : null;
            if (l?.Why != "까닭") return "의향서 철회 까닭을 쓰고 읽은 값이 달라요";
            if (id != 7 || at != new DateTime(2026, 7, 20) || l.Id != 6 || l.Band != "Band A" || l.Agency != 4 || l.Site != 2 || l.Days != 3 || l.Hour != 20 || l.Price != 3200
                || l.Arrived != new DateTime(2026, 7, 17) || l.Deadline != new DateTime(2026, 7, 27) || l.State != LetterState.Booked || !l.Held || l.Venue != 4321) return "의향서를 쓰고 읽은 값이 달라요";
            // 세이브 byte 상태 번호(번호 고정 전 순서): 0 기다림 · 1 승인 · 2 잡음 · 3 거절 · 4 철회
            var states = new[] { LetterState.Waiting, LetterState.Approved, LetterState.Booked, LetterState.Rejected, LetterState.Withdrawn };
            for (int i = 0; i < states.Length; i++)
            {
                if ((byte)states[i] != i) return "의향서 상태 번호가 세이브와 달라요";
                got = Parse(OldLetters(3, (byte)i), out _, out _);
                if (got.Count != 1 || got[0].State != states[i]) return $"의향서 상태 {i} 를 읽지 못해요";
            }
            // 형식 1(공연장 · 까닭 없음) · 형식 2(0.3 판, 까닭 없음): 승인한 의향서는 승인 기한을 비워 Tick 이 지금부터 셈
            for (int f = 1; f <= 2; f++)
            {
                got = Parse(OldLetters(f, (byte)LetterState.Approved), out id, out _);
                var old = got.Count == 1 ? got[0] : null;
                if (id != 9 || old == null || old.Band != "Band B" || old.Price != 3000 || old.State != LetterState.Approved || old.Deadline != default || old.Why != null || old.Venue != (f >= 2 ? 5 : 0))
                    return $"형식 {f} 승인 의향서를 읽지 못해요";
                got = Parse(OldLetters(f, (byte)LetterState.Waiting), out _, out _);
                if (got.Count != 1 || got[0].Deadline != new DateTime(2L)) return $"형식 {f} 기다리는 의향서 기한이 바뀌어요";
            }
            var t0 = new DateTime(2026, 8, 1);
            if (Deadline(t0, false, t0) != default || Deadline(default, true, t0) != t0.AddDays(ApproveDays) || Deadline(t0, true, t0.AddDays(3)) != t0) return "승인 기한 셈이 틀려요";
            // 답 기한: 지나면(같은 때 포함) 기다리는 의향서만 철회
            var wait = new List<Letter>
            {
                new Letter { State = LetterState.Waiting, Deadline = t0 },
                new Letter { State = LetterState.Waiting, Deadline = t0.AddTicks(1) },
                new Letter { State = LetterState.Approved, Deadline = t0.AddDays(-1) },
            };
            Expire(wait, t0);
            if (wait[0].State != LetterState.Withdrawn || wait[1].State != LetterState.Waiting || wait[2].State != LetterState.Approved) return "답 기한 지난 의향서 철회가 틀려요";
            // 보류: 기다리는 의향서에 한 번만 HoldDays 더함
            var held = new Letter { State = LetterState.Waiting, Deadline = t0 };
            Hold(held);
            Hold(held);
            var notWaiting = new Letter { State = LetterState.Approved, Deadline = t0 };
            Hold(notWaiting);
            if (!held.Held || held.Deadline != t0.AddDays(HoldDays) || notWaiting.Held || notWaiting.Deadline != t0) return "보류 셈이 틀려요";
            // 계획 풀림: -1 = 일수 - 1 · 0 은 철회 · 연 회차가 있어도 '잡음' 이 아니면 철회
            int o = -1;
            if (!KeepsBooked(new Letter { Days = 3, State = LetterState.Booked }, ref o) || o != 2) return "다 만든 계획 마지막 회차를 지울 때 판단이 틀려요";
            o = -1;
            if (KeepsBooked(new Letter { Days = 1, State = LetterState.Booked }, ref o) || o != 0) return "하루짜리 의향서 마지막 회차를 지울 때 철회하지 않아요";
            o = 0;
            if (KeepsBooked(new Letter { Days = 3, State = LetterState.Booked }, ref o)) return "연 회차 없이 풀린 의향서를 철회하지 않아요";
            o = 2;
            if (!KeepsBooked(new Letter { Days = 3, State = LetterState.Booked }, ref o) || o != 2) return "일부만 열린 의향서를 철회해요";
            o = 2;
            if (KeepsBooked(new Letter { Days = 3, State = LetterState.Approved }, ref o)) return "잡히지 않은 의향서를 '잡음' 으로 둬요";
            // 기획사 지역: 명단 지역 경계 앞뒤 · 모르는 이름은 해외
            if (Region("ROSÉ") != 0 || Region("YOASOBI") != 1 || Region("ONE OK ROCK") != 1 || Region("Taylor Swift") != 2 || Region("모르는 밴드") != 2) return "아티스트 지역 경계가 틀려요";
            return null;
        }

        /// <summary>자체 검사용 옛 형식 세이브(의향서 한 통: Band B · 기한 2 tick · 상태 state).</summary>
        static byte[] OldLetters(int format, byte state)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(format); w.Write(9); w.Write(0L); w.Write(1);
            w.Write(8); w.Write("Band B"); w.Write(0); w.Write(-1); w.Write(1); w.Write(19); w.Write(3000); w.Write(1L); w.Write(2L); w.Write(0L); w.Write(state); w.Write(false);
            if (format >= 2) w.Write((ushort)5);
            if (format >= 3) w.Write("");
            w.Flush();
            return ms.ToArray();
        }
    }

    /// <summary>세이브에 의향서를 넣고 꺼냄.</summary>
    public sealed class LettersSave : SerializableDataExtensionBase
    {
        public override void OnLoadData()
        {
            Letters.Clear();
            try
            {
                var data = serializableDataManager.LoadData(Letters.SaveKey);
                if (data != null && data.Length > 0) Letters.Read(data);
            }
            catch (Exception e)
            {
                Core.Log.Warn("의향서를 읽지 못해 빈 우편함으로 시작: " + e.Message);
                Letters.Clear();
            }
        }

        public override void OnSaveData()
        {
            try
            {
                serializableDataManager.SaveData(Letters.SaveKey, Letters.Write());
            }
            catch (Exception e)
            {
                Core.Log.Error("의향서 저장 실패: " + e);
            }
        }
    }
}
