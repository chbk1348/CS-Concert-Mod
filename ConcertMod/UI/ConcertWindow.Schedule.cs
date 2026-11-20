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
    /// 일정 탭(캔버스 Main · schedule): 지금 무대(무대 장면 · 관객 막대 · 입장권 · 매표 수입 · 보안 예산) · 다음 공연 표 2장 ·
    /// 지난 공연(모드 기록, 투어 기록 줄) · 현장 상황(소음 · 치안 · 오가는 관객). 숫자는 모두 게임 공연 값.
    /// </summary>
    internal sealed partial class ConcertWindow
    {
        const string StageIcon = "M3 20h18M5 20V9l7-5 7 5v11M9 20v-6h6v6", ClockIcon = "M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18zM12 7v5l3 2";
        const string ListIcon = "M8 6h12M8 12h12M8 18h12M4 6h.01M4 12h.01M4 18h.01", ShieldIcon = "M12 3l8 4v5c0 5-3.5 8-8 9-4.5-1-8-4-8-9V7z";

        /// <summary>현장 상황 막대 끝(이만큼이면 꽉 참): 소음 · 범죄 누적(게임 값 단위).</summary>
        const float NoiseFull = 100f, CrimeFull = 100f;

        float ScheduleTab(float w)
        {
            float gap = S.Gap, left = Mathf.Round((w - gap) * 2f / 3f), right = w - gap - left;
            float stageW = left - S.Pad * 2f, stageH = Mathf.Round(stageW * C.StageH / C.StageW);
            float h1 = PlateTop + stageH + gap + D.LineBody + D.Gap * 3f + C.BarH + gap + CellH + S.PadBottom;
            float hNext = PlateTop + Mathf.Max(1, Shows.Next.Count) * (C.TicketH + gap) + D.LineSub + S.PadBottom;
            float row1 = Mathf.Max(h1, hNext);
            NowPlate(new Rect(0f, 0f, left, row1), stageH);
            NextPlate(new Rect(left + gap, 0f, right, row1));
            var past = Last(History.All, 4);
            float row2 = PlateTop + D.LineSub + Mathf.Max(1, past.Count) * C.PastRowH + S.PadBottom;
            float siteH = PlateTop + 3 * (CellH + gap + D.LineSub) + S.PadBottom;
            row2 = Mathf.Max(row2, siteH);
            float y2 = row1 + gap;
            PastPlate(new Rect(0f, y2, left, row2), past);
            SitePlate(new Rect(left + gap, y2, right, row2));
            return y2 + row2;
        }

        void NowPlate(Rect r, float stageH)
        {
            var s = Shows.Now;
            string sub = s == null ? "" : s.Active ? $"공연 끝까지 {Left(s.End)}" : $"시작까지 {Left(s.Start)}";
            var body = Plate(r, "지금 무대", StageIcon, sub);
            var stage = new Rect(body.x, body.y, body.width, stageH);
            if (s == null)
            {
                C.Stage(stage, Pal.Dim, "공연 없음", Shows.Next.Count > 0 ? "다음 공연 " + When(Shows.Next[0].Start) : "");
                return;
            }
            C.Stage(stage, s.Color, s.Band.Name, $"{Day(s.Start)} {(s.Start.Hour >= 18 ? "밤" : "낮")} 공연");
            float bx = stage.x + S.Pad, by = stage.y + S.Pad;
            bx += Badge.Pair(bx, by + Badge.H * 0.5f, s.Active ? $"공연 중 · {s.Start:HH:mm} 시작" : $"준비 중 · {s.Start:HH:mm} 시작", s.Color, Draw.On(s.Color)) + D.Gap * 2f;
            string pop = $"인기 {s.Band.Pop} · {Delta(s.PopDelta)}";
            float pw = Badge.Pair(0f, 0f, pop, Pal.ConcertScene, Pal.Txt, false);
            Badge.Pair(stage.xMax - S.Pad - pw, by + Badge.H * 0.5f, pop, Pal.ConcertScene, Pal.Txt);
            float y = stage.yMax + S.Gap;
            string head = s.Arrived.ToString("#,0");
            float hw = Mathf.Ceil(Draw.TextWidth(head, C.BigFont, true));
            Draw.TextFit(new Rect(body.x, y, hw, D.LineBody + D.Gap), head, C.BigFont, Pal.Txt, true);
            Draw.TextFit(new Rect(body.x + hw + D.Gap * 2f, y, body.width * 0.6f, D.LineBody + D.Gap), $"명 입장 · 오는 중 {s.Incoming:#,0} · 최대 {s.Max:#,0}", D.FontMin, S.SubInk);
            float pct = s.Max > 0 ? Mathf.Clamp01((float)s.Arrived / s.Max) : 0f, inc = s.Max > 0 ? Mathf.Clamp01((float)s.Incoming / s.Max) : 0f;
            Draw.TextFit(new Rect(body.x, y, body.width, D.LineBody + D.Gap), Mathf.RoundToInt(pct * 100f) + "%", D.FontValue, Look.PastelSubInk, true, TextAnchor.MiddleRight);
            y += D.LineBody + D.Gap * 3f;
            if (Draw.Repaint)
            {
                var bar = new Rect(body.x, y, body.width, C.BarH);
                Draw.Round(bar, Pal.ConcertSold, C.BarH * 0.5f);
                if (pct + inc > 0f) Draw.Round(new Rect(bar.x, bar.y, bar.width * Mathf.Min(1f, pct + inc), bar.height), Draw.Alpha(s.Color, 0.35f), C.BarH * 0.5f);
                if (pct > 0f) Draw.Round(new Rect(bar.x, bar.y, bar.width * pct, bar.height), s.Color, C.BarH * 0.5f);
            }
            y += C.BarH + S.Gap;
            float cw = (body.width - D.Gap * 4f) / 3f;
            Cell(new Rect(body.x, y, cw, CellH), "입장권", Price(s.Price));
            Cell(new Rect(body.x + cw + D.Gap * 2f, y, cw, CellH), "매표 수입", Money(s.Ticket));
            Cell(new Rect(body.x + (cw + D.Gap * 2f) * 2f, y, cw, CellH), "보안 예산", Price(s.Security));
        }

        void NextPlate(Rect r)
        {
            var body = Plate(r, "다음 공연", ClockIcon);
            float y = body.y;
            if (Shows.Next.Count == 0)
            {
                Draw.TextFit(new Rect(body.x, y, body.width, S.RowH), Shows.Venue == 0 ? "페스티벌 구역이나 경기장(Match Day · 대학)을 지으면 공연이 잡혀요" : "잡힌 공연이 없어요", D.FontBody, S.SubInk);
                return;
            }
            foreach (var n in Shows.Next)
            {
                TicketCard(new Rect(body.x, y, body.width, C.TicketH), n);
                y += C.TicketH + S.Gap;
            }
            Draw.TextFit(new Rect(body.x, y, body.width, D.LineSub), "다음 밴드 · 입장권은 예약 탭에서 바꿔요", D.FontMin, S.SubInk);
        }

        /// <summary>입장권 모양 다음 공연: 왼쪽 밴드 색 띠 · 밴드 · 시각 · 인기, 오른쪽 점선 너머 입장권 값 · 반원 홈.</summary>
        static void TicketCard(Rect r, NextShow n)
        {
            if (Draw.Repaint)
            {
                Draw.Round(r, Pal.Inset, Board.BoxRadius * 2f);
                Draw.Fill(new Rect(r.x, r.y + D.Gap * 2f, C.Strip, r.height - D.Gap * 4f), n.Band.Color);
                float sx = r.xMax - C.StubW;
                Draw.Dashed(new Vector2(sx, r.y + D.Gap * 3f), new Vector2(sx, r.yMax - D.Gap * 3f), 2f, Pal.Line, D.Gap, D.Gap);
                Draw.Dot(new Vector2(sx, r.y), S.Pad * 0.55f, S.Plate);
                Draw.Dot(new Vector2(sx, r.yMax), S.Pad * 0.55f, S.Plate);
            }
            float x = r.x + C.Strip + S.Pad, iw = r.width - C.Strip - C.StubW - S.Pad * 2f, y = r.y + S.Pad;
            Draw.TextFit(new Rect(x, y, iw, D.LineSub), "입장권 · " + (Shows.VenueName.Length > 0 ? Shows.VenueName : "공연장"), D.FontMin, S.SubInk);
            y += D.LineSub + D.Gap;
            Draw.TextFit(new Rect(x, y, iw, D.LineBody + D.Gap * 2f), n.Band.Name, D.FontValue + D.Gap, Pal.Txt, true);
            y += D.LineBody + D.Gap * 3f;
            var now = SimulationManager.instance.m_currentGameTime;
            Draw.TextFit(new Rect(x, y, iw, D.LineSub), $"{When(n.Start)} · {Math.Max(0, (int)(n.Start.Date - now.Date).TotalDays)}일 뒤", D.FontMin, Pal.Light);
            y += D.LineSub + D.Gap;
            Draw.TextFit(new Rect(x, y, iw, D.LineSub), "인기 " + n.Band.Pop, D.FontMin, S.SubInk);
            var stub = new Rect(r.xMax - C.StubW, r.y, C.StubW, r.height);
            Draw.TextFit(new Rect(stub.x, stub.center.y - D.LineSub - D.Gap, stub.width, D.LineSub), "입장권", D.FontMin, S.SubInk, false, TextAnchor.MiddleCenter);
            Draw.TextFit(new Rect(stub.x, stub.center.y, stub.width, D.LineBody), Price(n.Band.Price), D.FontValue, Pal.Txt, true, TextAnchor.MiddleCenter);
        }

        void PastPlate(Rect r, List<Record> past)
        {
            var body = Plate(r, "지난 공연", ListIcon, "모드를 넣은 뒤 끝난 공연 · 투어 기록");
            float y = body.y + D.LineSub;
            if (past.Count == 0)
            {
                Draw.TextFit(new Rect(body.x, body.y, body.width, S.RowH), "아직 끝난 공연이 없어요 · 공연이 끝나면 여기에 남아요", D.FontBody, S.SubInk);
                return;
            }
            for (int i = past.Count - 1; i >= 0; i--)
            {
                PastRow(new Rect(body.x, y, body.width, C.PastRowH), past[i]);
                y += C.PastRowH;
            }
        }

        static void PastRow(Rect r, Record rec)
        {
            var band = Shows.BandOf(rec.Key);
            string name = band?.Name ?? Artists.Of(rec.Key);
            Color color = band?.Color ?? Pal.Dim;
            S.Hairline(new Rect(r.x, r.yMax - 1f, r.width, 1f));
            var date = new Rect(r.x, r.center.y - C.DateH * 0.5f, C.DateW, C.DateH);
            if (Draw.Repaint) Draw.Round(date, Pal.Inset, Board.BoxRadius * 1.5f);
            Draw.TextFit(new Rect(date.x, date.y + D.Gap * 2f, date.width, D.LineSub), rec.Start.Month + "월", D.FontMin, S.SubInk, false, TextAnchor.MiddleCenter);
            Draw.TextFit(new Rect(date.x, date.y + D.Gap * 2f + D.LineSub, date.width, D.LineBody + D.Gap * 2f), rec.Start.Day.ToString(), C.BigFont, Pal.Txt, true, TextAnchor.MiddleCenter);
            var tile = new Rect(date.xMax + S.Pad, r.center.y - C.Tile * 0.5f, C.Tile, C.Tile);
            Tile(tile, color, name);
            float badgeW = Badge.Pair(0f, 0f, rec.Success ? "성공" : "실패", Pal.Inset, Pal.Txt, false);
            float right = r.xMax - badgeW;
            Badge.Pair(right, r.center.y, rec.Success ? "성공" : "실패", Draw.Alpha(rec.Success ? Pal.ConcertUp : Pal.ConcertDown, 0.14f), rec.Success ? Pal.ConcertUp : Pal.ConcertDown);
            string pop = "인기 " + Delta(rec.PopDelta);
            float popW = Mathf.Ceil(Draw.TextWidth(pop, D.FontBody, true));
            right -= popW + S.Pad;
            Draw.TextFit(new Rect(right, r.y, popW, r.height), pop, D.FontBody, DeltaInk(rec.PopDelta), true, TextAnchor.MiddleRight);
            float tx = tile.xMax + S.Pad;
            float top = r.center.y - (D.LineBody + D.LineSub + D.Gap) * 0.5f;
            Draw.TextFit(new Rect(tx, top, right - S.Pad - tx, D.LineBody), name, D.FontValue, Pal.Txt, true);
            Draw.TextFit(new Rect(tx, top + D.LineBody + D.Gap, right - S.Pad - tx, D.LineSub), $"관객 {rec.Visitors:#,0}명 · 매표 {Money(rec.Ticket)} · 보상금 {Money(rec.Reward)}", D.FontMin, S.SubInk);
        }

        void SitePlate(Rect r)
        {
            var body = Plate(r, "현장 상황", ShieldIcon, "공연장 주변 · 지금");
            var s = Shows.Now;
            float y = body.y;
            if (s == null || !s.Active)
            {
                Draw.TextFit(new Rect(body.x, y, body.width, S.RowH), "공연이 열리면 소음 · 치안 · 오가는 관객을 보여 줘요", D.FontBody, S.SubInk);
                return;
            }
            Meter(new Rect(body.x, y, body.width, CellH + D.LineSub), "소음", s.Noise >= NoiseFull * 0.6f ? "높음" : s.Noise >= NoiseFull * 0.3f ? "보통" : "낮음", s.Noise / NoiseFull, Pal.ConcertSeatA, "공연 중엔 주변 소음이 늘어요");
            y += CellH + D.LineSub + S.Gap;
            Meter(new Rect(body.x, y, body.width, CellH + D.LineSub), "치안", s.Crime >= CrimeFull * 0.6f ? "나쁨" : s.Crime >= CrimeFull * 0.3f ? "보통" : "좋음", s.Crime / CrimeFull, Pal.ConcertSeatS, $"보안 예산 {Price(s.Security)}");
            y += CellH + D.LineSub + S.Gap;
            Meter(new Rect(body.x, y, body.width, CellH + D.LineSub), "오가는 관객", $"{s.Incoming:#,0}명 오는 중", s.Max > 0 ? (float)s.Incoming / s.Max : 0f, Pal.ConcertSeatR, "대중교통 · 도로로 들어오는 관객");
        }

        static void Meter(Rect r, string name, string value, float pct, Color bar, string desc)
        {
            if (Draw.Repaint) Draw.Round(r, Pal.Inset, Board.BoxRadius * 2f);
            float x = r.x + S.Pad, w = r.width - S.Pad * 2f, y = r.y + D.Gap * 3f;
            Draw.TextFit(new Rect(x, y, w, D.LineBody), name, D.FontMin, S.SubInk);
            Draw.TextFit(new Rect(x, y, w, D.LineBody), value, D.FontValue, Pal.Txt, true, TextAnchor.MiddleRight);
            y += D.LineBody + D.Gap * 2f;
            if (Draw.Repaint)
            {
                Draw.Round(new Rect(x, y, w, Board.BoxLine * 4f), Pal.ConcertSold, Board.BoxLine * 2f);
                if (pct > 0f) Draw.Round(new Rect(x, y, w * Mathf.Clamp01(pct), Board.BoxLine * 4f), bar, Board.BoxLine * 2f);
            }
            y += Board.BoxLine * 4f + D.Gap * 2f;
            Draw.TextFit(new Rect(x, y, w, D.LineSub), desc, D.FontMin, S.SubInk);
        }

        static List<Record> Last(List<Record> all, int n) => all.GetRange(Math.Max(0, all.Count - n), Math.Min(n, all.Count));

        static string Left(DateTime t)
        {
            var span = t - SimulationManager.instance.m_currentGameTime;
            if (span.TotalMinutes < 1) return "곧";
            return span.TotalHours >= 1 ? $"{(int)span.TotalHours}시간 {span.Minutes}분" : $"{span.Minutes}분";
        }
    }
}
