using System;
using System.Collections.Generic;
using System.IO;
using ICities;

namespace ConcertMod.Data
{
    /// <summary>끝난 공연 하나(모드가 세이브에 남김): 밴드 · 날짜 · 공연장 · 최다 입장 · 매표 수입 · 보상금 · 성공 · 인기 변화 · 그때 모든 밴드 인기.</summary>
    internal sealed class Record
    {
        public uint Frame;
        public string Key, Venue;
        public long StartTicks, Ticket, Reward;
        public int Visitors, PopDelta, PopAfter, Security;
        public bool Success;
        /// <summary>공연장 건물(형식 3부터 · 옛 기록은 0 이라 공연장 이름으로 가림).</summary>
        public ushort Building;
        public Dictionary<string, int> Pops = new();

        public DateTime Start => new(StartTicks);
    }

    /// <summary>
    /// 공연 기록: 공연이 열리는 동안 최다 입장을 세고, 끝나면 한 줄 남김(같은 공연은 한 번).
    /// 모드를 넣기 전에 끝난 공연은 없음. 세이브 키 ConcertMod.History(형식 3: 공연장 건물 더함 · 형식 2: 보안 예산 더함 · 판 1도 읽음).
    /// </summary>
    internal static class History
    {
        /// <summary>지난 공연(본 스레드가 바꾸고 화면이 읽음 · 저장은 시뮬레이션 스레드). 바꾸거나 사본 뜰 때 lock (All) — released lock 과 겹쳐 잡지 않음.</summary>
        public static readonly List<Record> All = new();

        const int Format = 3;
        const int Keep = 500;
        public const string SaveKey = "ConcertMod.History";

        static readonly Dictionary<ushort, int> peak = new();
        /// <summary>
        /// 적은 공연: (공연장 건물, 시작 프레임) — 페스티벌 · 경기장에서 같은 시각에 열린 공연을 따로 셈.
        /// 옛 기록(건물 없음)은 시작 프레임만: 0.3 판도 프레임만으로 가려 같은 프레임 옛 기록은 하나뿐이고, 공연장 이름을 바꿔도 버퍼의 지난 공연을 다시 적지 않음.
        /// </summary>
        static readonly HashSet<ulong> seen = new();
        static readonly HashSet<uint> seenOld = new();

        static ulong Key(ushort building, uint frame) => (ulong)building << 32 | frame;

        static bool Seen(Show s) => seen.Contains(Key(s.Building, s.StartFrame)) || seenOld.Contains(s.StartFrame);

        static void Mark(Record r)
        {
            if (r.Building != 0) seen.Add(Key(r.Building, r.Frame));
            else seenOld.Add(r.Frame);
        }

        /// <summary>대관이 지우기 직전 넘긴 경기장 공연 결과(시뮬레이션 스레드가 넣고 본 스레드 Track 이 꺼냄).</summary>
        static readonly List<Show> released = new();

        public static void Released(Show s)
        {
            lock (released) released.Add(s);
        }

        /// <summary>Shows.Refresh 뒤 부름: 열리는 공연 최다 입장 갱신 · 끝난 공연 기록(지워진 경기장 공연 포함).</summary>
        public static void Track()
        {
            var set = ConcertSetting.Current;
            foreach (var s in Shows.All)
            {
                if (s.Active)
                {
                    if (!peak.ContainsKey(s.Id) && primed && set.NotifyStart)
                        CSCommonUX.Toast.Show($"{s.Band.Name} 공연을 시작했어요 · {s.Venue}");
                    peak[s.Id] = Math.Max(peak.TryGetValue(s.Id, out int p) ? p : 0, s.Arrived);
                }
                Finish(s);
            }
            Show[] gone;
            lock (released)
            {
                gone = released.ToArray();
                released.Clear();
            }
            foreach (var s in gone) Finish(s);
            primed = true;
        }

