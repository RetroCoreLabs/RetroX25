//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RetroIO.Protocols.X25.Routing;

namespace RetroX25.Desktop.ViewModels;

/// <summary>
/// One answer to "what do you want to reach?": a kind of route, with one line that says what it is.
/// </summary>
public sealed class RouteKindChoice
{
    /// <summary>
    /// Creates a choice.
    /// </summary>
    /// <param name="word">
    /// The kind word written in the file (<see cref="X121TableBuilder.KindWords"/>).
    /// </param>
    /// <param name="title">
    /// What the person picks.
    /// </param>
    /// <param name="help">
    /// One line on what it is.
    /// </param>
    /// <param name="matchLabel">
    /// The label of the match field.
    /// </param>
    /// <param name="example">
    /// An example match.
    /// </param>
    public RouteKindChoice(string word, string title, string help, string matchLabel, string example)
    {
        Word = word;
        Title = title;
        Help = help;
        MatchLabel = matchLabel;
        Example = example;
    }

    /// <summary>
    /// The kind word.
    /// </summary>
    public string Word { get; }

    /// <summary>
    /// What the person picks.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// One line on what it is.
    /// </summary>
    public string Help { get; }

    /// <summary>
    /// The label of the match field; empty when the kind matches everything.
    /// </summary>
    public string MatchLabel { get; }

    /// <summary>
    /// An example match.
    /// </summary>
    public string Example { get; }

    /// <summary>
    /// The title, for a list.
    /// </summary>
    /// <returns>
    /// <see cref="Title"/>.
    /// </returns>
    public override string ToString() => Title;
}

/// <summary>
/// The add/edit dialog of the routing screen: asks what the route should reach, checks every field as it
/// is typed (digits and lengths per X.121), and shows where a sample call would go with the route in place.
/// </summary>
public partial class RouteEditDialogViewModel : ObservableObject
{
    private readonly ILogger _logger;
    private readonly bool _isNew;
    private readonly Func<X121RouteConfig, string, X121RouteResult?>? _preview;
    private readonly IReadOnlyList<string> _otherNames;

    /// <summary>
    /// The kinds, most exact first, as the lookup ranks them.
    /// </summary>
    public static readonly RouteKindChoice[] Kinds =
    {
        new RouteKindChoice("exact", "One number",
            "A single terminal or service: the whole number, or a local number of our own network.", "Number", "2422110006"),
        new RouteKindChoice("prefix", "Numbers that start with ...",
            "Every number that begins with these digits, for example one exchange or one customer's range.", "Starts with", "242201"),
        new RouteKindChoice("dnic", "A whole network",
            "Every number of one network: its 4-digit DNIC, or 5 digits when networks share a DNIC (X.121 A.2).", "Network (DNIC)", "2402"),
        new RouteKindChoice("dcc", "A whole country",
            "Every network in one country: its 3-digit country code (X.121 Annex J).", "Country code (DCC)", "240"),
        new RouteKindChoice("default", "Everything else (default gateway)",
            "Calls no other route takes, usually to the gateway to other networks (X.75).", string.Empty, string.Empty),
        new RouteKindChoice("hunt", "One number shared by several lines (hunt group)",
            "Calls to one number go to the next line of the group that is up, in turn.", "Number", "2422110100"),
        new RouteKindChoice("regex", "A pattern (advanced)",
            "A regular expression over the whole number; tried after the exact numbers, before prefixes.", "Pattern", "^2422(10|11)[0-9]+$"),
    };

    [ObservableProperty]
    private RouteKindChoice _selectedKind = Kinds[2];

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _match = string.Empty;

    [ObservableProperty]
    private string _port = string.Empty;

    [ObservableProperty]
    private string _huntPortsText = string.Empty;

    [ObservableProperty]
    private bool _enabled = true;

    [ObservableProperty]
    private string _note = string.Empty;

    [ObservableProperty]
    private string? _backupToAdd;

    [ObservableProperty]
    private string? _selectedBackup;

    [ObservableProperty]
    private string _previewNumber = "024020001730";

    [ObservableProperty]
    private string _previewText = string.Empty;

    [ObservableProperty]
    private string _validationError = string.Empty;

    private bool _suspendRefresh;
    private int _priority;
    private string _direction = "both";
    private string _callUserDataPattern = string.Empty;

