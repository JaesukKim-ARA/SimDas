using SimDas.ViewModels.Base;
using SimDas.Services;
using SimDas.Parser;
using System.Collections.Generic;
using System.Linq;
using System;
using SimDas.Models.Common;
using SimDas.Models.Parser;

namespace SimDas.ViewModels
{
    public class InputViewModel : ViewModelBase
    {
        private readonly ILoggingService _loggingService;
        private readonly ModelParser _modelParser;
        private string _modelInput = string.Empty;
        private ParsedModel _currentModel;
        private double _startTime;
        private double _endTime = 10.0;
        private bool _isValid;

        public event EventHandler InputChanged;

        public string ModelInput
        {
            get => _modelInput;
            set => SetProperty(ref _modelInput, value, ValidateInput);
        }

        public bool IsValid
        {
            get => _isValid;
            private set => SetProperty(ref _isValid, value);
        }

        public double StartTime
        {
            get => _startTime;
            set => SetProperty(ref _startTime, value, ValidateInput);
        }

        public double EndTime
        {
            get => _endTime;
            set => SetProperty(ref _endTime, value, ValidateInput);
        }

        public InputViewModel(ILoggingService loggingService)
        {
            _loggingService = loggingService;
            _modelParser = new ModelParser(_loggingService);
        }

        private void ValidateInput()
        {
            try
            {
                IsValid = false;
                _currentModel = _modelParser.ParseModel(ModelInput);
                IsValid = _currentModel.IsValid;
            }
            catch (Exception ex)
            {
                _loggingService.Error($"Model validation error: {ex.Message}");
                IsValid = false;
            }
        }

        public (DAESystem daeSystem, int dimension) ParseEquations()
        {
            return _modelParser.CreateDAESystem();
        }

        public double[] GetInitialState()
        {
            if (_currentModel == null || !IsValid)
                throw new InvalidOperationException("No valid model available");

            var initialState = new double[_currentModel.Variables.Count];
            foreach (var ic in _currentModel.InitialConditions)
            {
                var variable = _currentModel.Variables[ic.VariableName];
                initialState[variable.Index] = ic.Value;
            }

            _loggingService.Debug($"GetInitialState - InitialState: {string.Join(", ", initialState)}");
            return initialState;
        }

        public List<string> GetVariableNames()
        {
            var variableNames = _currentModel?.Variables.Values
                .OrderBy(v => v.Index)
                .Select(v => v.Name)
                .ToList() ?? new List<string>();

            _loggingService.Debug($"GetVariableNames - VariableNames: {string.Join(", ", variableNames)}");
            return variableNames;
        }

        public Dictionary<string, double> GetParameters()
        {
            if (_currentModel == null || !IsValid)
                throw new InvalidOperationException("No valid model available");

            var parameters = _currentModel.Parameters.ToDictionary(
                p => p.Key,
                p => p.Value.Value);

            _loggingService.Debug($"GetParameters - Parameters: {string.Join(", ", parameters.Select(kvp => $"{kvp.Key}={kvp.Value}"))}");

            return parameters;
        }

        public void Clear()
        {
            ModelInput = string.Empty;
            if (_currentModel != null)
            {
                _currentModel.Clear();
            }
            _currentModel = new ParsedModel();  // 새로운 빈 모델로 초기화
            IsValid = false;
            _modelParser.Reset();

            // 로깅 추가
            _loggingService.Debug("InputViewModel cleared and reset");
        }
    }
}