using System.IO;
using System.Xml.Serialization;
using ColossalFramework.IO;

namespace ConcertMod
{
    /// <summary>게임 전체 설정(세이브와 따로, %LOCALAPPDATA%\Colossal Order\Cities_Skylines\ConcertMod.xml): 모드 허브 연결 · 자기 퀵 버튼 자리 · 창 자리 · 업데이트 내역 판.</summary>
    [XmlRoot("ConcertMod")]
    public sealed class ConcertSetting : CSCommonSettingsXml
    {
        const string FileName = "ConcertMod.xml";

        public static ConcertSetting Current { get; private set; } = new ConcertSetting();

        static string FilePath => Path.Combine(DataLocation.localApplicationData, FileName);

        /// <summary>모드 허브 목록에 넣고 퀵 버튼을 허브에 맡김.</summary>
        public bool HubLinked = true;

        /// <summary>자기 퀵 버튼 자리(논리 좌표, 음수면 기본 자리). 허브가 없거나 연결을 껐을 때만 보임.</summary>
        public float LauncherX = -1f, LauncherY = -1f;

        /// <summary>머리줄을 끌어 옮긴 거리.</summary>
        public float WindowDX, WindowDY;

        /// <summary>예매: 공연 7일 전에 열고, 예매한 만큼만 관객이 와요(끄면 게임대로).</summary>
        public bool Ticketing = true;

        /// <summary>공연 중 공연 소리: 공연장(경기장 포함) 관중 · 공연 소리를 끊기지 않게 · 더 멀리까지.</summary>
        public bool ConcertSound = true;

        /// <summary>공연 중 배경음악: 공연이 열리는 동안 게임 배경음악을 줄여 공연 소리가 들리게 · 끝나면 원래대로.</summary>
        public bool ConcertMusic = true;

        /// <summary>알림: 공연이 시작하면 · 끝나면(관객 · 수입 · 성공) 화면 위 토스트.</summary>
        public bool NotifyStart = true, NotifyResult = true;

        /// <summary>예매 오픈: 공연 며칠 전(20:00) · 주민 한 명이 살 수 있는 최대 매수.</summary>
        public int TicketOpenDays = 7, TicketMax = 4;

        /// <summary>자세한 로그: 예약 · 의향서 · 대관을 output_log 에.</summary>
        public bool VerboseLog;

        public static void Load() => Current = Load<ConcertSetting>(FilePath);

        public static void Save() => Save(FilePath, Current);
    }
}
