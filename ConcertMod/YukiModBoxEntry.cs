// 모드 허브 연결 계약 v1. 원본: CS-YukiModBox/contract/YukiModBoxEntry.cs — 고치면 원본도 같이.
// 허브(YukiModBox.dll)가 이 모드 어셈블리에서 이름 'YukiModBoxEntry'(전역 이름공간)로 찾아 리플렉션으로 부름.
// 규칙: 정적 멤버 · 기본 형만(CSCommon 형식은 DLL 을 건너지 못함). 본 스레드에서만 불림. 던져도 허브가 잡음.
// 이 모드: 콘서트 Extension(공연 창, 허브를 숨기는 도구 아님).
public static class YukiModBoxEntry
{
    /// <summary>계약 판. 이름 · 뜻이 바뀌면 올림(덧붙이기만 하면 그대로).</summary>
    public const int ContractVersion = 1;

    /// <summary>바뀌지 않는 키(어셈블리 이름과 같게). 허브 설정의 순서 · 숨김이 이것으로 기억됨.</summary>
    public static string Id => "CS-Concert-Mod";

    /// <summary>목록 이름(판 없이).</summary>
    public static string Name => "콘서트 Extension";

    /// <summary>아이콘 SVG path(24 viewBox, 음표 두 개 획).</summary>
    public static string Icon => "M9 18V5l12-2v13M6 21a3 3 0 1 0 0-6 3 3 0 0 0 0 6zM18 19a3 3 0 1 0 0-6 3 3 0 0 0 0 6z";

    /// <summary>강조색 이름(CSCommonPalette 이름). 색 값을 쓰지 않음(CSC001).</summary>
    public static string Accent => "Violet";

    /// <summary>열 수 있음(도시를 불러와 본체가 붙음). false 면 허브 행이 '대기'.</summary>
    public static bool Ready => ConcertMod.ConcertController.Instance != null;

    /// <summary>창이 열려 있음.</summary>
    public static bool IsOpen => Ready && ConcertMod.ConcertController.Instance.Window.Visible;

    /// <summary>열기 · 닫기.</summary>
    public static void Toggle() => ConcertMod.ConcertController.Instance?.Window.Toggle();

    /// <summary>true 인 동안 허브 퀵 버튼 · 패널을 숨김. 선택(없으면 false).</summary>
    public static bool HideHub => false;

    /// <summary>선택: 모드 설정의 '모드 허브 연결' 스위치 값. false 면 허브 목록에서 빠지고 자기 퀵 버튼이 돌아옴.</summary>
    public static bool HubLinked => ConcertMod.ConcertSetting.Current.HubLinked;

    /// <summary>허브가 씀: true 면 이 모드의 자기 퀵 버튼을 그리지도 막지도 않음.</summary>
    public static bool HubOwnsLauncher = false;
}
