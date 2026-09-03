
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Media;
using LogAnalyzerClient.Converters;
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
        public MainView()
        {
            InitializeComponent();
            BuildResultGridColumns();

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

        private void BuildResultGridColumns()
        {
            var columns = new (string Header, string? Property, double Width, bool Fill)[]
            {
                ("Line", "LineNo", 64, false),
                ("Timestamp", "Timestamp", 176, false),
                ("PodName", "PodName", 124, false),
                ("Severity", null, 96, false),
                ("EventType", "EventType", 88, false),
                ("RequestId", "RequestId", 148, false),
                ("TargetService", "TargetService", 128, false),
                ("DurationMs", "DurationMs", 92, false),
                ("Method", "Method", 76, false),
                ("Path", "Path", 150, false),
                ("StatusCode", "StatusCode", 88, false),
                ("ExceptionName", "ExceptionName", 220, false),
                ("ExceptionMessage", "ExceptionMessage", 0, true),
            };

            foreach (var (header, property, width, fill) in columns)
            {
                DataGridColumn column;
                if (property is null)
                {
                    // Severity column: render a colored pill per severity.
                    var templateColumn = new DataGridTemplateColumn
                    {
                        Header = header,
                        Width = fill ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(width),
                        IsReadOnly = true,
                    };
                    templateColumn.CellTemplate = new FuncDataTemplate<LogEntryRow>(
                        (_, _) => BuildSeverityPill(),
                        supportsRecycling: false);
                    column = templateColumn;
                }
                else
                {
                    column = new DataGridTextColumn
                    {
                        Header = header,
                        Width = fill ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(width),
                        IsReadOnly = true,
                        Binding = new Binding(property),
                    };
                }
                ResultEntryGrid.Columns.Add(column);
            }
        }

        private static Control BuildSeverityPill()
        {
            var text = new TextBlock
            {
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brushes.White,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Thickness(0, 1),
            };
            text.Bind(TextBlock.TextProperty, new Binding("Severity"));

            var pill = new Border
            {
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(8, 1),
                Margin = new Thickness(4, 2),
                Background = Brushes.Transparent,
                Child = text,
            };
            pill.Bind(Border.BackgroundProperty,
                new Binding("Severity") { Converter = new SeverityBrushConverter() });
            return pill;
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