using ConcertMod.Data;
using UnityEngine;

namespace ConcertMod.UI
{
    /// <summary>예약 탭 입력 값(밴드 · 공연장 · 기간 · 시각)과 잡힌 계획을 덮거나 풀지 묻는 확인 창 상태.</summary>
    internal sealed class BookingForm
    {
        /// <summary>고른 밴드 · 시작 시각(null · -1 = 그릴 때 그 공연장 계획 또는 게임 값으로 채움).</summary>
        public string Band;
        public int Days = Booking.MinDays, Hour = -1;

        /// <summary>고른 공연장(페스티벌 구역 · 빌린 경기장, 0 = 아직 안 고름).</summary>
        public ushort Venue { get; private set; }

        /// <summary>확인 창: 떠 있음 · 풀기(아니면 덮기) · 뜬 때(열림 움직임).</summary>
        public bool Confirming, ConfirmClear;
        public float ConfirmAt;

        public void Ask(bool clear)
        {
            Confirming = true;
            ConfirmClear = clear;
            ConfirmAt = Time.realtimeSinceStartup;
        }

        /// <summary>열린 확인 창 닫기(창 닫기 · Esc). 닫았으면 true.</summary>
        public bool CloseConfirm()
        {
            if (!Confirming) return false;
            Confirming = false;
            return true;
        }

        /// <summary>공연장 바꾸기: 그 공연장에 잡힌 계획 값(없으면 기본값)으로 밴드 · 시각 · 기간을 다시 채우고 확인 창 닫기.</summary>
        public void SetVenue(ushort venue)
        {
            if (venue == Venue) return;
            Venue = venue;
            Reset();
            Days = Booking.At(venue)?.Days ?? Booking.MinDays;
            Confirming = false;
        }

        /// <summary>기본값으로(밴드 · 시각은 그릴 때 다시 채움).</summary>
        public void Reset()
        {
            Band = null;
            Hour = -1;
            Days = Booking.MinDays;
        }
    }
}
