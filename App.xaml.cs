using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace HyIO
{
    public partial class App : Application
    {
        // public static AppConfig Config { get; private set; } = null!;
        public static AppConfig Config { get; set; } = null!;


        public App()
        {
            // WPF UI 스레드에서 터지는 예외
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;

            // 백그라운드 스레드, 초기화 중 예외 등
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 기존처럼 설정 로드
            Config = ConfigManager.Load();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 앱 종료 시 마지막 설정 저장
            ConfigManager.Save(Config);
            base.OnExit(e);
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogException("DispatcherUnhandledException", e.Exception);

            MessageBox.Show(
                e.Exception.ToString(),
                "HyIO - Unhandled UI Exception",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            // e.Handled = true 로 하면 앱이 계속 살아있고,
            // false 로 하면 그대로 죽어요. 일단 살려두는 쪽으로.
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                LogException("CurrentDomain.UnhandledException", ex);

                MessageBox.Show(
                    ex.ToString(),
                    "HyIO - Fatal Exception",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void LogException(string kind, Exception ex)
        {
            try
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "HyIO_error.log");

                File.AppendAllText(path,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {kind}{Environment.NewLine}" +
                    $"{ex}{Environment.NewLine}" +
                    new string('-', 80) + Environment.NewLine);
            }
            catch
            {
                // 로깅 중에 또 죽는 건 무시
            }
        }
    }
}