    /// <summary>
    /// The ports the route may use, by name: the lines of this node and <c>local</c> (this node).
    /// </summary>
    public ObservableCollection<string> PortNames { get; } = new ObservableCollection<string>();

    /// <summary>
    /// The backup ports, in the order they are tried when the route's port is out of order (X.110 3.5:
    /// direct route, alternative 1, alternative 2).
    /// </summary>
    public ObservableCollection<string> BackupPorts { get; } = new ObservableCollection<string>();

    /// <summary>
    /// The kinds to pick from.
    /// </summary>
    public IReadOnlyList<RouteKindChoice> KindChoices => Kinds;

    /// <summary>
    /// The route the dialog produced; null until OK is pressed with every field valid.
    /// </summary>
    public X121RouteConfig? Result { get; private set; }

    /// <summary>
    /// Gets the title of the dialog window: <c>Add Route</c> when the dialog adds an entry and
    /// <c>Edit Route</c> when it edits one.
    /// </summary>
    public string DialogTitle => _isNew ? "Add Route" : "Edit Route";

    /// <summary>
    /// One line on the chosen kind.
    /// </summary>
    public string KindHelp => SelectedKind.Help;

    /// <summary>
    /// The label of the match field.
    /// </summary>
    public string MatchLabel => SelectedKind.MatchLabel;

    /// <summary>
    /// False for the default route, which matches everything.
    /// </summary>
    public bool HasMatch => SelectedKind.Word != "default";

    /// <summary>
    /// True for a hunt route, which names its member lines instead of one port.
    /// </summary>
    public bool IsHunt => SelectedKind.Word == "hunt";

    /// <summary>
    /// True for every kind but a hunt route.
    /// </summary>
    public bool HasPort => !IsHunt;

    /// <summary>
    /// The example for the match field.
    /// </summary>
    public string MatchWatermark => SelectedKind.Example.Length > 0 ? "for example " + SelectedKind.Example : string.Empty;

    /// <summary>
    /// The country X.121 Annex J names for the digits typed, or empty.
    /// </summary>
    public string MatchCountry
    {
        get
        {
            string m = Match.Trim();
            if ((SelectedKind.Word == "dnic" || SelectedKind.Word == "dcc") && m.Length >= 3
                && X121CountryCodes.TryGetCountry(m, out string name))
            {
                return name;
            }

            return string.Empty;
        }
    }

