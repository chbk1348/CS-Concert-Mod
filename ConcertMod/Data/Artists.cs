using System.Collections.Generic;

namespace ConcertMod.Data
{
    /// <summary>
    /// 아티스트 명단 40팀(캔버스 아티스트 명단). 무대에 오르는 건 앞에서부터 Lineup.Count 팀(Lineup 이 공연마다 붙임).
    /// Assign · Of 의 프리팹 → 이름표는 옛 세이브(0.2.x: 밴드 프리팹마다 이름 하나)를 읽을 때만 씀.
    /// </summary>
    internal static class Artists
    {
        public static readonly string[] Names =
        {
            "BTS", "BLACKPINK", "NewJeans", "TWICE", "SEVENTEEN", "Stray Kids", "aespa", "IVE", "LE SSERAFIM", "(G)I-DLE", "EXO", "BIGBANG", "IU", "PSY", "Jung Kook", "ROSÉ",
            "YOASOBI", "Ado", "Kenshi Yonezu", "Official HIGE DANdism", "King Gnu", "LiSA", "Mrs. GREEN APPLE", "Vaundy", "Fujii Kaze", "Hikaru Utada", "BABYMETAL", "ONE OK ROCK",
            "Taylor Swift", "Ed Sheeran", "Billie Eilish", "Ariana Grande", "Beyoncé", "Bruno Mars", "The Weeknd", "Coldplay", "Dua Lipa", "Harry Styles", "Olivia Rodrigo", "Lady Gaga",
        };

        /// <summary>명단 지역 경계(Names 순서): 한국 0 ~ 15 · 일본 16 ~ 27 · 해외 28 ~.</summary>
        public const int JapanFrom = 16, WorldFrom = 28;

        static readonly Dictionary<string, string> byKey = new();

        /// <summary>밴드 프리팹 이름들(정렬)로 이름표를 다시 만듦. 도시를 불러올 때 한 번.</summary>
        public static void Assign(List<string> keys)
        {
            byKey.Clear();
            keys.Sort(System.StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++) byKey[keys[i]] = Names[i % Names.Length];
        }

        /// <summary>옛 밴드 프리팹 이름 → 그때 붙던 아티스트 이름(아티스트 이름 · 모르는 이름은 그대로).</summary>
        public static string Of(string key) => key != null && byKey.TryGetValue(key, out var n) ? n : key ?? "";

        /// <summary>이름 머리글자(앨범 칸): 공백으로 나뉘면 첫 글자들, 아니면 앞 3자.</summary>
        public static string Initials(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var parts = name.Split(' ');
            if (parts.Length > 1)
            {
                string s = "";
                foreach (var p in parts) if (p.Length > 0 && char.IsLetterOrDigit(p[0])) s += char.ToUpperInvariant(p[0]);
                return s.Length > 3 ? s.Substring(0, 3) : s;
            }
            return name.Length > 3 ? name.Substring(0, 3).ToUpperInvariant() : name.ToUpperInvariant();
        }
    }
}
