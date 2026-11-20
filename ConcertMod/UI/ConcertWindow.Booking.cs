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
    /// 예약 탭 · 다음 공연(캔버스 Main · booking): 공연 포스터(무대 장면 · D-n · 회차 칸) · 밴드 바꾸기 후보 5팀 · 공연 기간 1 ~ 3일 · 시작 시각 ·
    /// 게임이 고른 밴드로 / 이 일정으로 잡기 · 라인업 · 밴드별 입장권(− + · 기본 대비). 잡은 계획은 Booking 이 게임 공연 만들기에 넣음.
    /// </summary>
    internal sealed partial class ConcertWindow
    {
        const string CalendarIcon = "M4 5h16v15H4zM4 10h16M9 3v4M15 3v4M12 13v5M9.5 15.5h5", TicketIcon = "M3 8a2 2 0 0 0 0 4v4h18v-4a2 2 0 0 0 0-4V4H3z";

        /// <summary>후보 카드 수 · 시작 시각 고르기 범위(시) · 입장권 한 번 누를 때(₡1.00) · 기본값 대비 범위.</summary>
        const int Candidates = 5, FirstHour = 12, LastHour = 23, PriceStep = 100;
        const float PriceMin = 0.5f, PriceMax = 2f, PriceFree = 0.3f;

        string pickBand;
        int pickDays = Booking.MinDays, pickHour = -1;

        float BookingTab(float w)
        {
            if (Shows.Venue == 0) return Note(0f, w, "예약", "페스티벌 구역이나 경기장(Match Day · 대학)을 지으면 다음 공연을 잡을 수 있어요");
            var plan = Booking.Current;
            Band gameBand = null;
            foreach (var n in Shows.Next) { gameBand = n.Band; break; }
            if (plan != null && plan.Done > 0) gameBand = null;
            pickBand ??= plan?.Band ?? gameBand?.Key ?? (Shows.Bands.Count > 0 ? Shows.Bands[0].Key : null);
            var band = Shows.BandOf(pickBand);
            if (band == null) return Note(0f, w, "예약", "밴드가 없어요");
            if (pickHour < 0) pickHour = plan?.Hour ?? Mathf.RoundToInt(((EventAI)band.Info.m_eventAI).m_startTime);
            float gap = S.Gap, left = Mathf.Round((w - gap) * 7f / 12f), right = w - gap - left;
            float artH = Mathf.Round((left) * C.StageH / C.StageW * 0.82f);
            float candH = C.TicketH - D.Gap * 5f;
            float leftH = artH + S.Pad + D.LineBody + D.Gap * 3f + candH + S.Gap + CellH + D.Gap * 4f + S.Gap + Button.H + S.Pad;
            PosterPlate(new Rect(0f, 0f, left, leftH), band, gameBand, plan, artH, candH);
            LineupPlate(new Rect(left + gap, 0f, right, leftH), plan);
            int rows = (Shows.Bands.Count + 2) / 3;
            float priceH = PlateTop + rows * (C.TicketH - D.Gap * 5f + D.Gap * 2.5f) + S.PadBottom;
            PricePlate(new Rect(0f, leftH + gap, w, priceH));
            return leftH + gap + priceH;
        }

        /// <summary>이 공연 첫 회차 시작: 다음 칸 날짜의 고른 시각(지나면 하루 뒤).</summary>
        DateTime FirstStart()
        {
            var now = SimulationManager.instance.m_currentGameTime;
            var day = Shows.Next.Count > 0 ? Shows.Next[0].Start.Date : now.Date.AddDays(1);
            var t = day.AddHours(pickHour);
            return t < now ? t.AddDays(1) : t;
        }

        void PosterPlate(Rect r, Band band, Band gameBand, Plan plan, float artH, float candH)
        {
            S.Frame(r);
            var art = new Rect(r.x, r.y, r.width, artH);
            var first = FirstStart();
            C.Stage(art, band.Color, band.Name, Shows.VenueName.Length > 0 ? Shows.VenueName : "공연장");
            var now = SimulationManager.instance.m_currentGameTime;
            float bx = art.x + S.Pad, by = art.y + S.Pad + Badge.H * 0.5f;
            bx += Badge.Pair(bx, by, "D-" + Math.Max(0, (int)(first.Date - now.Date).TotalDays), band.Color, Draw.On(band.Color)) + D.Gap * 2f;
            Badge.Pair(bx, by, $"다음 공연 · {pickDays}일 공연", Pal.ConcertScene, Pal.Txt);
            // 회차 칸: 무대 아래쪽
            float cw = (art.width - S.Pad * 2f - D.Gap * 2f * (pickDays - 1)) / pickDays, cy = art.yMax - S.Pad - S.RowH;
            for (int i = 0; i < pickDays; i++)
            {
                var cell = new Rect(art.x + S.Pad + (cw + D.Gap * 2f) * i, cy, cw, S.RowH);
                if (Draw.Repaint)
                {
                    Draw.Round(cell, Draw.Alpha(Pal.Win, 0.85f), Board.BoxRadius * 1.5f);
                    Draw.RoundOutline(cell, i == 0 ? band.Color : Pal.Line, Board.BoxRadius * 1.5f);
                }
                var d = first.AddDays(i);
                Draw.TextFit(new Rect(cell.x + S.Pad, cell.y, cell.width - S.Pad * 2f, cell.height * 0.5f + D.Gap), $"{i + 1}회차 · {"일월화수목금토"[(int)d.DayOfWeek]}", D.FontMin, i == 0 ? band.Color : S.SubInk);
                Draw.TextFit(new Rect(cell.x + S.Pad, cell.y + cell.height * 0.45f, cell.width - S.Pad * 2f, cell.height * 0.5f), When(d), D.FontDesc, Pal.Txt, true);
            }
            float x = r.x + S.Pad, iw = r.width - S.Pad * 2f, y = art.yMax + S.Pad;
            Draw.TextFit(new Rect(x, y, iw, D.LineBody), "밴드 바꾸기", D.FontBody, Pal.Txt, true);
            Draw.TextFit(new Rect(x + Draw.TextWidth("밴드 바꾸기", D.FontBody, true) + D.Gap * 3f, y, iw, D.LineBody), gameBand != null ? $"게임이 고른 밴드는 {gameBand.Name}예요 · 바꾸면 이 공연만 바뀌어요" : "고른 밴드로 공연을 잡아요", D.FontMin, S.SubInk);
            y += D.LineBody + D.Gap * 3f;
            var cands = new List<Band>();
            if (gameBand != null) cands.Add(gameBand);
            if (!cands.Contains(band)) cands.Add(band);
            var byPop = new List<Band>(Shows.Bands);
            byPop.Sort((a, b) => b.Pop.CompareTo(a.Pop));
            foreach (var b in byPop) { if (cands.Count >= Candidates) break; if (!cands.Contains(b)) cands.Add(b); }
            float kw = (iw - D.Gap * 2f * (cands.Count - 1)) / cands.Count;
            for (int i = 0; i < cands.Count; i++)
            {
                var b = cands[i];
                var card = new Rect(x + (kw + D.Gap * 2f) * i, y, kw, candH);
                bool on = b.Key == pickBand;
                if (Draw.Repaint)
                {
                    Draw.Round(card, on ? Pal.Inset : S.Plate, Board.BoxRadius * 2f);
                    Draw.RoundOutline(card, on ? b.Color : Pal.Line, Board.BoxRadius * 2f, on ? 2f : 1f);
                }
                float cd = C.Tile * 1.2f;
                var disc = new Vector2(card.center.x, card.y + S.Pad + cd * 0.5f);
                if (Draw.Repaint) Draw.Dot(disc, cd * 0.5f, b.Color);
                Draw.TextFit(new Rect(disc.x - cd * 0.5f, disc.y - cd * 0.5f, cd, cd), Artists.Initials(b.Name), D.FontMin, Draw.On(b.Color), true, TextAnchor.MiddleCenter);
                Draw.TextFit(new Rect(card.x + D.Gap, disc.y + cd * 0.5f + D.Gap, card.width - D.Gap * 2f, D.LineSub + D.Gap), b.Name, D.FontDesc, Pal.Txt, true, TextAnchor.MiddleCenter);
                Draw.TextFit(new Rect(card.x, disc.y + cd * 0.5f + D.LineSub + D.Gap * 2f, card.width, D.LineSub), "인기 " + b.Pop, D.FontMin, S.SubInk, false, TextAnchor.MiddleCenter);
                string tag = on ? "고름" : b == gameBand ? "게임 추천" : null;
                if (tag != null)
                {
                    float tw = Badge.Pair(0f, 0f, tag, Pal.Inset, Pal.Txt, false);
                    Badge.Pair(card.xMax - tw - D.Gap * 2f, card.y + D.Gap * 2f + Badge.H * 0.5f, tag, on ? b.Color : Pal.Inset, on ? Draw.On(b.Color) : Pal.Light);
                }
                if (Draw.Button(card)) pickBand = b.Key;
            }
            y += candH + S.Gap;
            float hw = (iw - D.Gap * 2f) * 0.5f;
            var lenCell = new Rect(x, y, hw, CellH + D.Gap * 4f);
            var hourCell = new Rect(x + hw + D.Gap * 2f, y, hw, CellH + D.Gap * 4f);
            if (Draw.Repaint)
            {
                Draw.Round(lenCell, Pal.Inset, Board.BoxRadius * 2f);
                Draw.Round(hourCell, Pal.Inset, Board.BoxRadius * 2f);
            }
            Draw.TextFit(new Rect(lenCell.x + S.Pad, lenCell.y + D.Gap * 2f, lenCell.width, D.LineSub), "공연 기간", D.FontMin, S.SubInk);
            Segmented.In(new Rect(lenCell.x + S.Pad, lenCell.y + D.Gap * 3f + D.LineSub, lenCell.width - S.Pad * 2f, Segmented.H), new[] { "1일", "2일", "3일" }, pickDays - 1, i => pickDays = i + 1);
            Draw.TextFit(new Rect(hourCell.x + S.Pad, hourCell.y + D.Gap * 2f, hourCell.width, D.LineSub), "시작 시각 · 회차마다 같은 시각", D.FontMin, S.SubInk);
            var hours = new string[LastHour - FirstHour + 1];
            for (int i = 0; i < hours.Length; i++) hours[i] = $"{FirstHour + i:00}:00";
            int hi = Mathf.Clamp(pickHour - FirstHour, 0, hours.Length - 1);
            Select.In(new Rect(hourCell.x + S.Pad, hourCell.y + D.Gap * 3f + D.LineSub, hourCell.width - S.Pad * 2f, Segmented.H), hours[hi], null, hours, null, hi, i => pickHour = FirstHour + i);
            y += CellH + D.Gap * 4f + S.Gap;
            float okW = Button.Width(null, "이 일정으로 잡기"), resetW = Button.Width(null, "게임이 고른 밴드로");
            Button.In(new Rect(r.xMax - S.Pad - okW, y, okW, Button.H), null, "이 일정으로 잡기", "고른 밴드 · 기간 · 시각으로 다음 공연을 잡아요", Tone.Primary, click: () => Booking.Set(pickBand, pickDays, pickHour));
            Button.In(new Rect(r.xMax - S.Pad - okW - D.Gap * 2f - resetW, y, resetW, Button.H), null, "게임이 고른 밴드로", "잡은 계획을 지우고 게임대로 둬요", Tone.Neutral, enabled: plan != null, click: () =>
            {
                Booking.Clear();
                pickBand = null;
                pickHour = -1;
                pickDays = Booking.MinDays;
            });
            if (plan != null)
                Draw.TextFit(new Rect(x, y, iw - okW - resetW - S.Pad, Button.H), $"잡힘 · {Artists.Of(plan.Band)} {plan.Days}일 · {plan.Hour:00}:00 · {plan.Done}/{plan.Days}회차 만들어짐", D.FontMin, Look.PastelSubInk, true);
        }

        void LineupPlate(Rect r, Plan plan)
        {
            var body = Plate(r, "라인업", ListIcon, "다음 두 공연");
            var box = new Rect(body.x, body.y, body.width, body.height);
            if (Draw.Repaint)
            {
                Draw.Round(box, Pal.ConcertScene, S.Radius - S.Pad * 0.5f);
                Draw.RoundOutline(box, Pal.Line, S.Radius - S.Pad * 0.5f);
            }
            float y = box.y + S.Pad * 1.5f;
            Draw.TextFit(new Rect(box.x, y, box.width, D.LineSub), (Shows.VenueName.Length > 0 ? Shows.VenueName : "공연장") + " 라인업", D.FontMin, Pal.Light, false, TextAnchor.MiddleCenter);
            y += D.LineSub + S.Pad;
            if (Shows.Next.Count == 0)
            {
                Draw.TextFit(new Rect(box.x, y, box.width, S.RowH), "잡힌 공연이 없어요", D.FontBody, S.SubInk, false, TextAnchor.MiddleCenter);
                return;
            }
            for (int i = 0; i < Shows.Next.Count; i++)
            {
                var n = Shows.Next[i];
                float size = i == 0 ? C.NameFont + D.Gap * 2.5f : C.NameFont - D.Gap * 0.5f;
                Draw.TextFit(new Rect(box.x + S.Pad, y, box.width - S.Pad * 2f, size + D.Gap * 2f), n.Band.Name, size, n.Band.Color, true, TextAnchor.MiddleCenter);
                y += size + D.Gap * 3f;
                bool mine = plan != null && plan.Band == n.Band.Key;
                Draw.TextFit(new Rect(box.x, y, box.width, D.LineSub), $"{When(n.Start)} · {(mine ? "내가 고름" : "게임이 고름")}", D.FontMin, Pal.Light, false, TextAnchor.MiddleCenter);
                y += D.LineSub + S.Pad * 1.5f;
            }
            if (Draw.Repaint) Draw.Fill(new Rect(box.center.x - S.Pad * 1.5f, y, S.Pad * 3f, 2f), Pal.Line);
            y += S.Pad;
            Draw.TextFit(new Rect(box.x, y, box.width, D.LineSub), "한 번에 한 팀 · 공연은 1 ~ 3일 · 회차마다 정한 시각에", D.FontMin, S.SubInk, false, TextAnchor.MiddleCenter);
        }

        void PricePlate(Rect r)
        {
            var body = Plate(r, "밴드별 입장권", TicketIcon, $"기본값에서 ±{Mathf.RoundToInt(PriceFree * 100f)}% 넘게 바꾸면 관객이 줄어요");
            float gap = D.Gap * 2.5f, cw = (body.width - gap * 2f) / 3f, ch = C.TicketH - D.Gap * 5f;
            for (int i = 0; i < Shows.Bands.Count; i++)
            {
                var b = Shows.Bands[i];
                PriceCard(new Rect(body.x + (cw + gap) * (i % 3), body.y + (ch + gap) * (i / 3), cw, ch), b);
            }
        }

        static void PriceCard(Rect r, Band b)
        {
            int baseP = b.Info.m_eventAI.m_ticketPrice;
            int pct = baseP > 0 ? Mathf.RoundToInt(100f * (b.Price - baseP) / baseP) : 0;
            if (Draw.Repaint)
            {
                Draw.Round(r, Pal.Inset, Board.BoxRadius * 2f);
                Draw.Fill(new Rect(r.x, r.y + D.Gap * 2f, C.Strip, r.height - D.Gap * 4f), b.Color);
                float sx = r.xMax - C.StubW * 0.75f;
                Draw.Dashed(new Vector2(sx, r.y + D.Gap * 3f), new Vector2(sx, r.yMax - D.Gap * 3f), 2f, Pal.Line, D.Gap, D.Gap);
                Draw.Dot(new Vector2(sx, r.y), S.Pad * 0.55f, S.Plate);
                Draw.Dot(new Vector2(sx, r.yMax), S.Pad * 0.55f, S.Plate);
            }
            float x = r.x + C.Strip + S.Pad, iw = r.width - C.Strip - C.StubW * 0.75f - S.Pad * 2f, y = r.y + S.Pad;
            Draw.TextFit(new Rect(x, y, iw, D.LineBody), b.Name, D.FontValue, Pal.Txt, true);
            y += D.LineBody;
            string est = pct > PriceFree * 100f ? "비싸서 관객이 줄어요" : pct < -PriceFree * 100f ? "싸서 관객이 늘어요" : pct == 0 ? "기본값" : "관객 영향 적음";
            Draw.TextFit(new Rect(x, y, iw, D.LineSub), $"기본 {Price(baseP)} · {est}", D.FontMin, S.SubInk);
            float by = r.yMax - S.Pad - Button.H * 0.4f;
            float bw = Button.Width(null, "−", true);
            Button.At(x, by, null, "−", "내리기", Tone.Neutral, enabled: b.Price - PriceStep >= baseP * PriceMin, click: () => Booking.SetPrice(b, b.Price - PriceStep), small: true);
            string price = Price(b.Price);
            float pw = Mathf.Ceil(Draw.TextWidth(price, D.FontValue + D.Gap, true));
            Draw.TextFit(new Rect(x + bw + D.Gap * 2f, by - D.LineBody * 0.6f, pw, D.LineBody * 1.2f), price, D.FontValue + D.Gap, Pal.Txt, true, TextAnchor.MiddleCenter);
            Button.At(x + bw + pw + D.Gap * 4f, by, null, "+", "올리기", Tone.Neutral, enabled: b.Price + PriceStep <= baseP * PriceMax, click: () => Booking.SetPrice(b, b.Price + PriceStep), small: true);
            var stub = new Rect(r.xMax - C.StubW * 0.75f, r.y, C.StubW * 0.75f, r.height);
            Draw.TextFit(new Rect(stub.x, stub.center.y - D.LineSub - D.Gap, stub.width, D.LineSub), "기본 대비", D.FontMin, S.SubInk, false, TextAnchor.MiddleCenter);
            Draw.TextFit(new Rect(stub.x, stub.center.y, stub.width, D.LineBody), (pct > 0 ? "+" : "") + pct + "%", D.FontValue, pct == 0 ? Pal.Light : Look.PastelSubInk, true, TextAnchor.MiddleCenter);
        }
    }
}
