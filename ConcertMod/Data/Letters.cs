using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ColossalFramework;
using ColossalFramework.Math;
using ICities;
using UnityEngine;

namespace ConcertMod.Data
{
    internal enum LetterState : byte { Waiting, Approved, Booked, Rejected, Withdrawn }

    /// <summary>공연개최의향서 한 통: 밴드 · 기획사(또는 예매처 단독 제안) · 바라는 기간 · 시각 · 입장권 · 도착 · 철회 날 · 상태.</summary>
    internal sealed class Letter
    {
        public int Id, Agency, Site = -1, Days, Hour, Price;
        public string Band;
        public DateTime Arrived, Deadline, Booked;
        public LetterState State;
        public bool Held;
    }

    /// <summary>
    /// 의향서(캔버스 예약 · 의향서): 며칠마다 아티스트 기획사가 의향서를 보냄(밴드는 이 도시 게임 밴드, 기획사 이름은 가상 · 지역마다 문서 모양이 다름).
    /// 시장이 승인하면 바라는 입장권으로 맞추고 다음 빈 공연 칸에 Booking 으로 잡음(이미 잡힌 계획이 있으면 끝난 뒤). 10일 안에 답이 없으면 철회, 보류는 한 번 10일 더.
    /// 예매처 단독 제안은 승인하면 그 공연 표를 그 사이트에서만 팔아요. 모든 값은 본 스레드에서만 바꿈.
    /// </summary>
    internal static class Letters
    {
        public const string SaveKey = "ConcertMod.Letters";
        const int Format = 1;

        /// <summary>답 기다리는 날 · 보류로 더하는 날 · 동시에 기다리는 의향서 최대 · 남길 지난 의향서 · 다음 의향서까지 최소 · 최대 날 · 단독 제안 확률(%) · 단독 예매가 그 공연에 붙어 있는 날.</summary>
        public const int WaitDays = 10, HoldDays = 10, MaxWaiting = 4, Keep = 12, GapMin = 3, GapMax = 6, ExclusivePct = 20, ExclusiveDays = 30;

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

        /// <summary>아티스트 지역(명단 순서: 한국 16 · 일본 12 · 해외 12).</summary>
        static int Region(string key)
        {
            int i = Array.IndexOf(Artists.Names, Artists.Of(key));
            return i < 0 ? 2 : i < 16 ? 0 : i < 28 ? 1 : 2;
        }

        /// <summary>1초마다(본 스레드): 철회 · 승인된 의향서를 빈 칸에 잡기 · 새 의향서.</summary>
        public static void Tick()
        {
            lock (All) TickLocked();
        }

        static void TickLocked()
        {
            if (Shows.Festival == 0 || Shows.Bands.Count == 0) return;
            var now = SimulationManager.instance.m_currentGameTime;
            if (NextAt == default) NextAt = now.Date.AddDays(1);
            foreach (var l in All)
                if (l.State == LetterState.Waiting && now >= l.Deadline) l.State = LetterState.Withdrawn;
            if (Booking.Current == null && FreeSlot())
                foreach (var l in All)
                    if (l.State == LetterState.Approved)
                    {
                        var band = Shows.BandOf(l.Band);
                        if (band == null) { l.State = LetterState.Withdrawn; continue; }
                        Booking.SetPrice(band.Info, l.Price);
                        Booking.Set(l.Band, l.Days, l.Hour);
                        l.State = LetterState.Booked;
                        l.Booked = now;
                        break;
                    }
            if (now >= NextAt)
            {
                var rng = new Randomizer((ulong)now.Ticks);
                if (Waiting < MaxWaiting) Make(rng, now);
                NextAt = now.Date.AddDays(GapMin + rng.Int32((uint)(GapMax - GapMin + 1)));
            }
            Trim();
        }

