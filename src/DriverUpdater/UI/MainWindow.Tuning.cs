using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DriverUpdater.UI;

/// <summary>One "PC de guerre" setting with its state as read from Windows.</summary>
public sealed class TweakRow
{
    public required Tweak Tweak { get; init; }
    public required TweakState State { get; init; }
    public required bool CanUndo { get; init; }

    public bool CanAct => State != TweakState.NotApplicable && (State == TweakState.NotApplied || CanUndo);
    public string ActionText => State switch
    {
        TweakState.NotApplicable => "Non disponible",
        TweakState.NotApplied => "Appliquer",
        _ => CanUndo ? "Annuler" : "Déjà réglé",
    };
    public string Glyph => State == TweakState.Applied ? "" : State == TweakState.NotApplicable ? "" : "";
    public Brush Color => (Brush)Application.Current.FindResource(State switch
    {
        TweakState.Applied => "NeonGreen",
        TweakState.NotApplied => "NeonAmber",
        _ => "InkMuted",
    });
    public string Badges => string.Concat(
        Tweak.NeedsAdmin ? "   ADMIN" : "",
        Tweak.NeedsRestart ? "   REDÉMARRAGE" : "",
        State == TweakState.NotApplicable ? "   WINDOWS 11 SEULEMENT" : "");
}

public sealed class CheckRow(PcCheck check)
{
    public PcCheck Check { get; } = check;
    public bool HasAction => Check.Action is not null;
    public string? ActionText => Check.ActionText;
    public string? Action => Check.Action;
    public string Glyph => Check.NeedsAttention ? "" : "";
    public Brush Color => (Brush)Application.Current.FindResource(Check.NeedsAttention ? "NeonAmber" : "NeonGreen");
}

public sealed class GameRow(InstalledGame game)
{
    public InstalledGame Game { get; } = game;
    public string CfgAction => Game.CfgApplied ? "Retirer le cfg conseillé" : "Ajouter le cfg conseillé";
}

// "PC de guerre" tab, and the game tips of the "Jeux" tab.
public partial class MainWindow
{
    List<TweakRow> tweakRows = [];

    Task LoadTuning() => Task.WhenAll(LoadTweaks(), LoadChecks());

    async Task LoadTweaks()
    {
        try
        {
            // powercfg and the registry: off the interface thread.
            tweakRows = await Task.Run(() => TweakCatalog.All
                .Select(t => new TweakRow { Tweak = t, State = Tuner.State(t), CanUndo = Tuner.CanUndo(t) })
                .ToList());
            TweakListGame.ItemsSource = tweakRows.Where(r => r.Tweak.Group == TweakCatalog.Game).ToList();
            TweakListPrivacy.ItemsSource = tweakRows.Where(r => r.Tweak.Group == TweakCatalog.Privacy).ToList();
            TweakListSpace.ItemsSource = tweakRows.Where(r => r.Tweak.Group == TweakCatalog.Space).ToList();
            var possible = tweakRows.Where(r => r.State != TweakState.NotApplicable).ToList();
            int applied = possible.Count(r => r.State == TweakState.Applied);
            TuningScore.Text = $"{applied}/{possible.Count}";
            TuningGauge.Value = possible.Count == 0 ? 0 : 100.0 * applied / possible.Count;
        }
        catch (Exception e)
        {
            SetStatus("Lecture des réglages impossible : " + Errors.Describe(e));
        }
    }

    async Task LoadChecks()
    {
        try
        {
            CheckList.ItemsSource = (await Task.Run(PcChecks.Run)).Select(c => new CheckRow(c)).ToList();
        }
        catch (Exception e)
        {
            SetStatus("Vérification du matériel impossible : " + Errors.Describe(e));
        }
    }

    async void OnTuningRefresh(object sender, RoutedEventArgs e) => await LoadTuning();

    async void OnTweakAction(object sender, RoutedEventArgs e)
    {
        if (busy || sender is not Button { Tag: TweakRow row }) return;
        bool apply = row.State == TweakState.NotApplied;
        await RunBusy(row.Tweak.NeedsAdmin ? "En attente de l'autorisation administrateur…" : "Modification…", async () =>
        {
            if (row.Tweak.NeedsAdmin)
                await RunAdminAsync<int>(apply ? AdminCommand.TweakApply : AdminCommand.TweakUndo, row.Tweak.Id);
            else
                await Task.Run(() => { if (apply) Tuner.Apply(row.Tweak); else Tuner.Undo(row.Tweak); });
            await LoadTweaks();
            return $"{row.Tweak.Title} : {(apply ? "appliqué" : "remis comme avant")}." +
                   (row.Tweak.NeedsRestart ? " Effet au prochain redémarrage." : "");
        });
    }

