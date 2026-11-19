using System;
using ConcertMod.Data;
using UnityEngine;
using D = CSCommonUX.CSCommonDesign;
using S = CSCommonUX.Section;

namespace ConcertMod.UI
{
    /// <summary>
    /// 콘서트 창(캔버스 '[CSCommonUX 3.0] 콘서트 Extension'): 머리줄 · 우물 몸(판을 위에서 아래로, 툴바 높이만큼 안쪽 아래 여백) · 하단 알약 툴바 일정 · 차트 · 예매 · 흥행 · 예약 + 설정 FAB.
    /// 0.2 판: 일정 · 차트 · 흥행 · 예약(다음 공연)은 게임 공연 데이터로, 예매는 다음 판에서.
    /// </summary>
    internal sealed partial class ConcertWindow
    {
        const int TabSchedule = 0, TabChart = 1, TabTicket = 2, TabBox = 3, TabBooking = 4, TabSettings = 5;

        static readonly string[] Names = { "일정", "차트", "예매", "흥행", "예약", "설정" };

        // 탭 아이콘(캔버스 툴바): 달력 · 순위 막대 · 매표 · 동전 · 달력 + 더하기 · 설정 슬라이더
        static readonly string[] TabIcons =
        {
            "M4 5h16v15H4zM4 10h16M9 3v4M15 3v4",
            "M5 20V10M12 20V4M19 20v-7",
            "M3 8a2 2 0 0 0 0 4v4h18v-4a2 2 0 0 0 0-4V4H3zM9 4v12",
            "M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18zM9 9h6M9 15h6M12 6v12",
            "M4 5h16v15H4zM4 10h16M9 3v4M15 3v4M12 13v5M9.5 15.5h5",
            "M5 7h9M18 7h1M5 17h1M10 17h9M14 5v4M8 15v4",
        };

        const string BodyId = "concert.body";

        public bool Visible { get; private set; }

        int tab;

        readonly Func<int, string> tabIcon = i => TabIcons[i], tabName = i => Names[i];

        public void Toggle()
        {
            if (Visible) Close();
            else
            {
                Visible = true;
                ScrollArea.Reset(BodyId);
            }
        }

        public void Close() => Visible = false;

        public Rect Rect
        {
            get
            {
                float w = Mathf.Min(D.WindowW, Draw.Width - Modal.Margin * 2f);
                float h = Mathf.Min(D.WindowH, Draw.Height - Modal.Margin * 2f);
                float x = Mathf.Clamp(Mathf.Round((Draw.Width - w) * 0.5f + moved.x), -w + WindowHead.H * 2f, Draw.Width - WindowHead.H * 2f);
                float y = Mathf.Clamp(Mathf.Round((Draw.Height - h) * 0.5f + moved.y), 0f, Draw.Height - WindowHead.H);
                return new Rect(x, y, w, h);
            }
        }

        Vector2 moved = new(ConcertSetting.Current.WindowDX, ConcertSetting.Current.WindowDY), grab;
        bool dragging;

        /// <summary>머리줄(닫기 단추 빼고)을 끌어 창을 옮김.</summary>
        void HandleDrag(Rect r)
        {
            var e = Event.current;
            var m = Draw.Mouse;
            var head = new Rect(r.x, r.y, r.width - IconButton.Size - WindowHead.PadR * 2f, WindowHead.H);
            if (e.type == EventType.MouseDown && e.button == 0 && head.Contains(m))
            {
                dragging = true;
                grab = m - moved;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && dragging)
            {
                moved = m - grab;
                e.Use();
            }
            else if (e.rawType == EventType.MouseUp && dragging)
            {
                dragging = false;
                ConcertSetting.Current.WindowDX = moved.x;
                ConcertSetting.Current.WindowDY = moved.y;
                ConcertSetting.Save();
            }
        }

        public void OnGUI()
        {
            if (!Visible) return;
            var e = Event.current;
            HandleDrag(Rect);
            var r = Rect;
            Panel.Frame(r, D.RadiusWindow);
            string title = "콘서트 Extension · " + Names[tab] + (tab == TabBooking ? " · " + BookSubs[bookSub] : "");
            var now = SimulationManager.instance.m_currentGameTime;
            if (WindowHead.In(r, null, title, Mod.Version, HeadSub(), $"{now.Year}년 {now.Month}월 {now.Day}일 {now.Hour:00}:{now.Minute:00}", Shows.Now != null && Shows.Now.Active ? "공연 중" : "", Pal.Primary, "닫기(Esc)")) Close();
            DrawBody(r);
            DrawBar(r);
            if (r.Contains(Draw.Mouse) && (e.type == EventType.MouseDown || e.type == EventType.ScrollWheel)) e.Use();
        }

