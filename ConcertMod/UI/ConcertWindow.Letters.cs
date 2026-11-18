using System;
using ConcertMod.Data;
using UnityEngine;
using C = CSCommonUX.Concert;
using D = CSCommonUX.CSCommonDesign;
using S = CSCommonUX.Section;

namespace ConcertMod.UI
{
    /// <summary>
    /// 예약 탭 · 의향서(캔버스 Main · letters): 왼쪽 받은 의향서 목록(밴드 · 기획사 · 바라는 일정 · 도착 · 상태) ·
    /// 오른쪽 고른 의향서 문서(기획사마다 다른 모양 · 인장) · 승인하면 잡힐 날짜 · 거절 / 보류 / 승인하고 날짜 잡기.
    /// </summary>
    internal sealed partial class ConcertWindow
    {
        const string MailIcon = "M3 6h18v12H3zM3 7l9 6 9-6", DocIcon = "M6 3h9l4 4v14H6zM14 3v5h5M9 13h7M9 17h5";

        int pickLetter = -1;

        float LettersTab(float w)
        {
            if (Shows.Festival == 0) return Note(0f, w, "의향서", "페스티벌 구역을 지으면 아티스트 의향서가 와요");
            var all = Letters.All;
            Letter sel = null;
            foreach (var l in all) if (l.Id == pickLetter) sel = l;
            if (sel == null && all.Count > 0) { sel = all[0]; pickLetter = sel.Id; }
            float gap = S.Gap, left = Mathf.Round((w - gap) * 5f / 12f), right = w - gap - left;
            float listH = PlateTop + Mathf.Max(1, all.Count) * (C.LetterRowH + D.Gap) + S.PadBottom;
            float docW = Mathf.Min(C.LetterW, right - S.Pad * 2f), docH = Mathf.Round(docW * C.LetterH / C.LetterW);
            float rightH = sel == null ? listH : PlateTop + S.Pad + docH + S.Pad * 2f + CellH + D.LineSub + D.Gap * 4f + S.Gap + Button.H + S.PadBottom;
            float h = Mathf.Max(listH, rightH);
            if (sel == null)
            {
                ListPlate(new Rect(0f, 0f, w, listH));
                return listH;
            }
            ListPlate(new Rect(0f, 0f, left, h));
            DocPlate(new Rect(left + gap, 0f, right, h), sel, docW, docH);
            return h;
        }

        void ListPlate(Rect r)
        {
            var body = Plate(r, "받은 의향서", MailIcon, $"검토 기다림 {Letters.Waiting} · {Letters.WaitDays}일 지나면 철회");
            if (Letters.All.Count == 0)
            {
                Draw.TextFit(new Rect(body.x, body.y, body.width, S.RowH), "아직 받은 의향서가 없어요 · 며칠마다 와요", D.FontBody, S.SubInk);
                return;
            }
            var now = SimulationManager.instance.m_currentGameTime;
            float y = body.y;
            foreach (var l in Letters.All)
            {
                var row = new Rect(body.x, y, body.width, C.LetterRowH);
                bool on = l.Id == pickLetter;
                if (Draw.Repaint)
                {
                    if (on)
                    {
                        Draw.Round(row, Pal.Inset, Board.BoxRadius * 2f);
                        Draw.RoundOutline(row, Pal.Line, Board.BoxRadius * 2f);
                    }
                    else Draw.Fill(new Rect(row.x, row.yMax, row.width, 1f), Pal.Line);
                }
                var band = Shows.BandOf(l.Band);
                string name = band?.Name ?? Artists.Of(l.Band);
                var tile = new Rect(row.x + S.Pad, row.center.y - C.Tile * 0.55f, C.Tile * 1.1f, C.Tile * 1.1f);
                Tile(tile, band?.Color ?? Pal.Primary, name);
                float tx = tile.xMax + S.Pad, rw = S.CodeW * 3.2f, tw = row.xMax - S.Pad - rw - tx;
                float nw = Mathf.Min(tw, Mathf.Ceil(Draw.TextWidth(name, D.FontBody, true)));
                Draw.TextFit(new Rect(tx, row.center.y - D.LineBody, nw, D.LineBody), name, D.FontBody, Pal.Txt, true);
                string tag = l.Site >= 0 ? Tickets.Sites[l.Site] + " 단독" : l.State == LetterState.Waiting && (now - l.Arrived).TotalDays < 1 ? "새로 옴" : null;
                if (tag != null) Badge.Pair(tx + nw + D.Gap * 2f, row.center.y - D.LineBody * 0.5f, tag, l.Site >= 0 ? Pal.ConcertSeatR : Look.PastelFill, l.Site >= 0 ? Draw.On(Pal.ConcertSeatR) : Look.PastelInk);
                Draw.TextFit(new Rect(tx, row.center.y + D.Gap, tw, D.LineSub), $"{Letters.FromOf(l)} · {l.Days}일 · {l.Hour:00}:00 · {Price(l.Price)}", D.FontMin, S.SubInk);
                var rr = new Rect(row.xMax - S.Pad - rw, row.y, rw, row.height);
                Draw.TextFit(new Rect(rr.x, row.center.y - D.LineBody, rr.width, D.LineBody), Day(l.Arrived), D.FontMin, Pal.Light, false, TextAnchor.MiddleRight);
                StateText(l, now, out string st, out Color sc);
                Draw.TextFit(new Rect(rr.x, row.center.y + D.Gap, rr.width, D.LineSub), st, D.FontDesc, sc, true, TextAnchor.MiddleRight);
                if (Draw.Button(row)) pickLetter = l.Id;
                y += C.LetterRowH + D.Gap;
            }
        }

