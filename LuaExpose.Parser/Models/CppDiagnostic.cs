// LuaExpose.Parser - Custom C++ Parser without libclang dependency

using System;
using System.Collections.Generic;
using System.Text;

namespace CppAst
{
    /// <summary>
    /// Provides a diagnostic message for a specific location in the source code.
    /// </summary>
    public class CppDiagnosticMessage
    {
        public CppDiagnosticMessage(CppLogMessageType type, string text, CppSourceLocation location)
        {
            Type = type;
            Text = text ?? throw new ArgumentNullException(nameof(text));
            Location = location;
        }

        public readonly CppLogMessageType Type;
        public readonly string Text;
        public readonly CppSourceLocation Location;

        public override string ToString() => $"{Location}: {Type.ToString().ToLowerInvariant()}: {Text}";
    }

    /// <summary>
    /// A bag of diagnostic messages.
    /// </summary>
    public class CppDiagnosticBag
    {
        private readonly List<CppDiagnosticMessage> _messages;

        public CppDiagnosticBag()
        {
            _messages = new List<CppDiagnosticMessage>();
        }

        public void Clear()
        {
            _messages.Clear();
            HasErrors = false;
        }

        public IReadOnlyList<CppDiagnosticMessage> Messages => _messages;

        public bool HasErrors { get; private set; }

        public void Info(string message, CppSourceLocation? location = null)
        {
            LogMessage(CppLogMessageType.Info, message, location);
        }

        public void Warning(string message, CppSourceLocation? location = null)
        {
            LogMessage(CppLogMessageType.Warning, message, location);
        }

        public void Error(string message, CppSourceLocation? location = null)
        {
            LogMessage(CppLogMessageType.Error, message, location);
        }

        public void Log(CppDiagnosticMessage message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (message.Type == CppLogMessageType.Error)
            {
                HasErrors = true;
            }
            _messages.Add(message);
        }

        protected void LogMessage(CppLogMessageType type, string message, CppSourceLocation? location = null)
        {
            var locationResolved = location ?? new CppSourceLocation();
            Log(new CppDiagnosticMessage(type, message, locationResolved));
        }

        public override string ToString()
        {
            var diagnostics = new StringBuilder();
            foreach (var message in Messages)
            {
                diagnostics.AppendLine(message.ToString());
            }
            return diagnostics.ToString();
        }
    }
}
