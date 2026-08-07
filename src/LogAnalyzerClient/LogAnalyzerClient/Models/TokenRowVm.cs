using Avalonia.Media;
using LogAnalyzerRpc.Protos;

namespace LogAnalyzerClient.Models
{
    /// <summary>
    /// Token 管理窗口中一行的展示模型（T5.1.a.b）。只读——每次刷新整体重建列表。
    /// </summary>
    internal sealed class TokenRowVm
    {
        // 徽标 / 高亮用静态画刷，避免每行新建。
        private static readonly ISolidColorBrush AdminBadge = new SolidColorBrush(Color.Parse("#7c3aed"));
        private static readonly ISolidColorBrush NormalBadge = new SolidColorBrush(Color.Parse("#6b7280"));
        private static readonly ISolidColorBrush SelfRow = new SolidColorBrush(Color.FromArgb(0x1A, 0x3b, 0x82, 0xf6));
        private static readonly ISolidColorBrush Transparent = new SolidColorBrush(Colors.Transparent);

        public string Token { get; }
        public TokenRoleEnum Role { get; }
        public string Note { get; }
        public bool IsSelf { get; }

        public TokenRowVm(string token, TokenRoleEnum role, string note, bool isSelf)
        {
            Token = token;
            Role = role;
            Note = note;
            IsSelf = isSelf;
        }

        public string RoleDisplay => Role == TokenRoleEnum.TokenAdmin ? "Admin" : "Normal";

        /// <summary>切换权限按钮的文案：管理员显示「Demote」，普通显示「Promote」。</summary>
        public string ToggleLabel => Role == TokenRoleEnum.TokenAdmin ? "Demote" : "Promote";

        public IBrush BadgeBrush => Role == TokenRoleEnum.TokenAdmin ? AdminBadge : NormalBadge;

        /// <summary>当前调用者自身的行用极淡的蓝色高亮。</summary>
        public IBrush RowBackground => IsSelf ? SelfRow : Transparent;
    }
}