    /// <summary>
    /// The order the ports are tried, in words.
    /// </summary>
    public string BackupOrderText
    {
        get
        {
            if (IsHunt)
            {
                return "A hunt group takes the next member that is up; it has no backups of its own.";
            }

            if (BackupPorts.Count == 0)
            {
                return "No backup: when " + (Port.Length > 0 ? Port : "the port") + " is out of order the call is cleared (out of order).";
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("Tried in this order: ").Append(Port.Length > 0 ? Port : "the port");
            for (int i = 0; i < BackupPorts.Count; i++)
            {
                sb.Append(", then backup ").Append(i + 1).Append(' ').Append(BackupPorts[i]);
            }

            sb.Append('.');
            return sb.ToString();
        }
    }

    /// <summary>
    /// True when every field is valid.
    /// </summary>
    public bool IsValid => ValidationError.Length == 0;

    /// <summary>
    /// Creates the dialog view-model.
    /// </summary>
    /// <param name="logger">
    /// Logger for this view-model.
    /// </param>
    /// <param name="existing">
    /// The route whose values fill the fields, or null for a new route.
    /// </param>
    /// <param name="isNew">
    /// True when the dialog adds a route; it decides the words in <see cref="DialogTitle"/>.
    /// </param>
    /// <param name="preview">
    /// Runs the real lookup for a sample number with this route in the table: the routing screen's own
    /// table, with the route put in place of the one being edited. Null shows no preview.
    /// </param>
    /// <param name="portNames">
    /// The ports to offer; null for none.
    /// </param>
    /// <param name="otherNames">
    /// The names of the other routes of the table; a route name must be unique, because the hit counts and
    /// the explanations name the route.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="logger"/> is null.
    /// </exception>
    public RouteEditDialogViewModel(ILogger logger, X121RouteConfig? existing, bool isNew,
        Func<X121RouteConfig, string, X121RouteResult?>? preview = null, IReadOnlyList<string>? portNames = null,
        IReadOnlyList<string>? otherNames = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _isNew = isNew;
        _preview = preview;
        _otherNames = otherNames ?? Array.Empty<string>();

        if (portNames != null)
        {
            for (int i = 0; i < portNames.Count; i++)
            {
                PortNames.Add(portNames[i]);
            }
        }

        BackupPorts.CollectionChanged += (_, _) => Refresh();

        if (existing != null)
        {
            Load(existing);
        }

        Refresh();
    }

    private void Load(X121RouteConfig rc)
    {
        // The fields are set one by one; the checks and the preview run once, after the last.
        _suspendRefresh = true;
        try
        {
            if (X121TableBuilder.TryParseKind(rc.Kind, out X121RouteType type))
            {
                string word = X121TableBuilder.KindWord(type);
                for (int i = 0; i < Kinds.Length; i++)
                {
                    if (Kinds[i].Word == word)
                    {
                        SelectedKind = Kinds[i];
                    }
                }
            }

            string port = rc.Port ?? string.Empty;
            if (port.Length > 0 && !PortNames.Contains(port))
            {
                PortNames.Add(port);
            }

            Name = rc.Name ?? string.Empty;
            Match = rc.Match ?? string.Empty;
            Port = port;
            Enabled = rc.Enabled;
            Note = rc.Note ?? string.Empty;
            HuntPortsText = string.Join(", ", rc.HuntPorts ?? new List<string>());
            _priority = rc.Priority;
            _direction = rc.Direction ?? "both";
            _callUserDataPattern = rc.CallUserDataPattern ?? string.Empty;

            if (rc.BackupPorts != null)
            {
                for (int i = 0; i < rc.BackupPorts.Count; i++)
                {
                    BackupPorts.Add(rc.BackupPorts[i]);
                }
            }
        }
        finally
        {
            _suspendRefresh = false;
        }
    }

    partial void OnSelectedKindChanged(RouteKindChoice value)
    {
        OnPropertyChanged(nameof(KindHelp));
        OnPropertyChanged(nameof(MatchLabel));
        OnPropertyChanged(nameof(HasMatch));
        OnPropertyChanged(nameof(IsHunt));
        OnPropertyChanged(nameof(HasPort));
        OnPropertyChanged(nameof(MatchWatermark));
        Refresh();
    }

    partial void OnNameChanged(string value) => Refresh();

    partial void OnMatchChanged(string value)
    {
        string example = ExampleNumber(SelectedKind.Word, value.Trim());
        if (example.Length > 0)
        {
            PreviewNumber = example;
        }

        Refresh();
    }

    partial void OnPortChanged(string value) => Refresh();

    partial void OnHuntPortsTextChanged(string value) => Refresh();

    partial void OnEnabledChanged(bool value) => Refresh();

    partial void OnPreviewNumberChanged(string value) => Refresh();

    // A number the route takes, for the preview: the match itself, or the match made up to a full number.
    private static string ExampleNumber(string kind, string match)
    {
        if (!X121RouteRules.IsDigits(match))
        {
            return string.Empty;
        }

        switch (kind)
        {
            case "exact":
            case "hunt":
                return match;
            case "prefix":
            case "dnic":
            case "dcc":
                return match.Length >= 12 ? match : match + "000000000000".Substring(0, 12 - match.Length);
            default:
                return string.Empty;
        }
    }

    /// <summary>
    /// The route as the fields stand, whether or not it is valid.
    /// </summary>
    /// <returns>
    /// The route.
    /// </returns>
    public X121RouteConfig Build()
    {
        X121RouteConfig rc = new X121RouteConfig
        {
            Name = Name.Trim(),
            Kind = SelectedKind.Word,
            Match = HasMatch ? Match.Trim() : string.Empty,
            Port = IsHunt ? string.Empty : Port.Trim(),
            Enabled = Enabled,
            Note = Note.Trim(),
            Priority = _priority,
            Direction = _direction,
            CallUserDataPattern = _callUserDataPattern,
        };

        if (IsHunt)
        {
            string[] parts = HuntPortsText.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Trim().Length > 0)
                {
                    rc.HuntPorts.Add(parts[i].Trim());
                }
            }
        }
        else
        {
            for (int i = 0; i < BackupPorts.Count; i++)
            {
                rc.BackupPorts.Add(BackupPorts[i]);
            }
        }