        static string HeadSub()
        {
            if (!Shows.HasDlc) return "콘서트 DLC가 없어요";
            string place = Shows.Venue == 0 ? "공연장이 없어요" : Shows.VenueName + (Shows.StadiumVenue ? " (기본 공연장)" : "");
            return $"{place} · 밴드 {Shows.Bands.Count} · 기록한 공연 {History.All.Count}";
        }

        /// <summary>예약 탭 서브 알약: 다음 공연 · 의향서(기다리는 수 뱃지).</summary>
        static readonly string[] BookSubs = { "다음 공연", "공연장", "의향서" };
        const int SubNext = 0, SubVenue = 1, SubLetters = 2;
        int bookSub;
        readonly Func<int, string> bookSubName = i => BookSubs[i];
        readonly Func<int, float> bookSubBadgeW = i => i == SubLetters && Letters.Waiting > 0 ? Badge.Count(0f, 0f, Letters.Waiting.ToString(), false) : 0f;
        readonly Action<int, Rect> bookSubBadge = (i, c) => Badge.Count(c.x, c.center.y, Letters.Waiting.ToString());

        void DrawBar(Rect r)
        {
            int sh = SubToolBar.Bar(r, tab == TabBooking, BookSubs.Length, bookSub, bookSubName, bookSubBadgeW, bookSubBadge, id: "concert.booking");
            if (sh >= 0 && sh != bookSub)
            {
                bookSub = sh;
                ScrollArea.Reset(BodyId);
            }
            int hit = Toolbar.Bar(r, TabSettings, tab == TabSettings ? -1 : tab, tabIcon, tabName, TabIcons[TabSettings], Names[TabSettings], tab == TabSettings, out bool fab);
            if (fab) hit = TabSettings;
            if (hit >= 0 && hit != tab)
            {
                tab = hit;
                ScrollArea.Reset(BodyId);
            }
        }

        float contentH;

        void DrawBody(Rect r)
        {
            var view = new Rect(r.x, r.y + WindowHead.H, r.width, r.height - WindowHead.H);
            if (Draw.Repaint) Draw.RoundBottom(view, S.WellFill, D.RadiusWindow);
            const float pad = S.WellPad;
            var inner = new Rect(view.x + pad, view.y + pad, view.width - pad * 2f, view.height - pad);
            ScrollArea.In(inner, BodyId, contentH + (tab == TabBooking ? SubToolBar.SafeH : Toolbar.SafeH), true, _ => contentH = DrawPage(inner.width));
        }

        float DrawPage(float w)
        {
            if (!Shows.HasDlc && tab != TabSettings) return Note(0f, w, "콘서트 DLC", "페스티벌 구역이 있는 콘서트 DLC가 있어야 공연을 보여 줘요");
            switch (tab)
            {
                case TabSchedule: return ScheduleTab(w);
                case TabChart: return ChartTab(w);
                case TabTicket: return TicketTab(w);
                case TabBox: return BoxTab(w);
                case TabBooking: return bookSub == SubLetters ? LettersTab(w) : bookSub == SubVenue ? VenueTab(w) : BookingTab(w);
                default: return SettingsTab(w);
            }
        }

        // ---- 판 부품

        static float PlateTop => S.Pad + S.ChipH + S.HeadGap;

        /// <summary>판 바탕 + 제목 칩(+ 오른쪽 작은 글): 안쪽 칸을 돌려줌.</summary>
        static Rect Plate(Rect r, string title, string icon, string sub = null)
        {
            S.Frame(r);
            S.Title(new Rect(r.x + S.Pad, r.y + S.Pad, r.width - S.Pad * 2f, S.ChipH), title, sub, icon);
            float top = r.y + PlateTop;
            return new Rect(r.x + S.Pad, top, r.width - S.Pad * 2f, Mathf.Max(0f, r.yMax - S.PadBottom - top));
        }

        static float Note(float y, float w, string title, string text)
        {
            float h = PlateTop + S.RowH + S.PadBottom;
            var body = Plate(new Rect(0f, y, w, h), title, "M9 18V5l12-2v13M6 21a3 3 0 1 0 0-6 3 3 0 0 0 0 6zM18 19a3 3 0 1 0 0-6 3 3 0 0 0 0 6z");
            Draw.TextFit(new Rect(body.x, body.y, body.width, S.RowH), text, D.FontBody, S.SubInk, false, TextAnchor.MiddleLeft);
            return h;
        }

