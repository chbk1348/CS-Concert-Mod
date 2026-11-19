using System;
using System.Collections.Generic;
using ColossalFramework;
using ConcertMod.Data;
using UnityEngine;
using C = CSCommonUX.Concert;
using D = CSCommonUX.CSCommonDesign;
using S = CSCommonUX.Section;

namespace ConcertMod.UI
{
    /// <summary>
    /// 예약 탭 · 공연장(캔버스 Main · venue): 위 숫자 칸(빌린 경기장 · 잡힌 경기장 공연 · 지난 경기장 공연) ·
    /// 경기장 카드(경기장 모양 포도알 좌석 · 다음 경기 · 빌리기 스위치 · 30일 띠 · 잡힌 공연). 경기 일정은 그대로, 다음 경기 준비 전 빈 틈에만 공연을 넣음.
    /// </summary>
    internal sealed partial class ConcertWindow
    {
        /// <summary>카드 좌석 점 최대 수 · 30일 띠 날 수.</summary>
        const int VenueDots = 300, StripDays = 30;

        // 경기장 모양: 축구(Match Day) = 양옆 스탠드 + 가운데, 대학 경기장 = 가운데만
        readonly Zone[] bowlL = { Z("A 스탠드", 0.15f, 10, 1, 0f, 0f) }, bowlR = { Z("B 스탠드", 0.15f, 10, 1, 0f, 0f) };
        readonly Zone[] bowlC = { Z("그라운드 스탠딩", 0.25f, 20, 0, 0f, 0f), Z("관중석 1층", 0.25f, 20, 2, 0f, 0f), Z("관중석 2층", 0.2f, 20, 3, 0f, 0f) };
        readonly Zone[] arenaC = { Z("코트 스탠딩", 0.3f, 24, 0, 0f, 0f), Z("1층", 0.4f, 24, 1, 0f, 0f), Z("2층", 0.3f, 24, 2, 0f, 0f) };

        readonly EventManager.FutureEvent[] future = new EventManager.FutureEvent[8];

        static void SizeZones(Zone[] zones, int n)
        {
            foreach (var z in zones) z.Count = Math.Max(z.Cols, Mathf.RoundToInt(n * z.Share));
        }

        float ZonesH(Zone[] zones, float step, float label, float gap)
        {
            float h = 0f;
            foreach (var z in zones) h += Mathf.Ceil(z.Count / (float)z.Cols) * step + label + gap;
            return h - gap;
        }

        static bool IsVarsity(ushort b) => Singleton<BuildingManager>.instance.m_buildings.m_buffer[b].Info?.m_buildingAI is VarsitySportsArenaAI;

        float VenueTab(float w)
        {
            var venues = Rental.Venues;
            int rented = 0, booked = 0;
            foreach (var v in venues)
            {
                if (Rental.IsRented(v)) rented++;
                if (Rental.ActiveAt(v) != null) booked++;
            }
            float gap = S.Gap, cw = (w - gap * 2f) / 3f, y = 0f;
            if (Shows.StadiumVenue) y = DefaultVenue(w) + gap;
            Cell(new Rect(0f, y, cw, CellH), "빌린 경기장", $"{rented}곳 · {venues.Count}곳 중");
            Cell(new Rect(cw + gap, y, cw, CellH), "잡힌 경기장 공연", booked + "회");
            Cell(new Rect((cw + gap) * 2f, y, cw, CellH), "지난 경기장 공연", Rental.LogSnapshot().Length + "회");
            y += CellH + gap;
            if (venues.Count == 0) return y + Note(y, w, "공연장", "Match Day 경기장이나 대학 경기장을 지으면 공연장으로 빌릴 수 있어요");
            float hw = (w - gap) * 0.5f;
            for (int i = 0; i < venues.Count; i += 2)
            {
                float mw = hw - S.Pad * 2f;
                float h = Mathf.Max(VenueCardH(venues[i], mw), i + 1 < venues.Count ? VenueCardH(venues[i + 1], mw) : 0f);
                VenueCard(new Rect(0f, y, hw, h), venues[i]);
                if (i + 1 < venues.Count) VenueCard(new Rect(hw + gap, y, hw, h), venues[i + 1]);
                y += h + gap;
            }
            Legend(y, w);
            return y + D.LineSub;
        }

