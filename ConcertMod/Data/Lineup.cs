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
    /// 아티스트 출연표: 게임 밴드 프리팹(콘서트 DLC 3팀)은 공연을 여는 자리로만 쓰고, 공연(이벤트)마다 모드 아티스트 한 팀을 붙임.
    /// 아티스트는 자리 하나에 묶이고(명단 순서대로 돌아가며), 게임이 그 자리로 공연을 만들면 그 자리 아티스트 중 가장 오래 쉰 팀이 나감.
    /// 인기 · 입장권은 아티스트마다 따로 세고, 공연 전 · 중에는 그 자리 프리팹 값에 아티스트 값을 넣어 게임 계산(관객 · 성공 · 인기 변화)이 아티스트 기준이 되게 함.
    /// 새 프리팹을 만들지 않아 모드를 빼도 세이브는 그대로. 세이브 키 ConcertMod.Lineup.
    /// </summary>
    internal static class Lineup
    {
        public const string SaveKey = "ConcertMod.Lineup";
        const int Format = 1;

        /// <summary>무대에 올리는 아티스트 수(Artists.Names 앞에서부터 — 지금은 한국 대표 5팀). 늘리면 명단 다음 팀이 차례로 들어옴.</summary>
        public const int Count = 5;

        /// <summary>아티스트 값: 인기 오프셋(50 기준, 게임 m_popularityOffset 과 같은 뜻) · 입장권 오프셋 · 마지막으로 공연을 잡은 프레임.</summary>
        sealed class Stat
        {
            public int Pop, Price;
            public uint Last;
        }

        struct Bound
        {
            public uint Frame;
            public string Artist;
        }

        static readonly Dictionary<string, Stat> stats = new();
        static readonly Dictionary<ushort, Bound> events = new();
        static readonly object gate = new();

        /// <summary>세이브에서 출연표를 읽었나(아니면 LoadBands 가 옛 밴드 인기 · 입장권을 넘겨받음).</summary>
        public static bool Loaded { get; private set; }

        static Stat StatOf(string a)
        {
            if (!stats.TryGetValue(a, out var s)) stats[a] = s = new Stat();
            return s;
        }

        /// <summary>아티스트 인기 0 ~ 100(ConcertAI.GetPopularity 와 같은 식, 프리팹 대신 아티스트 오프셋).</summary>
        public static int Pop(string artist)
        {
            int factor = Singleton<BuildingManager>.instance.m_finalMonumentEffect[1].m_factor;
            int bonus = Singleton<EventManager>.instance.m_finalBandPopularityBonus * (100 + factor) / 102400;
            int off;
            lock (gate) off = StatOf(artist).Pop;
            return Mathf.Clamp(50 + off + bonus, 0, 100);
        }

        public static int PriceOffset(string artist)
        {
            lock (gate) return StatOf(artist).Price;
        }

        public static void SetPrice(string artist, int offset)
        {
            lock (gate) StatOf(artist).Price = offset;
        }

        /// <summary>옛 세이브(출연표 없음): 그 아티스트가 쓰던 밴드 프리팹 인기 · 입장권을 넘겨받음.</summary>
        public static void Seed(string artist, int pop, int price)
        {
            lock (gate)
            {
                var s = StatOf(artist);
                s.Pop = pop;
                s.Price = price;
            }
        }

        public static void MarkLoaded() => Loaded = true;

        /// <summary>공연이 끝나 게임이 매긴 인기 변화(m_popularityDelta)를 그 아티스트에 더함 — History 가 공연마다 한 번.</summary>
        public static void Finish(string artist, int delta)
        {
            lock (gate)
            {
                var s = StatOf(artist);
                s.Pop = Mathf.Clamp(50 + s.Pop + delta, 0, 100) - 50;
            }
        }

        /// <summary>이 공연에 붙인 아티스트(이벤트 번호를 다시 쓴 다른 공연이면 null).</summary>
        public static string ArtistOf(ushort id, uint startFrame)
        {
            lock (gate) return events.TryGetValue(id, out var b) && b.Frame == startFrame ? b.Artist : null;
        }

        public static void Bind(ushort id, uint startFrame, string artist)
        {
            if (artist == null) return;
            lock (gate)
            {
                events[id] = new Bound { Frame = startFrame, Artist = artist };
                var s = StatOf(artist);
                s.Last = Math.Max(s.Last, startFrame);
            }
        }

        /// <summary>이 자리(밴드 프리팹)에 다음으로 올릴 아티스트: 그 자리 아티스트 중 가장 오래 쉰 팀(미리 보기와 공연 만들기가 같은 답).</summary>
        public static Band Pick(EventInfo slot)
        {
            Band best = null;
            uint bestLast = 0;
            lock (gate)
                foreach (var b in Shows.Bands)
                {
                    if (b.Info != slot) continue;
                    uint last = StatOf(b.Key).Last;
                    if (best == null || last < bestLast)
                    {
                        best = b;
                        bestLast = last;
                    }
                }
            return best;
        }

        /// <summary>ConcertAI.CreateEvent 뒤(예약 · 대관이 붙인 뒤): 아직 아티스트가 없으면 그 자리 차례 아티스트.</summary>
        public static void OnCreate(ushort id, ref EventData data)
        {
            if (ArtistOf(id, data.m_startFrame) != null) return;
            Bind(id, data.m_startFrame, Pick(data.Info)?.Key);
        }

        /// <summary>
        /// 본 스레드 1초마다(Shows.Refresh 뒤): 잡힌 · 준비 중 · 열리는 공연의 자리 프리팹에 그 아티스트 인기 · 입장권을 넣음(열리는 공연이 마지막에 이김).
        /// shortcut: 같은 자리 공연이 페스티벌 · 경기장에서 동시에 열리면 한쪽 값으로 계산됨, 겹침이 잦아지면 ConcertAI 메서드마다 앞뒤로 바꿔 넣기.
        /// </summary>
        public static void Sync()
        {
            for (int pass = 0; pass < 3; pass++)
                foreach (var s in Shows.All)
                {
                    if (s.Done || s.Band?.Info == null || (s.Active ? 2 : s.Preparing ? 1 : 0) != pass) continue;
                    lock (gate)
                    {
                        var st = StatOf(s.Band.Key);
                        s.Band.Info.m_popularityOffset = st.Pop;
                        s.Band.Info.m_ticketPriceOffset = st.Price;
                    }
                }
        }

        public static void Clear()
        {
            lock (gate)
            {
                stats.Clear();
                events.Clear();
            }
            Loaded = false;
        }

        public static byte[] Write() => Write(true);

        /// <summary>prune: 없어진 공연(지워짐 · 번호를 다른 공연이 씀)은 빼고 씀(자체 검사는 끔).</summary>
        static byte[] Write(bool prune)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            var em = prune && Singleton<EventManager>.exists ? Singleton<EventManager>.instance : null;
            lock (gate)
            {
                w.Write(Format);
                w.Write(stats.Count);
                foreach (var kv in stats)
                {
                    w.Write(kv.Key);
                    w.Write(kv.Value.Pop);
                    w.Write(kv.Value.Price);
                    w.Write(kv.Value.Last);
                }
                var keep = new List<KeyValuePair<ushort, Bound>>();
                foreach (var kv in events)
                {
                    if (em != null && kv.Key < em.m_events.m_size)
                    {
                        ref var d = ref em.m_events.m_buffer[kv.Key];
                        if ((d.m_flags & (EventData.Flags.Created | EventData.Flags.Deleted)) != EventData.Flags.Created || d.m_startFrame != kv.Value.Frame) continue;
                    }
                    keep.Add(kv);
                }
                w.Write(keep.Count);
                foreach (var kv in keep)
                {
                    w.Write(kv.Key);
                    w.Write(kv.Value.Frame);
                    w.Write(kv.Value.Artist ?? "");
                }
            }
            w.Flush();
            return ms.ToArray();
        }

        public static void Read(byte[] data)
        {
            Clear();
            using var r = new BinaryReader(new MemoryStream(data));
            int format = r.ReadInt32();
            if (format > Format) throw new InvalidDataException($"출연표 형식 {format} 은 이 판보다 새것");
            lock (gate)
            {
                int n = r.ReadInt32();
                for (int i = 0; i < n; i++) stats[r.ReadString()] = new Stat { Pop = r.ReadInt32(), Price = r.ReadInt32(), Last = r.ReadUInt32() };
                n = r.ReadInt32();
                for (int i = 0; i < n; i++) events[r.ReadUInt16()] = new Bound { Frame = r.ReadUInt32(), Artist = r.ReadString() };
            }
            Loaded = true;
        }

        /// <summary>자체 검사: 쓰고 다시 읽어 같은지 · 인기 변화가 0 ~ 100 안에서 더해지는지. 틀리면 설명, 맞으면 null.</summary>
        public static string SelfCheck()
        {
            byte[] keep = Write(false);
            bool loaded = Loaded;
            try
            {
                Clear();
                lock (gate)
                {
                    stats["BTS"] = new Stat { Pop = 45, Price = -300, Last = 900 };
                    events[12] = new Bound { Frame = 777, Artist = "BTS" };
                }
                Read(Write(false));
                Finish("BTS", 10);
                lock (gate)
                {
                    var s = StatOf("BTS");
                    if (s.Pop != 50 || s.Price != -300 || s.Last != 900) return "출연표 아티스트 값을 쓰고 읽은 값이 달라요";
                }
                return ArtistOf(12, 777) != "BTS" || ArtistOf(12, 778) != null ? "출연표 공연 아티스트를 쓰고 읽은 값이 달라요" : null;
            }
            finally
            {
                Read(keep);
                Loaded = loaded;
            }
        }
    }

    /// <summary>게임 화면(정보 창 · 이름표)의 밴드 이름도 그 공연 아티스트로.</summary>
    [HarmonyPatch(typeof(ConcertAI), nameof(ConcertAI.GetBandName))]
    internal static class BandNamePatch
    {
        static void Postfix(ushort eventID, ref EventData data, ref string __result)
        {
            try
            {
                var b = Shows.BandOf(eventID, ref data);
                if (b != null) __result = b.Name;
            }
            catch (Exception e)
            {
                Core.Safe.Report("출연표: 밴드 이름", e);
            }
        }
    }

    /// <summary>세이브에 출연표(아티스트 인기 · 입장권 · 공연마다 아티스트)를 넣고 꺼냄.</summary>
    public sealed class LineupSave : SerializableDataExtensionBase
    {
        public override void OnLoadData()
        {
            Lineup.Clear();
            try
            {
                var data = serializableDataManager.LoadData(Lineup.SaveKey);
                if (data != null && data.Length > 0) Lineup.Read(data);
            }
            catch (Exception e)
            {
                Core.Log.Warn("출연표를 읽지 못해 옛 밴드 인기로 시작: " + e.Message);
                Lineup.Clear();
            }
        }

        public override void OnSaveData()
        {
            try
            {
                serializableDataManager.SaveData(Lineup.SaveKey, Lineup.Write());
            }
            catch (Exception e)
            {
                Core.Log.Error("출연표 저장 실패: " + e);
            }
        }
    }
}
