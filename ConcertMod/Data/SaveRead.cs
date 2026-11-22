using System.IO;

namespace ConcertMod.Data
{
    /// <summary>
    /// 세이브 읽기 도우미. 형식 번호를 올리지 않고 값을 더할 때는 맨 끝에만 덧붙이고, 읽을 때 More 로 남았는지 봄(없으면 기본값 · 이미 있는 byte 는 바꾸지 않음).
    /// </summary>
    internal static class SaveRead
    {
        /// <summary>읽을 자료가 bytes 바이트 이상 남았나.</summary>
        public static bool More(BinaryReader r, int bytes) => r.BaseStream.Length - r.BaseStream.Position >= bytes;
    }
}
