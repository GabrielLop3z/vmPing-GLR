using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace vmPing.Classes
{
    public class PingStatistics : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private uint sent;
        private uint received;
        private uint lost;
        private uint error;

        // RTT (latency) Statistics
        private long minRtt = long.MaxValue;
        private long maxRtt;
        private long totalRtt;
        private uint rttCount;

        public uint Sent
        {
            get => sent;
            set { sent = value; OnPropertyChanged(); }
        }

        public uint Received
        {
            get => received;
            set { received = value; OnPropertyChanged(); }
        }

        public uint Lost
        {
            get => lost;
            set { lost = value; OnPropertyChanged(); }
        }

        public uint Error
        {
            get => error;
            set { error = value; OnPropertyChanged(); }
        }

        public long MinRtt
        {
            get => minRtt == long.MaxValue ? 0 : minRtt;
            set { minRtt = value == 0 ? long.MaxValue : value; OnPropertyChanged(); }
        }

        public long MaxRtt
        {
            get => maxRtt;
            set { maxRtt = value; OnPropertyChanged(); }
        }

        public double AvgRtt
        {
            get => rttCount > 0 ? (double)totalRtt / rttCount : 0;
            set { OnPropertyChanged(); }
        }

        public void AddRttSample(long rttMs)
        {
            if (rttMs < 0) return;
            if (rttMs < minRtt) minRtt = rttMs;
            if (rttMs > maxRtt) maxRtt = rttMs;
            totalRtt += rttMs;
            rttCount++;
            OnPropertyChanged(nameof(MinRtt));
            OnPropertyChanged(nameof(MaxRtt));
            OnPropertyChanged(nameof(AvgRtt));
        }

        public void Reset()
        {
            sent = received = lost = error = 0;
            minRtt = long.MaxValue;
            maxRtt = 0;
            totalRtt = 0;
            rttCount = 0;
            OnPropertyChanged(nameof(Sent));
            OnPropertyChanged(nameof(Received));
            OnPropertyChanged(nameof(Lost));
            OnPropertyChanged(nameof(Error));
            OnPropertyChanged(nameof(MinRtt));
            OnPropertyChanged(nameof(MaxRtt));
            OnPropertyChanged(nameof(AvgRtt));
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
