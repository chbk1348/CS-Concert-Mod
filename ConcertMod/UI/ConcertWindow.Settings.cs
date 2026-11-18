using System;
using ConcertMod.Data;
using UnityEngine;
using D = CSCommonUX.CSCommonDesign;
using S = CSCommonUX.Section;

namespace ConcertMod.UI
{
    /// <summary>
    /// 설정 탭(캔버스 Main · settings): 두 줄 판 창 · 알림 · 예매 · 소리 · 기록 · 시험. 줄마다 이름 · 설명 · 오른쪽 스위치 / 고르기 / 단추.
    /// 바꾸면 바로 ConcertMod.xml 에 저장.
    /// </summary>
    internal sealed partial class ConcertWindow
    {
        /// <summary>'공연 기록 지우기' 한 번 누른 뒤 다시 누르면 지우는 시각(실시간, 3초 안).</summary>
        float forgetArmed = -1f;
        const float ForgetSeconds = 3f;

        static float SetRowH => S.Pad * 2f + D.LineBody + D.Gap + D.LineSub;

        static float SetPlateH(int rows) => PlateTop + rows * SetRowH + (rows - 1) * D.Gap * 2f + S.PadBottom;

        float SettingsTab(float w)
        {
            var s = ConcertSetting.Current;
            float gap = S.Gap, hw = (w - gap) * 0.5f, y = 0f, h2 = SetPlateH(2), h1 = SetPlateH(1);

            var a = Plate(new Rect(0f, y, hw, h2), "창", "M3 5h18v14H3zM3 9h18");
            SwitchRow(a, 0, "모드 허브 연결", "끄면 왼쪽 퀵 버튼으로 열어요", s.HubLinked, v => s.HubLinked = v);
            ButtonRow(a, 1, "퀵 버튼 자리 처음으로", "끌어 옮긴 퀵 버튼을 왼쪽 기본 자리로", "되돌리기", s.LauncherX >= 0f || s.LauncherY >= 0f, () => { s.LauncherX = -1f; s.LauncherY = -1f; });
            var b = Plate(new Rect(hw + gap, y, hw, h2), "알림", "M6 16V11a6 6 0 0 1 12 0v5l2 2H4zM10 21h4");
            SwitchRow(b, 0, "공연 시작 알림", "공연이 시작하면 화면 위 카드로", s.NotifyStart, v => s.NotifyStart = v);
            SwitchRow(b, 1, "공연 결과 알림", "끝나면 관객 · 입장권 수입 · 성공 여부", s.NotifyResult, v => s.NotifyResult = v);
            y += h2 + gap;

            var c = Plate(new Rect(0f, y, hw, h2), "예매", TicketIcon);
            SelectRow(c, 0, "예매 오픈", $"공연 며칠 전 {Tickets.OpenHour}:00에 예매를 열지 · 기본 7일 전", Tickets.OpenDayChoices, "일 전", s.TicketOpenDays, v => s.TicketOpenDays = v);
            SelectRow(c, 1, "한 사람 최대", "주민 한 명이 살 수 있는 매수", Tickets.MaxChoices, "매", s.TicketMax, v => s.TicketMax = v);
            var d = Plate(new Rect(hw + gap, y, hw, h2), "소리", "M4 9h4l5-4v14l-5-4H4zM16 9a4 4 0 0 1 0 6M19 6a8 8 0 0 1 0 12");
            SwitchRow(d, 0, "공연 중 공연 소리", "공연장(경기장 포함) 관중 · 공연 소리를 끊기지 않게", s.ConcertSound, v => s.ConcertSound = v);
            SwitchRow(d, 1, "공연 중 배경음악", "공연이 열리는 동안 배경음악을 줄여요 · 끝나면 원래대로", s.ConcertMusic, v => s.ConcertMusic = v);
            y += h2 + gap;

            var e = Plate(new Rect(0f, y, hw, h1), "기록", ListIcon);
            bool armed = forgetArmed >= 0f && Time.realtimeSinceStartup - forgetArmed < ForgetSeconds;
            ButtonRow(e, 0, "공연 기록 지우기", armed ? "한 번 더 누르면 지워요" : $"이 도시 세이브에 남긴 지난 공연 {History.All.Count}개를 모두", armed ? "정말 지우기" : "지우기", History.All.Count > 0, () =>
            {
                if (!armed) { forgetArmed = Time.realtimeSinceStartup; return; }
                forgetArmed = -1f;
                History.Forget();
                CSCommonUX.Toast.Show("공연 기록을 지웠어요");
            }, armed ? Tone.Danger : Tone.Neutral, false);
            var f = Plate(new Rect(hw + gap, y, hw, h1), "시험", "M8 8a4 4 0 0 1 8 0v8a4 4 0 0 1-8 0zM4 12h4M16 12h4");
            SwitchRow(f, 0, "자세한 로그", "예약 · 의향서 · 대관을 output_log 에", s.VerboseLog, v => s.VerboseLog = v);
            return y + h1;
        }

        static Rect SetRow(Rect body, int i, string name, string sub, float controlW)
        {
            var row = new Rect(body.x, body.y + i * (SetRowH + D.Gap * 2f), body.width, SetRowH);
            if (Draw.Repaint) Draw.Round(row, Pal.Inset, Board.BoxRadius * 2f);
            float tw = row.width - S.Pad * 3f - controlW;
            Draw.TextFit(new Rect(row.x + S.Pad, row.y + S.Pad, tw, D.LineBody), name, D.FontBody, Pal.Txt, true);
            Draw.TextFit(new Rect(row.x + S.Pad, row.y + S.Pad + D.LineBody + D.Gap, tw, D.LineSub), sub, D.FontMin, S.SubInk);
            return new Rect(row.xMax - S.Pad - controlW, row.y, controlW, row.height);
        }

        static void SwitchRow(Rect body, int i, string name, string sub, bool on, Action<bool> set)
        {
            var c = SetRow(body, i, name, sub, D.SwitchW);
            if (!Switch.In(new Rect(c.x, Mathf.Round(c.center.y - D.SwitchH * 0.5f), D.SwitchW, D.SwitchH), on)) return;
            set(!on);
            ConcertSetting.Save();
        }

        static void ButtonRow(Rect body, int i, string name, string sub, string label, bool enabled, Action click, Tone tone = Tone.Neutral, bool save = true)
        {
            float bw = Button.Width(null, label);
            var c = SetRow(body, i, name, sub, bw);
            Button.In(new Rect(c.x, Mathf.Round(c.center.y - Button.H * 0.5f), bw, Button.H), null, label, sub, tone, enabled: enabled, click: () =>
            {
                click();
                if (save) ConcertSetting.Save();
            });
        }

        static void SelectRow(Rect body, int i, string name, string sub, int[] choices, string unit, int value, Action<int> set)
        {
            var labels = new string[choices.Length];
            int cur = 0;
            for (int k = 0; k < choices.Length; k++)
            {
                labels[k] = choices[k] + unit;
                if (choices[k] == value) cur = k;
            }
            float sw = Button.Width(null, labels[labels.Length - 1]) + S.Pad * 2f;
            var c = SetRow(body, i, name, sub, sw);
            Select.In(new Rect(c.x, Mathf.Round(c.center.y - Segmented.H * 0.5f), sw, Segmented.H), value + unit, null, labels, null, cur, k =>
            {
                set(choices[k]);
                ConcertSetting.Save();
            });
        }
    }
}
