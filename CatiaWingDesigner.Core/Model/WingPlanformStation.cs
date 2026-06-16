using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CatiaWingDesigner.Core.Model
{
    public sealed class WingPlanformStation : INotifyPropertyChanged
    {
        private string _name = "Station";
        private double _spanY;
        private double _leadingEdgeX;
        private double _trailingEdgeX = 1000.0;
        private double _twistDeg;
        private double _dihedralDegFromPrevious;
        private AirfoilRef _airfoil = AirfoilRef.Naca("2412");

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Name
        {
            get => _name;
            set => SetValue(ref _name, value);
        }

        public double SpanY
        {
            get => _spanY;
            set => SetValue(ref _spanY, value);
        }

        public double LeadingEdgeX
        {
            get => _leadingEdgeX;
            set => SetValue(ref _leadingEdgeX, value);
        }

        public double TrailingEdgeX
        {
            get => _trailingEdgeX;
            set => SetValue(ref _trailingEdgeX, value);
        }

        public double TwistDeg
        {
            get => _twistDeg;
            set => SetValue(ref _twistDeg, value);
        }

        public double DihedralDegFromPrevious
        {
            get => _dihedralDegFromPrevious;
            set => SetValue(ref _dihedralDegFromPrevious, value);
        }

        public AirfoilRef Airfoil
        {
            get => _airfoil;
            set => SetValue(ref _airfoil, value);
        }

        public WingPlanformStation Clone()
        {
            return new WingPlanformStation
            {
                Name = Name,
                SpanY = SpanY,
                LeadingEdgeX = LeadingEdgeX,
                TrailingEdgeX = TrailingEdgeX,
                TwistDeg = TwistDeg,
                DihedralDegFromPrevious = DihedralDegFromPrevious,
                Airfoil = Airfoil.Clone()
            };
        }

        private void SetValue<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(storage, value))
            {
                return;
            }

            storage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
