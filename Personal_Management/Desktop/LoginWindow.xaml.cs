using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PersonalManagement.Desktop;

public partial class LoginWindow : Window
{
    public string? SelectedUser { get; private set; }

    private readonly bool _needsMigration;

    public LoginWindow()
    {
        InitializeComponent();
        Theme.Tint(this);
        _needsMigration = LegacyMigrator.NeedsMigration();
        var users = UserAccounts.ListUsers();
        if (_needsMigration)
        {
            HintText.Text = "检测到本机旧版数据。请输入用户名，创建后将把现有数据归入该用户（无密码）。连接与本地库以旧数据为准。";
            CreateTitle.Text = "迁移并新建用户";
            CreateButton.Content = "迁移并进入";
            ListPanel.Visibility = Visibility.Collapsed;
            NocoSectionHint.Visibility = Visibility.Collapsed;
            NocoBizBox.Visibility = Visibility.Collapsed;
            NocoFavBox.Visibility = Visibility.Collapsed;
            NocoWeightBox.Visibility = Visibility.Collapsed;
            NocoFields.Visibility = Visibility.Collapsed;
        }
        else if (users.Count == 0)
        {
            HintText.Text = "还没有用户。请创建第一个用户（无密码）。";
            ListPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            HintText.Text = "选择已有用户，或在下方新建。";
            foreach (var u in users)
                UserList.Items.Add(u);
            if (UserList.Items.Count > 0)
                UserList.SelectedIndex = 0;
        }
        NocoFlag_Changed(this, new RoutedEventArgs());
    }

    private void NocoFlag_Changed(object sender, RoutedEventArgs e)
    {
        var any = NocoBizBox.IsChecked == true || NocoFavBox.IsChecked == true || NocoWeightBox.IsChecked == true;
        NocoFields.IsEnabled = any;
    }

    private void EnterSelected_Click(object sender, RoutedEventArgs e) => TryEnterSelected();

    private void UserList_DoubleClick(object sender, MouseButtonEventArgs e) => TryEnterSelected();

    private void TryEnterSelected()
    {
        if (UserList.SelectedItem is not string name) return;
        SelectedUser = name;
        DialogResult = true;
        Close();
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var err = UserAccounts.ValidateUserName(name);
        if (err is not null)
        {
            MessageBox.Show(err);
            return;
        }

        var settings = new StorageSettings
        {
            UseNocoBusiness = NocoBizBox.IsChecked == true,
            UseNocoFavorites = NocoFavBox.IsChecked == true,
            UseNocoWeight = NocoWeightBox.IsChecked == true
        };
        if (settings.UseNocoBusiness || settings.UseNocoFavorites || settings.UseNocoWeight)
        {
            settings.Url = string.IsNullOrWhiteSpace(UrlBox.Text) ? null : UrlBox.Text.Trim().TrimEnd('/');
            settings.Email = string.IsNullOrWhiteSpace(EmailBox.Text) ? null : EmailBox.Text.Trim();
            settings.Password = string.IsNullOrEmpty(PasswordBox.Password) ? null : PasswordBox.Password;
            settings.ApiToken = string.IsNullOrWhiteSpace(TokenBox.Text) ? null : TokenBox.Text.Trim();
            if (settings.Url is null || settings.Email is null || settings.Password is null)
            {
                MessageBox.Show("已勾选 NocoDB 服务时，请填写 URL、Email 与 Password。");
                return;
            }
        }

        try
        {
            if (_needsMigration)
                LegacyMigrator.MigrateIntoUser(name);
            else
                UserAccounts.CreateUserSkeleton(name, settings);

            var cfg = ProgramConfig.Load();
            cfg.LastUser = name;
            cfg.Save();

            SelectedUser = name;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "创建失败");
        }
    }
}
