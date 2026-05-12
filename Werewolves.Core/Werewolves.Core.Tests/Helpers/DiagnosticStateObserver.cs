using System.Text;
using System.Text.RegularExpressions;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.Tests.Helpers;

/// <summary>
/// Captures state changes for diagnostic output in tests.
/// </summary>
internal partial class DiagnosticStateObserver : IStateChangeObserver
{
    private readonly List<string> _log = new();
    private readonly object _lock = new();
    private IGameSession? _session;

    /// <summary>
    /// Sets the session reference for resolving player GUIDs to names.
    /// </summary>
    public void SetSession(IGameSession session) => _session = session;

    public IReadOnlyList<string> Log
    {
        get { lock (_lock) return _log.ToList(); }
    }

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidRegex();

    private string ReplaceGuidsWithNames(string text)
    {
        if (_session == null) return text;

        var playerLookup = _session.GetPlayers().ToDictionary(
            p => p.Id.ToString(),
            p => p.Name);

        return GuidRegex().Replace(text, match =>
        {
            var guid = match.Value;
            return playerLookup.TryGetValue(guid, out var name)
                ? $"{name}"
                : guid;
        });
    }

    public void OnMainPhaseChanged(GamePhase newPhase)
    {
        lock (_lock) _log.Add($"[Phase] → {newPhase}");
    }

    public void OnSubPhaseChanged(string? newSubPhase)
    {
        lock (_lock) _log.Add($"[SubPhase] → {newSubPhase ?? "(cleared)"}");
    }

    public void OnSubPhaseStageChanged(string? newSubPhaseStage)
    {
        lock (_lock) _log.Add($"[SubPhaseStage] → {newSubPhaseStage ?? "(cleared)"}");
    }

    public void OnListenerChanged(ListenerIdentifier? listener, string? listenerState)
    {
        var listenerStr = listener?.ToString() ?? "(none)";
        var stateStr = listenerState ?? "(none)";
        lock (_lock) _log.Add($"[Listener] → {listenerStr} | State: {stateStr}");
    }

    public void OnTurnNumberChanged(int newTurnNumber)
    {
        lock (_lock) _log.Add($"[Turn] → {newTurnNumber}");
    }

    public void OnPendingInstructionChanged(ModeratorInstruction? instruction)
    {
        var instructionStr = instruction?.GetType().Name ?? "(null)";
        lock (_lock) _log.Add($"[Instruction] → {instructionStr}");
    }

    public void OnLogEntryApplied(GameLogEntryBase entry)
    {
        lock (_lock) _log.Add($"[Log] {entry.GetType().Name}: {entry}");
    }

    public string GetFormattedLog()
    {
        lock (_lock)
        {
            if (_log.Count == 0) return "(no state changes recorded)";

            var entries = new List<(string Type, string Content)>();
            foreach (var logLine in _log)
            {
                var (type, content) = ParseLogEntry(logLine);
                content = ReplaceGuidsWithNames(content);
                entries.Add((type, content));
            }

            // Calculate content column width (minimum 20, max based on content)
            int contentWidth = Math.Max(20, entries.Max(e => e.Content.Length));
            
            var sb = new StringBuilder();
            sb.AppendLine("=== State Change Timeline ===");
            sb.AppendLine();
            
            // Header row with box-drawing dividers
            // Columns: # | Phase | Sub | Stage | Instr | Log | Lstnr | Turn | Content
            string divider = $"{"- - ",4} - {" - ",3} - {" - ",3} - {" - ",3} - {" - ",3} - {" - ",3} - {" - ",3} - {" - ",3} + - -";
            
            sb.AppendLine(divider);
            sb.AppendLine($"{"#",4} | {"Phs",3} | {"Sub",3} | {"Stg",3} | {"Ins",3} | {"Log",3} | {"Lst",3} | {"Trn",3} | Content");
            sb.AppendLine(divider);
            
            for (int i = 0; i < entries.Count; i++)
            {
                var (type, content) = entries[i];
                
                string phs = type == "Phase" ? " X " : "   ";
                string sub = type == "SubPhase" ? " X " : "   ";
                string stg = type == "Stage" ? " X " : "   ";
                string ins = type == "Instruction" ? " X " : "   ";
                string log = type == "Log" ? " X " : "   ";
                string lst = type == "Listener" ? " X " : "   ";
                string trn = type == "Turn" ? " X " : "   ";
                
                sb.AppendLine($"{i + 1,4} | {phs} | {sub} | {stg} | {ins} | {log} | {lst} | {trn} | {content}");
                sb.AppendLine(divider);
            }
            
            return sb.ToString();
        }
    }

    private static (string Type, string Content) ParseLogEntry(string logLine)
    {
        // Parse entries like "[Phase] → Night" or "[Log] NightActionLogEntry: ..."
        if (logLine.StartsWith("[Phase]"))
            return ("Phase", logLine.Substring("[Phase] → ".Length));
        if (logLine.StartsWith("[SubPhaseStage]"))
            return ("Stage", logLine.Substring("[SubPhaseStage] → ".Length));
        if (logLine.StartsWith("[SubPhase]"))
            return ("SubPhase", logLine.Substring("[SubPhase] → ".Length));
        if (logLine.StartsWith("[Instruction]"))
            return ("Instruction", logLine.Substring("[Instruction] → ".Length));
        if (logLine.StartsWith("[Log]"))
            return ("Log", logLine.Substring("[Log] ".Length));
        if (logLine.StartsWith("[Listener]"))
            return ("Listener", logLine.Substring("[Listener] → ".Length));
        if (logLine.StartsWith("[Turn]"))
            return ("Turn", logLine.Substring("[Turn] → ".Length));
        
        return ("Unknown", logLine);
    }

    public void Clear()
    {
        lock (_lock) _log.Clear();
    }
}
