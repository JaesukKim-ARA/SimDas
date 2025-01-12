using System.Collections.Generic;
using System;
using System.Globalization;
using System.Windows.Data;
using SimDas.Models.Common;
using System.Windows;

namespace SimDas.Views.Converters
{
	public class InverseBooleanConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is bool boolValue)
            { 
				return !boolValue;
            }
			return value;
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is bool boolValue)
            {
				return !boolValue;
            }
			return value;
		}
	}

    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool booleanValue)
            {
                return booleanValue ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibilityValue)
            {
                return visibilityValue == Visibility.Visible ? true : false;
            }
            return false;
        }
    }

    public class InvertedBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool booleanValue)
            {
                return booleanValue ? Visibility.Collapsed : Visibility.Visible;
            }
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibilityValue)
            {
                return visibilityValue == Visibility.Collapsed ? true : false;
            }
            return false;
        }
    }

    public class SolverDescriptionConverter : IValueConverter
    {
        public readonly Dictionary<SolverType, string> SolverDescriptions = new()
        {
            {
                SolverType.ExplicitEuler,
                "ExplicitEuler is a first-order numerical method for solving ODEs. " +
                "It's simple but requires small step sizes for stability. " +
                "Best for non-stiff problems where accuracy isn't critical."
            },
            {
                SolverType.ImplicitEuler,
                "ImplicitEuler is a first-order A-stable method. " +
                "It's more stable than Explicit Euler but requires solving nonlinear equations. " +
                "Good for stiff problems despite lower accuracy."
            },
            {
                SolverType.RungeKutta4,
                "RungeKutta4 is a 4th-order Runge-Kutta method offering good accuracy. " +
                "It provides a good balance between computational cost and precision. " +
                "Recommended for non-stiff problems requiring moderate accuracy."
            },
            {
                SolverType.DASSL,
                "DASSL(Differential Algebraic System Solver) is a variable-order, variable-step BDF method. " +
                "It's highly efficient for stiff problems and DAEs. " +
                "Features automatic step size and order selection for optimal performance."
            }
        };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SolverType solverType && SolverDescriptions.TryGetValue(solverType, out string description))
                return description;

            return "No description available.";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class HalfSizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double originalSize)
            {
                return originalSize / 2.0; // 크기를 절반으로 줄임
            }
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}