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
    /// 예매 탭(캔버스 Ticket · TicketPre · TicketQueue): 오픈 전 = 가상 예매 사이트 상품 화면(포스터 · 공연 정보 · 카운트다운),
    /// 오픈 직후 몰리면 = 사이트 접속 대기 화면, 그 뒤 = 포도알 좌석(창 너비 가득 · 앞자리부터 팔림) · 예매처 · 방금 예매(실제 주민) · 다음 오픈.
    /// </summary>
    internal sealed partial class ConcertWindow
    {
        /// <summary>좌석 점 최대 수.</summary>
        const int SeatDotsMax = 1600;

        /// <summary>구역: 이름 · 정원 비율 · 열 수 · 등급(0 VIP · 1 R · 2 S · 3 일반) · 팔리기 시작(작을수록 먼저) · 퍼짐.</summary>
        sealed class Zone
        {
            public string Name;
            public float Share, Start, Spread;
            public int Cols, Grade, Count, First;
        }

        static Zone Z(string n, float share, int cols, int grade, float start, float spread) => new() { Name = n, Share = share, Cols = cols, Grade = grade, Start = start, Spread = spread };

        // 캔버스 예매 좌석: 왼쪽 날개 A · C, 가운데 스탠딩 VIP · R석 1층 · S석 2층 · 일반석 3층, 오른쪽 날개 B · D
        readonly Zone[] zonesL = { Z("A 구역", 0.09f, 16, 1, 2f, 16f), Z("C 구역", 0.09f, 16, 2, 10f, 26f) };
        readonly Zone[] zonesC = { Z("스탠딩 VIP", 0.08f, 32, 0, 0f, 3f), Z("R석 1층", 0.18f, 32, 1, 1f, 14f), Z("S석 2층", 0.18f, 32, 2, 8f, 24f), Z("일반석 3층", 0.2f, 36, 3, 16f, 34f) };
        readonly Zone[] zonesR = { Z("B 구역", 0.09f, 16, 1, 2f, 16f), Z("D 구역", 0.09f, 16, 2, 10f, 26f) };

        int seatTotal = -1, seatPer = 1;
        int[] seatRank;

        /// <summary>정원이 바뀌면 점 수 · 팔리는 차례를 다시: 차례 = 구역 시작 + 앞줄일수록 빠름 + 무작위(흩어져 사라짐).</summary>
        void BuildSeats(int cap)
        {
            seatPer = Math.Max(1, Mathf.CeilToInt(cap / (float)SeatDotsMax));
            int n = Math.Max(1, Mathf.RoundToInt(cap / (float)seatPer));
            if (n == seatTotal) return;
            seatTotal = 0;
            var order = new List<KeyValuePair<float, int>>();
            var rng = new ColossalFramework.Math.Randomizer(7u);
            foreach (var z in Zones())
            {
                z.Count = Math.Max(z.Cols, Mathf.RoundToInt(n * z.Share));
                z.First = seatTotal;
                int rows = Mathf.CeilToInt(z.Count / (float)z.Cols);
                for (int k = 0; k < z.Count; k++)
                {
                    float front = (k / z.Cols) / (float)Math.Max(1, rows - 1);
                    float at = z.Start + z.Spread * (0.35f * front + 0.65f * rng.Int32(1000u) / 1000f) * (0.4f + front);
                    order.Add(new KeyValuePair<float, int>(at, seatTotal + k));
                }
                seatTotal += z.Count;
            }
            order.Sort((a, b) => a.Key.CompareTo(b.Key));
            seatRank = new int[seatTotal];
            for (int i = 0; i < order.Count; i++) seatRank[order[i].Value] = i;
            seatTotal = n;
        }

        IEnumerable<Zone> Zones()
        {
            foreach (var z in zonesL) yield return z;
            foreach (var z in zonesC) yield return z;
            foreach (var z in zonesR) yield return z;
        }

        float TicketTab(float w)
        {
            var s = Tickets.Current;
            if (s == null) return Note(0f, w, "예매", Shows.Venue == 0 ? "페스티벌 구역이나 경기장(Match Day · 대학)을 지으면 공연 예매가 열려요" : "예매할 다음 공연이 없어요");
            if (!ConcertSetting.Current.Ticketing) return Note(0f, w, "예매", "예매를 껐어요 · 공연은 게임대로 관객을 받아요");
            if (!s.Opened) return SitePre(w, s);
            int queue = Tickets.Queue(s);
            if (queue > 0) return SiteQueue(w, s, queue);
            return Selling(w, s);
        }

        // ---- 예매 중: 포도알 좌석

        float Selling(float w, Sale s)
        {
            BuildSeats(s.Cap);
            float step = C.SeatDot + C.SeatGap, zoneGap = S.Pad * 2f, label = D.LineSub + D.Gap;
            float ColH(Zone[] zs) { float h = 0f; foreach (var z in zs) h += Mathf.Ceil(z.Count / (float)z.Cols) * step + label + zoneGap; return h - zoneGap; }
            float mapH = S.Pad * 2f + D.LineBody * 2f + S.Pad + Mathf.Max(ColH(zonesC), Mathf.Max(ColH(zonesL), ColH(zonesR)) + S.Pad * 2.5f) + S.Pad + D.LineSub;
            float plateH = PlateTop + mapH + S.Gap + D.LineBody + D.Gap * 3f + C.BarH + S.PadBottom;
            var now = SimulationManager.instance.m_currentGameTime;
            S.Frame(new Rect(0f, 0f, w, plateH));
            S.Title(new Rect(S.Pad, S.Pad, w - S.Pad * 2f, S.ChipH), s.SoldOut ? "매진" : "예매 중", $"{Day(s.Open)} 오픈 · 공연까지 {Math.Max(0, (int)(s.Start.Date - now.Date).TotalDays)}일", TicketIcon);
            var map = new Rect(S.Pad, PlateTop, w - S.Pad * 2f, mapH);
            if (Draw.Repaint) Draw.Round(map, Pal.ConcertScene, S.Radius - S.Pad * 0.5f);
            string head = $"{s.Band.Name} · {When(s.Start)}{(s.Days > 1 ? $" · {s.Days}회차" : "")}";
            float hw = Badge.Pair(0f, 0f, head, s.Band.Color, Pal.Txt, false);
            Badge.Pair(map.center.x - hw * 0.5f, map.y + S.Pad + Badge.H * 0.5f, head, s.Band.Color, Draw.On(s.Band.Color));
            var stage = new Rect(map.center.x - S.CodeW * 4.5f, map.y + S.Pad + Badge.H + S.Pad, S.CodeW * 9f, D.LineBody * 2f);
            if (Draw.Repaint) Draw.Round(stage, Pal.ConcertTruss, Board.BoxRadius);
            Draw.TextFit(stage, "무대", D.FontBody, Pal.Light, true, TextAnchor.MiddleCenter);
            int soldDots = Mathf.RoundToInt(s.Fill * seatTotal);
            float top = stage.yMax + S.Pad;
            float wC = zonesC[3].Cols * step, wL = zonesL[0].Cols * step, gapX = S.Pad * 3f;
            float xC = map.center.x - wC * 0.5f;
            DrawZones(zonesC, xC, top, wC, step, label, zoneGap, soldDots, seatPer);
            DrawZones(zonesL, xC - gapX - wL, top + S.Pad * 2.5f, wL, step, label, zoneGap, soldDots, seatPer);
            DrawZones(zonesR, xC + wC + gapX, top + S.Pad * 2.5f, wL, step, label, zoneGap, soldDots, seatPer);
            Draw.TextFit(new Rect(map.x, map.yMax - S.Pad - D.LineSub, map.width, D.LineSub), $"정원 {s.Cap:#,0}석{(s.Days > 1 ? $" ({s.Days}회차 합계)" : "")} · 점 하나 = {seatPer}석", D.FontMin, S.SubInk, false, TextAnchor.MiddleCenter);
            float y = map.yMax + S.Gap;
            string big = s.Sold.ToString("#,0");
            float bigW = Mathf.Ceil(Draw.TextWidth(big, C.BigFont, true));
            Draw.TextFit(new Rect(S.Pad, y, bigW, D.LineBody + D.Gap), big, C.BigFont, Pal.Txt, true);
            Draw.TextFit(new Rect(S.Pad + bigW + D.Gap * 2f, y, w * 0.5f, D.LineBody + D.Gap), $"매 팔림 · 남은 자리 {Math.Max(0, s.Cap - s.Sold):#,0} · 정원 {s.Cap:#,0}", D.FontMin, S.SubInk);
            double left = Tickets.HoursToSellOut(s);
            string tail = s.SoldOut ? "매진" : left < 0 ? "다 팔리지 않을 수 있어요" : left < 1 ? "곧 매진" : $"매진까지 약 {Math.Ceiling(left)}시간";
            Draw.TextFit(new Rect(S.Pad, y, w - S.Pad * 2f, D.LineBody + D.Gap), $"{Mathf.RoundToInt(s.Fill * 100f)}% · {tail}", D.FontValue, Look.PastelSubInk, true, TextAnchor.MiddleRight);
            y += D.LineBody + D.Gap * 3f;
            if (Draw.Repaint)
            {
                var bar = new Rect(S.Pad, y, w - S.Pad * 2f, C.BarH);
                Draw.Round(bar, Pal.ConcertSold, C.BarH * 0.5f);
                if (s.Fill > 0f) Draw.Round(new Rect(bar.x, bar.y, bar.width * s.Fill, bar.height), Pal.ConcertSeatR, C.BarH * 0.5f);
            }
            float y2 = plateH + S.Gap, gap = S.Gap, left5 = Mathf.Round((w - gap) * 5f / 12f);
            float rowH = PlateTop + Tickets.Sites.Length * S.RowH * 1.3f + S.PadBottom;
            float feedH = PlateTop + D.LineSub + 6 * D.LineBody * 2.4f + S.PadBottom;
            float row2 = Mathf.Max(rowH, feedH);
            SitesPlate(new Rect(0f, y2, left5, row2), s);
            FeedPlate(new Rect(left5 + gap, y2, w - gap - left5, row2), s);
            return y2 + row2;
        }

        void DrawZones(Zone[] zones, float x, float y, float width, float step, float label, float gap, int soldDots, int per, float dot = C.SeatDot)
        {
            foreach (var z in zones)
            {
                int rows = Mathf.CeilToInt(z.Count / (float)z.Cols), left = 0;
                float zx = x + (width - z.Cols * step) * 0.5f;
                for (int k = 0; k < z.Count; k++)
                {
                    bool sold = seatRank != null && z.First + k < seatRank.Length && seatRank[z.First + k] < soldDots;
                    if (!sold) left++;
                    if (Draw.Repaint) Draw.Dot(new Vector2(zx + (k % z.Cols) * step + dot * 0.5f, y + (k / z.Cols) * step + dot * 0.5f), dot * 0.5f, sold ? Pal.ConcertSold : C.Seat(z.Grade));
                }
                y += rows * step;
                Draw.TextFit(new Rect(x - S.Pad, y, width + S.Pad * 2f, label), left == 0 ? z.Name + " · 매진" : $"{z.Name} · 남은 {left * per:#,0}", D.FontMin, left == 0 ? S.SubInk : Pal.Light, false, TextAnchor.MiddleCenter);
                y += label + gap;
            }
        }

        void SitesPlate(Rect r, Sale s)
        {
            var body = Plate(r, "예매처", "M3 5h18v14H3zM3 9h18M7 13h4", "주민이 고른 사이트");
            int top = 0;
            for (int i = 0; i < Tickets.Sites.Length; i++) top = Math.Max(top, Tickets.SiteSold(s, i));
            float rh = S.RowH * 1.3f;
            for (int i = 0; i < Tickets.Sites.Length; i++)
            {
                var row = new Rect(body.x, body.y + rh * i, body.width, rh);
                var ic = new Rect(row.x, row.center.y - C.Tile * 0.45f, C.Tile * 0.9f, C.Tile * 0.9f);
                if (Draw.Repaint) Draw.Round(ic, Pal.Inset, Board.BoxRadius * 1.25f);
                Draw.TextFit(ic, Tickets.Sites[i].Substring(Tickets.Sites[i].Length - 1), D.FontDesc, Look.PastelSubInk, true, TextAnchor.MiddleCenter);
                float tx = ic.xMax + S.Pad, tw = row.xMax - tx;
                int n = Tickets.SiteSold(s, i);
                Draw.TextFit(new Rect(tx, row.y + D.Gap * 2f, tw, D.LineBody), Tickets.Sites[i], D.FontBody, Pal.Txt, true);
                Draw.TextFit(new Rect(tx, row.y + D.Gap * 2f, tw, D.LineBody), n.ToString("#,0") + "매", D.FontBody, Pal.Txt, true, TextAnchor.MiddleRight);
                var bar = new Rect(tx, row.y + D.Gap * 3f + D.LineBody, tw, Board.BoxLine * 2.5f);
                if (Draw.Repaint)
                {
                    Draw.Round(bar, Pal.ConcertSold, Board.BoxLine);
                    if (top > 0) Draw.Round(new Rect(bar.x, bar.y, bar.width * n / top, bar.height), i == 0 ? Pal.ConcertSeatR : Pal.Line2, Board.BoxLine);
                }
            }
        }

        void FeedPlate(Rect r, Sale s)
        {
            var body = Plate(r, "방금 예매", ListIcon, "이 도시 주민 · 관광객");
            if (Tickets.Feed.Count == 0)
            {
                Draw.TextFit(new Rect(body.x, body.y, body.width, S.RowH), s.SoldOut ? "매진됐어요" : "예매가 들어오면 여기에 보여요", D.FontBody, S.SubInk);
                return;
            }
            string[] grades = { "스탠딩 VIP", "R석", "S석", "일반석" };
            float rh = D.LineBody * 2.4f, y = body.y + D.LineSub;
            float tW = S.CodeW * 1.5f, siteW = S.CodeW * 3f, gradeW = S.CodeW * 2.6f, nW = S.CodeW * 1.4f;
            foreach (var b in Tickets.Feed)
            {
                var row = new Rect(body.x, y, body.width, rh);
                S.Hairline(new Rect(row.x, row.yMax - 1f, row.width, 1f));
                Draw.TextFit(new Rect(row.x, row.y, tW, rh), $"{b.At:HH:mm}", D.FontMin, Pal.Light);
                float nameW = row.width - tW - siteW - gradeW - nW;
                Draw.TextFit(new Rect(row.x + tW, row.y, nameW, rh), b.Name + (b.Tourist ? " · 관광객" : ""), D.FontBody, Pal.Txt);
                Draw.TextFit(new Rect(row.xMax - siteW - gradeW - nW, row.y, siteW, rh), Tickets.Sites[b.Site], D.FontMin, S.SubInk);
                Draw.TextFit(new Rect(row.xMax - gradeW - nW, row.y, gradeW, rh), grades[b.Grade], D.FontMin, S.SubInk);
                Draw.TextFit(new Rect(row.xMax - nW, row.y, nW, rh), b.Count + "매", D.FontDesc, Pal.Txt, true, TextAnchor.MiddleRight);
                y += rh;
            }
        }

        // ---- 가상 예매 사이트(오픈 전 · 대기줄)

        /// <summary>예매 화면 사이트: 단독 예매처가 있으면 그곳, 아니면 가장 큰 곳.</summary>
        static string SiteOf(Sale s) => Tickets.Sites[Math.Max(0, s.Site)];

        static string City => SimulationManager.instance.m_metaData?.m_CityName ?? "";

        /// <summary>브라우저 틀: 머리(점 셋 · 주소) + 사이트 머리(로고 · 메뉴 · 찾기 · 로그인). 사이트 몸 칸을 돌려줌.</summary>
        static Rect Browser(Rect r, string site, string address)
        {
            if (Draw.Repaint)
            {
                Draw.Round(r, Pal.SiteBg, S.Radius);
                Draw.Round(new Rect(r.x, r.y, r.width, C.SiteChromeH + S.Radius), Pal.SiteChrome, S.Radius);
                Draw.Fill(new Rect(r.x, r.y + C.SiteChromeH, r.width, S.Radius), Pal.SiteBg);
                Color[] dots = { Pal.ConcertDown, Pal.ConcertSeatA, Pal.ConcertUp };
                for (int i = 0; i < 3; i++) Draw.Dot(new Vector2(r.x + S.Pad + D.Gap * 1.25f + i * D.Gap * 4f, r.y + C.SiteChromeH * 0.5f), D.Gap * 1.25f, dots[i]);
                var addr = new Rect(r.x + S.Pad * 5f, r.y + D.Gap * 2f, r.width - S.Pad * 6f, C.SiteChromeH - D.Gap * 4f);
                Draw.Round(addr, Pal.SiteAddress, addr.height * 0.5f);
            }
            Draw.TextFit(new Rect(r.x + S.Pad * 6f, r.y, r.width - S.Pad * 8f, C.SiteChromeH), address, D.FontMin, Pal.Light);
            var head = new Rect(r.x, r.y + C.SiteChromeH, r.width, C.SiteHeadH);
            if (Draw.Repaint)
            {
                Draw.Fill(head, Pal.SiteCard);
                Draw.Fill(new Rect(head.x, head.yMax - 1f, head.width, 1f), Pal.SiteLine);
            }
            float x = head.x + S.Pad * 1.75f;
            Draw.TextFit(new Rect(x, head.y, S.CodeW * 2.5f, head.height), site, C.LogoFont, Pal.SiteAccent, true);
            x += S.CodeW * 2.5f + S.Pad;
            string[] nav = { "콘서트", "뮤지컬", "연극", "스포츠", "전시" };
            for (int i = 0; i < nav.Length; i++)
            {
                float nw = Mathf.Ceil(Draw.TextWidth(nav[i], D.FontBody, true)) + S.Pad * 1.25f;
                Draw.TextFit(new Rect(x, head.y, nw, head.height), nav[i], D.FontBody, i == 0 ? Pal.SiteAccent : Pal.SiteSub, true);
                x += nw;
            }
            Draw.TextFit(new Rect(head.x, head.y, head.width - S.Pad * 1.75f, head.height), "로그인", D.FontDesc, Pal.SiteSub, false, TextAnchor.MiddleRight);
            return new Rect(r.x, head.yMax, r.width, r.yMax - head.yMax);
        }

        float SitePre(float w, Sale s)
        {
            var now = SimulationManager.instance.m_currentGameTime;
            float pad = S.Pad * 2.5f, posterW = C.PosterW, posterH = C.PosterH;
            float h = C.SiteChromeH + C.SiteHeadH + pad * 2f + posterH;
            var body = Browser(new Rect(0f, 0f, w, h), SiteOf(s), $"{SiteOf(s)}.example/goods/{s.Start:yyyyMMdd}");
            var poster = new Rect(body.x + pad, body.y + pad, posterW, posterH);
            if (Draw.Repaint) Draw.Shadow(poster, S.Pad * 0.75f, 0.18f);
            string date = s.Days > 1 ? $"{s.Start:yyyy. M. d} – {s.Start.AddDays(s.Days - 1):M. d}" : $"{s.Start:yyyy. M. d}";
            C.Poster(poster, s.Poster, s.Band.Color, s.Band.Name, date, Shows.VenueName, City);
            float x = poster.xMax + pad, iw = body.xMax - pad - x, y = poster.y;
            float tagW = Badge.Pair(x, y + Badge.H * 0.5f, "콘서트", Pal.SiteNoteBg, Pal.SiteAccent);
            y += Badge.H + S.Pad;
            Draw.TextFit(new Rect(x, y, iw, C.NameFont * 1.2f), $"{s.Band.Name} LIVE in {City}", C.NameFont * 0.85f, Pal.SiteInk, true);
            y += C.NameFont * 1.2f + S.Pad;
            string[,] info = { { "장소", Shows.VenueName }, { "공연 기간", date + $" · {s.Days}회차" }, { "공연 시간", $"{s.Start:HH:mm}" }, { "입장권", Price(s.Band.Price) }, { "정원", $"{s.Cap:#,0}석" } };
            for (int i = 0; i < info.GetLength(0); i++)
            {
                Draw.TextFit(new Rect(x, y, S.CodeW * 2.4f, D.LineBody + D.Gap), info[i, 0], D.FontBody, Pal.SiteMuted);
                Draw.TextFit(new Rect(x + S.CodeW * 2.4f, y, iw - S.CodeW * 2.4f, D.LineBody + D.Gap), info[i, 1], D.FontBody, Pal.SiteInk);
                y += D.LineBody + D.Gap * 2.5f;
            }
            y += S.Pad * 0.5f;
            var cd = new Rect(x, y, iw, C.CountBox + S.Pad * 2.5f);
            if (Draw.Repaint)
            {
                Draw.Round(cd, Pal.SiteCard, Board.BoxRadius * 1.5f);
                Draw.RoundOutline(cd, Pal.SiteLine, Board.BoxRadius * 1.5f);
            }
            Draw.TextFit(new Rect(cd.x + S.Pad, cd.y + S.Pad, iw * 0.4f, D.LineSub), "티켓 오픈까지", D.FontMin, Pal.SiteMuted);
            Draw.TextFit(new Rect(cd.x + S.Pad, cd.y + S.Pad + D.LineSub, iw * 0.4f, D.LineBody), $"{s.Open.Month}월 {s.Open.Day}일({"일월화수목금토"[(int)s.Open.DayOfWeek]}) {s.Open:HH:mm}", D.FontDesc, Pal.SiteInk, true);
            var span = s.Open - now;
            string[] vals = { ((int)span.TotalDays).ToString(), span.Hours.ToString("00"), span.Minutes.ToString("00") }, units = { "일", "시간", "분" };
            float bx = cd.xMax - S.Pad - (C.CountBox + D.Gap * 1.5f) * vals.Length;
            for (int i = 0; i < vals.Length; i++)
            {
                var box = new Rect(bx + (C.CountBox + D.Gap * 1.5f) * i, cd.y + D.Gap * 2f, C.CountBox, C.CountBox);
                if (Draw.Repaint) Draw.Round(box, Pal.SiteInk, Board.BoxRadius * 1.25f);
                Draw.TextFit(box, vals[i], C.BigFont + D.Gap, Pal.SiteCard, true, TextAnchor.MiddleCenter);
                Draw.TextFit(new Rect(box.x, box.yMax, box.width, D.LineSub), units[i], D.FontMin, Pal.SiteMuted, false, TextAnchor.MiddleCenter);
            }
            y = cd.yMax + S.Pad;
            var off = new Rect(x, y, iw, C.SiteButtonH);
            if (Draw.Repaint) Draw.Round(off, Pal.SiteOff, Board.BoxRadius * 1.25f);
            Draw.TextFit(off, $"{s.Open.Month}.{s.Open.Day} {s.Open:HH:mm} 오픈 예정 · 관심 {s.Want:#,0}명", D.FontValue, Pal.SiteMuted, true, TextAnchor.MiddleCenter);
            y += C.SiteButtonH + S.Pad;
            float cw = (iw - D.Gap * 2f) / 2f;
            for (int i = 0; i < Tickets.Sites.Length; i++)
            {
                var cell = new Rect(x + (cw + D.Gap * 2f) * (i % 2), y + (S.RowH + D.Gap * 2f) * (i / 2), cw, S.RowH);
                if (Draw.Repaint)
                {
                    Draw.Round(cell, Pal.SiteCard, Board.BoxRadius * 1.25f);
                    Draw.RoundOutline(cell, Pal.SiteLine, Board.BoxRadius * 1.25f);
                }
                bool sells = s.Site < 0 || s.Site == i;
                Draw.TextFit(new Rect(cell.x + S.Pad, cell.y, cell.width - S.Pad * 2f, cell.height), Tickets.Sites[i], D.FontDesc, sells ? Pal.SiteInk : Pal.SiteMuted, true);
                Draw.TextFit(new Rect(cell.x + S.Pad, cell.y, cell.width - S.Pad * 2f, cell.height), !sells ? "판매 안 해요" : s.Site == i ? $"단독 · {s.Open:M/d HH:mm}" : $"{s.Open:M/d HH:mm}", D.FontDesc, sells ? Pal.SiteInk : Pal.SiteMuted, true, TextAnchor.MiddleRight);
            }
            return h;
        }

        float SiteQueue(float w, Sale s, int queue)
        {
            float cardH = S.Pad * 4f + C.LogoFont * 1.4f + D.LineBody * 3f + C.WaitFont * 1.3f + S.Pad * 6f + S.RowH * 2f;
            float h = C.SiteChromeH + C.SiteHeadH + S.Pad * 5f + cardH;
            var body = Browser(new Rect(0f, 0f, w, h), SiteOf(s), $"{SiteOf(s)}.example/waiting?goods={s.Start:yyyyMMdd}");
            var card = new Rect(body.center.x - C.WaitCardW * 0.5f, body.y + S.Pad * 2.5f, C.WaitCardW, cardH);
            if (Draw.Repaint)
            {
                Draw.Shadow(card, S.Pad, 0.12f);
                Draw.Round(card, Pal.SiteCard, S.Pad);
            }
            float x = card.x + S.Pad * 3f, iw = card.width - S.Pad * 6f, y = card.y + S.Pad * 2.5f;
            Draw.TextFit(new Rect(x, y, iw, C.LogoFont * 1.4f), SiteOf(s), C.LogoFont, Pal.SiteAccent, true, TextAnchor.MiddleCenter);
            y += C.LogoFont * 1.4f + D.Gap * 2f;
            Draw.TextFit(new Rect(x, y, iw, D.LineBody + D.Gap), "접속 대기 중이에요", D.FontValue + D.Gap, Pal.SiteInk, true, TextAnchor.MiddleCenter);
            y += D.LineBody + D.Gap * 2f;
            Draw.TextFit(new Rect(x, y, iw, D.LineBody), $"{s.Band.Name} LIVE in {City} · 예매 화면으로 순서대로 들어가요", D.FontBody, Pal.SiteSub, false, TextAnchor.MiddleCenter);
            y += D.LineBody + S.Pad;
            var box = new Rect(x, y, iw, C.WaitFont * 1.3f + S.Pad * 3f + D.LineSub);
            if (Draw.Repaint) Draw.Round(box, Pal.SiteBg, Board.BoxRadius * 1.5f);
            Draw.TextFit(new Rect(box.x, box.y + S.Pad * 0.75f, box.width, D.LineSub), "지금 기다리는 사람", D.FontMin, Pal.SiteMuted, false, TextAnchor.MiddleCenter);
            Draw.TextFit(new Rect(box.x, box.y + S.Pad * 0.75f + D.LineSub, box.width, C.WaitFont * 1.2f), queue.ToString("#,0") + "명", C.WaitFont, Pal.SiteInk, true, TextAnchor.MiddleCenter);
            var bar = new Rect(box.x + S.Pad * 1.5f, box.yMax - S.Pad * 1.5f - C.BarH, box.width - S.Pad * 3f, C.BarH);
            float pct = s.Want > 0 ? Mathf.Clamp01((float)s.Sold / Math.Min(s.Want, s.Cap)) : 0f;
            if (Draw.Repaint)
            {
                Draw.Round(bar, Pal.SiteLine, C.BarH * 0.5f);
                Draw.Round(new Rect(bar.x, bar.y, bar.width * pct, bar.height), Pal.SiteAccent, C.BarH * 0.5f);
            }
            y = box.yMax + S.Pad;
            var note = new Rect(x, y, iw, S.RowH);
            if (Draw.Repaint) Draw.Round(note, Pal.SiteNoteBg, Board.BoxRadius * 1.25f);
            Draw.TextFit(new Rect(note.x + S.Pad, note.y, note.width - S.Pad * 2f, note.height), $"남은 자리 {Math.Max(0, s.Cap - s.Sold):#,0}석 · 원하는 사람이 많아 오픈 직후 몰렸어요", D.FontDesc, Pal.SiteNoteInk);
            return h;
        }
    }
}
