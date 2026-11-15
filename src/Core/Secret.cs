namespace vShell.Core
{
    /// <summary>
    /// Keeps a secret (the Home Assistant token, a saved password) out of settings.json in plain text: Windows DPAPI, tied to the current
    /// Windows user. Stored as "dpapi:" + base64. A value without the prefix is treated as plain text (and gets protected the next time it
    /// is saved). The work is vSuite.Core's Secret (the same in vTerm and vPad, since 1.0.27); this class stays for the plugin API.
    /// </summary>
    [PluginApi]
    public static class Secret
    {
        public static bool IsProtected(string stored) { return vSuite.Secret.IsProtected(stored); }

        /// <summary>Plain text -> value to store. Empty stays empty; if protection fails the plain text is kept (so nothing is lost).</summary>
        public static string Protect(string plain) { return vSuite.Secret.Protect(plain); }

        /// <summary>Stored value -> plain text ("" if it cannot be decrypted, e.g. it was saved by another Windows user).</summary>
        public static string Unprotect(string stored) { return vSuite.Secret.Unprotect(stored); }
    }
}
