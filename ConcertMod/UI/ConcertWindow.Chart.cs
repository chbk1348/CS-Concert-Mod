using System;
using System.Collections.Generic;
using ConcertMod.Data;
using UnityEngine;
using C = CSCommonUX.Concert;
using D = CSCommonUX.CSCommonDesign;
using S = CSCommonUX.Section;

namespace ConcertMod.UI
{
    /// <summary>
    /// 차트 탭(캔버스 Main · chart): 1위 레코드 카드(인기 · 연속 1위 · 공연 기록 6칸 · 다음 공연) · 인기 추이(최근 10공연, 위 3팀 · 1위만 밴드 색) ·
    /// 인기 차트(순위 · 변동 · 앨범 칸 · 최근 공연 · 인기 막대 · 최고 순위 · 차트 기간) · 명예의 전당(출입증 4장). 인기는 게임 값, 변동 · 기록은 모드 기록.
    /// </summary>
    internal sealed partial class ConcertWindow
    {
        const string RankIcon = "M5 20V10M12 20V4M19 20v-7", TrendIcon = "M3 17l6-6 4 4 8-8", TrophyIcon = "M8 21h8M12 17v4M7 4h10v5a5 5 0 0 1-10 0zM7 6H4a3 3 0 0 0 3 4M17 6h3a3 3 0 0 1-3 4";

        /// <summary>차트에 드는 순위 · 추이에 그리는 공연 수 · 추이 선 수.</summary>
        const int ChartTop = 10, TrendN = 10, TrendBands = 3;

        sealed class Rank
        {
            public Band Band;
            public int Pos, Move, Peak, Weeks;
            public bool New;
        }

        float ChartTab(float w)
        {
            var ranks = Ranks();
            if (ranks.Count == 0) return Note(0f, w, "차트", "밴드가 없어요");
            float gap = S.Gap, left = Mathf.Round((w - gap) * 5f / 12f), right = w - gap - left;
            float row1 = C.HeroArtH + S.Pad + 2 * (CellH + D.Gap * 2f) + S.RowH + S.Pad;
            HeroPlate(new Rect(0f, 0f, left, row1), ranks[0]);
            TrendPlate(new Rect(left + gap, 0f, right, row1), ranks);
            int shown = Math.Min(ChartTop, ranks.Count);
            float listH = PlateTop + D.LineSub + shown * C.RankRowH + S.PadBottom;
            float fameH = PlateTop + 2 * C.PassH + gap + S.PadBottom;
            float row2 = Mathf.Max(listH, fameH), y2 = row1 + gap;
            float wide = Mathf.Round((w - gap) * 7f / 12f);
            ChartPlate(new Rect(0f, y2, wide, row2), ranks, shown);
            FamePlate(new Rect(wide + gap, y2, w - gap - wide, row2));
            return y2 + row2;
        }

        /// <summary>지금 인기 순위 + 지난 기록 순위와 비교한 변동 · 기록 속 최고 순위 · 위 10 안에 든 공연 수.</summary>
        static List<Rank> Ranks()
        {
            var list = new List<Rank>();
            foreach (var b in Shows.Bands) list.Add(new Rank { Band = b });
            list.Sort((a, b) => b.Band.Pop != a.Band.Pop ? b.Band.Pop.CompareTo(a.Band.Pop) : string.CompareOrdinal(a.Band.Name, b.Band.Name));
            for (int i = 0; i < list.Count; i++) list[i].Pos = i + 1;
            var hist = History.All;
            Dictionary<string, int> prev = hist.Count > 0 ? RankOf(hist[hist.Count - 1].Pops) : null;
            var seenPeak = new Dictionary<string, int>();
            var weeks = new Dictionary<string, int>();
            foreach (var rec in hist)
                foreach (var kv in RankOf(rec.Pops))
                {
                    seenPeak[kv.Key] = Math.Min(seenPeak.TryGetValue(kv.Key, out int p) ? p : int.MaxValue, kv.Value);
                    if (kv.Value <= ChartTop) weeks[kv.Key] = (weeks.TryGetValue(kv.Key, out int n) ? n : 0) + 1;
                }
            foreach (var r in list)
            {
                if (prev != null && prev.TryGetValue(r.Band.Key, out int before)) r.Move = before - r.Pos;
                else r.New = hist.Count > 0;
                r.Peak = Math.Min(r.Pos, seenPeak.TryGetValue(r.Band.Key, out int pk) ? pk : int.MaxValue);
                r.Weeks = weeks.TryGetValue(r.Band.Key, out int wk) ? wk : 0;
            }
            return list;
        }

