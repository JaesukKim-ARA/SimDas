// EquationSampleViewModel.cs 파일 생성
using SimDas.Services;
using SimDas.ViewModels.Base;
using System;

namespace SimDas.ViewModels
{
    public class SampleViewModel : ViewModelBase
    {
        private readonly ILoggingService _loggingService;
        private readonly InputViewModel _inputViewModel;

        public enum SampleEquationType
        {
            Manual,
            MassSpringDamper,
            Robertson,
            Bioreactor,
            PendulumDAE,      // 새로운 예제 추가
            RLC_Circuit       // 새로운 예제 추가
        }

        private SampleEquationType _selectedSampleType = SampleEquationType.Manual;

        public SampleEquationType SelectedSampleType
        {
            get => _selectedSampleType;
            set
            {
                if (SetProperty(ref _selectedSampleType, value))
                {
                    ApplySampleEquation();
                }
            }
        }

        public Array SampleTypes => Enum.GetValues(typeof(SampleEquationType));

        public SampleViewModel(InputViewModel inputViewModel, ILoggingService loggingService)
        {
            _loggingService = loggingService;
            _inputViewModel = inputViewModel;
        }

        private void ApplySampleEquation()
        {
            _inputViewModel.Clear();

            switch (SelectedSampleType)
            {
                case SampleEquationType.Manual:
                    break;
                case SampleEquationType.MassSpringDamper:
                    ApplyMSDExample();                    
                    break;
                case SampleEquationType.Robertson:
                    ApplyRobertsonExample();
                    break;
                case SampleEquationType.Bioreactor:
                    ApplyBioreactorExample();
                    break;
                case SampleEquationType.PendulumDAE:
                    ApplyPendulumExample();
                    break;
                case SampleEquationType.RLC_Circuit:
                    ApplyRLCExample();
                    break;
            }
        }

        private void ApplyMSDExample()
        {
            _inputViewModel.ModelInput = @"// Mass-Spring-Damper System
// Variables
Real x, v;

// Parameters
parameter Real k = 2.0;    // spring constant
parameter Real c = 0.5;    // damping coefficient
parameter Real m = 1.0;    // mass

// Initial conditions
initial equation
x = 1.0;    // initial displacement
v = 0.0;    // initial velocity

// System equations
equation
der(x) = v;                  // velocity-position relation
der(v) = (-k*x - c*v)/m;    // Newton's second law";

            _inputViewModel.StartTime = 0;
            _inputViewModel.EndTime = 10;
        }

        private void ApplyRobertsonExample()
        {
            _inputViewModel.ModelInput = @"// Robertson Chemical Reaction System
// A -> B -> C
Real x, y, z;   // concentrations of species A, B, C

// Reaction rate constants
parameter Real a = 0.04;   // k1
parameter Real b = 1e4;    // k2
parameter Real c = 3e7;    // k3

// Initial conditions
initial equation
x = 1.0;    // initial concentration of A
y = 0.0;    // initial concentration of B
z = 0.0;    // initial concentration of C

// Mass balance equations
equation
der(x) = -a*x + b*y*z;           // rate of change of A
der(y) = a*x - b*y*z - c*z*z;    // rate of change of B
x + y + z = 1.0;                 // mass conservation";

            _inputViewModel.StartTime = 0;
            _inputViewModel.EndTime = 100;
        }

        private void ApplyBioreactorExample()
        {
            _inputViewModel.ModelInput = @"// Bioreactor System
// Variables
Real x;    // biomass concentration
Real s;    // substrate concentration
Real p;    // product concentration

// Parameters
parameter Real mu = 0.2;     // maximum specific growth rate
parameter Real Yx = 0.5;     // biomass yield coefficient
parameter Real alpha = 2.0;  // growth-associated product formation coefficient
parameter Real beta = 0.05;  // non-growth-associated product formation rate

// Initial conditions
initial equation
x = 0.1;    // initial biomass
s = 10.0;   // initial substrate
p = 0.0;    // initial product

// System equations
equation
der(x) = mu * s/(1+s) * x;                          // biomass growth
der(s) = -(1/Yx) * mu * s/(1+s) * x;               // substrate consumption
der(p) = alpha * mu * s/(1+s) * x + beta * x;      // product formation";

            _inputViewModel.StartTime = 0;
            _inputViewModel.EndTime = 24;
        }

        private void ApplyPendulumExample()
        {
            _inputViewModel.ModelInput = @"// Pendulum System (DAE formulation)
// Variables
Real x, y;    // cartesian coordinates
Real vx, vy;  // velocities
Real T;       // tension force
Real L = 1;   // pendulum length (constant)

// Parameters
parameter Real g = 9.81;    // gravitational acceleration
parameter Real m = 1.0;     // mass

// Initial conditions
initial equation
x = 0.7071;     // initial x position (pendulum at 45 degrees)
y = -0.7071;    // initial y position
vx = 0.0;       // initial x velocity
vy = 0.0;       // initial y velocity
T = m*g*1.414;  // initial tension

// System equations
equation
der(x) = vx;                  // x velocity
der(y) = vy;                  // y velocity
der(vx) = -(T/m)*x;          // x acceleration
der(vy) = -(T/m)*y + g;      // y acceleration
x*x + y*y = L*L;             // constraint equation";

            _inputViewModel.StartTime = 0;
            _inputViewModel.EndTime = 10;
        }

        private void ApplyRLCExample()
        {
            _inputViewModel.ModelInput = @"// RLC Circuit
// Variables
Real i;     // current
Real v;     // capacitor voltage
Real vL;    // inductor voltage
Real vR;    // resistor voltage

// Parameters
parameter Real L = 0.1;    // inductance (H)
parameter Real R = 100;    // resistance (Ω)
parameter Real C = 1e-6;   // capacitance (F)
parameter Real Vs = 10;    // source voltage (V)

// Initial conditions
initial equation
i = 0;      // initial current
v = 0;      // initial capacitor voltage
vL = 10;    // initial inductor voltage
vR = 0;     // initial resistor voltage

// Circuit equations
equation
der(i) = vL/L;                // inductor equation
der(v) = i/C;                 // capacitor equation
vR = R*i;                     // resistor equation
Vs = vR + vL + v;            // voltage loop equation";

            _inputViewModel.StartTime = 0;
            _inputViewModel.EndTime = 0.001;
        }
    }
}
