using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace HyIO
{
    public partial class App : Application
    {
        private const string SingleInstanceMutexName = @"Local\HyIO.SingleInstance";
        private const string ActivateExistingEventName = @"Local\HyIO.ActivateExisting";

        private Mutex _singleInstanceMutex;
        private EventWaitHandle _activateExistingEvent;
        private Thread _activateListenerThread;
        private volatile bool _isShuttingDown;

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
            _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool isFirstInstance);

            if (!isFirstInstance)
            {
                TrySignalExistingInstance();
                Shutdown();
                return;
            }

            Config = ConfigManager.Load();

            _activateExistingEvent = new EventWaitHandle(
                initialState: false,
                EventResetMode.AutoReset,
                ActivateExistingEventName);

            StartActivateListener();

            base.OnStartup(e);

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _isShuttingDown = true;

            try
            {
                _activateExistingEvent?.Set();
            }
            catch
            {
            }

            _activateListenerThread?.Join(300);
            _activateExistingEvent?.Dispose();

            if (Config != null)
            {
                ConfigManager.Save(Config);
            }

            try
            {
                _singleInstanceMutex?.ReleaseMutex();
            }
            catch
            {
            }

            _singleInstanceMutex?.Dispose();
            base.OnExit(e);
        }

        private void TrySignalExistingInstance()
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    using var activateEvent = EventWaitHandle.OpenExisting(ActivateExistingEventName);
                    activateEvent.Set();
                    return;
                }
                catch
                {
                    Thread.Sleep(120);
                }
            }
        }

        private void StartActivateListener()
        {
            _activateListenerThread = new Thread(() =>
            {
                while (!_isShuttingDown)
                {
                    bool signaled;

                    try
                    {
                        signaled = _activateExistingEvent.WaitOne();
                    }
                    catch
                    {
                        break;
                    }

                    if (!signaled || _isShuttingDown)
                        continue;

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (Current?.MainWindow is MainWindow mainWindow)
                        {
                            mainWindow.ActivateFromExternalLaunch();
                        }
                    }));
                }
            })
            {
                IsBackground = true,
                Name = "HyIO Activate Listener"
            };

            _activateListenerThread.SetApartmentState(ApartmentState.MTA);
            _activateListenerThread.Start();
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