        static Dictionary<string, int> RankOf(Dictionary<string, int> pops)
        {
            var keys = new List<string>(pops.Keys);
            keys.Sort((a, b) => pops[b] != pops[a] ? pops[b].CompareTo(pops[a]) : string.CompareOrdinal(a, b));
            var d = new Dictionary<string, int>();
            for (int i = 0; i < keys.Count; i++) d[keys[i]] = i + 1;
            return d;
        }

        /// <summary>그 밴드가 1위를 이어 온 기록 수(끝에서부터).</summary>
        static int Streak(string key)
        {
            int n = 0;
            for (int i = History.All.Count - 1; i >= 0; i--)
            {
                if (!RankOf(History.All[i].Pops).TryGetValue(key, out int p) || p != 1) break;
                n++;
            }
            return n;
        }

        void HeroPlate(Rect r, Rank top)
        {
            var b = top.Band;
            S.Frame(r);
            var art = new Rect(r.x, r.y, r.width, C.HeroArtH);
            if (Draw.Repaint)
            {
                Draw.Round(art, Pal.ConcertScene, S.Radius);
                Draw.Fill(new Rect(art.x, art.yMax - S.Radius, art.width, S.Radius), Pal.ConcertScene);
            }
            float vr = Mathf.Min(C.VinylR, art.height * 0.44f);
            var vc = new Vector2(art.x + S.Pad + vr + D.Gap * 2f, art.center.y);
            C.Vinyl(vc, vr, b.Color, Artists.Initials(b.Name));
            float tx = vc.x + vr + S.Pad * 1.5f, tw = art.xMax - S.Pad - tx, ty = art.y + S.Pad * 1.5f;
            int streak = Streak(b.Key);
            Badge.Pair(tx, ty + Badge.H * 0.5f, streak > 1 ? $"No.1 · {streak}회 연속" : "No.1", b.Color, Draw.On(b.Color));
            ty += Badge.H + D.Gap * 2f;
            Draw.TextFit(new Rect(tx, ty, tw, C.NameFont + D.Gap * 2f), b.Name, C.NameFont, Pal.Txt, true);
            ty += C.NameFont + D.Gap * 3f;
            string pop = b.Pop.ToString();
            float pw = Mathf.Ceil(Draw.TextWidth(pop, C.NameFont + D.Gap * 2f, true));
            Draw.TextFit(new Rect(tx, ty, pw, C.NameFont + D.Gap * 2f), pop, C.NameFont + D.Gap * 2f, Pal.Txt, true);
            Draw.TextFit(new Rect(tx + pw + D.Gap * 2f, ty, tw - pw, C.NameFont + D.Gap * 2f), "인기  " + Delta(top.Move), D.FontBody, top.Move != 0 ? DeltaInk(top.Move) : S.SubInk, true);
            var recs = History.All.FindAll(x => x.Key == b.Key);
            int ok = recs.FindAll(x => x.Success).Count, maxV = 0, ones = 0;
            long sumV = 0, sumT = 0;
            foreach (var x in recs)
            {
                sumV += x.Visitors;
                sumT += x.Ticket;
                maxV = Math.Max(maxV, x.Visitors);
            }
            foreach (var x in History.All) if (RankOf(x.Pops).TryGetValue(b.Key, out int p) && p == 1) ones++;
            float y = art.yMax + S.Pad, cw = (r.width - S.Pad * 2f - D.Gap * 4f) / 3f, x0 = r.x + S.Pad;
            string[] names = { "공연", "성공률", "평균 관객", "최다 관객", "매표 수입", "최고 순위" };
            string[] vals =
            {
                recs.Count + "회",
                recs.Count > 0 ? Mathf.RoundToInt(100f * ok / recs.Count) + "%" : "–",
                recs.Count > 0 ? (sumV / recs.Count).ToString("#,0") + "명" : "–",
                recs.Count > 0 ? maxV.ToString("#,0") + "명" : "–",
                Money(sumT),
                top.Peak + "위" + (ones > 0 ? $" · {ones}회" : ""),
            };
            for (int i = 0; i < 6; i++) Cell(new Rect(x0 + (cw + D.Gap * 2f) * (i % 3), y + (CellH + D.Gap * 2f) * (i / 3), cw, CellH), names[i], vals[i]);
            y += 2 * (CellH + D.Gap * 2f);
            var strip = new Rect(x0, y, r.width - S.Pad * 2f, S.RowH);
            if (Draw.Repaint) Draw.Round(strip, Pal.Inset, Board.BoxRadius * 1.5f);
            NextShow? next = null;
            foreach (var n in Shows.Next) if (n.Band == b) { next = n; break; }
            Draw.TextFit(new Rect(strip.x + S.Pad, strip.y, strip.width - S.Pad * 2f, strip.height), next.HasValue ? $"다음 공연 {When(next.Value.Start)} · {Shows.VenueName}" : "잡힌 다음 공연이 없어요", D.FontMin, Pal.Light);
            Draw.TextFit(new Rect(strip.x + S.Pad, strip.y, strip.width - S.Pad * 2f, strip.height), Price(b.Price), D.FontDesc, Pal.Txt, true, TextAnchor.MiddleRight);
        }

