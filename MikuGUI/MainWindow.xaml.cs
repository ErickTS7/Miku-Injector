using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace MikuGUI
{
    public partial class MainWindow : Window
    {

        private string _selectedDllPath;
        private Process _selectedProcess;


        private enum BinaryArchitecture
        {
            Unknown,
            x86,
            x64
        }


        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool IsWow64Process(IntPtr hProcess, out bool wow64Process);


        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        private delegate bool InjectDelegate(
            uint pid,
            string dllPath,
            StringBuilder errorMsg,
            int errorMsgSize
        );


        public MainWindow()
        {
            InitializeComponent();
            UpdateInjectButtonState();
            LogInfo("Miku Injector initialized");
        }


        private BinaryArchitecture GetDllArchitecture(string dllPath)
        {
            try
            {
                using var fs = new FileStream(dllPath, FileMode.Open, FileAccess.Read);
                using var br = new BinaryReader(fs);

                fs.Seek(0x3C, SeekOrigin.Begin);
                int peOffset = br.ReadInt32();

                fs.Seek(peOffset + 4, SeekOrigin.Begin);
                ushort machine = br.ReadUInt16();

                return machine switch
                {
                    0x014c => BinaryArchitecture.x86,
                    0x8664 => BinaryArchitecture.x64,
                    _ => BinaryArchitecture.Unknown
                };
            }
            catch
            {
                return BinaryArchitecture.Unknown;
            }
        }


        private bool IsProcess64Bit(Process process)
        {
            if (!Environment.Is64BitOperatingSystem)
                return false;

            IsWow64Process(process.Handle, out bool wow64);
            return !wow64;
        }


        private string ExtractResource(string resourceName, string fileName)
        {
            string path = Path.Combine(Path.GetTempPath(), fileName);

            if (File.Exists(path))
                return path;

            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(resourceName);

            if (stream == null)
                throw new Exception($"Resource not found: {resourceName}");

            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            stream.CopyTo(fs);

            return path;
        }

        private string EnsureInjector64()
            => ExtractResource(
                "MikuGUI.Injectors.InjectorCore64.dll",
                "InjectorCore64.dll"
            );

        private string EnsureHelper32()
            => ExtractResource(
                "MikuGUI.Injectors.MikuHelper.exe",
                "MikuHelper.exe"
            );


        private bool Inject64(uint pid, string dllPath, out string error)
        {
            error = string.Empty;

            string injectorPath = EnsureInjector64();

            IntPtr hModule = LoadLibrary(injectorPath);
            if (hModule == IntPtr.Zero)
            {
                error = "LoadLibrary failed (x64 injector)";
                return false;
            }

            IntPtr proc = GetProcAddress(hModule, "InjectDLL");
            if (proc == IntPtr.Zero)
            {
                error = "InjectDLL export not found";
                return false;
            }

            var inject = (InjectDelegate)Marshal.GetDelegateForFunctionPointer(
                proc, typeof(InjectDelegate));

            var errBuf = new StringBuilder(256);

            bool result = inject(pid, dllPath, errBuf, errBuf.Capacity);

            if (!result)
                error = errBuf.ToString();

            return result;
        }


        private bool Inject32(uint pid, string dllPath, out string error)
        {
            error = string.Empty;

            string helperPath = EnsureHelper32();

            var psi = new ProcessStartInfo
            {
                FileName = helperPath,
                Arguments = $"{pid} \"{dllPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            proc.WaitForExit();

            if (proc.ExitCode != 0)
            {
                error = $"helper32 failed (exit {proc.ExitCode})";
                return false;
            }

            return true;
        }

        private async void InjectButton_Click(object sender, RoutedEventArgs e)
        {
            if (!File.Exists(_selectedDllPath))
            {
                LogError("DLL not found");
                return;
            }

            bool proc64 = IsProcess64Bit(_selectedProcess);
            var dllArch = GetDllArchitecture(_selectedDllPath);

            LogInfo($"Process: {(proc64 ? "x64" : "x86")} | DLL: {dllArch}");

            await Task.Run(() =>
            {
                bool success = false;
                string error = string.Empty;

                if (proc64 && dllArch == BinaryArchitecture.x64)
                {
                    success = Inject64((uint)_selectedProcess.Id, _selectedDllPath, out error);
                }
                else if (!proc64 && dllArch == BinaryArchitecture.x86)
                {
                    success = Inject32((uint)_selectedProcess.Id, _selectedDllPath, out error);
                }
                else
                {
                    error = "Architecture mismatch";
                }

                // Atualiza a UI no thread correto
                Dispatcher.Invoke(() =>
                {
                    if (success)
                        LogInfo("Injection successful");
                    else
                        LogError(error);
                });
            });
        }



        private bool CanInject()
            => !string.IsNullOrEmpty(_selectedDllPath) && _selectedProcess != null;

        private void UpdateInjectButtonState()
            => InjectButton.IsEnabled = CanInject();

        private void LogInfo(string msg)
        {
            LogTextBox.AppendText($"[INFO] {msg}\n");
            LogTextBox.ScrollToEnd();
        }

        private void LogError(string msg)
        {
            LogTextBox.AppendText($"[ERROR] {msg}\n");
            LogTextBox.ScrollToEnd();
        }

        private void SelectDllButton_Click(object sender, RoutedEventArgs e)
{
    var dialog = new OpenFileDialog
    {
        Filter = "DLL Files (*.dll)|*.dll",
        Title = "Select DLL to inject"
    };

    if (dialog.ShowDialog() == true)
    {
        _selectedDllPath = dialog.FileName;
        SelectedDllText.Text = _selectedDllPath;
        LogInfo($"DLL selected: {_selectedDllPath}");
    }

    UpdateInjectButtonState();
}

private void SelectProcessButton_Click(object sender, RoutedEventArgs e)
{
    var window = new ProcessSelectorWindow { Owner = this };

    if (window.ShowDialog() == true)
    {
        _selectedProcess = window.SelectedProcess;
        SelectedProcessText.Text =
            $"  Target: {_selectedProcess.ProcessName} ({_selectedProcess.Id})";

        LogInfo($"Process selected: {_selectedProcess.ProcessName} ({_selectedProcess.Id})");
    }

    UpdateInjectButtonState();
}

    }
}
