using CSCommonUX.UI;

namespace ConcertMod.UI
{
    /// <summary>인게임 설정 패널(CSCommon): 모드 허브 연결.</summary>
    public sealed class OptionsPanel : CSCommonOptionsPanel
    {
        protected override string LogoIcon => YukiModBoxEntry.Icon;

        protected override void Build()
        {
            var main = AddPage("일반");
            var s = ConcertSetting.Current;
            main.Section("퀵 버튼", null, Pal.Primary).Toggle("모드 허브 연결", "켜면 모드 허브가 있을 때 이 모드가 허브 목록에 들어가요. 끄거나 허브가 없으면 화면 왼쪽 퀵 버튼으로 창을 열어요.", () => s.HubLinked, v =>
            {
                s.HubLinked = v;
                ConcertSetting.Save();
            });
        }
    }
}
