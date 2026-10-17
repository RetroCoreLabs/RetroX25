//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RetroX25.Desktop.ViewModels;

namespace RetroX25.Desktop.Tests;

/// <summary>
/// Checks that each edit dialog's title says whether it adds a new entry or edits an existing one.
/// </summary>
/// <remarks>
/// Every dialog view-model takes an <c>isNew</c> flag from the tab that opens it. It used to be stored
/// and never read, so all four dialogs had the same fixed title whether they were adding or editing.
/// </remarks>
[TestFixture]
public class EditDialogTitleTests
{
    /// <summary>
    /// The known host dialog says Add when adding and Edit when editing.
    /// </summary>
    [Test]
    public void KnownHostDialog_TitleFollowsIsNew()
    {
        Assert.That(new KnownHostEditDialogViewModel(NullLogger.Instance, null, true).DialogTitle, Is.EqualTo("Add Known Host"));
        Assert.That(new KnownHostEditDialogViewModel(NullLogger.Instance, null, false).DialogTitle, Is.EqualTo("Edit Known Host"));
    }

    /// <summary>
    /// The route dialog says Add when adding and Edit when editing.
    /// </summary>
    [Test]
    public void RouteDialog_TitleFollowsIsNew()
    {
        Assert.That(new RouteEditDialogViewModel(NullLogger.Instance, null, true).DialogTitle, Is.EqualTo("Add Route"));
        Assert.That(new RouteEditDialogViewModel(NullLogger.Instance, null, false).DialogTitle, Is.EqualTo("Edit Route"));
    }

    /// <summary>
    /// The serial interface dialog says Add when adding and Edit when editing.
    /// </summary>
    [Test]
    public void SerialInterfaceDialog_TitleFollowsIsNew()
    {
        Assert.That(new SerialInterfaceEditDialogViewModel(NullLogger.Instance, null, true).DialogTitle, Is.EqualTo("Add Serial Interface"));
        Assert.That(new SerialInterfaceEditDialogViewModel(NullLogger.Instance, null, false).DialogTitle, Is.EqualTo("Edit Serial Interface"));
    }

    /// <summary>
    /// The XOT interface dialog says Add when adding and Edit when editing.
    /// </summary>
    [Test]
    public void XotInterfaceDialog_TitleFollowsIsNew()
    {
        Assert.That(new XotInterfaceEditDialogViewModel(NullLogger.Instance, null, true).DialogTitle, Is.EqualTo("Add XOT Interface"));
        Assert.That(new XotInterfaceEditDialogViewModel(NullLogger.Instance, null, false).DialogTitle, Is.EqualTo("Edit XOT Interface"));
    }
}
