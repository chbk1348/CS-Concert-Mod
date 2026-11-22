using System;
using ConcertMod.Core;
using ConcertMod.UI;
using ICities;
using UnityEngine;

namespace ConcertMod
{
    /// <summary>게임 화면에 붙는 본체: Esc 닫기 · 창 그리기 · 입력 막기. 매 프레임 처리는 Safe 로 감쌈.</summary>
    internal sealed class ConcertController : MonoBehaviour
    {
        public static ConcertController Instance { get; private set; }

        public readonly ConcertWindow Window = new();

        Action esc, gui, tick;
        float tickAt;

        /// <summary>공연 데이터 다시 읽는 간격(초).</summary>
        const float TickSeconds = 1f;

        void Awake()
        {
            Instance = this;
            esc = EscClose;
            gui = DrawGui;
            tick = () =>
            {
                if (Time.realtimeSinceStartup - tickAt < TickSeconds) return;
                tickAt = Time.realtimeSinceStartup;
                // 단계마다 따로 감쌈: 한 단계가 던져도 뒤 단계는 돎(순서는 그대로)
                // 공연 읽기가 실패한 tick 은 대관 · 예매를 건너뜀(지난 공연 값으로 경기장 넣기 · 예매를 하지 않게)
                bool read = false, wrote = false;
                Safe.Run("공연 읽기: 공연", () => { Data.Shows.Refresh(); read = true; });
                // 게임에 쓰기가 실패해도 예전처럼(한 함수였을 때) 읽기 실패로 봄
                if (read) Safe.Run("공연 쓰기: 게임", () => { Data.Shows.ApplyToGame(); wrote = true; });
                read &= wrote;
                Safe.Run("공연 읽기: 기록", Data.History.Track);
                Safe.Run("공연 읽기: 예약", () => Data.Booking.Tick(read));
                Safe.Run("공연 읽기: 의향서", Data.Letters.Tick);
                if (read) Safe.Run("공연 읽기: 대관", Data.Rental.Tick);
                Safe.Run("공연 읽기: 소리", Data.Sound.Tick);
                if (read) Safe.Run("공연 읽기: 예매", Data.Tickets.Refresh);
            };
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            InputBlocker.Release();
        }

        void Update()
        {
            Safe.Run("Esc 닫기", esc);
            if (Data.Shows.HasDlc) Safe.Run("공연 읽기", tick);
            InputBlocker.Begin("ConcertMod");
            if (!ColossalFramework.UI.UIView.HasModalInput())
            {
                if (ConcertWindow.OwnLauncher) InputBlocker.Add(Window.LauncherRect);
                if (Window.Visible) InputBlocker.Add(Window.Rect);
            }
            InputBlocker.End();
        }

        void OnGUI()
        {
            if (ColossalFramework.UI.UIView.HasModalInput()) return;
            Draw.Begin();
            Safe.Run("화면 그리기", gui);
        }

        void DrawGui()
        {
            if (ConcertWindow.OwnLauncher) Window.LauncherGui();
            Window.OnGUI();
            CSCommonUX.Select.Flush();
            CSCommonUX.Toast.Flush();
            Draw.EndInput();
        }

        int escFrame = -1;

        void EscClose()
        {
            if (escFrame == Time.frameCount || !Window.Visible || !Input.GetKeyDown(KeyCode.Escape)) return;
            Window.Close();
            escFrame = Time.frameCount;
        }

        /// <summary>게임 Esc(EscapePatch): 창이 열려 있으면 닫고 true — 같은 프레임에 이미 닫았어도 true(일시정지 메뉴를 열지 않음).</summary>
        internal bool TakeEscape()
        {
            if (escFrame == Time.frameCount) return true;
            if (!Window.Visible) return false;
            Window.Close();
            escFrame = Time.frameCount;
            return true;
        }
    }

    /// <summary>게임 Esc(GameKeyShortcuts.Escape)를 먼저 받음: 창이 열려 있거나 이번 프레임에 Esc 로 닫았으면 일시정지 메뉴를 열지 않음. 게임 판에 메서드가 없으면 이 패치만 건너뜀.</summary>
    [HarmonyLib.HarmonyPatch]
    [CSCommonOptionalPatch]
    internal static class EscapePatch
    {
        static System.Reflection.MethodBase TargetMethod() => HarmonyLib.AccessTools.Method(typeof(GameKeyShortcuts), "Escape");

        static bool Prepare()
        {
            if (TargetMethod() != null) return true;
            Log.Warn("GameKeyShortcuts.Escape 를 찾지 못해 Esc 패치를 쉼(게임 판 확인)");
            return false;
        }

        static bool Prefix()
        {
            try
            {
                return !(ConcertController.Instance?.TakeEscape() ?? false);
            }
            catch (Exception e)
            {
                Safe.Report("Esc", e);
                return true;
            }
        }
    }

    /// <summary>도시를 불러오고 내릴 때 본체를 붙이고 뗌(CSCommon CSCommonLoading: 게임 모드에서만).</summary>
    public sealed class Hooks : CSCommonLoading<OptionsPanel, ConcertPatcher>
    {
        GameObject host;

        protected override void LoadedActions(LoadMode mode)
        {
            if (Data.Shows.HasDlc) Safe.Run("밴드 목록", Data.Shows.LoadBands);
            // 자체 검사는 경고만: 던져도 모드 붙이기를 멈추지 않음
            SelfCheck("공연 기록", Data.History.SelfCheck);
            SelfCheck("공연 예약", Data.Booking.SelfCheck);
            SelfCheck("의향서", Data.Letters.SelfCheck);
            SelfCheck("대관", Data.Rental.SelfCheck);
            SelfCheck("출연표", Data.Lineup.SelfCheck);
            SelfCheck("예매", Data.Tickets.SelfCheck);
            host = new GameObject("ConcertMod");
            host.AddComponent<ConcertController>();
        }

        static void SelfCheck(string what, Func<string> check)
        {
            try
            {
                string bad = check();
                if (bad != null) Log.Warn($"{what} 자체 검사 실패: {bad}");
            }
            catch (Exception e)
            {
                Log.Warn($"{what} 자체 검사 예외: {e.GetType().Name}: {e.Message}");
            }
        }

        public override void OnLevelUnloading()
        {
            var h = host;
            host = null;
            Safe.Run("도시 내리기: 본체 없애기", () =>
            {
                if (h != null) UnityEngine.Object.Destroy(h);
            });
            Data.Shows.Clear();
            Data.History.Clear();
            Data.Booking.Reset();
            Data.Tickets.Clear();
            Data.Letters.Clear();
            Data.Rental.Clear();
            Data.Sound.Clear();
            Data.Lineup.Clear();
            ConcertWindow.ClearPlaces();
            base.OnLevelUnloading();
        }
    }
}
