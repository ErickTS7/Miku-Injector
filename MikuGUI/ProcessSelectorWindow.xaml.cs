using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;

namespace MikuGUI
{
    public partial class ProcessSelectorWindow : Window
    {
        public Process SelectedProcess { get; private set; }

        private List<ProcessInfo> _processes;
        private ICollectionView _processView;

        public ProcessSelectorWindow()
        {
            InitializeComponent();
            LoadProcesses();
        }

        private void LoadProcesses()
        {
            _processes = Process.GetProcesses()
                .Select(p =>
                {
                    try
                    {
                        return new ProcessInfo
                        {
                            ProcessName = p.ProcessName,
                            Id = p.Id,
                            Architecture = IsProcess64Bit(p) ? "x64" : "x86"
                        };
                    }
                    catch
                    {
                        return null;
                    }
                })
                .Where(p => p != null)
                .OrderBy(p => p.ProcessName)
                .ToList();

            _processView = CollectionViewSource.GetDefaultView(_processes);
            ProcessListView.ItemsSource = _processView;
        }

        // ===============================
        // Search filter
        // ===============================
        private void SearchTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            string filter = SearchTextBox.Text.ToLower();

            _processView.Filter = obj =>
            {
                if (obj is ProcessInfo p)
                    return p.ProcessName.ToLower().Contains(filter);

                return false;
            };
        }

        // ===============================
        // Select process
        // ===============================
        private void ProcessListView_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (ProcessListView.SelectedItem is ProcessInfo info)
            {
                SelectedProcess = Process.GetProcessById(info.Id);
                DialogResult = true;
                Close();
            }
        }

        // ===============================
        // Architecture detection
        // ===============================
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool IsWow64Process(
            System.IntPtr hProcess,
            out bool wow64Process
        );

        private static bool IsProcess64Bit(Process process)
        {
            if (!Environment.Is64BitOperatingSystem)
                return false;

            IsWow64Process(process.Handle, out bool isWow64);
            return !isWow64;
        }
    }
}