        /// <summary>새 계획을 잡아도 되는지: 페스티벌 맨 앞 이벤트가 방금 만든 계획 마지막 회차(아직 준비 전)가 아닐 때.</summary>
        static bool FreeSlot()
        {
            ushort head = Singleton<BuildingManager>.instance.m_buildings.m_buffer[Shows.Festival].m_eventIndex;
            if (head == 0) return true;
            ref var e = ref Singleton<EventManager>.instance.m_events.m_buffer[head];
            bool started = (e.m_flags & (EventData.Flags.Preparing | EventData.Flags.Active | EventData.Flags.Completed | EventData.Flags.Cancelled)) != 0;
            return started || e.m_startFrame != Booking.LastPlanStart;
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
            All.Insert(0, l2);
            Core.Log.Detail($"의향서 도착: {Artists.Of(l2.Band)} · {FromOf(l2)} · {l2.Days}일 {l2.Hour:00}:00 · {l2.Price}");
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

        public static void Approve(Letter l) { if (l.State == LetterState.Waiting) l.State = LetterState.Approved; }

        public static void Reject(Letter l) { if (l.State == LetterState.Waiting) l.State = LetterState.Rejected; }

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

        /// <summary>문서 내용(정본 Concert.Letter 가 그림).</summary>
        public static CSCommonUX.Concert.Doc Doc(Letter l)
        {
            var band = Shows.BandOf(l.Band);
            string name = band?.Name ?? Artists.Of(l.Band), city = SimulationManager.instance.m_metaData?.m_CityName ?? "";
            int kind = KindOf(l), pop = band?.Pop ?? 0;
            string from = FromOf(l), en = l.Site >= 0 ? from : AgencyEn[Mathf.Clamp(l.Agency, 0, AgencyEn.Length - 1)];
            var t = l.Arrived;
            string seal = (from.Replace(" ", "").Replace("&", "") + "印印印印").Substring(0, 4);
            string price = "₡" + (l.Price / 100.0).ToString("0.00");
            var d = new CSCommonUX.Concert.Doc
            {
                Kind = kind,
                Band = band?.Color ?? Pal.Primary,
                Agency = from,
                AgencyEn = en,
                City = city,
                To = city + " 시장",
                Year = t.Year.ToString(),
                Seal = kind == 0 ? from : seal,
                Keys = new[] { "희망 공연장", "희망 일정", "희망 입장권", "예상 관객", l.Site >= 0 ? "예매처" : "시에 드릴 것" },
                Vals = new[]
                {
                    Shows.FestivalName.Length > 0 ? Shows.FestivalName : "페스티벌 구역",
                    $"{l.Days}일 · 매일 {l.Hour:00}:00 시작",
                    price,
                    $"회차마다 약 {Tickets.SessionCap(Shows.Festival):#,0}명 · 인기 {pop}",
                    l.Site >= 0 ? $"{from} 단독 예매" : "입장권 수입은 시에 그대로",
                },
            };
            switch (kind)
            {
                case 0:
                    d.No = $"{(from.Length > 2 ? from.Substring(0, 2) : from)}-공연 제{t.Year}-{l.Id:000}호";
                    d.Date = $"{t.Year}. {t.Month}. {t.Day}.";
                    d.Subject = l.Site >= 0 ? $"{name} 단독 공연 제안" : $"{name} 공연 개최 의향 알림";
                    d.Body = l.Site >= 0
                        ? new[] { "1. 귀 시의 무궁한 발전을 기원합니다.", $"2. 당사는 {name}의 공연을 귀 시에서 열고 표를 당사에서만 팔고자 아래와 같이 제안합니다." }
                        : new[] { "1. 귀 시의 무궁한 발전을 기원합니다.", $"2. 당사 소속 아티스트 {name}의 공연을 귀 시에서 열고자 아래와 같이 의향을 알립니다." };
                    break;
                case 1:
                    d.No = $"MRR-{t.Year}-{t.Month:00}{t.Day:00}";
                    d.Date = $"{t.Year}년 {t.Month}월 {t.Day}일";
                    d.Body = new[] { "늘 귀 도시의 발전을 기원합니다.", $"저희 {en}는 소속 아티스트 {name}의 공연을 귀 도시에서 열고자 합니다." };
                    break;
                case 2:
                    d.No = $"LOI/{t.Year % 100:00}-{l.Id:000}";
                    d.Date = t.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
                    d.Subject = $"Letter of Intent — {name} Live in {city}";
                    d.Body = new[] { $"We would be delighted to bring {name} to your city.", $"저희는 {name}의 공연을 귀 도시에서 열고 싶어요. 아래 조건을 검토해 주세요." };
                    d.Sign = Signers[l.Id % Signers.Length];
                    break;
                default:
                    d.No = $"memo #{l.Id}";
                    d.Date = $"{t:yyyy-MM-dd}";
                    d.Subject = $"{name} 공연 하고 싶어요";
                    d.Body = new[] { $"안녕하세요. 저희 {name}가 이 도시 무대에 서고 싶어요.", "크지 않아도 괜찮아요. 아래가 저희가 바라는 거예요." };
                    d.Sign = $"답 기다릴게요. — {name} 드림";
                    break;
            }
            return d;
        }

        public static byte[] Write()
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(Format);
            w.Write(nextId);
            w.Write(NextAt.Ticks);
            List<Letter> copy;
            lock (All) copy = new List<Letter>(All);
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
            }
            w.Flush();
            return ms.ToArray();
        }

        public static void Read(byte[] data)
        {
            Clear();
            using var r = new BinaryReader(new MemoryStream(data));
            int format = r.ReadInt32();
            if (format > Format) throw new InvalidDataException($"의향서 형식 {format} 은 이 판보다 새것");
            nextId = r.ReadInt32();
            NextAt = new DateTime(r.ReadInt64());
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
                All.Add(new Letter
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
                });
        }

        public static void Clear()
        {
            All.Clear();
            NextAt = default;
            nextId = 1;
        }

        /// <summary>자체 검사: 쓰고 다시 읽어 같은지. 틀리면 설명, 맞으면 null.</summary>
        public static string SelfCheck()
        {
            var keep = new List<Letter>(All);
            var keepAt = NextAt;
            int keepId = nextId;
            try
            {
                Clear();
                nextId = 7;
                NextAt = new DateTime(2026, 7, 20);
                All.Add(new Letter { Id = 6, Band = "Band A", Agency = 4, Site = 2, Days = 3, Hour = 20, Price = 3200, Arrived = new DateTime(2026, 7, 17), Deadline = new DateTime(2026, 7, 27), State = LetterState.Booked, Held = true });
                Read(Write());
                var l = All.Count == 1 ? All[0] : null;
                return l == null || nextId != 7 || NextAt != new DateTime(2026, 7, 20) || l.Id != 6 || l.Band != "Band A" || l.Agency != 4 || l.Site != 2 || l.Days != 3 || l.Hour != 20 || l.Price != 3200
                    || l.Deadline != new DateTime(2026, 7, 27) || l.State != LetterState.Booked || !l.Held ? "의향서를 쓰고 읽은 값이 달라요" : null;
            }
            finally
            {
                Clear();
                All.AddRange(keep);
                NextAt = keepAt;
                nextId = keepId;
            }
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
