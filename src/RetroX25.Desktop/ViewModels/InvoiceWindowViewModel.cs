//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RetroIO.Protocols.X25.Switch.Billing;
using RetroIO.Protocols.X25.Switch.Model;
using RetroX25.Desktop.Services;

namespace RetroX25.Desktop.ViewModels;

/// <summary>
/// ViewModel for the Bill window: an account's bill for one month, laid out like a printed statement from a 1980s
/// packet network.
/// </summary>
/// <remarks>
/// <para>
/// The bill is worked out by RetroIO's billing code (<see cref="InvoiceBuilder"/>, printed by <see cref="InvoiceText"/>),
/// the same code the switch console's <c>show x25 invoice</c> uses, so the window and the console can never disagree.
/// It reads two files: the call record file and the price list. They start as the built-in switch's
/// <c>cdr_file</c> and <c>tariff_file</c>; either can be changed, so a record file written by x25emu's switch can be
/// billed too.
/// </para>
/// </remarks>
public partial class InvoiceWindowViewModel : ViewModelWithLogger
{
    private readonly Func<DateTimeOffset> _clock;
    private string _networkName;

    /// <summary>Gets or sets the call record file.</summary>
    [ObservableProperty]
    private string _recordFile = string.Empty;

    /// <summary>Gets or sets the price list file.</summary>
    [ObservableProperty]
    private string _tariffFile = string.Empty;

    /// <summary>Gets or sets the month, as yyyy-mm.</summary>
    [ObservableProperty]
    private string _month = string.Empty;

    /// <summary>Gets or sets the account billed.</summary>
    [ObservableProperty]
    private string? _selectedAccount;

    /// <summary>Gets or sets the printed bill; empty until one is made.</summary>
    [ObservableProperty]
    private string _billText = string.Empty;

    /// <summary>Gets or sets what went wrong, in words; empty when nothing did.</summary>
    [ObservableProperty]
    private string _errorText = string.Empty;

    /// <summary>
    /// Gets the accounts that pay for at least one call in the record file.
    /// </summary>
    public ObservableCollection<string> Accounts { get; } = new ObservableCollection<string>();

    /// <summary>
    /// Creates the view-model.
    /// </summary>
    /// <param name="logger">The logger for this view-model.</param>
    /// <param name="embeddedSwitch">The built-in switch, whose file names it starts with; null when the app has none.</param>
    /// <param name="clock">The wall clock for "this month"; the system clock when null (a test sets it).</param>
    public InvoiceWindowViewModel(
        ILogger<InvoiceWindowViewModel> logger,
        EmbeddedSwitchService? embeddedSwitch = null,
        Func<DateTimeOffset>? clock = null) : base(logger)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _networkName = embeddedSwitch?.NetworkName ?? string.Empty;
        _recordFile = embeddedSwitch?.CdrFile ?? string.Empty;
        _tariffFile = embeddedSwitch?.TariffFile ?? string.Empty;
        DateTimeOffset now = _clock();
        _month = now.Year.ToString("D4", CultureInfo.InvariantCulture) + "-" + now.Month.ToString("D2", CultureInfo.InvariantCulture);
        if (_recordFile.Length > 0)
        {
            LoadAccounts();
        }
    }

    /// <summary>
    /// Reads the record file and fills the list of accounts that pay for calls in it.
    /// </summary>
    [RelayCommand]
    public void LoadAccounts()
    {
        ErrorText = string.Empty;
        Accounts.Clear();
        List<Cdr>? records = ReadRecords();
        if (records == null)
        {
            return;
        }

        List<string> payers = InvoiceBuilder.PayingAccounts(records);
        for (int i = 0; i < payers.Count; i++)
        {
            Accounts.Add(payers[i]);
        }

        if (SelectedAccount == null || !Accounts.Contains(SelectedAccount))
        {
            SelectedAccount = Accounts.Count > 0 ? Accounts[0] : null;
        }

        if (Accounts.Count == 0)
        {
            ErrorText = "No account pays for a connected call in " + RecordFile + ".";
        }
    }

    /// <summary>
    /// Works out and prints the bill of the selected account for the month.
    /// </summary>
    [RelayCommand]
    public void MakeBill()
    {
        ErrorText = string.Empty;
        BillText = string.Empty;
        if (string.IsNullOrEmpty(SelectedAccount))
        {
            ErrorText = "Choose an account.";
            return;
        }

        if (!TryParseMonth(Month, out int year, out int month))
        {
            ErrorText = "The month is written yyyy-mm, for example 2026-11.";
            return;
        }

        if (TariffFile.Length == 0)
        {
            ErrorText = "Give the price list file (tariff_file).";
            return;
        }

        Tariff tariff;
        try
        {
            tariff = Tariff.Load(TariffFile);
        }
        catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException)
        {
            ErrorText = "Price list: " + e.Message;
            return;
        }

        List<Cdr>? records = ReadRecords();
        if (records == null)
        {
            return;
        }

        Invoice inv = InvoiceBuilder.Build(tariff, records, SelectedAccount, year, month);
        BillText = InvoiceText.Format(inv, _networkName);
        Logger.LogInformation("Bill made for {Account}, {Month}", SelectedAccount, Month);
    }

    /// <summary>
    /// Reads "yyyy-mm".
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="year">The year.</param>
    /// <param name="month">The month, 1 to 12.</param>
    /// <returns>True when the text is a month.</returns>
    public static bool TryParseMonth(string text, out int year, out int month)
    {
        year = 0;
        month = 0;
        string t = text.Trim();
        return t.Length == 7 && t[4] == '-'
            && int.TryParse(t.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out year)
            && int.TryParse(t.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out month)
            && month >= 1 && month <= 12;
    }

    // The record file, or null with ErrorText saying why. A bad line is reported, never billed around.
    private List<Cdr>? ReadRecords()
    {
        if (RecordFile.Length == 0)
        {
            ErrorText = "Give the call record file (cdr_file).";
            return null;
        }

        try
        {
            return CdrReader.ReadFile(RecordFile);
        }
        catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException)
        {
            ErrorText = "Call records: " + e.Message;
            return null;
        }
    }
}