        /// <summary>끝난 공연 한 줄(같은 시작 프레임은 한 번): 알림 · 인기 반영 · 기록. 적었으면 true.</summary>
        static bool Finish(Show s)
        {
            var set = ConcertSetting.Current;
            if (!s.Done || s.Cancelled || Seen(s)) return false;
            if (primed && set.NotifyResult)
                CSCommonUX.Toast.Show($"{s.Band.Name} 공연이 끝났어요 · 관객 {(peak.TryGetValue(s.Id, out int pv) ? pv : s.Arrived):#,0}명 · 입장권 ₡{s.Ticket / 100.0:#,0} · {(s.Success ? "성공" : "아쉬움")}",
                    s.Success ? CSCommonUX.ToastKind.Success : CSCommonUX.ToastKind.Info);
            seen.Add(Key(s.Building, s.StartFrame));
            // 인기 변화는 아티스트에(불러온 뒤 첫 Track 의 이미 끝난 공연은 그 전에 더해졌으므로 뺌)
            if (primed)
            {
                Lineup.Finish(s.Band.Key, s.PopDelta);
                s.Band.Pop = Lineup.Pop(s.Band.Key);
            }
            var r = new Record
            {
                Frame = s.StartFrame,
                Key = s.Band.Key,
                Venue = s.Venue,
                StartTicks = s.Start.Ticks,
                Ticket = s.Ticket,
                Reward = s.Reward,
                Building = s.Building,
                Visitors = peak.TryGetValue(s.Id, out int v) ? v : s.Arrived,
                PopDelta = s.PopDelta,
                PopAfter = s.Band.Pop,
                Success = s.Success,
                Security = s.Security,
            };
            foreach (var b in Shows.Bands) r.Pops[b.Key] = b.Pop;
            peak.Remove(s.Id);
            lock (All)
            {
                All.Add(r);
                if (All.Count > Keep) All.RemoveAt(0);
            }
            return true;
        }

        /// <summary>도시를 불러온 뒤 첫 Track 은 알림 없이(이미 열리고 있거나 끝난 공연을 다시 알리지 않게).</summary>
        static bool primed;

        /// <summary>설정 '공연 기록 지우기': 세이브에 남긴 지난 공연을 모두(이미 끝난 공연을 다시 적지 않게 본 표시는 둠).</summary>
        public static void Forget()
        {
            lock (All) All.Clear();
        }

        /// <summary>옛 세이브: 밴드 프리팹 이름으로 남은 기록을 그때 붙던 아티스트 이름으로(LoadBands 에서 Artists.Assign 뒤).</summary>
        public static void Rekey()
        {
            lock (All)
                foreach (var r in All)
                {
                    r.Key = Artists.Of(r.Key);
                    var pops = new Dictionary<string, int>();
                    foreach (var kv in r.Pops) pops[Artists.Of(kv.Key)] = kv.Value;
                    r.Pops = pops;
                }
        }

        public static void Clear()
        {
            primed = false;
            lock (All) All.Clear();
            peak.Clear();
            seen.Clear();
            seenOld.Clear();
            lock (released) released.Clear();
        }

        /// <summary>쓰기: 사본으로(저장은 시뮬레이션 스레드라 본 스레드 Track 이 목록을 바꾸는 중일 수 있음).</summary>
        public static byte[] Write()
        {
            List<Record> copy;
            lock (All) copy = new List<Record>(All);
            return Write(copy);
        }

        static byte[] Write(List<Record> list)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(Format);
            w.Write(list.Count);
            foreach (var r in list)
            {
                w.Write(r.Frame);
                w.Write(r.Key ?? "");
                w.Write(r.Venue ?? "");
                w.Write(r.StartTicks);
                w.Write(r.Ticket);
                w.Write(r.Reward);
                w.Write(r.Visitors);
                w.Write(r.PopDelta);
                w.Write(r.PopAfter);
                w.Write(r.Success);
                w.Write(r.Security);
                w.Write(r.Building);
                w.Write(r.Pops.Count);
                foreach (var kv in r.Pops)
                {
                    w.Write(kv.Key);
                    w.Write(kv.Value);
                }
            }
            w.Flush();
            return ms.ToArray();
        }

        /// <summary>읽기. 0.2.0 판은 형식 2 인데 보안 예산을 쓰지 않았음 → 그렇게 읽다 어긋나면 보안 예산 없이 다시 읽음.</summary>
        public static void Read(byte[] data)
        {
            Clear();
            var list = Parse(data);
            lock (All) All.AddRange(list);
            foreach (var rec in list) Mark(rec);
        }

