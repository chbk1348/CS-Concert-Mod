using ConcertMod.Core;
using ConcertMod.UI;
using ICities;

namespace ConcertMod
{
    /// <summary>
    /// 모드 진입(CSCommon CSCommonMod): 설정 읽기 · 인게임 설정 패널 걸기 · Harmony 준비.
    /// 설정 파일 이름은 ConcertMod.xml.
    /// </summary>
    public sealed class Mod : CSCommonMod<OptionsPanel, ConcertPatcher>, IUserMod
    {
        public const string Version = "0.1.0";

        public override string BaseName => "콘서트 확장판";

        /// <summary>output_log.txt 머리 [콘서트 확장판].</summary>
        public override string LogName => "[콘서트 확장판]";

        public override string HarmonyID => "chbk1348.concertmod";

        public string Description => "콘서트 DLC 페스티벌을 넓혀요: 공연 일정 · 인기 차트 · 흥행 · 공연 예약.";

        /// <summary>업데이트 내역: 새 판을 처음 불러올 때 알림으로 한 번, 설정 '업데이트 내역'에서 언제든.</summary>
        public override CSCommonRelease[] Releases => Changelog;

        static readonly CSCommonRelease[] Changelog =
        {
            new(Version, "2026-11-18",
                "[신규] 기본 틀: 공용 UI(CSCommonUX) · 모드 허브 연결 · 창(다크 · 보라) · 하단 툴바 일정 · 차트 · 흥행 · 예약 + 설정 FAB · 탭 내용은 다음 판에서 채워요"),
        };

        public override void LoadSettings() => ConcertSetting.Load();

        public override void SaveSettings() => ConcertSetting.Save();

        public override void OnEnabled()
        {
            base.OnEnabled();
            CSCommonTheme.Use(false, CSCommonPalette.Violet); // 다크 · 보라 팔레트(캔버스)
        }
    }
}
