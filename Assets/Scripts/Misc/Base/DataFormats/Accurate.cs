namespace MajdataPlay
{
    public readonly struct Accurate
    {
        public double DX { get; init; }
        public double Classic { get; init; }
        public Accurate Update(Accurate newRecord)
        {
            double dx = 0;
            double classic = 0;

            dx = newRecord.DX > DX ? newRecord.DX : DX;
            classic = newRecord.Classic > Classic ? newRecord.Classic : Classic;

            return new Accurate()
            {
                DX = dx,
                Classic = classic,
            };
        }
        public static bool operator >(Accurate left, Accurate right)
        {
            var dx = left.DX > right.DX;
            var classic = left.Classic > right.Classic;

            return dx || classic;
        }
        public static bool operator ==(Accurate left, Accurate right)
        {
            var dx = left.DX == right.DX;
            var classic = left.Classic == right.Classic;

            return dx && classic;
        }
        public static bool operator <(Accurate left, Accurate right) => !(left > right);
        public static bool operator !=(Accurate left, Accurate right) => !(left == right);
    }
}
