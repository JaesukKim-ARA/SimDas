using Microsoft.Extensions.DependencyInjection;
using SimDas.Models.Analysis;
using SimDas.Services;
using SimDas.ViewModels;
using SimDas.Views;
using System.Windows;

namespace SimDas
{
    public partial class App : Application
    {
        private ServiceProvider _serviceProvider;

        public App()
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // Services
            services.AddSingleton<ILoggingService, LoggingService>();
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<IPlottingService, PlottingService>();
            services.AddSingleton<DAEAnalyzer>();

            // ViewModels
            services.AddTransient<MainViewModel>();
            services.AddTransient<InputViewModel>();
            services.AddTransient<SolverSettingsViewModel>();
            services.AddTransient<ResultViewModel>();
            services.AddTransient<LogViewModel>();
            services.AddTransient<SampleViewModel>();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var mainWindow = new MainWindow()
            {
                DataContext = _serviceProvider.GetRequiredService<MainViewModel>()
            };

            mainWindow.Show();
        }
    }
}
