// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if PREDICTOR

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Management.Automation.Language;
using System.Management.Automation.Subsystem;

namespace Microsoft.PowerShell.UnixCompleters
{
    public abstract class BashCommons
    {
        private readonly Dictionary<string, string> _commandCompletionFunctions;
        private static readonly string s_resolveCompleterCommandTemplate = string.Join("; ", new []
        {
            "-lic \". /usr/share/bash-completion/bash_completion 2>/dev/null",
            "_completion_loader {0} 2>/dev/null",
            "complete -p {0} 2>/dev/null | sed -E 's/^complete.*-F ([^ ]+).*$/\\1/'\""
        });

        protected const string BashCompletionInitCommand = ". /usr/share/bash-completion/bash_completion 2>/dev/null";
        protected const string ResolveCompleterCommandTemplate = "_completion_loader {0} 2>/dev/null; complete -p {0} 2>/dev/null | sed -E 's/^complete.*-F ([^ ]+).*$/\\1/'";

        private readonly string _bashPath;

        protected BashCommons(string bashPath)
        {
            _bashPath = bashPath;
            _commandCompletionFunctions = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        private string ResolveCommandCompleterFunction(string commandName)
        {
            if (string.IsNullOrEmpty(commandName))
            {
                throw new ArgumentException(nameof(commandName));
            }

            string completerFunction;
            if (_commandCompletionFunctions.TryGetValue(commandName, out completerFunction))
            {
                return completerFunction;
            }

            string resolveCompleterInvocation = string.Format(s_resolveCompleterCommandTemplate, commandName);
            completerFunction = InvokeBashWithArguments(resolveCompleterInvocation).Trim();
            _commandCompletionFunctions[commandName] = completerFunction;

            return completerFunction;
        }

        private static string BuildCompWordsBashArrayString(string line, bool cursorRightAtCmdEnd)
        {
            // Build a bash array of line components, like "('ls' '-a')"
            string[] lineElements = line.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries);

            int approximateLength = 0;
            foreach (string element in lineElements)
            {
                approximateLength += element.Length + 2;
            }

            var sb = new StringBuilder(approximateLength);

            sb.Append('(')
                .Append('\'')
                .Append(lineElements[0].Replace("'", "\\'"))
                .Append('\'');

            for (int i = 1; i < lineElements.Length; i++)
            {
                sb.Append(' ')
                    .Append('\'')
                    .Append(lineElements[i].Replace("'", "\\'"))
                    .Append('\'');
            }

            sb.Append(cursorRightAtCmdEnd ? ")" : " '')");
            return sb.ToString();
        }

        protected string InvokeBashWithArguments(string argumentString)
        {
            using (var bashProc = new Process())
            {
                bashProc.StartInfo.FileName = _bashPath;
                bashProc.StartInfo.Arguments = argumentString;
                bashProc.StartInfo.UseShellExecute = false;
                bashProc.StartInfo.RedirectStandardOutput = true;
                bashProc.Start();

                return bashProc.StandardOutput.ReadToEnd();
            }
        }

        protected static string BuildCompletionCommand(
            string command,
            string COMP_LINE,
            string COMP_WORDS,
            int COMP_CWORD,
            int COMP_POINT,
            string completionFunction,
            string wordToComplete,
            string previousWord)
        {
            return new StringBuilder(512)
                .Append("-lic \". /usr/share/bash-completion/bash_completion 2>/dev/null; ")
                .Append("_completion_loader ").Append(command).Append(" 2>/dev/null; ")
                .Append("COMP_LINE=").Append(COMP_LINE).Append("; ")
                .Append("COMP_WORDS=").Append(COMP_WORDS).Append("; ")
                .Append("COMP_CWORD=").Append(COMP_CWORD).Append("; ")
                .Append("COMP_POINT=").Append(COMP_POINT).Append("; ")
                .Append("bind 'set completion-ignore-case on' 2>/dev/null; ")
                .Append(completionFunction)
                    .Append(" '").Append(command).Append("'")
                    .Append(" '").Append(wordToComplete).Append("'")
                    .Append(" '").Append(previousWord).Append("' 2>/dev/null; ")
                .Append("IFS=$'\\n'; ")
                .Append("echo \"\"\"${COMPREPLY[*]}\"\"\"\"")
                .ToString();
        }

        protected List<string> CompleteCommand(
            string command,
            string wordToComplete,
            CommandAst commandAst,
            int cursorPosition)
        {
            string completerFunction = ResolveCommandCompleterFunction(command);

            var commandElements = commandAst.CommandElements;
            string commandText = commandAst.Extent.Text;
            bool cursorRightAtCmdEnd = cursorPosition == commandAst.Extent.EndOffset;

            int cursorWordIndex = cursorRightAtCmdEnd ? commandElements.Count - 1 : commandElements.Count;
            string commandLine = cursorRightAtCmdEnd ? $"'{commandText}'" : $"'{commandText} '";

            string previousWord = commandElements[cursorWordIndex - 1].Extent.Text;
            string bashWordArray = BuildCompWordsBashArrayString(commandText, cursorRightAtCmdEnd);

            string completionCommand = BuildCompletionCommand(
                command,
                COMP_LINE: commandLine,
                COMP_WORDS: bashWordArray,
                COMP_CWORD: cursorWordIndex,
                COMP_POINT: cursorPosition,
                completerFunction,
                wordToComplete,
                previousWord);

            List<string> completionResults = InvokeBashWithArguments(completionCommand)
                .Split('\n')
                .Distinct(StringComparer.Ordinal)
                .ToList();

            completionResults.Sort(StringComparer.Ordinal);

            return completionResults;
        }
    }

    public class BashUtilPredictor : BashCommons, IPredictor
    {
        public Guid Id { get; }
        public string Name => UnixHelpers.PredictorName;
        public string Description => UnixHelpers.PredictorDescription;

        public BashUtilPredictor(string bashPath)
            : base(bashPath)
        {
            Id = Guid.Parse(UnixHelpers.PredictorId);
        }

        public List<string> GetSuggestion(PredictionContext context, CancellationToken cancellationToken)
        {
            Ast lastAst = context.RelatedAsts.Last();
            if (lastAst is CommandElementAst && lastAst.Parent is CommandAst commandAst)
            {
                if (commandAst.CommandElements[0] is StringConstantExpressionAst cmdName &&
                    UnixHelpers.NativeUtilNames.Contains(cmdName.Value))
                {
                    int cursorOffset = context.CursorPosition.Offset;
                    string resultPrefix = string.Empty;
                    string wordAtCursor = context.TokenAtCursor != null ? context.TokenAtCursor.Text : string.Empty;

                    if (commandAst.CommandElements.Count == 1 && cursorOffset == commandAst.Extent.EndOffset)
                    {
                        // If the cursor is at the end of the command name, we adjust the cursor offset
                        // to try calculating the first argument completion texts.
                        cursorOffset += 1;
                        resultPrefix = " ";
                        wordAtCursor = string.Empty;
                    }

                    return CompleteCommand(cmdName.Value, wordAtCursor, commandAst, cursorOffset);
                }
            }

            return null;
        }

        #region "No need to process early or accept feedback."

        public bool SupportEarlyProcessing => false;
        public bool AcceptFeedback => false;
        public void EarlyProcessWithHistory(IReadOnlyList<string> history) { }
        public void LastSuggestionAccepted(string acceptedSuggestion) { }
        public void LastSuggestionDenied() { }

        #endregion
    }
}

#endif