        /// <summary>
        /// 기본 공연장 판(캔버스 VenueDefault): 페스티벌 구역이 없을 때 다음 공연 · 예매 · 의향서가 가는 경기장 —
        /// 둥근 아이콘 · 이름 + '기본 공연장' · 설명 두 줄 · 오른쪽 세 칸(다음 공연 · 예매 · 의향서).
        /// </summary>
        float DefaultVenue(float w)
        {
            float ic = Button.H + D.Gap * 2f, cellW = S.CodeW * 4f, lines = D.LineBody + D.Gap + D.LineSub * 2f + D.Gap;
            float h = S.Pad * 2f + Mathf.Max(ic, Mathf.Max(lines, CellH));
            var r = new Rect(0f, 0f, w, h);
            S.Frame(r);
            float x = r.x + S.Pad * 1.5f, cy = r.center.y;
            if (Draw.Repaint)
            {
                Draw.Round(new Rect(x, cy - ic * 0.5f, ic, ic), Draw.Alpha(Pal.Primary, 0.18f), ic * 0.5f);
                Draw.Icon("M3 10c0-2.8 4-5 9-5s9 2.2 9 5-4 5-9 5-9-2.2-9-5zM3 10v5c0 2.8 4 5 9 5s9-2.2 9-5v-5M9 9.5h6", new Rect(x + (ic - Button.Icon) * 0.5f, cy - Button.Icon * 0.5f, Button.Icon, Button.Icon), Pal.Primary);
            }
            x += ic + S.Pad;
            // 오른쪽 세 칸
            var next = Shows.Next.Count > 0 ? Shows.Next[0] : (NextShow?)null;
            var sale = Tickets.Current;
            string[,] route =
            {
                { "다음 공연", next.HasValue ? $"{next.Value.Band.Name} · {Day(next.Value.Start)}" : "빈 날이 나면 잡아요" },
                { "예매", sale == null ? "공연이 잡히면 열려요" : sale.Opened ? "예매 중" : $"{Day(sale.Open)} {sale.Open:HH:mm} 오픈" },
                { "의향서", "승인하면 빈 날에" },
            };
            float cx = r.xMax - S.Pad * 1.5f - cellW * 3f - D.Gap * 4f;
            for (int i = 0; i < 3; i++) Cell(new Rect(cx + (cellW + D.Gap * 2f) * i, cy - CellH * 0.5f, cellW, CellH), route[i, 0], route[i, 1]);
            // 이름 · 표시 · 설명
            float tw = cx - S.Pad - x, ty = cy - lines * 0.5f;
            string name = Shows.VenueName.Length > 0 ? Shows.VenueName : "경기장";
            float nw = Mathf.Min(tw - Badge.Pair(0f, 0f, "기본 공연장", Pal.Primary, Pal.OnPrimary, false) - D.Gap * 2f, Mathf.Ceil(Draw.TextWidth(name, D.FontBody, true)));
            Draw.TextFit(new Rect(x, ty, nw, D.LineBody), name, D.FontBody, Pal.Txt, true);
            Badge.Pair(x + nw + D.Gap * 2f, ty + D.LineBody * 0.5f, "기본 공연장", Pal.Primary, Draw.On(Pal.Primary));
            ty += D.LineBody + D.Gap;
            Draw.TextFit(new Rect(x, ty, tw, D.LineSub), "페스티벌 구역이 없어 다음 공연 · 예매 · 의향서가 이 경기장으로 가요 · 경기 사이 빈 날에 잡아요", D.FontMin, S.SubInk);
            Draw.TextFit(new Rect(x, ty + D.LineSub, tw, D.LineSub), "페스티벌 구역을 지으면 기본 공연장은 페스티벌 구역으로 돌아가요", D.FontMin, Pal.Dim);
            return h;
        }

        void Legend(float y, float w)
        {
            string[] names = { "경기 날", "공연 잡힘", "빈 날" };
            Color[] cols = { Pal.Light, Pal.ConcertSeatVip, Pal.Inset };
            float x = 0f;
            for (int i = 0; i < names.Length; i++)
            {
                if (Draw.Repaint) Draw.Round(new Rect(x, y + D.LineSub * 0.5f - D.Gap, D.Gap * 2f, D.Gap * 2f), cols[i], D.Gap * 0.5f);
                float tw = Mathf.Ceil(Draw.TextWidth(names[i], D.FontMin, false));
                Draw.TextFit(new Rect(x + D.Gap * 3f, y, tw + D.Gap, D.LineSub), names[i], D.FontMin, S.SubInk);
                x += D.Gap * 3f + tw + S.Pad * 1.5f;
            }
            Draw.TextFit(new Rect(x, y, w - x, D.LineSub), "경기 일정은 그대로 · 공연이 끝나면 바로 정리 · 경기장에 무대 장식은 없어요", D.FontMin, S.SubInk, false, TextAnchor.MiddleRight);
        }

        int VenueSeats(ushort b, out int per)
        {
            int cap = Math.Max(1, Tickets.SessionCap(b));
            per = Math.Max(1, Mathf.CeilToInt(cap / (float)VenueDots));
            int n = Math.Max(1, Mathf.RoundToInt(cap / (float)per));
            if (IsVarsity(b)) SizeZones(arenaC, n);
            else
            {
                SizeZones(bowlC, n);
                SizeZones(bowlL, n);
                SizeZones(bowlR, n);
            }
            return cap;
        }

