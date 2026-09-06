using Avalonia.Controls;
using Google.Protobuf;
using System;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace LogAnalyzerClient.Helpers
{
    internal interface IDialogHelper
    {
        Task<ConnectInfo?> ShowConnectDialogAsync(string currentAddress, string token);
        Task<string> ShowSaveAnalysisResultDialogAsync(string fileName);
        Task ShowMessageDialogAsync(string title, string message);
    }

    internal class NullDialogHelper : IDialogHelper
    {
        public Task<ConnectInfo?> ShowConnectDialogAsync(string currentAddress, string token)
        {
            throw new ClientInternalException("Unknown error: No Window owner.");
        }

        public Task<string> ShowSaveAnalysisResultDialogAsync(string fileName)
        {
            throw new ClientInternalException("Unknown error: No Window owner.");
        }

        public Task ShowMessageDialogAsync(string title, string message)
        {
            throw new ClientInternalException("Unknown error: No Window owner.");
        }
    }

    internal class DesktopDialogHelper : IDialogHelper
    {
        private readonly Window _owner;

        public DesktopDialogHelper(Window owner)
        {
            _owner = owner;
        }

        public async Task<ConnectInfo?> ShowConnectDialogAsync(string currentAddress, string token)
        {
            var dialog = new ConnectDialog(currentAddress, token);
            return await dialog.ShowDialog<ConnectInfo>(_owner);
        }

        public async Task<string> ShowSaveAnalysisResultDialogAsync(string fileName)
        {
            var dialog = new SaveAnalysisResultDialog(fileName);
            return await dialog.ShowDialog<string>(_owner);
        }

        public async Task ShowMessageDialogAsync(string title, string message)
        {
            var dialog = new MessageDialog(title, message);
            await dialog.ShowDialog(_owner);
        }
    }

    [SupportedOSPlatform("browser")]
    internal class BrowserDialogHelper : IDialogHelper
    {
        public async Task<ConnectInfo?> ShowConnectDialogAsync(string currentAddress, string token)
        {
            return await Task.Run(() =>
            {
                var address = BrowserInterop.Prompt("Please input the address of Agent:", currentAddress);
                var tokenInput = BrowserInterop.Prompt("Please input the token for authentication:", token);
                if (address == null || tokenInput == null)
                {
                    return null;
                }
                return new ConnectInfo(address, tokenInput);
            });
        }

        public Task<string> ShowSaveAnalysisResultDialogAsync(string fileName)
        {
            throw new PlatformNotSupportedException("Saving analysis results is only supported by the Desktop client.");
        }

        public async Task ShowMessageDialogAsync(string title, string message)
        {
            await Task.Run(() =>
            {
                BrowserInterop.Alert($"[{title}]\n\n{message}");
            });
        }
    }

    [SupportedOSPlatform("browser")]
    internal static partial class BrowserInterop
    {
        [JSImport("globalThis.alert")]
        public static partial void Alert(string message);

        [JSImport("globalThis.prompt")]
        public static partial string? Prompt(string message, string defaultValue);
    }
}
