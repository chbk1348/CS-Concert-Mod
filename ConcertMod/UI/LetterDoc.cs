using System.Globalization;
using ColossalFramework;
using UnityEngine;

namespace ConcertMod.Data
{
    /// <summary>의향서 문서 문구 조립(화면 전용 · Letters 나머지는 Data/Letters.cs).</summary>
    internal static partial class Letters
    {
        /// <summary>문서 내용(정본 Concert.Letter 가 그림).</summary>
        public static CSCommonUX.Concert.Doc Doc(Letter l)
        {
            var band = Shows.BandOf(l.Band);
            string name = band?.Name ?? Artists.Of(l.Band), city = SimulationManager.instance.m_metaData?.m_CityName ?? "";
            ushort venue = VenueOf(l);
            string venueName = venue != 0 ? Singleton<BuildingManager>.instance.GetBuildingName(venue, InstanceID.Empty) ?? "" : "";
            int kind = KindOf(l), pop = band?.Pop ?? 0;
            string from = FromOf(l), en = l.Site >= 0 ? from : AgencyEn[Mathf.Clamp(l.Agency, 0, AgencyEn.Length - 1)];
            var t = l.Arrived;
            string seal = (from.Replace(" ", "").Replace("&", "") + "印印印印").Substring(0, 4);
            string price = "₡" + (l.Price / 100.0).ToString("0.00");
            var d = new CSCommonUX.Concert.Doc
            {
                Kind = kind,
                Band = band?.Color ?? Pal.Primary,
                Agency = from,
                AgencyEn = en,
                City = city,
                To = city + " 시장",
                Year = t.Year.ToString(),
                Seal = kind == 0 ? from : seal,
                Keys = new[] { "희망 공연장", "희망 일정", "희망 입장권", "예상 관객", l.Site >= 0 ? "예매처" : "시에 드릴 것" },
                Vals = new[]
                {
                    venueName.Length > 0 ? venueName : "공연장",
                    $"{l.Days}일 · 매일 {l.Hour:00}:00 시작",
                    price,
                    $"회차마다 약 {Tickets.SessionCap(venue):#,0}명 · 인기 {pop}",
                    l.Site >= 0 ? $"{from} 단독 예매" : "입장권 수입은 시에 그대로",
                },
            };
            switch (kind)
            {
                case 0:
                    d.No = $"{(from.Length > 2 ? from.Substring(0, 2) : from)}-공연 제{t.Year}-{l.Id:000}호";
                    d.Date = $"{t.Year}. {t.Month}. {t.Day}.";
                    d.Subject = l.Site >= 0 ? $"{name} 단독 공연 제안" : $"{name} 공연 개최 의향 알림";
                    d.Body = l.Site >= 0
                        ? new[] { "1. 귀 시의 무궁한 발전을 기원합니다.", $"2. 당사는 {name}의 공연을 귀 시에서 열고 표를 당사에서만 팔고자 아래와 같이 제안합니다." }
                        : new[] { "1. 귀 시의 무궁한 발전을 기원합니다.", $"2. 당사 소속 아티스트 {name}의 공연을 귀 시에서 열고자 아래와 같이 의향을 알립니다." };
                    break;
                case 1:
                    d.No = $"MRR-{t.Year}-{t.Month:00}{t.Day:00}";
                    d.Date = $"{t.Year}년 {t.Month}월 {t.Day}일";
                    d.Body = new[] { "늘 귀 도시의 발전을 기원합니다.", $"저희 {en}는 소속 아티스트 {name}의 공연을 귀 도시에서 열고자 합니다." };
                    break;
                case 2:
                    d.No = $"LOI/{t.Year % 100:00}-{l.Id:000}";
                    d.Date = t.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
                    d.Subject = $"Letter of Intent — {name} Live in {city}";
                    d.Body = new[] { $"We would be delighted to bring {name} to your city.", $"저희는 {name}의 공연을 귀 도시에서 열고 싶어요. 아래 조건을 검토해 주세요." };
                    d.Sign = Signers[l.Id % Signers.Length];
                    break;
                default:
                    d.No = $"memo #{l.Id}";
                    d.Date = $"{t:yyyy-MM-dd}";
                    d.Subject = $"{name} 공연 하고 싶어요";
                    d.Body = new[] { $"안녕하세요. 저희 {name}가 이 도시 무대에 서고 싶어요.", "크지 않아도 괜찮아요. 아래가 저희가 바라는 거예요." };
                    d.Sign = $"답 기다릴게요. — {name} 드림";
                    break;
            }
            return d;
        }
    }
}
