using System.Runtime.InteropServices;

namespace SantanderTest.Api.Objects
{
    [StructLayout(LayoutKind.Auto)]
    public readonly record struct NewsPriority(int Score, long Time, int Id) : IComparable<NewsPriority>
    {
        public int CompareTo(NewsPriority other)
        {
            var byScore = Score.CompareTo(other.Score);
            if(byScore != 0) return byScore;
            var byTime = Time.CompareTo(other.Time);
            if(byTime != 0) return byTime;
            return Id.CompareTo(other.Id);
        }
        public static bool operator <(NewsPriority left, NewsPriority right) => left.CompareTo(right) < 0;
        public static bool operator <=(NewsPriority left, NewsPriority right) => left.CompareTo(right) <= 0;
        public static bool operator >(NewsPriority left, NewsPriority right) => left.CompareTo(right) > 0;
        public static bool operator >=(NewsPriority left, NewsPriority right) => left.CompareTo(right) >= 0;
    }
}