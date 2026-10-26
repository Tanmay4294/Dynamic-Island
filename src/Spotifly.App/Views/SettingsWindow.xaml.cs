using System.Windows;
using Spotifly.App.ViewModels;

namespace Spotifly.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = _viewModel;
        InitializeComponent();
    }

    private async void ConnectSpotifyButton_Click(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("Connect Spotify button clicked.");
        await _viewModel.ConnectSpotifyAsync();
    }

    private async void DisconnectSpotifyButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.DisconnectSpotifyAsync();
    }

    private void ResetProgressButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ResetSimulatedProgress();
    }

    private void NextTrackButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SimulateNextTrack();
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            this,
            "Are you sure you want to reset all Spotifly settings to their defaults?",
            "Reset to Defaults",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _viewModel.ResetToDefaults();
        }
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            this,
            "Are you sure you want to exit Spotifly?",
            "Exit Spotifly",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            Close();
            _viewModel.ExitApplication();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
