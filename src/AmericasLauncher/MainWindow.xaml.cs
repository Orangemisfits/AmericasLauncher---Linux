// SPDX-License-Identifier: GPL-2.0-only
using System.Windows;
using AmericasLauncher.Models;
using AmericasLauncher.Services;
using Microsoft.Win32;

namespace AmericasLauncher;

public partial class MainWindow : Window
{
    private readonly ConfigService _configService = new();
    private readonly EmulatorService _emulators = new();
    private readonly LaunchService _launcher = new();
    private readonly AppConfig _config;
    private readonly List<EmulatorProfile> _profiles;

    public MainWindow()
    {
        InitializeComponent();
        _config = _configService.Load();
        _profiles = ConfigService.LoadProfiles();

        EmulatorBox.ItemsSource = _profiles;
        EmulatorBox.SelectedItem = _profiles.FirstOrDefault(p => p.Id == _config.SelectedEmulatorId) ?? _profiles.FirstOrDefault();
        GamePathBox.Text = _config.GamePath;
    }

    private EmulatorProfile? Selected => EmulatorBox.SelectedItem as EmulatorProfile;

    private void Log(string msg) => Dispatcher.Invoke(() =>
    {
        LogBox.AppendText(msg + Environment.NewLine);
        LogBox.ScrollToEnd();
    });

    private void BrowseGame_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select game file",
            Filter = "Xbox 360 games (*.iso;*.xex;*.zar)|*.iso;*.xex;*.zar|All files|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            GamePathBox.Text = _config.GamePath = dlg.FileName;
            _configService.Save(_config);
        }
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } profile) return;
        InstallButton.IsEnabled = false;
        try
        {
            var log = new Progress<string>(Log);
            var prog = new Progress<double>(v => Progress.Value = v);
            await _emulators.InstallOrUpdateAsync(profile, _config, log, prog);
            _config.SelectedEmulatorId = profile.Id;
            _configService.Save(_config);
        }
        catch (Exception ex)
        {
            Log("ERROR: " + ex.Message);
        }
        finally
        {
            InstallButton.IsEnabled = true;
            Progress.Value = 0;
        }
    }

    private void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } profile) return;
        if (!_emulators.IsInstalled(profile)) { Log("Click 'Install / Update' first."); return; }
        if (string.IsNullOrWhiteSpace(_config.GamePath)) { Log("Select a game file first."); return; }

        try
        {
            _launcher.Launch(profile, _config.GamePath);
            Log("Launched.");
        }
        catch (Exception ex)
        {
            Log("ERROR: " + ex.Message);
        }
    }
}
