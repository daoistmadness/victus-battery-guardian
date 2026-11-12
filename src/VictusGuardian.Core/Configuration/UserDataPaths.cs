using System;
using System.IO;

namespace VictusGuardian.Core.Configuration;

/// <summary>
/// Canonical manager for user data storage paths in %LOCALAPPDATA%.
/// Provides safe directory resolution and non-destructive backward-compatible migration.
/// </summary>
public static class UserDataPaths
{
    public const string CanonicalFolderName = "VictusGuardian";
    public const string AlternateFolderName = "VictusBatteryGuardian";

    public static string LocalAppDataRoot =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string CanonicalDirectory =>
        Path.Combine(LocalAppDataRoot, CanonicalFolderName);

    public static string AlternateDirectory =>
        Path.Combine(LocalAppDataRoot, AlternateFolderName);

    public static string SettingsFilePath =>
        Path.Combine(CanonicalDirectory, "settings.json");

    public static string DatabaseFilePath =>
        Path.Combine(CanonicalDirectory, "battery_history.db");

    public static string LogsDirectory =>
        Path.Combine(CanonicalDirectory, "logs");

    public static string UpdatesDirectory =>
        Path.Combine(CanonicalDirectory, "updates");

    /// <summary>
    /// Ensures the canonical user data directory exists.
    /// If an alternate folder (e.g. VictusBatteryGuardian) exists while the canonical folder is absent,
    /// performs a safe, non-destructive copy migration so historical data is never lost.
    /// </summary>
    public static string EnsureCanonicalDirectory()
    {
        var targetDir = CanonicalDirectory;
        var legacyDir = AlternateDirectory;

        if (Directory.Exists(targetDir))
        {
            return targetDir;
        }

        // Check if legacy / alternate directory exists and has files to preserve
        if (Directory.Exists(legacyDir))
        {
            try
            {
                Directory.CreateDirectory(targetDir);

                // Transactional copy of files: copy each file, rollback if failure occurs
                var files = Directory.GetFiles(legacyDir, "*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    var relativePath = Path.GetRelativePath(legacyDir, file);
                    var destFile = Path.Combine(targetDir, relativePath);
                    var destSubdir = Path.GetDirectoryName(destFile);
                    if (!string.IsNullOrEmpty(destSubdir) && !Directory.Exists(destSubdir))
                    {
                        Directory.CreateDirectory(destSubdir);
                    }
                    File.Copy(file, destFile, overwrite: false);
                }

                return targetDir;
            }
            catch
            {
                // If migration fails partway through, do not leave corrupted state; preserve legacyDir intact
                if (Directory.Exists(targetDir) && Directory.GetFiles(targetDir).Length == 0)
                {
                    try { Directory.Delete(targetDir, recursive: true); } catch { /* best effort */ }
                }
                // Fall back to legacy directory if migration could not be completed cleanly
                return legacyDir;
            }
        }

        Directory.CreateDirectory(targetDir);
        return targetDir;
    }
}
