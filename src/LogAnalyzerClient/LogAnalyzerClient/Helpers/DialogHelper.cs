using Avalonia.Controls;
using Google.Protobuf;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace LogAnalyzerClient.Helpers
{
    internal interface IDialogHelper
    {
        Task<string?> ShowConnectDialogAsync(string currentAddress);
        Task ShowMessageDialogAsync(string title, string message);
    }

    internal class NullDialogHelper : IDialogHelper
    {
        public Task<string?> ShowConnectDialogAsync(string currentAddress)
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

        public async Task<string?> ShowConnectDialogAsync(string currentAddress)
        {
            var dialog = new ConnectDialog(currentAddress);
            return await dialog.ShowDialog<string?>(_owner);
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
        public async Task<string?> ShowConnectDialogAsync(string currentAddress)
        {
            return await Task.Run(() =>
            {
                return BrowserInterop.Prompt("Please input the address of Agent:", currentAddress);
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