        void TrendPlate(Rect r, List<Rank> ranks)
        {
            var body = Plate(r, "인기 추이", TrendIcon, $"최근 {TrendN}공연 · 위 {TrendBands}팀");
            var hist = Last(History.All, TrendN);
            if (hist.Count < 2)
            {
                Draw.TextFit(new Rect(body.x, body.y, body.width, S.RowH), "공연이 두 번 넘게 끝나면 인기 흐름을 그려요", D.FontBody, S.SubInk);
                return;
            }
            float axisW = S.CodeW + D.Gap * 2f, labelH = D.LineSub + D.Gap * 2f;
            var plot = new Rect(body.x + axisW, body.y + D.Gap * 2f, body.width - axisW - S.CodeW * 3f, body.height - labelH - D.Gap * 2f);
            for (int g = 0; g <= 4; g++)
            {
                float gy = plot.yMax - plot.height * g / 4f;
                if (Draw.Repaint) Draw.Fill(new Rect(plot.x, gy, plot.width, 1f), Pal.Line);
                Draw.TextFit(new Rect(body.x, gy - D.LineSub * 0.5f, axisW - D.Gap * 2f, D.LineSub), (g * 25).ToString(), D.FontMin, S.SubInk, false, TextAnchor.MiddleRight);
            }
            for (int k = Math.Min(TrendBands, ranks.Count) - 1; k >= 0; k--)
            {
                var b = ranks[k].Band;
                var pts = new List<Vector2>();
                for (int i = 0; i < hist.Count; i++)
                    if (hist[i].Pops.TryGetValue(b.Key, out int p)) pts.Add(new Vector2(plot.x + plot.width * i / (hist.Count - 1), plot.yMax - plot.height * p / 100f));
                if (pts.Count < 2) continue;
                bool lead = k == 0;
                if (Draw.Repaint)
                {
                    Draw.Curve(pts, lead ? 3f : 2f, lead ? b.Color : Pal.Line2);
                    if (lead) Draw.Dot(pts[pts.Count - 1], D.Gap * 1.25f, b.Color);
                }
                var end = pts[pts.Count - 1];
                Draw.TextFit(new Rect(end.x + D.Gap * 2f, end.y - D.LineSub * 0.5f, S.CodeW * 3f, D.LineSub), $"{b.Name} {b.Pop}", D.FontMin, lead ? b.Color : S.SubInk, true);
            }
            Draw.TextFit(new Rect(plot.x, plot.yMax + D.Gap * 2f, plot.width * 0.5f, D.LineSub), Day(hist[0].Start), D.FontMin, S.SubInk);
            Draw.TextFit(new Rect(plot.x + plot.width * 0.5f, plot.yMax + D.Gap * 2f, plot.width * 0.5f, D.LineSub), Day(hist[hist.Count - 1].Start), D.FontMin, S.SubInk, false, TextAnchor.MiddleRight);
        }