        return rc;
    }

    private string Check(X121RouteConfig rc)
    {
        if (rc.Name.Length == 0)
        {
            return "Give the route a name, for example 'Sweden' or 'Gateway'.";
        }

        for (int i = 0; i < _otherNames.Count; i++)
        {
            if (string.Equals(_otherNames[i], rc.Name, StringComparison.OrdinalIgnoreCase))
            {
                return "Another route is already called '" + rc.Name + "'; give this one its own name.";
            }
        }

        if (!X121TableBuilder.TryParseKind(rc.Kind, out X121RouteType type))
        {
            return "Pick what the route should reach.";
        }

        if (HasMatch && rc.Match.Length == 0)
        {
            return MatchLabel + ": type the digits (" + MatchWatermark + ").";
        }

        if (!X121RouteRules.CheckMatch(type, rc.Match, out string error))
        {
            return MatchLabel + ": " + error + ".";
        }

        if (IsHunt)
        {
            if (rc.HuntPorts.Count == 0)
            {
                return "Name the lines of the hunt group, separated by commas.";
            }
        }
        else if (rc.Port.Length == 0)
        {
            return "Pick the port the calls go to.";
        }

        for (int i = 0; i < rc.BackupPorts.Count; i++)
        {
            if (string.Equals(rc.BackupPorts[i], rc.Port, StringComparison.OrdinalIgnoreCase))
            {
                return "Backup " + (i + 1) + " is the route's own port; a backup must be another port.";
            }

            for (int j = 0; j < i; j++)
            {
                if (string.Equals(rc.BackupPorts[i], rc.BackupPorts[j], StringComparison.OrdinalIgnoreCase))
                {
                    return "Backup " + (i + 1) + " repeats backup " + (j + 1) + ".";
                }
            }
        }

        return string.Empty;
    }

    private void Refresh()
    {
        if (_suspendRefresh)
        {
            return;
        }

        X121RouteConfig rc = Build();
        ValidationError = Check(rc);
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(MatchCountry));
        OnPropertyChanged(nameof(BackupOrderText));

        if (!IsValid || _preview == null)
        {
            PreviewText = IsValid ? string.Empty : "No preview until the route is complete.";
            return;
        }

        string number = PreviewNumber.Trim();
        if (number.Length == 0)
        {
            PreviewText = "Type a number to see where it would go.";
            return;
        }

        try
        {
            X121RouteResult? r = _preview(rc, number);
            PreviewText = r == null ? string.Empty : r.Explanation;
        }
        catch (System.IO.InvalidDataException ex)
        {
            PreviewText = ex.Message;
        }
    }

    [RelayCommand]
    private void AddBackup()
    {
        string? p = BackupToAdd?.Trim();
        if (string.IsNullOrEmpty(p))
        {
            return;
        }

        BackupPorts.Add(p);
        BackupToAdd = null;
    }

    [RelayCommand]
    private void RemoveBackup()
    {
        if (SelectedBackup != null)
        {
            BackupPorts.Remove(SelectedBackup);
        }
    }

    [RelayCommand]
    private void MoveBackupUp()
    {
        int i = SelectedBackup == null ? -1 : BackupPorts.IndexOf(SelectedBackup);
        if (i > 0)
        {
            string keep = SelectedBackup!;
            BackupPorts.Move(i, i - 1);
            SelectedBackup = keep;
        }
    }

    [RelayCommand]
    private void MoveBackupDown()
    {
        int i = SelectedBackup == null ? -1 : BackupPorts.IndexOf(SelectedBackup);
        if (i >= 0 && i < BackupPorts.Count - 1)
        {
            string keep = SelectedBackup!;
            BackupPorts.Move(i, i + 1);
            SelectedBackup = keep;
        }
    }

    [RelayCommand]
    private void Ok()
    {
        Refresh();
        if (!IsValid)
        {
            Result = null;
            return;
        }

        Result = Build();
        _logger.LogInformation("Route dialog OK: {Name}", Result.Name);
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        _logger.LogDebug("Route dialog cancelled");
    }
}