    /// <summary>
    /// Every one-click setting not yet applied: the user's own directly, the rest under a single
    /// administrator consent. A laptop keeps its power plan: the battery would pay for it.
    /// </summary>
    async void OnTuneAll(object sender, RoutedEventArgs e)
    {
        bool laptop = !System.Windows.Forms.SystemInformation.PowerStatus.BatteryChargeStatus
            .HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery);
        var chosen = tweakRows
            .Where(r => r.Tweak.InOneClick && r.State == TweakState.NotApplied && !(laptop && r.Tweak.Id == TweakCatalog.PowerPlan))
            .ToList();
        if (chosen.Count == 0)
        {
            SetStatus("Tout ce que « Tout optimiser » règle l'est déjà.");
            return;
        }
        if (Ask($"Appliquer {chosen.Count} réglage(s) ?\n\n" + string.Join("\n", chosen.Select(r => "• " + r.Tweak.Title)) +
                "\n\nChacun s'annule ensuite d'un clic, valeur d'origine comprise.") != MessageBoxResult.Yes)
            return;

        await RunBusy("Application des réglages…", async () =>
        {
            var failures = new List<string>();
            foreach (var row in chosen.Where(r => !r.Tweak.NeedsAdmin))
            {
                try
                {
                    await Task.Run(() => Tuner.Apply(row.Tweak));
                }
                catch (Exception ex)
                {
                    failures.Add($"{row.Tweak.Title} : {Errors.Describe(ex)}");
                }
            }
            var admin = chosen.Where(r => r.Tweak.NeedsAdmin).Select(r => r.Tweak.Id).ToArray();
            if (admin.Length > 0)
            {
                SetStatus("En attente de l'autorisation administrateur…");
                try
                {
                    await RunAdminAsync<int>(AdminCommand.TweakApply, admin);
                }
                catch (Exception ex)
                {
                    failures.Add("Réglages administrateur : " + Errors.Describe(ex));
                }
            }
            await LoadTweaks();
            if (failures.Count > 0)
                MessageBox.Show(this, string.Join("\n", failures), "Réglages non appliqués", MessageBoxButton.OK, MessageBoxImage.Warning);
            bool restart = chosen.Any(r => r.Tweak.NeedsRestart);
            return $"{chosen.Count - failures.Count} réglage(s) appliqué(s)." + (restart ? " Certains prennent effet au prochain redémarrage." : "");
        });
    }

    void OnCheckAction(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string action }) return;
        if (action.StartsWith("tab:", StringComparison.Ordinal))
        {
            if (FindName(action[4..]) is TabItem tab) Tabs.SelectedItem = tab;
        }
        // Only Windows' own settings pages: the action comes from our list, but stays checked.
        else if (action.StartsWith("ms-settings:", StringComparison.Ordinal) || action.StartsWith("windowsdefender:", StringComparison.Ordinal))
        {
            Process.Start(new ProcessStartInfo(action) { UseShellExecute = true });
        }
    }

    async Task LoadGameTips()
    {
        try
        {
            var games = await Task.Run(SteamGames.Find);
            GameTipList.ItemsSource = games.Select(g => new GameRow(g)).ToList();
            GameTipsStatus.Text = games.Count > 0 ? ""
                : "Aucun de ces jeux n'est installé par Steam sur ce PC : " + string.Join(", ", GameTips.Known.Select(t => t.Game)) + ".";
            GameTipsStatus.Visibility = games.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (Exception e)
        {
            GameTipsStatus.Text = "Lecture de la bibliothèque Steam impossible : " + Errors.Describe(e);
        }
    }

    async void OnGameCfg(object sender, RoutedEventArgs e)
    {
        if (busy || sender is not Button { Tag: GameRow row }) return;
        bool remove = row.Game.CfgApplied;
        await RunBusy("Modification du cfg…", async () =>
        {
            await Task.Run(() => { if (remove) SteamGames.RemoveCfg(row.Game); else SteamGames.ApplyCfg(row.Game); });
            await LoadGameTips();
            return remove
                ? $"{row.Game.Tip.Game} : cfg conseillé retiré, vos propres lignes sont restées."
                : $"{row.Game.Tip.Game} : cfg ajouté à {row.Game.Tip.CfgPath}. Il s'applique au prochain lancement du jeu.";
        });
    }
}
