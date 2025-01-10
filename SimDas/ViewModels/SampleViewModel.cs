// EquationSampleViewModel.cs 파일 생성
using SimDas.ViewModels.Base;
using System;

namespace SimDas.ViewModels
{
    public class SampleViewModel : ViewModelBase
    {
        private readonly InputViewModel _inputViewModel;
        private bool _isUpdatingFromSample = false;
        private bool _isUserSelectingManual = false;

        public enum SampleEquationType
        {
            Manual,
            MassSpringDamper,
            Robertson,
            Bioreactor
        }

        private SampleEquationType _selectedSampleType = SampleEquationType.Manual;

        public SampleEquationType SelectedSampleType
        {
            get => _selectedSampleType;
            set
            {
                if (SetProperty(ref _selectedSampleType, value))
                {
                    if (!_isUserSelectingManual)  // 사용자 입력에 의한 Manual 전환이 아닐 때만
                    {
                        _isUserSelectingManual = value == SampleEquationType.Manual;
                        ApplySampleEquation();
                    }
                }
            }
        }

        public Array SampleTypes => Enum.GetValues(typeof(SampleEquationType));

        public SampleViewModel(InputViewModel inputViewModel)
        {
            _inputViewModel = inputViewModel;
            _inputViewModel.InputChanged += InputViewModel_InputChanged;
        }

        private void InputViewModel_InputChanged(object sender, EventArgs e)
        {
            if (!_isUpdatingFromSample && _selectedSampleType != SampleEquationType.Manual)
            {
                _isUserSelectingManual = true;  // 사용자 입력에 의한 Manual 전환임을 표시
                SelectedSampleType = SampleEquationType.Manual;
                _isUserSelectingManual = false;  // 플래그 초기화
            }
        }

        private void ApplySampleEquation()
        {
            _isUpdatingFromSample = true;
            try
            {
                switch (SelectedSampleType)
                {
                    case SampleEquationType.Manual:
                        if (_isUserSelectingManual)
                        {
                            _inputViewModel.Clear();
                        }
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
                }
            }
            finally
            {
                _isUpdatingFromSample = false;
                _isUserSelectingManual = false;  // 플래그 초기화
            }
        }

        private void ApplyMSDExample()
        {
            _inputViewModel.EquationInput = "// Mass-Spring-Damper Example\r\nder(x) = v\r\nder(v) = (-k*x - c*v)/m";
            _inputViewModel.ParameterInput = "k=2; c=0.5; m=1";
            _inputViewModel.InitialValueInput = "x=1; v=0";
            _inputViewModel.StartTime = 0;
            _inputViewModel.EndTime = 10;
        }

        private void ApplyRobertsonExample()
        {
            _inputViewModel.EquationInput = "// Robertson Example\r\nder(x)=-a*x+b*y*z\r\nder(y)=a*x-b*y*z-c*z*z\r\nz=1-x-y";
            _inputViewModel.ParameterInput = "a=4e-2; b=1e4; c=3e7";
            _inputViewModel.InitialValueInput = "x=1; y=0; z=0";
            _inputViewModel.StartTime = 0;
            _inputViewModel.EndTime = 100;
        }

        private void ApplyBioreactorExample()
        {
            _inputViewModel.EquationInput = "// Bioreactorder Example\r\nder(x) = mu * s/(1+s) * x\r\nder(s) = -(1/Yx) * mu * s/(1+s) * x\r\nder(p) = alpha * mu * s/(1+s) * x + beta * x";
            _inputViewModel.ParameterInput = "mu=0.2; Yx=0.5; alpha=2.0; beta=0.05";
            _inputViewModel.InitialValueInput = "x=0.1; s=10; p=0";
            _inputViewModel.StartTime = 0;
            _inputViewModel.EndTime = 24;
            _inputViewModel.SolverType = Models.Common.SolverType.ImplicitEuler;
        }
    }
}
