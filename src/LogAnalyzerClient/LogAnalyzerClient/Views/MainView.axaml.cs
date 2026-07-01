
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using LogAnalyzerClient.Helpers;
using LogAnalyzerClient.Models;
using LogAnalyzerClient.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.JavaScript;

namespace LogAnalyzerClient.Views
{
    public partial class MainView : UserControl
    {
        public MainView()
        {
            InitializeComponent();

            Loaded += (sender, e) =>
            {
                if (DataContext is MainViewModel viewModel)
                {
                    if (TopLevel.GetTopLevel(this) is Window owner)
                    {
                        viewModel.DialogHelper = new DesktopDialogHelper(owner);
                    }
                    else if (OperatingSystem.IsBrowser())
                    {
                        Console.WriteLine("Browser environment detected.");
                        viewModel.DialogHelper = new BrowserDialogHelper();
                    }
                }
                else
                {
                    Console.Error.WriteLine("Error: DataContext is not MainViewModel.");
                }
            };

            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime)
            {
                Console.WriteLine("Non-desktop environment detected.");
                ExitMenuItem.IsEnabled = false;
            }
            else
            {
                Console.WriteLine("Desktop environment detected.");
            }
        }

        private void ExitMenuItem_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
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
    }
}