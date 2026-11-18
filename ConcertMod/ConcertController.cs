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

        Action esc, gui;

        void Awake()
        {
            Instance = this;
            esc = EscClose;
            gui = DrawGui;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            InputBlocker.Release();
        }

        void Update()
        {
            Safe.Run("Esc 닫기", esc);
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
            host = new GameObject("ConcertMod");
            host.AddComponent<ConcertController>();
        }

        public override void OnLevelUnloading()
        {
            var h = host;
            host = null;
            Safe.Run("도시 내리기: 본체 없애기", () =>
            {
                if (h != null) UnityEngine.Object.Destroy(h);
            });
            base.OnLevelUnloading();
        }
    }
}
