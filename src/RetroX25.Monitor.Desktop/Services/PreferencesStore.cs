//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs
// RetroX25 - Accessing yesterday's X.25 networks with today's standards
//

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RetroX25.Monitor.Desktop.Services
{
    /// <summary>
    /// Loads and saves <see cref="AppPreferences"/> as JSON under the user's application-data folder.
    /// All I/O is synchronous (the app is single-threaded by design) and failures fall back to defaults so
    /// a missing or corrupt file never blocks startup.
    /// </summary>
    public static class PreferencesStore
    {
        // Human-readable JSON with the theme written as its enum name (X25 / RetroFs).
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// The absolute path of the preferences file:
        /// <c>%USERPROFILE%\Documents\RetroProtocol\preferences.json</c>.
        /// </summary>
        public static string FilePath
        {
            get
            {
                string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                return Path.Combine(documents, "RetroProtocol", "preferences.json");
            }
        }

        /// <summary>
        /// Loads the preferences, returning defaults when the file is absent or unreadable.
        /// </summary>
        /// <returns>
        /// The loaded preferences, never null.
        /// </returns>
        public static AppPreferences Load()
        {
            try
            {
                string path = FilePath;
                if (!File.Exists(path))
                {
                    return new AppPreferences();
                }

                string json = File.ReadAllText(path);
                AppPreferences? prefs = JsonSerializer.Deserialize<AppPreferences>(json, Options);
                return prefs ?? new AppPreferences();
            }
            catch (Exception)
            {
                // Any read/parse failure: fall back to defaults rather than failing to start.
                return new AppPreferences();
            }
        }

        /// <summary>
        /// Saves the preferences, creating the containing folder if needed. Failures are swallowed so a
        /// non-writable profile never crashes the app.
        /// </summary>
        /// <param name="prefs">
        /// The preferences to persist.
        /// </param>
        public static void Save(AppPreferences prefs)
        {
            if (prefs == null)
            {
                return;
            }

            try
            {
                string path = FilePath;
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonSerializer.Serialize(prefs, Options);
                File.WriteAllText(path, json);
            }
            catch (Exception)
            {
                // Best-effort persistence.
            }
        }
    }
}
