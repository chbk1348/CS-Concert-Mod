using System.Collections.Generic;

namespace ConcertMod.Data
{
    /// <summary>
    /// 게임 밴드(콘서트 DLC EventInfo)에 붙이는 아티스트 이름 40팀(캔버스 아티스트 명단). 밴드 프리팹 이름 순서대로 차례로 붙여 언제나 같은 이름.
    /// 밴드 색 · 인기 · 공연은 게임 값 그대로, 이름만 바꿔 보여 줌.
    /// </summary>
    internal static class Artists
    {
        public static readonly string[] Names =
        {
            "BTS", "BLACKPINK", "NewJeans", "TWICE", "SEVENTEEN", "Stray Kids", "aespa", "IVE", "LE SSERAFIM", "(G)I-DLE", "EXO", "BIGBANG", "IU", "PSY", "Jung Kook", "ROSÉ",
            "YOASOBI", "Ado", "Kenshi Yonezu", "Official HIGE DANdism", "King Gnu", "LiSA", "Mrs. GREEN APPLE", "Vaundy", "Fujii Kaze", "Hikaru Utada", "BABYMETAL", "ONE OK ROCK",
            "Taylor Swift", "Ed Sheeran", "Billie Eilish", "Ariana Grande", "Beyoncé", "Bruno Mars", "The Weeknd", "Coldplay", "Dua Lipa", "Harry Styles", "Olivia Rodrigo", "Lady Gaga",
        };

        static readonly Dictionary<string, string> byKey = new();

        /// <summary>밴드 프리팹 이름들(정렬)로 이름표를 다시 만듦. 도시를 불러올 때 한 번.</summary>
        public static void Assign(List<string> keys)
        {
            byKey.Clear();
            keys.Sort(System.StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++) byKey[keys[i]] = Names[i % Names.Length];
        }

        /// <summary>밴드 프리팹 이름 → 아티스트 이름(모르면 프리팹 이름).</summary>
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
