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
    /// <summary>A fake boost said only (no proven harm, or a company PC's own choice): no button.</summary>
    public bool Info { get; init; }
    /// <summary>Why it is only said, when that is not obvious.</summary>
    public string? Note { get; init; }

    bool IsFakeBoost => Tweak.Group == FakeBoostCatalog.Group;
    /// <summary>Applying: a setting not yet set. Undoing: only what this app set (a retired one can only be undone).</summary>
    public bool Applies => State == TweakState.NotApplied && !Tweak.Retired && !Info;
    public bool CanAct => State != TweakState.NotApplicable && (Applies || CanUndo);
    public bool HasAction => !Info || CanUndo;
    public string ActionText => State switch
    {
        TweakState.NotApplicable => L.T("Non disponible"),
        _ when Applies => IsFakeBoost ? L.T("Rétablir") : L.T("Appliquer"),
        _ => CanUndo ? L.T("Annuler") : L.T("Déjà réglé"),
    };
    public string Glyph => Info && State == TweakState.NotApplied ? ""
        : State == TweakState.Applied ? "" : State == TweakState.NotApplicable ? "" : "";
    public Brush Color => (Brush)Application.Current.FindResource(Info && State == TweakState.NotApplied ? "NeonCyan" : State switch
    {
        TweakState.Applied => "NeonGreen",
        TweakState.NotApplied => "NeonAmber",
        _ => "InkMuted",
    });
    public string Why => Note is null ? Tweak.Why : $"{Tweak.Why} {Note}";
    public string Badges => string.Concat(
        Tweak.NeedsAdmin ? "   ADMIN" : "",
        Tweak.NeedsRestart ? L.T("   REDÉMARRAGE") : "",
        State != TweakState.NotApplicable ? "" : Tweak.MinBuild >= 22631 ? L.T("   WINDOWS 11 23H2 ET APRÈS") : L.T("   WINDOWS 11 SEULEMENT"));
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
    public string CfgAction => Game.CfgApplied ? L.T("Retirer le cfg conseillé") : L.T("Ajouter le cfg conseillé");
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
            // powercfg and the registry: off the interface thread. A retired setting only shows
            // where this app applied it, to be undone.
            tweakRows = await Task.Run(() => TweakCatalog.All
                .Select(t => (Tweak: t, CanUndo: Tuner.CanUndo(t)))
                .Where(t => !t.Tweak.Retired || t.CanUndo)
                .Select(t => new TweakRow { Tweak = t.Tweak, State = Tuner.State(t.Tweak), CanUndo = t.CanUndo })
                .ToList());
            TweakListGame.ItemsSource = tweakRows.Where(r => r.Tweak.Group == TweakCatalog.Game).ToList();
            TweakListComfort.ItemsSource = tweakRows.Where(r => r.Tweak.Group == TweakCatalog.Comfort).ToList();
            await LoadFakeBoosts();
            TweakListPrivacy.ItemsSource = tweakRows.Where(r => r.Tweak.Group == TweakCatalog.Privacy).ToList();
            TweakListSpace.ItemsSource = tweakRows.Where(r => r.Tweak.Group == TweakCatalog.Space).ToList();
            // The gauge counts the gaming settings: privacy and disk space have their own pages.
            var possible = tweakRows.Where(r => r.Tweak.Group == TweakCatalog.Game && !r.Tweak.Retired && r.State != TweakState.NotApplicable).ToList();
            int applied = possible.Count(r => r.State == TweakState.Applied);
            TuningScore.Text = $"{applied}/{possible.Count}";
            TuningGauge.Value = possible.Count == 0 ? 0 : 100.0 * applied / possible.Count;
        }
        catch (Exception e)
        {
            SetStatus(L.T("Lecture des réglages impossible : ") + Errors.Describe(e));
        }
    }

    async Task LoadChecks()
    {
        try
        {
            var rows = (await Task.Run(PcChecks.Run)).Select(c => new CheckRow(c)).ToList();
            // What needs doing stays in sight; what is fine folds under one line, so that the
            // settings below are not pushed out of the window.
            CheckList.ItemsSource = rows.Where(r => r.Check.NeedsAttention).ToList();
            var fine = rows.Where(r => !r.Check.NeedsAttention).ToList();
            CheckListOk.ItemsSource = fine;
            CheckOkExpander.Header = L.F("{0} point(s) vérifié(s) : rien à faire", fine.Count);
            CheckOkExpander.Visibility = fine.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception e)
        {
            SetStatus(L.T("Vérification du matériel impossible : ") + Errors.Describe(e));
        }
    }

    /// <summary>
    /// "Défaire les faux boosts": those whose fault is on this PC (fixed or only said, depending
    /// on the PC), and those fixed here, to undo. They join the tweak rows, so applying, "Tout
    /// rétablir" and undoing work as for the tweaks.
    /// </summary>
    async Task LoadFakeBoosts()
    {
        try
        {
            var rows = await Task.Run(() =>
            {
                var pc = FakeBoostFixes.Context();
                return FakeBoostCatalog.All
                    .Select(b => (Boost: b, Verdict: b.Verdict(pc), Found: Tuner.Found(b), CanUndo: Tuner.CanUndo(b.Tweak)))
                    .Where(b => b.Verdict != FakeBoostVerdict.Hidden && (b.Found || b.CanUndo))
                    .Select(b => new TweakRow
                    {
                        Tweak = b.Boost.Tweak,
                        State = b.Found ? TweakState.NotApplied : TweakState.Applied,
                        CanUndo = b.CanUndo,
                        Info = b.Verdict == FakeBoostVerdict.Info,
                        Note = b.Verdict == FakeBoostVerdict.Info && !b.Boost.InfoOnly
                            ? (pc.Managed ? L.T("PC géré par une entreprise : à voir avec son service informatique.") : L.T("Sur un PC de bureau, c'est un choix qui se défend."))
                            : null,
                    })
                    .ToList();
            });
            tweakRows = tweakRows.Where(r => r.Tweak.Group != FakeBoostCatalog.Group).Concat(rows).ToList();
            FakeBoostList.ItemsSource = rows;
            FakeBoostsStatus.Text = rows.Count == 0
                ? L.T("Rien à défaire : aucun des faux boosts connus n'est sur ce PC.")
                : L.F("{0} à rétablir, {1} pour information.", rows.Count(r => r.Applies), rows.Count(r => r.Info));
        }
        catch (Exception e)
        {
            FakeBoostsStatus.Text = L.T("Analyse impossible : ") + Errors.Describe(e);
        }
    }

    async void OnTuningRefresh(object sender, RoutedEventArgs e) => await LoadTuning();

    async void OnTweakAction(object sender, RoutedEventArgs e)
    {
        if (busy || sender is not Button { Tag: TweakRow row }) return;
        await ChangeTweak(row.Tweak, row.Applies);
    }

    /// <summary>Applies or undoes one setting, from its page or from the journal.</summary>
    Task ChangeTweak(Tweak tweak, bool apply) =>
        RunBusy(tweak.NeedsAdmin ? L.T("En attente de l'autorisation administrateur…") : L.T("Modification…"), async () =>
        {
            if (tweak.NeedsAdmin)
                await RunAdminAsync<int>(apply ? AdminCommand.TweakApply : AdminCommand.TweakUndo, tweak.Id);
            else
                await Task.Run(() => { if (apply) Tuner.Apply(tweak); else Tuner.Undo(tweak); });
            await LoadTweaks();
            return Logged(Journal.Tuning, L.F("{0} : {1}.", tweak.Title, apply ? L.T("appliqué") : L.T("remis comme avant")),
                       apply ? new JournalUndo(JournalUndo.Tweak, tweak.Id) : null) +
                   (tweak.NeedsRestart ? L.T(" Effet au prochain redémarrage.") : "");
        });

    /// <summary>
    /// Every one-click setting of the button's group (its Tag; the gaming ones by default) not yet
    /// applied: the user's own directly, the rest under a single administrator consent.
    /// </summary>
    async void OnTuneAll(object sender, RoutedEventArgs e)
    {
        var group = (sender as Button)?.Tag as string ?? TweakCatalog.Game;
        var chosen = tweakRows
            .Where(r => r.Tweak.Group == group && r.Tweak.InOneClick && r.Applies)
            .ToList();
        if (chosen.Count == 0)
        {
            SetStatus(tweakRows.Count == 0 ? L.T("Les réglages sont encore en cours de lecture.") : L.T("Tout ce que ce bouton règle l'est déjà."));
            return;
        }
        string verb = group == FakeBoostCatalog.Group ? L.T("Rétablir") : L.T("Appliquer");
        if (Ask(L.F("{0} {1} réglage(s) ?\n\n", verb, chosen.Count) + string.Join("\n", chosen.Select(r => "• " + r.Tweak.Title)) +
                L.T("\n\nChacun s'annule ensuite d'un clic, valeur d'origine comprise.")) != MessageBoxResult.Yes)
            return;

        await RunBusy(L.T("Application des réglages…"), async () =>
        {
            var failures = new List<string>();
            var applied = new List<Tweak>();
            foreach (var row in chosen.Where(r => !r.Tweak.NeedsAdmin))
            {
                try
                {
                    await Task.Run(() => Tuner.Apply(row.Tweak));
                    applied.Add(row.Tweak);
                }
                catch (Exception ex)
                {
                    failures.Add(L.F("{0} : {1}", row.Tweak.Title, Errors.Describe(ex)));
                }
            }
            var admin = chosen.Where(r => r.Tweak.NeedsAdmin).Select(r => r.Tweak).ToList();
            if (admin.Count > 0)
            {
                SetStatus(L.T("En attente de l'autorisation administrateur…"));
                try
                {
                    await RunAdminAsync<int>(AdminCommand.TweakApply, admin.Select(t => t.Id).ToArray());
                    applied.AddRange(admin); // under one consent: all of them, or none
                }
                catch (Exception ex)
                {
                    failures.Add(L.T("Réglages administrateur : ") + Errors.Describe(ex));
                }
            }
            foreach (var tweak in applied)
                Journal.Add(Journal.Tuning, L.F("{0} : {1}.", tweak.Title, L.T("appliqué")), new JournalUndo(JournalUndo.Tweak, tweak.Id));
            await LoadTweaks();
            if (failures.Count > 0)
                MessageBox.Show(this, string.Join("\n", failures), L.T("Réglages non appliqués"), MessageBoxButton.OK, MessageBoxImage.Warning);
            bool restart = chosen.Any(r => r.Tweak.NeedsRestart);
            return L.F("{0} réglage(s) appliqué(s).", applied.Count) + (restart ? L.T(" Certains prennent effet au prochain redémarrage.") : "");
        });
    }

    void OnCheckAction(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string action }) return;
        if (action is "gpu-preference" or "gpu-preference-undo")
        {
            ChangeGpuPreference(force: action == "gpu-preference");
        }
        else if (action.StartsWith("refresh:", StringComparison.Ordinal))
        {
            var parts = action.Split(':', 3); // refresh:144:\\.\DISPLAY1
            if (parts.Length == 3 && int.TryParse(parts[1], out int hz)) ChangeRefresh(parts[2], hz);
        }
        else if (action.StartsWith("tab:", StringComparison.Ordinal))
        {
            if (FindName(action[4..]) is TabItem tab) Tabs.SelectedItem = tab;
        }
        // Only Windows' own settings pages and https pages: the action comes from our list, but stays checked.
        else if (action.StartsWith("ms-settings:", StringComparison.Ordinal) || action.StartsWith("windowsdefender:", StringComparison.Ordinal)
                 || Uri.TryCreate(action, UriKind.Absolute, out var page) && page.Scheme == Uri.UriSchemeHttps)
        {
            Process.Start(new ProcessStartInfo(action) { UseShellExecute = true });
        }
    }

    async void ChangeGpuPreference(bool force) =>
        await RunBusy(force ? L.T("Réglage des jeux sur la carte graphique dédiée…") : L.T("Retour au choix de Windows…"), async () =>
        {
            var registry = new WindowsRegistry();
            int count = await Task.Run(() =>
            {
                if (!force) return GpuPreference.Undo(registry);
                var programs = SteamGames.AllGameFolders().SelectMany(SteamGames.GamePrograms).ToList();
                GpuPreference.Force(programs, registry);
                return programs.Count;
            });
            await LoadChecks();
            return Logged(Journal.Tuning, force
                ? L.F("{0} programme(s) de jeu réglé(s) sur la carte dédiée : effet au prochain lancement de chaque jeu.", count)
                : L.F("{0} programme(s) remis au choix de Windows.", count));
        });

    // Tried first without saving; kept only once the user sees the picture and says so.
    async void ChangeRefresh(string device, int hz)
    {
        if (busy) return;
        if (!Displays.TryRefresh(device, hz))
        {
            SetStatus(L.F("Windows refuse {0} Hz pour cet écran à cette définition : choisissez la fréquence dans Paramètres → Affichage.", hz));
            return;
        }
        var dialog = new KeepDisplayDialog(this, L.F("Écran passé à {0} Hz", hz));
        dialog.ShowDialog();
        if (dialog.Keep && Displays.SaveRefresh(device, hz)) SetStatus(Logged(Journal.Tuning, L.F("Écran réglé à {0} Hz.", hz)));
        else
        {
            Displays.RevertMode(device);
            SetStatus(L.T("Fréquence d'écran remise comme avant."));
        }
        await LoadChecks();
    }

    async Task LoadGameTips()
    {
        try
        {
            var games = await Task.Run(SteamGames.Find);
            GameTipList.ItemsSource = games.Select(g => new GameRow(g)).ToList();
            GameTipsStatus.Text = games.Count > 0 ? ""
                : L.T("Aucun de ces jeux n'est installé par Steam sur ce PC : ") + string.Join(", ", GameTips.Known.Select(t => t.Game)) + ".";
            GameTipsStatus.Visibility = games.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (Exception e)
        {
            GameTipsStatus.Text = L.T("Lecture de la bibliothèque Steam impossible : ") + Errors.Describe(e);
        }
    }

    async void OnGameCfg(object sender, RoutedEventArgs e)
    {
        if (busy || sender is not Button { Tag: GameRow row }) return;
        bool remove = row.Game.CfgApplied;
        await RunBusy(L.T("Modification du cfg…"), async () =>
        {
            await Task.Run(() => { if (remove) SteamGames.RemoveCfg(row.Game); else SteamGames.ApplyCfg(row.Game); });
            await LoadGameTips();
            return Logged(Journal.Tuning, remove
                ? L.F("{0} : cfg conseillé retiré, vos propres lignes sont restées.", row.Game.Tip.Game)
                : L.F("{0} : cfg ajouté à {1}. Il s'applique au prochain lancement du jeu.", row.Game.Tip.Game, row.Game.Tip.CfgPath));
        });
    }
}
