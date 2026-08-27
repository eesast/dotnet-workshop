using Avalonia.Controls;
using Google.Protobuf;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace LogAnalyzerClient.Helpers
{
    internal interface IDialogHelper
    {
        Task<ConnectInfo?> ShowConnectDialogAsync(string currentAddress, string currentToken);
        Task ShowMessageDialogAsync(string title, string message);
    }

    internal class NullDialogHelper : IDialogHelper
    {
        public Task<ConnectInfo?> ShowConnectDialogAsync(string currentAddress, string currentToken)
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

        public async Task<ConnectInfo?> ShowConnectDialogAsync(string currentAddress, string currentToken)
        {
            var dialog = new ConnectDialog(currentAddress, currentToken);
            return await dialog.ShowDialog<ConnectInfo?>(_owner);
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
        public async Task<ConnectInfo?> ShowConnectDialogAsync(string currentAddress, string currentToken)
        {
            return await Task.Run(() =>
            {
                var address = BrowserInterop.Prompt("Please input the address of Agent:", currentAddress);
                if (address is null) return null;
                var token = BrowserInterop.Prompt("Please input the Agent token:", currentToken);
                return new ConnectInfo(address, token ?? string.Empty);
            });
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
