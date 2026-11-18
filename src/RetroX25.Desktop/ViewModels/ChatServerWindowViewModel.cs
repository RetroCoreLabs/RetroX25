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
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using RetroIO.Protocols.X25.Protocols;
using RetroIO.Protocols.X25.Services;
using RetroX25.Desktop.Services;

namespace RetroX25.Desktop.ViewModels;

/// <summary>
/// One user row of the chat monitor.
/// </summary>
public sealed class ChatUserRow
{
    /// <summary>The place in the chat's user table, the number <c>.s</c> shows and <c>!N</c> whispers to.</summary>
    public int Slot { get; init; }

    /// <summary>The nickname now.</summary>
    public string Nickname { get; init; } = "";

    /// <summary>The calling X.121 address.</summary>
    public string From { get; init; } = "";

    /// <summary>The channel the user is on.</summary>
    public long Channel { get; init; }

    /// <summary>When the call reached the chat, as text (HH:mm:ss).</summary>
    public string ConnectedAt { get; init; } = "";

    /// <summary>How long the user has been in, as text (h:mm:ss).</summary>
    public string Duration { get; init; } = "";
}

/// <summary>
/// One channel row of the chat monitor.
/// </summary>
public sealed class ChatChannelRow
{
    /// <summary>The channel number.</summary>
    public long Channel { get; init; }

    /// <summary>How many users are on it.</summary>
    public int Users { get; init; }

    /// <summary>"yes" when a user has set a password with <c>.p</c>, else empty. The password itself is never shown.</summary>
    public string Protected { get; init; } = "";
}

/// <summary>
/// The chat monitor window (Ctrl+8): who is in the built-in Altos chat and which channels are in use. It reads the
/// runtime's chat (<c>ProtocolRuntime.ChatService</c>) and changes nothing. It refreshes when the chat says something
/// changed, and every 2 seconds for the time column, as the TAD Server Monitor does.
/// </summary>
public sealed partial class ChatServerWindowViewModel : ViewModelWithLogger
{
    private readonly ChatProtocolService? _chat;
    private readonly List<AltosChatUserInfo> _users = new List<AltosChatUserInfo>();
    private readonly List<AltosChatChannelInfo> _channels = new List<AltosChatChannelInfo>();
    private System.Threading.Timer? _refreshTimer;
    private bool _disposed;

    [ObservableProperty]
    private string _statusText = "";

    /// <summary>The users, lowest place first.</summary>
    public ObservableCollection<ChatUserRow> Users { get; } = new ObservableCollection<ChatUserRow>();

    /// <summary>The channels with users on them, lowest number first.</summary>
    public ObservableCollection<ChatChannelRow> Channels { get; } = new ObservableCollection<ChatChannelRow>();

    /// <summary>
    /// Creates the monitor and reads the chat once at once.
    /// </summary>
    /// <param name="logger">Where the view-model logs.</param>
    /// <param name="runtimeService">Holds the running runtime whose chat is shown.</param>
    public ChatServerWindowViewModel(ILogger<ChatServerWindowViewModel> logger, IRuntimeService runtimeService)
        : base(logger)
    {
        if (runtimeService == null)
        {
            throw new ArgumentNullException(nameof(runtimeService));
        }

        _chat = runtimeService.IsRunning ? runtimeService.Runtime.ChatService : null;
        if (_chat != null)
        {
            _chat.Changed += OnChatChanged;
        }

        Refresh();
        _refreshTimer = new System.Threading.Timer(_ => Post(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// Reads the chat again and rebuilds both lists. Must run on the UI thread (the lists are bound).
    /// </summary>
    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        if (_chat == null)
        {
            Users.Clear();
            Channels.Clear();
            StatusText = "The chat is not running (no Chat address in the configuration).";
            return;
        }

        _chat.GetUsers(_users);
        _chat.GetChannels(_channels);

        DateTime now = DateTime.Now;
        Users.Clear();
        for (int i = 0; i < _users.Count; i++)
        {
            AltosChatUserInfo u = _users[i];
            TimeSpan inFor = now - u.ConnectedAt;
            if (inFor < TimeSpan.Zero)
            {
                inFor = TimeSpan.Zero;
            }

            Users.Add(new ChatUserRow
            {
                Slot = u.Slot,
                Nickname = u.Nickname,
                From = u.From,
                Channel = u.Channel,
                ConnectedAt = u.ConnectedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                Duration = ((int)inFor.TotalHours).ToString(CultureInfo.InvariantCulture) + ":" +
                           inFor.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":" +
                           inFor.Seconds.ToString("00", CultureInfo.InvariantCulture),
            });
        }

        Channels.Clear();
        for (int i = 0; i < _channels.Count; i++)
        {
            AltosChatChannelInfo c = _channels[i];
            Channels.Add(new ChatChannelRow
            {
                Channel = c.Channel,
                Users = c.Users,
                Protected = c.IsProtected ? "yes" : "",
            });
        }

        StatusText = "Users: " + Users.Count.ToString(CultureInfo.InvariantCulture) +
                     "   Channels in use: " + Channels.Count.ToString(CultureInfo.InvariantCulture);
    }

    private void OnChatChanged(object? sender, EventArgs e)
    {
        Post();
    }

    private void Post()
    {
        if (_disposed)
        {
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);
    }

    /// <summary>
    /// Stops the timer and stops listening to the chat. Called when the window closes.
    /// </summary>
    public override void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_chat != null)
        {
            _chat.Changed -= OnChatChanged;
        }

        _refreshTimer?.Dispose();
        _refreshTimer = null;
        base.Dispose();
    }
}
