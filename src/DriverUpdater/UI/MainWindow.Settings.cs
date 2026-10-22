using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace DriverUpdater.UI;

// "Paramètres" page: the user's choices, saved as soon as they are made.
public partial class MainWindow
{
    bool loadingSettings;

    void LoadSettings()
    {
        loadingSettings = true;
        try
        {
            WeeklyNvidiaBox.IsChecked = Settings.WeeklyNvidia;
            WeeklySoftwareBox.IsChecked = Settings.WeeklySoftware;
            WeeklyNvidiaBox.IsEnabled = WeeklySoftwareBox.IsEnabled = WeeklyCheckBox.IsChecked == true;
            (Settings.NvidiaStudio ? StudioRadio : GameReadyRadio).IsChecked = true;
            LanguageBox.SelectedItem = LanguageBox.Items.OfType<ComboBoxItem>().First(i => (string)i.Tag == Settings.Language);
            AboutText.Text = L.F("Driver Updater {0}, sous licence MIT. ", typeof(MainWindow).Assembly.GetName().Version?.ToString(3)) +
                             L.T("Il ne contacte que Windows Update, le catalogue Microsoft, NVIDIA pour ses cartes, winget et GitHub pour ses propres mises à jour.");
        }
        finally
        {
            loadingSettings = false;
        }
    }

    void OnSettingChanged(object sender, RoutedEventArgs e)
    {
        if (loadingSettings) return;
        Settings.WeeklyNvidia = WeeklyNvidiaBox.IsChecked == true;
        Settings.WeeklySoftware = WeeklySoftwareBox.IsChecked == true;
        bool studio = StudioRadio.IsChecked == true;
        if (studio != Settings.NvidiaStudio)
        {
            Settings.NvidiaStudio = studio;
            // The graphics card page asks NVIDIA again for the other kind of driver.
            nvidiaReleases = [];
            openedTabs.Remove(GpuTab);
        }
        SetStatus(L.T("Paramètre enregistré."));
    }

    void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loadingSettings || LanguageBox.SelectedItem is not ComboBoxItem { Tag: string language }) return;
        Settings.Language = language;
        bool changes = L.Resolve(language, CultureInfo.CurrentUICulture) != L.Language;
        RestartButton.Visibility = changes ? Visibility.Visible : Visibility.Collapsed;
        SetStatus(L.T("Langue enregistrée : elle s'appliquera au prochain démarrage."));
    }

    /// <summary>Starts the app again, in the language just chosen.</summary>
    void OnRestart(object sender, RoutedEventArgs e)
    {
        bool closed = false;
        Closed += (_, _) => closed = true;
        Close(); // an administrator action under way is asked about first
        if (closed) Process.Start(Environment.ProcessPath!);
    }

    /// <summary>Opened from the weekly check's notification: where the news is.</summary>
    internal void ShowWeekly(List<DriverUpdate> drivers, bool nvidia, List<SoftwareUpdate> software)
    {
        if (drivers.Count > 0)
        {
            _ = ShowUpdates(drivers, L.T("Trouvé par la vérification hebdomadaire."));
            Tabs.SelectedItem = DriversTab;
        }
        else if (nvidia)
        {
            Tabs.SelectedItem = GpuTab;
        }
        else
        {
            ShowSoftware(software);
            Tabs.SelectedItem = SoftwareTab;
        }
    }
}