        /// <summary>경기장 모양 좌석 간격: 카드 칸 폭에 다 들어가게 줄임(넓으면 예매 탭과 같은 크기).</summary>
        float VenueStep(ushort b, float mapW)
        {
            float step = C.SeatDot + C.SeatGap, avail = mapW - S.Pad * 2f;
            float cols = IsVarsity(b) ? arenaC[0].Cols : bowlC[0].Cols + bowlL[0].Cols + bowlR[0].Cols, gaps = IsVarsity(b) ? 0f : S.Pad * 3f;
            return Mathf.Min(step, (avail - gaps) / cols);
        }

        float VenueMapH(ushort b, float mapW)
        {
            VenueSeats(b, out _);
            float step = VenueStep(b, mapW), label = D.LineSub + D.Gap, zg = S.Pad;
            float zones = IsVarsity(b) ? ZonesH(arenaC, step, label, zg) : Mathf.Max(ZonesH(bowlC, step, label, zg), ZonesH(bowlL, step, label, zg));
            return S.Pad + D.LineBody * 1.5f + S.Pad + zones + S.Pad + D.LineSub + S.Pad;
        }

        float VenueCardH(ushort b, float mapW) => PlateTop + VenueMapH(b, mapW) + S.Gap + S.RowH + D.Gap * 3f + C.BarH * 2f + D.LineSub + D.Gap * 3f + S.RowH + S.PadBottom;