        void ChartPlate(Rect r, List<Rank> ranks, int shown)
        {
            var now = SimulationManager.instance.m_currentGameTime;
            var body = Plate(r, "인기 차트", RankIcon, $"{Day(now)} 기준 · 공연이 끝날 때마다 바뀌어요");
            float numW = S.CodeW * 1.6f, barW = S.CodeW * 4f, colW = S.CodeW * 1.8f;
            float y = body.y;
            Draw.TextFit(new Rect(body.x, y, numW, D.LineSub), "순위", D.FontMin, S.SubInk);
            Draw.TextFit(new Rect(body.xMax - colW * 2f - barW - S.Pad, y, barW, D.LineSub), "인기", D.FontMin, S.SubInk);
            Draw.TextFit(new Rect(body.xMax - colW * 2f, y, colW, D.LineSub), "최고", D.FontMin, S.SubInk, false, TextAnchor.MiddleRight);
            Draw.TextFit(new Rect(body.xMax - colW, y, colW, D.LineSub), "차트 기간", D.FontMin, S.SubInk, false, TextAnchor.MiddleRight);
            y += D.LineSub;
            for (int i = 0; i < shown; i++)
            {
                var k = ranks[i];
                var row = new Rect(body.x, y, body.width, C.RankRowH);
                if (i == 0 && Draw.Repaint)
                {
                    Draw.Round(row, Pal.Inset, Board.BoxRadius * 1.5f);
                    Draw.RoundOutline(row, Pal.Line, Board.BoxRadius * 1.5f);
                }
                float x = row.x + D.Gap * 2f;
                Draw.TextFit(new Rect(x, row.y, numW * 0.6f, row.height), k.Pos.ToString(), C.RankFont, i == 0 ? k.Band.Color : Pal.Txt, true, TextAnchor.MiddleCenter);
                string mv = k.New ? "NEW" : k.Move > 0 ? "▲" + k.Move : k.Move < 0 ? "▼" + (-k.Move) : "–";
                Draw.TextFit(new Rect(x + numW * 0.6f, row.y, numW * 0.5f, row.height), mv, D.FontMin, k.New ? Look.PastelSubInk : DeltaInk(k.Move), true, TextAnchor.MiddleLeft);
                var tile = new Rect(x + numW + D.Gap * 2f, row.center.y - C.Tile * 0.5f, C.Tile, C.Tile);
                Tile(tile, k.Band.Color, k.Band.Name);
                float nameX = tile.xMax + S.Pad, nameW = body.xMax - colW * 2f - barW - S.Pad * 2f - nameX;
                Record last = History.All.FindLast(rc => rc.Key == k.Band.Key);
                Draw.TextFit(new Rect(nameX, row.center.y - D.LineBody, nameW, D.LineBody), k.Band.Name, D.FontBody, Pal.Txt, true);
                Draw.TextFit(new Rect(nameX, row.center.y + D.Gap * 0.5f, nameW, D.LineSub), last != null ? $"최근 공연 {Day(last.Start)} · {last.Visitors:#,0}명" : "아직 공연 기록 없음", D.FontMin, S.SubInk);
                var bar = new Rect(body.xMax - colW * 2f - barW - S.Pad, row.center.y - Board.BoxLine * 2f, barW - S.CodeW, Board.BoxLine * 4f);
                if (Draw.Repaint)
                {
                    Draw.Round(bar, Pal.ConcertSold, Board.BoxLine * 2f);
                    Draw.Round(new Rect(bar.x, bar.y, bar.width * k.Band.Pop / 100f, bar.height), i == 0 ? k.Band.Color : Pal.Line2, Board.BoxLine * 2f);
                }
                Draw.TextFit(new Rect(bar.xMax, row.y, S.CodeW, row.height), k.Band.Pop.ToString(), D.FontValue, Pal.Txt, true, TextAnchor.MiddleRight);
                Draw.TextFit(new Rect(body.xMax - colW * 2f, row.y, colW, row.height), k.Peak + "위", D.FontDesc, Pal.Light, false, TextAnchor.MiddleRight);
                Draw.TextFit(new Rect(body.xMax - colW, row.y, colW, row.height), k.Weeks + "회", D.FontDesc, Pal.Light, false, TextAnchor.MiddleRight);
                y += C.RankRowH;
            }
        }