        /// <summary>읽기(전역 값은 안 건드림): 보안 예산을 넣어 읽고, 어긋나면 보안 예산 없이 다시 읽음.</summary>
        static List<Record> Parse(byte[] data)
        {
            try
            {
                return Parse(data, true);
            }
            catch (Exception)
            {
                return Parse(data, false);
            }
        }

        static List<Record> Parse(byte[] data, bool security)
        {
            var list = new List<Record>();
            using var r = new BinaryReader(new MemoryStream(data));
            int format = r.ReadInt32();
            if (format > Format) throw new InvalidDataException($"공연 기록 형식 {format} 은 이 판보다 새것");
            int n = r.ReadInt32();
            if (n < 0 || n > Keep) throw new InvalidDataException($"공연 기록 수 {n} 이 이상해요");
            for (int i = 0; i < n; i++)
            {
                var rec = new Record
                {
                    Frame = r.ReadUInt32(),
                    Key = r.ReadString(),
                    Venue = r.ReadString(),
                    StartTicks = r.ReadInt64(),
                    Ticket = r.ReadInt64(),
                    Reward = r.ReadInt64(),
                    Visitors = r.ReadInt32(),
                    PopDelta = r.ReadInt32(),
                    PopAfter = r.ReadInt32(),
                    Success = r.ReadBoolean(),
                };
                if (format >= 2 && security) rec.Security = r.ReadInt32();
                if (format >= 3) rec.Building = r.ReadUInt16();
                int m = r.ReadInt32();
                for (int k = 0; k < m; k++) rec.Pops[r.ReadString()] = r.ReadInt32();
                list.Add(rec);
            }
            if (SaveRead.More(r, 1)) throw new InvalidDataException("공연 기록 뒤에 남은 자료가 있어요");
            return list;
        }

