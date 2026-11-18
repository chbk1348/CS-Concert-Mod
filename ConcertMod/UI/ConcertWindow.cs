using System;
using UnityEngine;
using D = CSCommonUX.CSCommonDesign;
using S = CSCommonUX.Section;

namespace ConcertMod.UI
{
    /// <summary>
    /// 콘서트 창(캔버스 '[CSCommonUX 3.0] 콘서트 확장판'): 머리줄 · 우물 몸(판을 위에서 아래로, 툴바 높이만큼 안쪽 아래 여백) · 하단 알약 툴바 일정 · 차트 · 흥행 · 예약 + 설정 FAB.
    /// 기본 틀: 탭마다 판 제목만 두고 내용은 다음 판에서 채움.
    /// </summary>
    internal sealed class ConcertWindow
    {
        const int TabSchedule = 0, TabChart = 1, TabBox = 2, TabBooking = 3, TabSettings = 4;

        static readonly string[] Names = { "일정", "차트", "흥행", "예약", "설정" };

        // 탭 아이콘(캔버스 툴바): 달력 · 순위 막대 · 매표 · 달력 + 더하기 · 설정 슬라이더
        static readonly string[] TabIcons =
        {
            "M4 5h16v15H4zM4 10h16M9 3v4M15 3v4",
            "M5 20V10M12 20V4M19 20v-7",
            "M3 8a2 2 0 0 0 0 4v4h18v-4a2 2 0 0 0 0-4V4H3zM9 4v12",
            "M4 5h16v15H4zM4 10h16M9 3v4M15 3v4M12 13v5M9.5 15.5h5",
            "M5 7h9M18 7h1M5 17h1M10 17h9M14 5v4M8 15v4",
        };

        /// <summary>탭마다 판(제목 · 아이콘) — 캔버스 보드와 같은 차례.</summary>
        static readonly string[][][] Plates =
        {
            new[] { new[] { "지금 공연", "M3 20h18M5 20V9l7-5 7 5v11M9 20v-6h6v6" }, new[] { "다음 공연", "M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18zM12 7v5l3 2" }, new[] { "입장권", "M3 8a2 2 0 0 0 0 4v4h18v-4a2 2 0 0 0 0-4V4H3z" } },
            new[] { new[] { "인기 순위", "M5 20V10M12 20V4M19 20v-7" }, new[] { "밴드", "M9 18V5l12-2v13M6 21a3 3 0 1 0 0-6 3 3 0 0 0 0 6zM18 19a3 3 0 1 0 0-6 3 3 0 0 0 0 6z" } },
            new[] { new[] { "이번 달 흥행", "M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18zM9 9h6M9 15h6M12 6v12" }, new[] { "공연별", "M8 6h12M8 12h12M8 18h12M4 6h.01M4 12h.01M4 18h.01" } },
            new[] { new[] { "공연 잡기", "M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18zM12 7v5l3 2" }, new[] { "라인업", "M8 6h12M8 12h12M8 18h12M4 6h.01M4 12h.01M4 18h.01" } },
            new[] { new[] { "창", "M3 5h18v14H3zM3 9h18" }, new[] { "알림", "M6 16V11a6 6 0 0 1 12 0v5l2 2H4zM10 21h4" }, new[] { "시험", "M8 8a4 4 0 0 1 8 0v8a4 4 0 0 1-8 0zM4 12h4M16 12h4" } },
        };

        const string BodyId = "concert.body";

        public bool Visible { get; private set; }

        int tab;

        readonly Func<int, string> tabIcon = i => TabIcons[i], tabName = i => Names[i];

        /// <summary>콘서트 DLC(페스티벌)가 있나 — 없으면 판 대신 안내.</summary>
        static bool HasDlc => SteamHelper.IsDLCOwned(SteamHelper.DLC.MusicFestival);

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
            string title = tab == TabSettings ? "콘서트 확장판 · 설정" : "콘서트 확장판 · " + Names[tab];
            if (WindowHead.In(r, null, title, Mod.Version, HasDlc ? "공연 일정 · 인기 차트 · 흥행 · 예약" : "콘서트 DLC가 없어요", "", "", Pal.Primary, "닫기(Esc)")) Close();
            DrawBody(r);
            DrawBar(r);
            if (r.Contains(Draw.Mouse) && (e.type == EventType.MouseDown || e.type == EventType.ScrollWheel)) e.Use();
        }

        void DrawBar(Rect r)
        {
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
            ScrollArea.In(inner, BodyId, contentH + Toolbar.SafeH, true, _ => contentH = DrawPage(inner.width));
        }

        /// <summary>기본 틀: 탭의 판마다 제목 칩 · 한 줄 안내. 기능 판에서 판마다 그리는 함수로 바꿈.</summary>
        float DrawPage(float w)
        {
            if (!HasDlc && tab != TabSettings) return Plate(0f, w, "콘서트 DLC", "M9 18V5l12-2v13M6 21a3 3 0 1 0 0-6 3 3 0 0 0 0 6zM18 19a3 3 0 1 0 0-6 3 3 0 0 0 0 6z", "페스티벌 구역이 있는 콘서트 DLC가 있어야 공연을 보여 줘요");
            float y = 0f;
            foreach (var p in Plates[tab]) y += Plate(y, w, p[0], p[1], "기본 틀이에요 · 다음 판에서 채워요") + S.Gap;
            return Mathf.Max(0f, y - S.Gap);
        }

        static float Plate(float y, float w, string title, string icon, string note)
        {
            float h = S.Pad + S.ChipH + S.HeadGap + S.RowH + S.PadBottom;
            var r = new Rect(0f, y, w, h);
            S.Frame(r);
            S.Title(new Rect(r.x + S.Pad, r.y + S.Pad, r.width - S.Pad * 2f, S.ChipH), title, null, icon);
            Draw.TextFit(new Rect(r.x + S.Pad, r.y + S.Pad + S.ChipH + S.HeadGap, r.width - S.Pad * 2f, S.RowH), note, D.FontBody, S.SubInk, false, TextAnchor.MiddleLeft);
            return h;
        }

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
            if (!fabDragging && Draw.Hover(r)) Tooltip.Draw(r, "콘서트 확장판 · 끌어서 옮기기");
        }
    }
}
