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
        public Dictionary<string, int> Pops = new();

        public DateTime Start => new(StartTicks);
    }

    /// <summary>
    /// 공연 기록: 공연이 열리는 동안 최다 입장을 세고, 끝나면 한 줄 남김(같은 공연은 한 번).
    /// 모드를 넣기 전에 끝난 공연은 없음. 세이브 키 ConcertMod.History(형식 판 2: 보안 예산 더함 · 판 1도 읽음).
    /// </summary>
    internal static class History
    {
        public static readonly List<Record> All = new();

        const int Format = 2;
        const int Keep = 500;
        public const string SaveKey = "ConcertMod.History";

        static readonly Dictionary<ushort, int> peak = new();
        static readonly HashSet<uint> seen = new();

        /// <summary>Shows.Refresh 뒤 부름: 열리는 공연 최다 입장 갱신 · 끝난 공연 기록.</summary>
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
                if (!s.Done || s.Cancelled || seen.Contains(s.StartFrame)) continue;
                if (primed && set.NotifyResult)
                    CSCommonUX.Toast.Show($"{s.Band.Name} 공연이 끝났어요 · 관객 {(peak.TryGetValue(s.Id, out int pv) ? pv : s.Arrived):#,0}명 · 입장권 ₡{s.Ticket / 100.0:#,0} · {(s.Success ? "성공" : "아쉬움")}",
                        s.Success ? CSCommonUX.ToastKind.Success : CSCommonUX.ToastKind.Info);
                seen.Add(s.StartFrame);
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
                    Visitors = peak.TryGetValue(s.Id, out int v) ? v : s.Arrived,
                    PopDelta = s.PopDelta,
                    PopAfter = s.Band.Pop,
                    Success = s.Success,
                    Security = s.Security,
                };
                foreach (var b in Shows.Bands) r.Pops[b.Key] = b.Pop;
                peak.Remove(s.Id);
                All.Add(r);
                if (All.Count > Keep) All.RemoveAt(0);
            }
            primed = true;
        }

        /// <summary>도시를 불러온 뒤 첫 Track 은 알림 없이(이미 열리고 있거나 끝난 공연을 다시 알리지 않게).</summary>
        static bool primed;

        /// <summary>설정 '공연 기록 지우기': 세이브에 남긴 지난 공연을 모두(이미 끝난 공연을 다시 적지 않게 본 표시는 둠).</summary>
        public static void Forget() => All.Clear();

        /// <summary>옛 세이브: 밴드 프리팹 이름으로 남은 기록을 그때 붙던 아티스트 이름으로(LoadBands 에서 Artists.Assign 뒤).</summary>
        public static void Rekey()
        {
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
            All.Clear();
            peak.Clear();
            seen.Clear();
        }

        public static byte[] Write()
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(Format);
            w.Write(All.Count);
            foreach (var r in All)
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
            try
            {
                Read(data, true);
            }
            catch (Exception)
            {
                Read(data, false);
            }
        }

        static void Read(byte[] data, bool security)
        {
            Clear();
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
                int m = r.ReadInt32();
                for (int k = 0; k < m; k++) rec.Pops[r.ReadString()] = r.ReadInt32();
                All.Add(rec);
                seen.Add(rec.Frame);
            }
            if (r.BaseStream.Position != r.BaseStream.Length) throw new InvalidDataException("공연 기록 뒤에 남은 자료가 있어요");
        }

        /// <summary>자체 검사: 쓰고 다시 읽어 같은지. 틀리면 설명, 맞으면 null.</summary>
        public static string SelfCheck()
        {
            var keep = new List<Record>(All);
            try
            {
                All.Clear();
                var a = new Record { Frame = 7, Key = "Band A", Venue = "공연장", StartTicks = 638000000000000000, Ticket = 252360, Reward = 50000, Visitors = 8412, PopDelta = -3, PopAfter = 61, Success = true, Security = 1200 };
                a.Pops["Band A"] = 61;
                All.Add(a);
                Read(Write());
                var b = All.Count == 1 ? All[0] : null;
                if (b == null || b.Key != a.Key || b.Visitors != a.Visitors || b.PopDelta != a.PopDelta || !b.Success || b.Security != 1200 || b.Pops["Band A"] != 61) return "공연 기록을 쓰고 읽은 값이 달라요";
                // 0.2.0 판 세이브: 형식 2 인데 보안 예산 없음
                using (var ms = new MemoryStream())
                using (var w = new BinaryWriter(ms))
                {
                    w.Write(2); w.Write(1);
                    w.Write(7u); w.Write("Band A"); w.Write("공연장"); w.Write(1L); w.Write(2L); w.Write(3L); w.Write(8412); w.Write(-3); w.Write(61); w.Write(true);
                    w.Write(1); w.Write("Band A"); w.Write(61);
                    w.Flush();
                    Read(ms.ToArray());
                }
                b = All.Count == 1 ? All[0] : null;
                if (b == null || b.Key != "Band A" || b.Visitors != 8412 || b.Pops["Band A"] != 61) return "0.2.0 판 공연 기록을 읽지 못해요";
                return null;
            }
            finally
            {
                Clear();
                All.AddRange(keep);
                foreach (var r in keep) seen.Add(r.Frame);
            }
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
