using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using LogAnalyzerClient.Helpers;
using LogAnalyzerClient.Models;
using LogAnalyzerClient.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LogAnalyzerClient.Views
{
    public partial class MainView : UserControl
    {
        private const string DefaultServerUrl = "http://localhost:5000";

        public MainView()
        {
            InitializeComponent();

            Loaded += (_, _) =>
            {
                if (DataContext is MainViewModel viewModel)
                {
                    if (TopLevel.GetTopLevel(this) is Window owner)
                    {
                        viewModel.DialogHelper = new DesktopDialogHelper(owner);
                    }
                    else if (OperatingSystem.IsBrowser())
                    {
                        viewModel.DialogHelper = new BrowserDialogHelper();
                    }
                }
            };

            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime)
            {
                ExitMenuItem.IsEnabled = false;
            }
        }

        private void ExitMenuItem_Click(object? sender, RoutedEventArgs e)
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }

        private void LogFileListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not MainViewModel viewModel || sender is not ListBox listBox)
            {
                return;
            }

            var selectedNames = listBox.SelectedItems?
                .OfType<LogFileItem>()
                .Select(item => item.FileName)
                .ToList() ?? new List<string>();
            viewModel.SelectedFiles = selectedNames;
        }

        private void OnOpenAdminClick(object? sender, RoutedEventArgs e)
        {
            var serverUrl = GetServerUrl();
            var window = new AdminTokenWindow(serverUrl);
            if (TopLevel.GetTopLevel(this) is Window owner)
            {
                window.Show(owner);
            }
            else
            {
                window.Show();
            }
        }

        private void OnShowTopologyClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel viewModel)
            {
                return;
            }

            if (viewModel.SelectedLogFile is null || string.IsNullOrEmpty(viewModel.SelectedLogFile.FileName))
            {
                _ = viewModel.DialogHelper.ShowMessageDialogAsync("Warning", "请先在左侧选择一个日志文件。");
                return;
            }

            var window = new TopologyWindow(GetServerUrl(), viewModel.SelectedLogFile.FileName);
            if (TopLevel.GetTopLevel(this) is Window owner)
            {
                window.Show(owner);
            }
            else
            {
                window.Show();
            }
        }

        private string GetServerUrl()
        {
            if (DataContext is MainViewModel { CurrentAddress: { Length: > 0 } address })
            {
                return address;
            }
            return DefaultServerUrl;
        }
    }
}