        void FamePlate(Rect r)
        {
            var body = Plate(r, "명예의 전당", TrophyIcon, "이 도시 공연 기록");
            var all = History.All;
            Record most = null, rich = null, jump = null;
            foreach (var x in all)
            {
                if (most == null || x.Visitors > most.Visitors) most = x;
                if (rich == null || x.Ticket > rich.Ticket) rich = x;
                if (jump == null || x.PopDelta > jump.PopDelta) jump = x;
            }
            string longKey = null;
            int longRun = 0, run = 0;
            string runKey = null;
            foreach (var x in all)
            {
                string first = null;
                foreach (var kv in RankOf(x.Pops)) if (kv.Value == 1) first = kv.Key;
                run = first == runKey ? run + 1 : 1;
                runKey = first;
                if (run > longRun) { longRun = run; longKey = first; }
            }
            float gap = S.Gap, pw = (body.width - gap) * 0.5f;
            Pass(new Rect(body.x, body.y, pw, C.PassH), "최다 관객", most != null ? most.Visitors.ToString("#,0") + "명" : "–", most);
            Pass(new Rect(body.x + pw + gap, body.y, pw, C.PassH), "최고 매표 수입", rich != null ? Money(rich.Ticket) : "–", rich);
            Pass(new Rect(body.x, body.y + C.PassH + gap, pw, C.PassH), "가장 크게 오름", jump != null ? Delta(jump.PopDelta) : "–", jump);
            var lb = Shows.BandOf(longKey);
            Pass(new Rect(body.x + pw + gap, body.y + C.PassH + gap, pw, C.PassH), "최장 1위", longRun > 0 ? longRun + "회 연속" : "–", null, lb);
        }

        /// <summary>백스테이지 출입증: 위 끈 구멍 · 아래 밴드 색 띠 · 제목 · 값 · 누가 언제.</summary>
        static void Pass(Rect r, string title, string value, Record rec, Band bandOverride = null)
        {
            var band = bandOverride ?? (rec != null ? Shows.BandOf(rec.Key) : null);
            Color c = band?.Color ?? Pal.Dim;
            if (Draw.Repaint)
            {
                Draw.Round(r, Pal.Inset, Board.BoxRadius * 1.75f);
                Draw.Round(new Rect(r.center.x - S.Pad * 0.9f, r.y + D.Gap * 2f, S.Pad * 1.8f, D.Gap * 2f), S.Plate, D.Gap);
                Draw.Fill(new Rect(r.x + Board.BoxRadius, r.yMax - D.Gap * 1.5f, r.width - Board.BoxRadius * 2f, D.Gap * 1.5f), c);
            }
            float x = r.x + S.Pad, w = r.width - S.Pad * 2f, y = r.y + S.Pad * 1.75f;
            Draw.TextFit(new Rect(x, y, w, D.LineSub), title, D.FontMin, S.SubInk);
            y += D.LineSub + D.Gap;
            Draw.TextFit(new Rect(x, y, w, C.BigFont + D.Gap * 2f), value, C.BigFont, Pal.Txt, true);
            y += C.BigFont + D.Gap * 3f;
            string who = band == null ? "기록이 쌓이면 채워져요" : rec != null ? $"{band.Name} · {Day(rec.Start)}" : band.Name;
            if (Draw.Repaint && band != null) Draw.Dot(new Vector2(x + D.Gap, y + D.LineSub * 0.5f), D.Gap, c);
            Draw.TextFit(new Rect(x + (band != null ? D.Gap * 3f : 0f), y, w, D.LineSub), who, D.FontMin, Pal.Light);
        }
    }
}