        /// <summary>작은 숫자 칸: 회색 칸에 이름 · 값.</summary>
        static void Cell(Rect r, string name, string value, Color? ink = null)
        {
            if (Draw.Repaint) Draw.Round(r, Pal.Inset, Board.BoxRadius * 2f);
            float pad = S.Pad;
            Draw.TextFit(new Rect(r.x + pad, r.y + D.Gap * 3f, r.width - pad * 2f, D.LineSub), name, D.FontMin, S.SubInk);
            Draw.TextFit(new Rect(r.x + pad, r.y + D.Gap * 3f + D.LineSub + D.Gap, r.width - pad * 2f, D.LineBody + D.Gap), value, D.FontValue, ink ?? Pal.Txt, true);
        }

        static float CellH => D.Gap * 6f + D.LineSub + D.Gap + D.LineBody + D.Gap;

        /// <summary>앨범 칸: 밴드 색 둥근 네모에 머리글자.</summary>
        static void Tile(Rect r, Color c, string name)
        {
            if (!Draw.Repaint) return;
            Draw.Round(r, c, r.width * 0.2f);
            Draw.TextFit(r, Artists.Initials(name), D.FontMin, Draw.On(c), true, TextAnchor.MiddleCenter);
        }

        static string Money(long v) => "₡" + (v / 100.0).ToString("#,0.##");

        static string Price(int cents) => "₡" + (cents / 100.0).ToString("0.00");

        static string Delta(int d) => d > 0 ? "▲ " + d : d < 0 ? "▼ " + (-d) : "–";

        static Color DeltaInk(int d) => d > 0 ? Pal.ConcertUp : d < 0 ? Pal.ConcertDown : S.SubInk;

        static string Day(DateTime t) => $"{t.Month}월 {t.Day}일";

        static string When(DateTime t) => $"{t.Month}월 {t.Day}일 {t.Hour:00}:{t.Minute:00}";

        // ---- 자기 퀵 버튼(허브가 없거나 '모드 허브 연결'을 껐을 때: 왼쪽 둥근 버튼 · 끌어 옮김 · 자리는 설정 파일에)

        public static bool OwnLauncher => !YukiModBoxEntry.HubOwnsLauncher || !ConcertSetting.Current.HubLinked;

        /// <summary>기본 자리 화면 높이 비율(다른 모드 퀵 버튼과 겹치지 않게).</summary>
        const float LauncherTop = 0.55f;

        bool fabPressed, fabDragging;
        Vector2 fabOffset, fabPressAt;

        public Rect LauncherRect
        {
            get
            {
                float d = D.FabSize;
                var s = ConcertSetting.Current;
                float x = s.LauncherX >= 0f ? s.LauncherX : D.FabMargin;
                float y = s.LauncherY >= 0f ? s.LauncherY : Draw.Height * LauncherTop;
                return new Rect(Mathf.Clamp(x, 0f, Mathf.Max(0f, Draw.Width - d)), Mathf.Clamp(y, 0f, Mathf.Max(0f, Draw.Height - d)), d, d);
            }
        }

        public void LauncherGui()
        {
            var r = LauncherRect;
            var e = Event.current;
            var m = Draw.Mouse;
            if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(m))
            {
                fabPressed = true;
                fabDragging = false;
                fabPressAt = m;
                fabOffset = m - r.position;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && fabPressed)
            {
                if (!fabDragging && (m - fabPressAt).sqrMagnitude > D.Gap * D.Gap) fabDragging = true;
                if (fabDragging)
                {
                    ConcertSetting.Current.LauncherX = m.x - fabOffset.x;
                    ConcertSetting.Current.LauncherY = m.y - fabOffset.y;
                    e.Use();
                }
            }
            else if (e.rawType == EventType.MouseUp && fabPressed)
            {
                fabPressed = false;
                if (fabDragging)
                {
                    fabDragging = false;
                    ConcertSetting.Save();
                }
                else if (r.Contains(m)) Toggle();
                e.Use();
            }
            if (Draw.Repaint) Fab.In(r, YukiModBoxEntry.Icon, FabTone.Plain, Visible);
            if (!fabDragging && Draw.Hover(r)) Tooltip.Draw(r, "콘서트 Extension · 끌어서 옮기기");
        }
    }
}
