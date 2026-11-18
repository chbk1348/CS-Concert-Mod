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

        public static void Load() => Current = Load<ConcertSetting>(FilePath);

        public static void Save() => Save(FilePath, Current);
    }
}
