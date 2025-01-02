using SimDas.Models.Common;
using ScottPlot;
using ScottPlot.Plottables;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;

namespace SimDas.Services
{
    public interface IPlottingService
    {
        Plot CreatePlot(string title, string xLabel, string yLabel);
        void ConfigurePlot(Plot plot, bool showLegend = true, bool showGrid = true);
        void SavePlot(Plot plot, string filename);
        void ExportToCsv(Solution solution, List<string> variableNames, string filename);
        void DisplayErrorAnalysis(Plot plot, ErrorAnalysis errorAnalysis);
        void UpdateTimeIndicator(Plot plot, VerticalLine indicator, double time);
    }

    public class PlottingService : IPlottingService
    {
        private readonly IPalette _colorPalette;
        private readonly ILoggingService _loggingService;

        public PlottingService(ILoggingService loggingService)
        {
            _loggingService = loggingService;
            _colorPalette = new ScottPlot.Palettes.Category10();
        }

        public Plot CreatePlot(string title, string xLabel, string yLabel)
        {
            var plot = new Plot();

            plot.Title(title);
            plot.XLabel(xLabel);
            plot.YLabel(yLabel);

            return plot;
        }

        public void ConfigurePlot(Plot plot, bool showLegend = true, bool showGrid = true)
        {
            if (showLegend)
            {
                plot.Legend.IsVisible = true;
                plot.Legend.Alignment = Alignment.UpperRight;
            }

            if (showGrid)
            {
                
                plot.Grid.MajorLineColor = new Color(0, 0, 0, 50);
                plot.Grid.MinorLineColor = new Color(0, 0, 0, 25);

                plot.Grid.MajorLineWidth = 1;
                plot.Grid.MinorLineWidth = 1;
            }
        }

        public void SavePlot(Plot plot, string filename)
        {
            try
            {
                plot.SavePng(filename, 800, 600);
                _loggingService.Info($"Plot saved to {filename}");
            }
            catch (Exception ex)
            {
                _loggingService.Error($"Failed to save plot: {ex.Message}");
                throw;
            }
        }

        public void ExportToCsv(Solution solution, List<string> variableNames, string filename)
        {
            try
            {
                using var writer = new StreamWriter(filename);

                // Write header
                writer.Write("Time");
                foreach (var name in variableNames ??
                    Enumerable.Range(0, solution.States[0].Length).Select(i => $"State_{i}"))
                {
                    writer.Write($",{name},d{name}/dt");
                }
                writer.WriteLine();

                // Write data
                for (int i = 0; i < solution.TimePoints.Count; i++)
                {
                    writer.Write(solution.TimePoints[i].ToString("G"));
                    for (int j = 0; j < solution.States[0].Length; j++)
                    {
                        writer.Write($",{solution.States[i][j]:G},{solution.Derivatives[i][j]:G}");
                    }
                    writer.WriteLine();
                }

                _loggingService.Info($"Data exported to {filename}");
            }
            catch (Exception ex)
            {
                _loggingService.Error($"Failed to export data: {ex.Message}");
                throw;
            }
        }

        public void DisplayErrorAnalysis(Plot plot, ErrorAnalysis errorAnalysis)
        {
            plot.Clear();
            plot.Title("Error Analysis");

            // Local Error plot
            var timePoints = Enumerable.Range(0, errorAnalysis.LocalErrors.Count).Select(i => (double)i).ToArray();
            var localErrors = errorAnalysis.LocalErrors.ToArray();

            var scatter = plot.Add.Scatter(timePoints, localErrors);
            scatter.LegendText = "Local Error";

            // RMSE and MAE horizontal lines
            var rmse = plot.Add.HorizontalLine(errorAnalysis.RootMeanSquareError);
            rmse.Color = Colors.Red;
            rmse.LegendText = $"RMSE: {errorAnalysis.RootMeanSquareError:E3}";

            var mae = plot.Add.HorizontalLine(errorAnalysis.MeanAbsoluteError);
            mae.Color = Colors.Green;
            mae.LegendText = $"MAE: {errorAnalysis.MeanAbsoluteError:E3}";

            plot.Legend.IsVisible = true;
            plot.XLabel("Time Step");
            plot.YLabel("Error");
        }

        public void UpdateTimeIndicator(Plot plot, VerticalLine indicator, double time)
        {
            if (indicator != null)
            {
                indicator.X = time;
                indicator.IsVisible = true;
            }
        }
    }
}