        static void StateText(Letter l, DateTime now, out string text, out Color ink)
        {
            int left = Math.Max(0, (int)Math.Ceiling((l.Deadline - now).TotalDays));
            switch (l.State)
            {
                case LetterState.Waiting:
                    text = left <= 3 ? $"{left}일 뒤 철회" : l.Held ? "보류 중" : "검토 중";
                    ink = left <= 3 ? Pal.ConcertSeatA : Look.PastelInk;
                    break;
                case LetterState.Approved: text = "승인 · 빈 칸 기다림"; ink = Look.PastelInk; break;
                case LetterState.Booked: text = $"{Day(l.Booked)} 잡힘"; ink = Pal.ConcertUp; break;
                case LetterState.Rejected: text = "거절"; ink = S.SubInk; break;
                default: text = "철회"; ink = S.SubInk; break;
            }
        }

        void DocPlate(Rect r, Letter l, float docW, float docH)
        {
            var body = Plate(r, "공연개최의향서", DocIcon, $"{Day(l.Arrived)} 도착 · {Letters.FromOf(l)}");
            var doc = new Rect(Mathf.Round(body.center.x - docW * 0.5f), body.y + S.Pad, docW, docH);
            if (Draw.Repaint) Draw.Shadow(doc, S.Pad, 0.45f);
            C.Letter(doc, Letters.Doc(l));
            float y = doc.yMax + S.Pad * 2f;
            var cell = new Rect(body.x, y, body.width, CellH + D.LineSub + D.Gap * 4f);
            if (Draw.Repaint) Draw.Round(cell, Pal.Inset, Board.BoxRadius * 2f);
            float cx = cell.x + S.Pad, cw = cell.width - S.Pad * 2f, cy = cell.y + D.Gap * 3f;
            var now = SimulationManager.instance.m_currentGameTime;
            string head, value, note, side;
            if (l.State == LetterState.Booked) { head = "잡은 날"; value = $"{Day(l.Booked)}에 다음 공연으로 잡았어요"; side = ""; note = "예약 · 다음 공연에서 회차를 볼 수 있어요"; }
            else if (l.State == LetterState.Rejected || l.State == LetterState.Withdrawn) { head = "결과"; value = l.State == LetterState.Rejected ? "거절했어요" : "답이 없어 철회했어요"; side = ""; note = "새 의향서는 며칠마다 와요"; }
            else if (Booking.Current != null || l.State == LetterState.Approved)
            {
                head = "승인하면 잡을 날짜";
                value = "지금 잡힌 공연이 끝나면 이어서";
                side = "다음 빈 공연 칸";
                note = $"{l.Days}일 · 매일 {l.Hour:00}:00 · 입장권 {Price(l.Price)}로 맞춰요";
            }
            else
            {
                var day = Shows.Next.Count > 0 ? Shows.Next[0].Start.Date : now.Date.AddDays(1);
                var start = day.AddHours(l.Hour);
                if (start < now) start = start.AddDays(1);
                var open = start.Date.AddDays(-Tickets.OpenDays).AddHours(Tickets.OpenHour);
                head = "승인하면 자동으로 잡을 날짜";
                value = $"{When(start)} · {(Shows.FestivalName.Length > 0 ? Shows.FestivalName : "페스티벌 구역")}";
                side = "다음 빈 공연 칸";
                note = $"{l.Days}일 공연 · 예매 오픈 {(open > now ? When(open) : "바로")} · 입장권 {Price(l.Price)}로 맞춰요";
            }
            Draw.TextFit(new Rect(cx, cy, cw, D.LineSub), head, D.FontMin, S.SubInk);
            cy += D.LineSub + D.Gap;
            Draw.TextFit(new Rect(cx, cy, cw, D.LineBody + D.Gap), value, D.FontValue, Pal.Txt, true);
            if (side.Length > 0) Draw.TextFit(new Rect(cx, cy, cw, D.LineBody + D.Gap), side, D.FontMin, S.SubInk, false, TextAnchor.MiddleRight);
            cy += D.LineBody + D.Gap * 2f;
            Draw.TextFit(new Rect(cx, cy, cw, D.LineSub), note, D.FontMin, S.SubInk);
            y = cell.yMax + S.Gap;
            if (l.State != LetterState.Waiting) return;
            float okW = Button.Width(null, "승인하고 날짜 잡기"), holdW = Button.Width(null, "보류"), noW = Button.Width(null, "거절");
            float bx = r.xMax - S.Pad - okW;
            Button.In(new Rect(bx, y, okW, Button.H), null, "승인하고 날짜 잡기", "바라는 입장권으로 맞추고 다음 빈 공연 칸에 잡아요", Tone.Primary, click: () => Letters.Approve(l));
            bx -= D.Gap * 2f + holdW;
            Button.In(new Rect(bx, y, holdW, Button.H), null, "보류", $"답을 {Letters.HoldDays}일 미뤄요 · 한 번만", Tone.Neutral, enabled: !l.Held, click: () => Letters.Hold(l));
            bx -= D.Gap * 2f + noW;
            Button.In(new Rect(bx, y, noW, Button.H), null, "거절", "이 의향서를 거절해요", Tone.Danger, click: () => Letters.Reject(l));
        }
    }
}
