namespace BloodCraftHub.Services;

// 0.10.6: chat-logging classification. Every outbound command BCH sends
// gets bucketed into a category (BchAuto / Bloodcraft / Kindred / Other),
// and additionally tagged with whether BCH parses + displays the reply in
// its UI somewhere.
//
// The category drives the Chat Logging visibility toggles in Settings.
// HasBchUIDisplay drives the safety rule per the design discussion: a
// reply gets suppressed only when BCH ALSO renders that information
// somewhere in its own UI. If BCH doesn't render it (just an action
// confirmation, an admin reply, or any command we don't structurally
// parse), the chat copy STAYS visible regardless of the category toggle —
// otherwise the user would lose the only place that info is displayed.
//
// This means most action commands (.fam b, .fam r, .lvl set, KindredCommands
// admin actions, etc.) always show their replies in chat. Toggling the
// Bloodcraft / Kindred category off only hides the chat copies of commands
// BCH has a dedicated UI surface for (BoxList, BloodInfo, PrestigeInfo,
// weapon expertise, V-Blood scanner, etc.) — exactly the cases where the
// chat copy is redundant noise.
public enum CommandCategory
{
    /// <summary>Unknown / not classified. Default for commands we don't know about — chat always visible.</summary>
    Other = 0,
    /// <summary>Fired automatically by BCH itself (V-Blood scanner, overlay bonus-stats ticker, tab auto-refresh).
    /// Always has BCH UI display by definition — that's the reason BCH fires them. Default visibility: hidden.</summary>
    BchAuto = 1,
    /// <summary>User-initiated command targeting Bloodcraft (.fam / .wep / .bl / .lvl / .class / .prestige / .prof / .quest / .misc).
    /// HasBchUIDisplay depends on whether BCH parses the specific command's reply.</summary>
    Bloodcraft = 2,
    /// <summary>User-initiated command targeting KindredCommands / KindredLogistics (.kp_ / .kc_ / .kl_ / .kindred…).
    /// HasBchUIDisplay = false for everything today — BCH doesn't parse Kindred replies structurally yet.</summary>
    Kindred = 3,
}

public readonly struct CommandClassification
{
    public readonly CommandCategory Category;
    public readonly bool HasBchUIDisplay;
    public CommandClassification(CommandCategory category, bool hasBchUIDisplay)
    {
        Category = category;
        HasBchUIDisplay = hasBchUIDisplay;
    }
}

public static class CommandClassifier
{
    // ---------- BCH-auto path ----------

    /// <summary>
    /// Used by MessageService.EnqueueMessageSilent — anything fired through
    /// the silent path is by definition BCH-auto and BCH-renders. Category
    /// suppression is keyed off the BchAuto toggle.
    /// </summary>
    public static CommandClassification ForBchAuto()
        => new(CommandCategory.BchAuto, hasBchUIDisplay: true);

    // ---------- User-fire path ----------

    /// <summary>
    /// Classify a user-initiated outbound command by prefix. Used by the
    /// regular EnqueueMessage path through NoteOutboundForIntercept arming.
    /// </summary>
    public static CommandClassification ForUserFire(string command)
    {
        if (string.IsNullOrEmpty(command)) return new(CommandCategory.Other, false);

        // -------- Bloodcraft: commands BCH parses + displays --------
        // These have a dedicated structured intercept feeding a UI surface.
        // Their reply gets suppressed when the Bloodcraft toggle is off
        // because BCH already shows the same information in its UI.
        if (CommandPrefixMatches(command,
                ".fam boxes", ".familiar listboxes",
                ".fam l",     ".familiar list",
                ".fam s ",    ".familiar search ",
                ".fam gl",
                ".bl get ",   ".blood get ",
                ".prestige get "))
            return new(CommandCategory.Bloodcraft, hasBchUIDisplay: true);

        // .wep get takes no arguments — match exact.
        if (command.Equals(".wep get",    System.StringComparison.Ordinal)
         || command.Equals(".weapon get", System.StringComparison.Ordinal))
            return new(CommandCategory.Bloodcraft, hasBchUIDisplay: true);

        // 0.10.12: commands whose chat reply is now mirrored to the global
        // LastResponse panel via the expanded ShouldArmGenericCapture list.
        // Tagging HasBchUIDisplay=true lets the Bloodcraft chat-logging
        // toggle suppress the chat copy when the user prefers the UI.
        if (CommandPrefixMatches(command,
                ".fam sb ",        // smart-bind (single hit, multi-match list, or no-match error)
                ".misc sct "))
            return new(CommandCategory.Bloodcraft, hasBchUIDisplay: true);
        if (command.Equals(".lvl log",       System.StringComparison.Ordinal)
         || command.Equals(".quest log",     System.StringComparison.Ordinal)
         || command.Equals(".prof log",      System.StringComparison.Ordinal)
         || command.Equals(".misc silence",  System.StringComparison.Ordinal))
            return new(CommandCategory.Bloodcraft, hasBchUIDisplay: true);

        // -------- Bloodcraft: commands BCH does NOT parse + display --------
        // Actions, confirmations, listings without dedicated UI. Always visible.
        if (CommandPrefixMatches(command,
                ".fam ", ".familiar ",
                ".bl ",  ".blood ",
                ".wep ", ".weapon ",
                ".lvl ", ".level ",
                ".class ",
                ".prestige ",
                ".prof ", ".profession ",
                ".quest ",
                ".misc "))
            return new(CommandCategory.Bloodcraft, hasBchUIDisplay: false);

        // -------- Kindred --------
        // KindredCommands / KindredLogistics — admin + player commands.
        // BCH doesn't structurally parse any Kindred replies today (0.10.6),
        // so HasBchUIDisplay = false. The Kindred toggle is wired but
        // currently no-op — added now so users can see + control it for
        // future Kindred structured parsing.
        if (CommandPrefixMatches(command,
                ".kp_", ".kc_", ".kl_",
                ".kindred",
                ".cmd ",
                ".clan ",
                ".boss list", ".boss ",
                ".region "))
            return new(CommandCategory.Kindred, hasBchUIDisplay: false);

        // Anything else (vanilla console-style or unknown).
        return new(CommandCategory.Other, hasBchUIDisplay: false);
    }

    // ---------- Helper ----------

    private static bool CommandPrefixMatches(string command, params string[] prefixes)
    {
        foreach (var p in prefixes)
            if (command.StartsWith(p, System.StringComparison.Ordinal)) return true;
        return false;
    }
}
