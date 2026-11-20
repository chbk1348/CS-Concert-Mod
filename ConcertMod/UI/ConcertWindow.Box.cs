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
    /// 흥행 탭(캔버스 Main · box): 이번 달 정산서(매표 수입 · 성공 보상금 − 보안 예산 = 이익) · 돈의 흐름(쌓이고 빠지는 막대) ·
    /// 월별 매표 수입(최근 12달 곡선 + 이익 막대) · 이달의 흥행왕 · 공연별 성적표. 모두 모드가 남긴 공연 기록(게임 공연 값).
    /// 운영비 · 대관료는 게임이 공연마다 따로 셈하지 않아 넣지 않음(페스티벌 건물 유지비는 게임 예산으로 나감).
    /// </summary>
    internal sealed partial class ConcertWindow
    {
        const string CoinIcon = "M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18zM9 9h6M9 15h6M12 6v12", FlowIcon = "M4 20V10M10 20V4M16 20v-8M22 20H2";

        /// <summary>월별 곡선 달 수 · 정산서 한 줄 높이.</summary>
        const int Months = 12;

        static long Profit(Record r) => r.Ticket + r.Reward - r.Security;

        float BoxTab(float w)
        {
            var now = SimulationManager.instance.m_currentGameTime;
            var month = History.All.FindAll(r => r.Start.Year == now.Year && r.Start.Month == now.Month);
            var prevMonthDate = now.AddMonths(-1);
            var prev = History.All.FindAll(r => r.Start.Year == prevMonthDate.Year && r.Start.Month == prevMonthDate.Month);
            long ticket = 0, reward = 0, sec = 0, prevProfit = 0;
            foreach (var r in month) { ticket += r.Ticket; reward += r.Reward; sec += r.Security; }
            foreach (var r in prev) prevProfit += Profit(r);
            long profit = ticket + reward - sec;
            float gap = S.Gap, left = Mathf.Round((w - gap) * 5f / 12f), right = w - gap - left;
            float rowH = S.RowH * 0.85f;
            float row1 = S.Pad * 2f + D.LineSub * 2f + C.NameFont * 1.5f + S.Pad + 4 * rowH + S.Pad * 2f;
            ReceiptPlate(new Rect(0f, 0f, left, row1), now, month.Count, ticket, reward, sec, profit, prevProfit, rowH);
            FlowPlate(new Rect(left + gap, 0f, right, row1), ticket, reward, sec, profit);
            float row2 = PlateTop + C.TrendH * 0.8f + S.PadBottom, y = row1 + gap;
            MonthlyPlate(new Rect(0f, y, w, row2), now);
            y += row2 + gap;
            float listH = PlateTop + D.LineSub + Mathf.Max(1, month.Count) * C.PastRowH + S.PadBottom;
            float heroH = C.HeroArtH + S.Pad + 2 * (CellH + D.Gap * 2f) + S.Pad;
            float row3 = Mathf.Max(listH, heroH), third = Mathf.Round((w - gap) / 3f);
            KingPlate(new Rect(0f, y, third, row3), month);
            ScorePlate(new Rect(third + gap, y, w - gap - third, row3), month);
            return y + row3;
        }

        void ReceiptPlate(Rect r, DateTime now, int shows, long ticket, long reward, long sec, long profit, long prevProfit, float rowH)
        {
            if (Draw.Repaint) Draw.Round(r, Pal.ConcertScene, S.Radius);
            float x = r.x + S.Pad * 1.5f, iw = r.width - S.Pad * 3f, y = r.y + S.Pad * 1.5f;
            Draw.TextFit(new Rect(x, y, iw, D.LineSub), $"{now.Month}월 정산서", D.FontMin, S.SubInk);
            bool plus = profit >= 0;
            float bw = Badge.Pair(0f, 0f, plus ? "흑자" : "적자", Pal.Inset, Pal.Txt, false);
            Badge.Pair(x + iw - bw, y + D.LineSub * 0.5f, plus ? "흑자" : "적자", Draw.Alpha(plus ? Pal.ConcertUp : Pal.ConcertDown, 0.14f), plus ? Pal.ConcertUp : Pal.ConcertDown);
            y += D.LineSub + D.Gap;
            Draw.TextFit(new Rect(x, y, iw, D.LineSub), "이번 달 이익", D.FontMin, Pal.Light);
            y += D.LineSub;
            Draw.TextFit(new Rect(x, y, iw, C.NameFont * 1.5f), Money(profit), C.NameFont * 1.4f, plus ? Pal.ConcertUp : Pal.ConcertDown, true);
            y += C.NameFont * 1.5f;
            long diff = profit - prevProfit;
            Draw.TextFit(new Rect(x, y, iw, D.LineSub), $"지난달보다 {Money(Math.Abs(diff))} {(diff >= 0 ? "더 남았어요" : "덜 남았어요")} · 공연 {shows}회", D.FontMin, S.SubInk);
            y += D.LineSub + S.Pad;
            if (Draw.Repaint) Draw.Fill(new Rect(r.x, y - D.Gap * 2f, r.width, 1f), Pal.Line);
            ReceiptLine(new Rect(x, y, iw, rowH), "매표 수입", Money(ticket), Pal.Txt);
            ReceiptLine(new Rect(x, y + rowH, iw, rowH), "성공 보상금", Money(reward), Pal.Txt);
            ReceiptLine(new Rect(x, y + rowH * 2f, iw, rowH), "보안 예산", "− " + Money(sec), Pal.ConcertDown);
            if (Draw.Repaint) Draw.Fill(new Rect(x, y + rowH * 3f, iw, 1f), Pal.Line2);
            ReceiptLine(new Rect(x, y + rowH * 3f, iw, rowH), "이익", Money(profit), plus ? Pal.ConcertUp : Pal.ConcertDown, true);
            // 아래 뜯는 자국
            if (Draw.Repaint)
                for (float cx = r.x + S.Pad * 0.5f; cx < r.xMax; cx += S.Pad) Draw.Dot(new Vector2(cx, r.yMax), D.Gap * 1.5f, S.WellFill);
        }

        static void ReceiptLine(Rect r, string name, string value, Color ink, bool bold = false)
        {
            Draw.TextFit(r, name, D.FontBody, bold ? Pal.Txt : Pal.Light, bold);
            Draw.TextFit(r, value, D.FontValue, ink, true, TextAnchor.MiddleRight);
        }

        void FlowPlate(Rect r, long ticket, long reward, long sec, long profit)
        {
            var body = Plate(r, "돈의 흐름", FlowIcon, "수입이 쌓이고 비용이 빠져 이익이 남아요");
            long top = Math.Max(1, ticket + reward);
            // 막대: 이름 · 시작 · 끝 · 종류(0 수입 · 1 비용 · 2 이익)
            string[] names = { "매표 수입", "보상금", "보안 예산", "이익" };
            long[] from = { 0, ticket, ticket + reward, 0 }, to = { ticket, ticket + reward, ticket + reward - sec, profit };
            int[] kinds = { 0, 0, 1, 2 };
            float labelH = D.LineSub + D.Gap * 3f, valueH = D.LineSub + D.Gap * 2f;
            var plot = new Rect(body.x, body.y + valueH, body.width, body.height - labelH - valueH);
            if (Draw.Repaint) Draw.Fill(new Rect(plot.x, plot.yMax, plot.width, 1f), Pal.Line);
            float bw = (plot.width - S.Pad * (names.Length - 1)) / names.Length;
            for (int i = 0; i < names.Length; i++)
            {
                float lo = Mathf.Max(0f, Math.Min(from[i], to[i])) / top, hi = Mathf.Max(0f, Math.Max(from[i], to[i])) / top;
                var rect = new Rect(plot.x + (bw + S.Pad) * i, plot.yMax - plot.height * hi, bw, Mathf.Max(2f, plot.height * (hi - lo)));
                Color c = kinds[i] == 0 ? Pal.ConcertSeatVip : kinds[i] == 1 ? Pal.ConcertDown : Pal.ConcertUp;
                if (Draw.Repaint) Draw.Round(rect, c, Board.BoxRadius * 0.75f);
                long amount = Math.Abs(to[i] - from[i]);
                Draw.TextFit(new Rect(rect.x, rect.y - valueH, rect.width, valueH), (kinds[i] == 1 ? "− " : "") + Money(amount), D.FontMin, c, true, TextAnchor.MiddleCenter);
                Draw.TextFit(new Rect(rect.x, plot.yMax + D.Gap * 2f, rect.width, D.LineSub), names[i], D.FontMin, Pal.Light, false, TextAnchor.MiddleCenter);
            }
        }

        void MonthlyPlate(Rect r, DateTime now)
        {
            var body = Plate(r, "월별 매표 수입", TrendIcon, $"최근 {Months}달 · 이번 달 강조");
            var rev = new long[Months];
            var pro = new long[Months];
            var start = new DateTime(now.Year, now.Month, 1).AddMonths(-(Months - 1));
            foreach (var rec in History.All)
            {
                int m = (rec.Start.Year - start.Year) * 12 + rec.Start.Month - start.Month;
                if (m < 0 || m >= Months) continue;
                rev[m] += rec.Ticket + rec.Reward;
                pro[m] += Profit(rec);
            }
            long top = 1;
            foreach (var v in rev) top = Math.Max(top, v);
            float axisW = S.CodeW * 2f, labelH = D.LineSub + D.Gap * 2f;
            var plot = new Rect(body.x + axisW, body.y + D.LineSub, body.width - axisW, body.height - labelH - D.LineSub);
            for (int g = 0; g <= 3; g++)
            {
                float gy = plot.yMax - plot.height * g / 3f;
                if (Draw.Repaint) Draw.Fill(new Rect(plot.x, gy, plot.width, 1f), Pal.Line);
                Draw.TextFit(new Rect(body.x, gy - D.LineSub * 0.5f, axisW - D.Gap * 2f, D.LineSub), Money(top * g / 3), D.FontMin, S.SubInk, false, TextAnchor.MiddleRight);
            }
            float step = plot.width / Months, barW = step * 0.4f;
            var pts = new List<Vector2>();
            for (int m = 0; m < Months; m++)
            {
                float cx = plot.x + step * (m + 0.5f);
                bool cur = m == Months - 1;
                if (pro[m] > 0 && Draw.Repaint) Draw.Round(new Rect(cx - barW * 0.5f, plot.yMax - plot.height * pro[m] / top, barW, plot.height * pro[m] / top), cur ? Pal.ConcertUp : Draw.Alpha(Pal.ConcertUp, 0.3f), Board.BoxRadius * 0.5f);
                pts.Add(new Vector2(cx, plot.yMax - plot.height * rev[m] / top));
                var mon = start.AddMonths(m);
                Draw.TextFit(new Rect(cx - step * 0.5f, plot.yMax + D.Gap * 2f, step, D.LineSub), mon.Month + "월", D.FontMin, cur ? Look.PastelSubInk : S.SubInk, cur, TextAnchor.MiddleCenter);
            }
            if (Draw.Repaint)
            {
                Draw.Curve(pts, 2.5f, Pal.Line2);
                Draw.Curve(pts.GetRange(Months - 2, 2), 3.5f, Pal.ConcertSeatVip);
                Draw.Dot(pts[Months - 1], D.Gap * 1.5f, Pal.ConcertSeatVip);
            }
            string tag = $"{now.Month}월 {Money(rev[Months - 1])}";
            float tw = Badge.Pair(0f, 0f, tag, Pal.Inset, Pal.Txt, false);
            Badge.Pair(Mathf.Min(pts[Months - 1].x - tw * 0.5f, plot.xMax - tw), body.y + D.LineSub * 0.5f, tag, Pal.ConcertSeatVip, Draw.On(Pal.ConcertSeatVip));
        }

        void KingPlate(Rect r, List<Record> month)
        {
            Record best = null;
            foreach (var rec in month) if (best == null || rec.Ticket > best.Ticket) best = rec;
            S.Frame(r);
            var art = new Rect(r.x, r.y, r.width, C.HeroArtH);
            var band = best != null ? Shows.BandOf(best.Key) : null;
            C.Stage(art, band?.Color ?? Pal.Dim, band?.Name ?? "기록 없음", best != null ? Day(best.Start) + " 공연" : "공연이 끝나면 채워져요");
            Badge.Pair(art.x + S.Pad, art.y + S.Pad + Badge.H * 0.5f, "이달의 흥행왕", Pal.ConcertSeatA, Draw.On(Pal.ConcertSeatA));
            float y = art.yMax + S.Pad, cw = (r.width - S.Pad * 2f - D.Gap * 2f) / 2f, x = r.x + S.Pad;
            Cell(new Rect(x, y, cw, CellH), "관객", best != null ? best.Visitors.ToString("#,0") + "명" : "–");
            Cell(new Rect(x + cw + D.Gap * 2f, y, cw, CellH), "이익", best != null ? Money(Profit(best)) : "–", Pal.ConcertUp);
            Cell(new Rect(x, y + CellH + D.Gap * 2f, cw, CellH), "결과", best != null ? (best.Success ? "성공" : "실패") : "–");
            Cell(new Rect(x + cw + D.Gap * 2f, y + CellH + D.Gap * 2f, cw, CellH), "인기", best != null ? $"{best.PopAfter} · {Delta(best.PopDelta)}" : "–");
        }

        void ScorePlate(Rect r, List<Record> month)
        {
            var body = Plate(r, "공연별 성적표", ListIcon, "이번 달");
            float colW = S.CodeW * 2.6f;
            float y = body.y;
            string[] heads = { "관객", "매출", "보안 예산", "이익" };
            for (int i = 0; i < heads.Length; i++)
                Draw.TextFit(new Rect(body.xMax - colW * (heads.Length - i), y, colW, D.LineSub), heads[i], D.FontMin, S.SubInk, false, TextAnchor.MiddleRight);
            Draw.TextFit(new Rect(body.x, y, colW, D.LineSub), "공연", D.FontMin, S.SubInk);
            y += D.LineSub;
            if (month.Count == 0)
            {
                Draw.TextFit(new Rect(body.x, y, body.width, S.RowH), "이번 달 끝난 공연이 없어요", D.FontBody, S.SubInk);
                return;
            }
            foreach (var rec in month)
            {
                var row = new Rect(body.x, y, body.width, C.PastRowH);
                S.Hairline(new Rect(row.x, row.yMax - 1f, row.width, 1f));
                var band = Shows.BandOf(rec.Key);
                string name = band?.Name ?? Artists.Of(rec.Key);
                Tile(new Rect(row.x, row.center.y - C.Tile * 0.5f, C.Tile, C.Tile), band?.Color ?? Pal.Dim, name);
                float tx = row.x + C.Tile + S.Pad, tw = body.width - colW * 4f - C.Tile - S.Pad * 2f;
                Draw.TextFit(new Rect(tx, row.center.y - D.LineBody, tw, D.LineBody), name, D.FontBody, Pal.Txt, true);
                Draw.TextFit(new Rect(tx, row.center.y + D.Gap * 0.5f, tw, D.LineSub), $"{When(rec.Start)} · {rec.Venue}", D.FontMin, S.SubInk);
                long pr = Profit(rec);
                string[] vals = { rec.Visitors.ToString("#,0") + "명", Money(rec.Ticket + rec.Reward), Money(rec.Security), (pr < 0 ? "− " : "") + Money(Math.Abs(pr)) };
                for (int i = 0; i < vals.Length; i++)
                    Draw.TextFit(new Rect(body.xMax - colW * (vals.Length - i), row.y, colW, row.height), vals[i], i == 3 ? D.FontValue : D.FontDesc, i == 3 ? (pr < 0 ? Pal.ConcertDown : Pal.ConcertUp) : i == 2 ? S.SubInk : Pal.Txt, i == 3, TextAnchor.MiddleRight);
                y += C.PastRowH;
            }
        }
    }
}