        void VenueCard(Rect r, ushort b)
        {
            var bm = Singleton<BuildingManager>.instance;
            var em = Singleton<EventManager>.instance;
            var sim = SimulationManager.instance;
            bool varsity = IsVarsity(b);
            var body = Plate(r, bm.GetBuildingName(b, InstanceID.Empty) ?? "경기장", "M3 20h18M5 20V9l7-5 7 5v11M9 20v-6h6v6", varsity ? "대학 경기장" : "축구 경기장 · Match Day");
            // 경기장 모양 포도알 좌석
            int cap = VenueSeats(b, out int per);
            var map = new Rect(body.x, body.y, body.width, VenueMapH(b, body.width));
            if (Draw.Repaint) Draw.Round(map, Pal.ConcertScene, S.Radius - S.Pad * 0.5f);
            var stage = new Rect(map.center.x - S.CodeW * 3f, map.y + S.Pad, S.CodeW * 6f, D.LineBody * 1.5f);
            if (Draw.Repaint) Draw.Round(stage, Pal.ConcertTruss, Board.BoxRadius);
            Draw.TextFit(stage, "무대", D.FontMin, Pal.Light, true, TextAnchor.MiddleCenter);
            float step = VenueStep(b, map.width), dot = step * C.SeatDot / (C.SeatDot + C.SeatGap), label = D.LineSub + D.Gap, top = stage.yMax + S.Pad;
            if (varsity)
            {
                float wc = arenaC[0].Cols * step;
                DrawZones(arenaC, map.center.x - wc * 0.5f, top, wc, step, label, S.Pad, 0, per, dot);
            }
            else
            {
                float wc = bowlC[0].Cols * step, ws = bowlL[0].Cols * step, gx = S.Pad * 1.5f, xc = map.center.x - wc * 0.5f;
                DrawZones(bowlC, xc, top, wc, step, label, S.Pad, 0, per, dot);
                DrawZones(bowlL, xc - gx - ws, top, ws, step, label, S.Pad, 0, per, dot);
                DrawZones(bowlR, xc + wc + gx, top, ws, step, label, S.Pad, 0, per, dot);
            }
            Draw.TextFit(new Rect(map.x, map.yMax - S.Pad - D.LineSub, map.width, D.LineSub), $"공연 정원 {cap:#,0}명 · 점 하나 = {per}명", D.FontMin, S.SubInk, false, TextAnchor.MiddleCenter);

            // 다음 경기 · 빌리기
            float y = map.yMax + S.Gap;
            DateTime? nextMatch = null;
            var days = new List<KeyValuePair<DateTime, bool>>();
            ushort head = bm.m_buildings.m_buffer[b].m_eventIndex;
            if (head != 0 && (em.m_events.m_buffer[head].m_flags & (EventData.Flags.Completed | EventData.Flags.Cancelled | EventData.Flags.Deleted)) == 0)
            {
                bool show = em.m_events.m_buffer[head].Info?.m_eventAI is ConcertAI;
                var t = em.m_events.m_buffer[head].StartTime;
                days.Add(new KeyValuePair<DateTime, bool>(t, show));
                if (!show) nextMatch = t;
            }
            int n = em.GetFutureEvents(b, future, future.Length);
            for (int i = 0; i < n; i++)
            {
                bool show = future[i].m_info?.m_eventAI is ConcertAI;
                days.Add(new KeyValuePair<DateTime, bool>(future[i].m_startTime, show));
                if (!show && nextMatch == null) nextMatch = future[i].m_startTime;
            }
            bool on = Rental.IsRented(b);
            var sw = new Rect(body.xMax - D.SwitchW, y + (S.RowH - D.SwitchH) * 0.5f, D.SwitchW, D.SwitchH);
            float lw = Mathf.Ceil(Draw.TextWidth("빌리기", D.FontDesc, false));
            float lx = sw.x - D.Gap * 2f - lw;
            Draw.TextFit(new Rect(lx, y, lw, S.RowH), "빌리기", D.FontDesc, S.SubInk);
            if (Switch.In(sw, on)) Rental.SetRented(b, !on);
            // 페스티벌 구역이 없을 때: 기본 공연장 표시 · 빌린 다른 경기장엔 '기본으로'
            if (Shows.Festival == 0 && b == Shows.Venue)
            {
                float bw = Badge.Pair(0f, 0f, "기본 공연장", Pal.Primary, Pal.OnPrimary, false);
                lx -= D.Gap * 3f + bw;
                Badge.Pair(lx, y + S.RowH * 0.5f, "기본 공연장", Pal.Primary, Draw.On(Pal.Primary));
            }
            else if (Shows.Festival == 0 && on)
            {
                float bw = Button.At(0f, 0f, null, "기본으로", null, Tone.Neutral, draw: false, small: true);
                lx -= D.Gap * 3f + bw;
                Button.At(lx, y + S.RowH * 0.5f, null, "기본으로", "다음 공연 · 예매 · 의향서를 이 경기장으로 보내요", Tone.Neutral, small: true, click: () => Rental.SetDefault(b));
            }
            Draw.TextFit(new Rect(body.x, y, lx - D.Gap * 2f - body.x, S.RowH), nextMatch != null ? "다음 경기 " + When(nextMatch.Value) : "잡힌 경기가 없어요", D.FontBody, Pal.Txt, true);
            y += S.RowH + D.Gap * 3f;

            // 30일 띠: 경기 날 · 공연 날 · 빈 날
            var today = sim.m_currentGameTime.Date;
            float cg = 2f, cwid = (body.width - cg * (StripDays - 1)) / StripDays;
            var rent = Rental.ActiveAt(b);
            Color showCol = rent != null ? Shows.BandOf(rent.Band)?.Color ?? Pal.ConcertSeatVip : Pal.ConcertSeatVip;
            for (int d = 0; d < StripDays; d++)
            {
                var day = today.AddDays(d);
                Color c = Pal.Inset;
                foreach (var kv in days) if (kv.Key.Date == day) c = kv.Value ? showCol : Pal.Light;
                if (rent != null && sim.FrameToTime(rent.Start).Date == day) c = showCol;
                if (Draw.Repaint) Draw.Round(new Rect(body.x + (cwid + cg) * d, y, cwid, C.BarH * 2f), c, 2f);
            }
            y += C.BarH * 2f + D.Gap;
            Draw.TextFit(new Rect(body.x, y, body.width, D.LineSub), Day(today), D.FontMin, S.SubInk);
            Draw.TextFit(new Rect(body.x, y, body.width, D.LineSub), Day(today.AddDays(StripDays - 1)), D.FontMin, S.SubInk, false, TextAnchor.MiddleRight);
            y += D.LineSub + D.Gap * 3f;

            // 잡힌 공연 · 안내
            string text;
            Color ink = S.SubInk;
            if (rent != null)
            {
                text = $"{Artists.Of(rent.Band)} · {When(sim.FrameToTime(rent.Start))} 공연" + (rent.State == 0 ? " · 자리 잡는 중" : " 잡힘");
                ink = Pal.Txt;
            }
            else if (on) text = Rental.WaitReason(b) ?? "다음 경기 전 틈이 생기면 인기 1위 밴드로 공연을 잡아요";
            else
            {
                text = "빌리기를 켜면 다음 경기 전 빈 틈에 공연을 잡아요";
                foreach (var l in Rental.LogSnapshot())
                    if (l.Stadium == b) { text = $"지난 공연 · {Artists.Of(l.Band)} · {Day(sim.FrameToTime(l.Start))}"; break; }
            }
            var line = new Rect(body.x, y, body.width, S.RowH);
            if (Draw.Repaint) Draw.Round(line, Pal.Inset, Board.BoxRadius * 2f);
            if (rent != null && Draw.Repaint) Draw.Dot(new Vector2(line.x + S.Pad + D.Gap, line.center.y), D.Gap, showCol);
            float tx = line.x + S.Pad + (rent != null ? D.Gap * 4f : 0f);
            Draw.TextFit(new Rect(tx, line.y, line.xMax - S.Pad - tx, line.height), text, D.FontDesc, ink, rent != null);
        }
    }
}
