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
        public const string Version = "0.2.3";

        public override string BaseName => "콘서트 Extension";

        /// <summary>output_log.txt 머리 [콘서트 Extension].</summary>
        public override string LogName => "[콘서트 Extension]";

        public override string HarmonyID => "chbk1348.concertmod";

        public string Description => "콘서트 DLC 페스티벌을 넓혀요: 공연 일정 · 인기 차트 · 흥행 · 공연 예약.";

        /// <summary>업데이트 내역: 새 판을 처음 불러올 때 알림으로 한 번, 설정 '업데이트 내역'에서 언제든.</summary>
        public override CSCommonRelease[] Releases => Changelog;

        static readonly CSCommonRelease[] Changelog =
        {
            new(Version, "2026-11-18",
                "[변경] 모드 이름을 '콘서트 Extension' 로 바꿨어요"),
            new("0.2.2", "2026-11-18",
                "[수정] 공연장 카드 좌석이 카드 밖으로 넘치던 문제 · 경기 표시만 붙은 다른 건물(택시 회사 등)이 경기장으로 잡히던 문제",
                "[개선] 빌린 경기장에 공연을 못 넣을 때 까닭을 보여 줘요(다음 경기까지 틈이 짧음 · 경기 중 · 밴드 없음)",
                "[수정] 의향서로 잡은 마지막 회차 보호 값을 세이브에 남겨, 불러온 뒤에도 그 회차를 덮어쓰지 않아요"),
            new("0.2.1", "2026-11-18",
                "[수정] 도시를 불러와도 창이 열리지 않던 문제(허브에서 '대기') · 공연 기록 저장에서 보안 예산이 빠져 있던 문제 · 0.2.0 으로 저장한 공연 기록도 읽어요"),
            new("0.2.0", "2026-11-18",
                "[신규] 일정: 지금 무대 · 다음 공연 표 · 지난 공연 · 공연장 주변(소음 · 치안 · 관객)을 게임 공연 데이터로 보여 줘요",
                "[신규] 차트: 밴드 인기 순위 · 1위 카드 · 인기 추이 · 명예의 전당 · 밴드 이름은 아티스트 40팀으로",
                "[신규] 흥행: 공연별 관객 · 입장권 수입 · 보안 비용 · 이익 정산서 · 달마다 흐름 · 흥행왕 · 공연 기록은 세이브에 남아요",
                "[신규] 예약: 다음 공연 밴드 · 1 ~ 3일 · 시작 시각을 잡고 밴드별 입장권을 바꿔요",
                "[신규] 예매: 공연 7일 전 20:00 오픈 · 오픈 전 예매 사이트 화면 · 몰리면 대기줄 · 포도알 좌석(앞자리부터 팔림) · 예매처 · 방금 예매한 주민 · 판 만큼만 관객이 와요",
                "[신규] 의향서: 아티스트 기획사가 며칠마다 공연개최의향서를 보내요 · 기획사마다 다른 문서와 인장 · 승인하면 다음 빈 칸에 자동으로 잡아요 · 10일 지나면 철회 · 예매처 단독 제안은 그 사이트에서만 팔아요",
                "[신규] 공연장: Match Day · 대학 경기장을 빌려 다음 경기 전 빈 틈에 인기 1위 밴드 공연을 잡아요 · 경기 일정 · 전적 · 상금 기록은 그대로 · 경기장 모양 좌석 · 30일 일정 띠",
                "[신규] 공연 중 소리: 공연장(경기장 포함) 관중 · 공연 소리가 끊기지 않고 더 멀리 들려요 · 공연이 열리는 동안 게임 배경음악을 줄였다가 끝나면 되돌려요",
                "[신규] 설정: 모드 허브 연결 · 퀵 버튼 자리 되돌리기 · 공연 시작 / 결과 알림 · 예매 오픈 날 · 한 사람 최대 매수 · 공연 소리 · 배경음악 · 공연 기록 지우기 · 자세한 로그"),
            new("0.1.0", "2026-11-18",
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