        /// <summary>
        /// 자체 검사: 쓰고 다시 읽어 같은지 · 옛 형식(지역 값만 — 실제 기록은 안 건드림) · 끝난 공연 한 번만 적기(이 부분만 전역 기록을 잠시 비웠다 되돌림).
        /// 틀리면 설명, 맞으면 null.
        /// </summary>
        public static string SelfCheck()
        {
            var a = new Record { Frame = 7, Key = "Band A", Venue = "공연장", StartTicks = 638000000000000000, Ticket = 252360, Reward = 50000, Visitors = 8412, PopDelta = -3, PopAfter = 61, Success = true, Security = 1200, Building = 44 };
            a.Pops["Band A"] = 61;
            var got = Parse(Write(new List<Record> { a }));
            var b = got.Count == 1 ? got[0] : null;
            if (b == null || b.Frame != 7 || b.Key != a.Key || b.Venue != a.Venue || b.StartTicks != a.StartTicks || b.Ticket != a.Ticket || b.Reward != a.Reward || b.Visitors != a.Visitors
                || b.PopDelta != a.PopDelta || b.PopAfter != 61 || !b.Success || b.Security != 1200 || b.Building != 44 || b.Pops["Band A"] != 61) return "공연 기록을 쓰고 읽은 값이 달라요";
            // 형식 1 · 형식 2(보안 예산 있음) · 0.2.0 판(형식 2 인데 보안 예산 없음)
            got = Parse(OldHistory(1, false, false));
            b = got.Count == 1 ? got[0] : null;
            if (b == null || b.Key != "Band A" || b.Visitors != 8412 || b.Security != 0 || b.Building != 0 || b.Pops["Band A"] != 61) return "형식 1 공연 기록을 읽지 못해요";
            got = Parse(OldHistory(2, true, false));
            b = got.Count == 1 ? got[0] : null;
            if (b == null || b.Key != "Band A" || b.Visitors != 8412 || b.Security != 1200 || b.Building != 0 || b.Pops["Band A"] != 61) return "형식 2 공연 기록(보안 예산 있음)을 읽지 못해요";
            got = Parse(OldHistory(2, false, false));
            b = got.Count == 1 ? got[0] : null;
            if (b == null || b.Key != "Band A" || b.Visitors != 8412 || b.Security != 0 || b.Pops["Band A"] != 61) return "0.2.0 판 공연 기록을 읽지 못해요";
            // 끝에 남은 자료가 있으면 읽지 않음
            bool threw = false;
            try { Parse(OldHistory(1, false, true)); }
            catch (Exception) { threw = true; }
            if (!threw) return "끝에 남은 자료가 있는 공연 기록을 그냥 읽어요";
            List<Record> keep;
            lock (All) keep = new List<Record>(All);
            var keepSeen = new HashSet<ulong>(seen);
            var keepSeenOld = new HashSet<uint>(seenOld);
            var keepPeak = new Dictionary<ushort, int>(peak);
            bool keepPrimed = primed;
            List<Show> keepReleased;
            lock (released) keepReleased = new List<Show>(released);
            try
            {
                // 지워진 경기장 공연: 같은 시작 프레임은 한 번만 적음(primed 전이라 알림 · 인기 반영 없음)
                Clear();
                var gone = new Show { Id = 3, Building = 31, Venue = "경기장", StartFrame = 900, Done = true, Band = new Band { Key = "Band A", Name = "Band A" }, Arrived = 120, Ticket = 5000 };
                if (!Finish(gone) || Finish(gone) || All.Count != 1 || All[0].Frame != 900 || All[0].Visitors != 120) return "지워진 경기장 공연 기록이 빠지거나 두 번 적혀요";
                // 같은 시각 다른 공연장 공연은 따로 적음
                var same = new Show { Id = 4, Building = 12, Venue = "페스티벌", StartFrame = 900, Done = true, Band = new Band { Key = "Band B", Name = "Band B" } };
                if (!Finish(same) || All.Count != 2) return "같은 시각 다른 공연장 공연이 기록에서 빠져요";
                // 옛 기록(건물 없음): 공연장 이름이 바뀌어도 같은 프레임 공연은 다시 적지 않음
                Mark(new Record { Frame = 500, Venue = "옛 이름" });
                var renamed = new Show { Id = 5, Building = 12, Venue = "새 이름", StartFrame = 500, Done = true, Band = new Band { Key = "Band A", Name = "Band A" } };
                if (Finish(renamed)) return "옛 기록 공연을 이름이 바뀐 뒤 다시 적어요";
                return null;
            }
            finally
            {
                Clear();
                lock (All) All.AddRange(keep);
                seen.UnionWith(keepSeen);
                seenOld.UnionWith(keepSeenOld);
                foreach (var kv in keepPeak) peak[kv.Key] = kv.Value;
                primed = keepPrimed;
                lock (released) released.AddRange(keepReleased);
            }
        }

        /// <summary>자체 검사용 옛 형식 세이브(기록 하나: Band A · 관객 8412): security = 보안 예산 1200 을 씀 · tail = 끝에 1 바이트 더.</summary>
        static byte[] OldHistory(int format, bool security, bool tail)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(format); w.Write(1);
            w.Write(7u); w.Write("Band A"); w.Write("공연장"); w.Write(1L); w.Write(2L); w.Write(3L); w.Write(8412); w.Write(-3); w.Write(61); w.Write(true);
            if (security) w.Write(1200);
            w.Write(1); w.Write("Band A"); w.Write(61);
            if (tail) w.Write((byte)0);
            w.Flush();
            return ms.ToArray();
        }
    }

    /// <summary>세이브에 공연 기록을 넣고 꺼냄(게임 SerializableDataExtension).</summary>
    public sealed class HistorySave : SerializableDataExtensionBase
    {
        public override void OnLoadData()
        {
            History.Clear();
            try
            {
                var data = serializableDataManager.LoadData(History.SaveKey);
                if (data != null && data.Length > 0) History.Read(data);
            }
            catch (Exception e)
            {
                Core.Log.Warn("공연 기록을 읽지 못해 빈 기록으로 시작: " + e.Message);
                History.Clear();
            }
        }

        public override void OnSaveData()
        {
            try
            {
                serializableDataManager.SaveData(History.SaveKey, History.Write());
            }
            catch (Exception e)
            {
                Core.Log.Error("공연 기록 저장 실패: " + e);
            }
        }
    }
}
