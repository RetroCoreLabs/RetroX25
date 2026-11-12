//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace RetroX25.Desktop.Services;

/// <summary>
/// Finds out whether the far end of an XOT link answers on its TCP port, without placing a call.
/// </summary>
/// <remarks>
/// An XOT client link only opens its TCP connection at the first call, so "the link is Active" says
/// nothing about whether the switch or router at the other end is there. The main window uses this
/// check to show "answers" or "NOT reachable: why" for every client link, before anybody dials.
/// </remarks>
public static class XotLinkProbe
{
    /// <summary>
    /// Opens a TCP connection to the host and port and closes it again.
    /// </summary>
    /// <param name="host">Host name or IP address of the far end.</param>
    /// <param name="port">TCP port of the far end (1998 for a Cisco router).</param>
    /// <param name="timeoutMs">How long to wait for an answer.</param>
    /// <returns>
    /// Null when the far end answered; otherwise a short plain-words reason it did not.
    /// </returns>
    public static async Task<string?> CheckAsync(string host, int port, int timeoutMs)
    {
        using TcpClient client = new TcpClient();
        try
        {
            Task connect = client.ConnectAsync(host, port);
            Task finished = await Task.WhenAny(connect, Task.Delay(timeoutMs)).ConfigureAwait(false);
            if (finished != connect)
            {
                return "no answer within " + (timeoutMs / 1000.0).ToString("0.#") + " s (nothing listens there, or a firewall blocks it)";
            }

            await connect.ConfigureAwait(false);
            return null;
        }
        catch (SocketException ex)
        {
            return ex.SocketErrorCode == SocketError.ConnectionRefused
                ? "connection refused (nothing listens on that port)"
                : ex.SocketErrorCode.ToString();
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
