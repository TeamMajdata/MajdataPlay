using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

#nullable enable

namespace MajdataPlay.Tools.AndroidJavaWrapperGenerator
{
    /// <summary>Distinguishes identifiers, opaque literals, punctuation, and the source terminator.</summary>
    internal enum JavaSourceTokenKind
    {
        /// <summary>A Java identifier or keyword.</summary>
        Identifier,
        /// <summary>A quoted or numeric literal whose contents cannot declare members.</summary>
        Literal,
        /// <summary>A punctuation token, with generic closing brackets kept separate.</summary>
        Symbol,
        /// <summary>The end of the compilation unit.</summary>
        End
    }

    /// <summary>Contains a lexical token and its translated-source diagnostic location.</summary>
    internal readonly struct JavaSourceToken
    {
        /// <summary>Gets the token category.</summary>
        internal JavaSourceTokenKind Kind { get; }

        /// <summary>Gets the identifier, punctuation, or literal text.</summary>
        internal string Text { get; }

        /// <summary>Gets the one-based line after Java Unicode escape translation.</summary>
        internal int Line { get; }

        /// <summary>Gets the one-based column after Java Unicode escape translation.</summary>
        internal int Column { get; }

        /// <summary>Creates a token.</summary>
        /// <param name="kind">The lexical category.</param>
        /// <param name="text">The token text.</param>
        /// <param name="line">The one-based translated line.</param>
        /// <param name="column">The one-based translated column.</param>
        internal JavaSourceToken(JavaSourceTokenKind kind, string text, int line, int column)
        {
            Kind = kind;
            Text = text;
            Line = line;
            Column = column;
        }
    }

    /// <summary>Tokenizes Java source after its mandatory Unicode-escape translation.</summary>
    internal sealed class JavaSourceLexer
    {
        /// <summary>Stores the source path for errors.</summary>
        private readonly string _path;

        /// <summary>Stores source after Unicode escape translation.</summary>
        private readonly string _text;

        /// <summary>Stores the extraction cancellation token.</summary>
        private readonly CancellationToken _cancellationToken;

        /// <summary>Tracks the next translated character.</summary>
        private int _position;

        /// <summary>Tracks the one-based translated line.</summary>
        private int _line = 1;

        /// <summary>Tracks the one-based translated column.</summary>
        private int _column = 1;

        /// <summary>Creates a lexer and performs Java Unicode-escape translation.</summary>
        /// <param name="path">The source path used in errors.</param>
        /// <param name="text">The UTF-8 decoded Java source.</param>
        /// <param name="cancellationToken">The extraction cancellation token.</param>
        /// <exception cref="InvalidDataException">A Unicode escape is malformed.</exception>
        private JavaSourceLexer(string path, string text, CancellationToken cancellationToken)
        {
            _path = path;
            _text = TranslateUnicode(path, text, cancellationToken);
            _cancellationToken = cancellationToken;
        }

        /// <summary>Tokenizes a compilation unit without inspecting literal contents as declarations.</summary>
        /// <param name="path">The source diagnostic path.</param>
        /// <param name="text">The UTF-8 decoded source.</param>
        /// <param name="cancellationToken">The extraction cancellation token.</param>
        /// <returns>The token sequence ending in an end token.</returns>
        /// <exception cref="InvalidDataException">A comment, escape, or quoted literal is malformed.</exception>
        /// <exception cref="OperationCanceledException">Extraction is canceled.</exception>
        internal static IReadOnlyList<JavaSourceToken> Tokenize(string path, string text, CancellationToken cancellationToken)
        {
            return new JavaSourceLexer(path, text, cancellationToken).ReadTokens();
        }

        /// <summary>Reads tokens, retaining punctuation needed by the declaration grammar.</summary>
        /// <returns>The complete token sequence.</returns>
        /// <exception cref="InvalidDataException">A lexical construct is unterminated.</exception>
        /// <exception cref="OperationCanceledException">Extraction is canceled.</exception>
        private IReadOnlyList<JavaSourceToken> ReadTokens()
        {
            var result = new List<JavaSourceToken>();
            while (_position < _text.Length)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var current = _text[_position];
                if (char.IsWhiteSpace(current) || current == '\uFEFF')
                {
                    Advance();
                    continue;
                }
                if (StartsWith("//"))
                {
                    while (_position < _text.Length && _text[_position] != '\r' && _text[_position] != '\n')
                    {
                        Advance();
                    }
                    continue;
                }
                if (StartsWith("/*"))
                {
                    var commentLine = _line;
                    var commentColumn = _column;
                    Advance();
                    Advance();
                    while (_position < _text.Length && !StartsWith("*/"))
                    {
                        Advance();
                    }
                    if (_position == _text.Length)
                    {
                        throw Invalid(commentLine, commentColumn, "unterminated block comment");
                    }
                    Advance();
                    Advance();
                    continue;
                }
                var start = _position;
                var line = _line;
                var column = _column;
                var kind = JavaSourceTokenKind.Symbol;
                if (IsIdentifierCharacter(_text, _position, true))
                {
                    kind = JavaSourceTokenKind.Identifier;
                    AdvanceCodePoint();
                    while (_position < _text.Length && IsIdentifierCharacter(_text, _position, false))
                    {
                        AdvanceCodePoint();
                    }
                }
                else if (current == '"' || current == '\'')
                {
                    kind = JavaSourceTokenKind.Literal;
                    ReadQuoted(current, line, column);
                }
                else if (char.IsDigit(current) || (current == '.' && _position + 1 < _text.Length && char.IsDigit(_text[_position + 1])))
                {
                    kind = JavaSourceTokenKind.Literal;
                    Advance();
                    while (_position < _text.Length)
                    {
                        var character = _text[_position];
                        var previous = _text[_position - 1];
                        if (char.IsLetterOrDigit(character) || character == '_' || character == '.'
                            || ((character == '+' || character == '-') && (previous == 'e' || previous == 'E' || previous == 'p' || previous == 'P')))
                        {
                            Advance();
                        }
                        else
                        {
                            break;
                        }
                    }
                }
                else if (StartsWith("..."))
                {
                    Advance();
                    Advance();
                    Advance();
                }
                else
                {
                    Advance();
                }
                var value = _text.Substring(start, _position - start);
                if (kind == JavaSourceTokenKind.Identifier)
                {
                    value = RemoveIdentifierIgnorables(value);
                }
                result.Add(new JavaSourceToken(kind, value, line, column));
            }
            result.Add(new JavaSourceToken(JavaSourceTokenKind.End, string.Empty, _line, _column));
            return result;
        }

        /// <summary>Skips an ordinary string, character literal, or Java text block.</summary>
        /// <param name="quote">The opening quote character.</param>
        /// <param name="line">The opening translated line.</param>
        /// <param name="column">The opening translated column.</param>
        /// <exception cref="InvalidDataException">The quoted literal is unterminated.</exception>
        private void ReadQuoted(char quote, int line, int column)
        {
            var textBlock = quote == '"' && StartsWith("\"\"\"");
            Advance();
            if (textBlock)
            {
                Advance();
                Advance();
            }
            while (_position < _text.Length)
            {
                if (_text[_position] == '\\')
                {
                    Advance();
                    if (_position < _text.Length)
                    {
                        Advance();
                    }
                }
                else if (textBlock && StartsWith("\"\"\""))
                {
                    Advance();
                    Advance();
                    Advance();
                    return;
                }
                else if (!textBlock && _text[_position] == quote)
                {
                    Advance();
                    return;
                }
                else
                {
                    if (!textBlock && (_text[_position] == '\r' || _text[_position] == '\n'))
                    {
                        throw Invalid(line, column, "line break in quoted literal");
                    }
                    Advance();
                }
            }
            throw Invalid(line, column, textBlock ? "unterminated text block" : "unterminated quoted literal");
        }

        /// <summary>Checks for literal text at the current source position.</summary>
        /// <param name="value">The text to match.</param>
        /// <returns>Whether the next characters match.</returns>
        private bool StartsWith(string value)
        {
            return _position + value.Length <= _text.Length && string.CompareOrdinal(_text, _position, value, 0, value.Length) == 0;
        }

        /// <summary>Advances one UTF-16 code unit and updates diagnostic coordinates.</summary>
        private void Advance()
        {
            if ((_position & 4095) == 0)
            {
                _cancellationToken.ThrowIfCancellationRequested();
            }
            var character = _text[_position++];
            if (character == '\r' || (character == '\n' && (_position < 2 || _text[_position - 2] != '\r')))
            {
                _line++;
                _column = 1;
            }
            else if (character != '\n')
            {
                _column++;
            }
        }

        /// <summary>Advances one identifier code point, preserving supplementary characters.</summary>
        private void AdvanceCodePoint()
        {
            var supplementary = char.IsHighSurrogate(_text[_position]) && _position + 1 < _text.Length && char.IsLowSurrogate(_text[_position + 1]);
            Advance();
            if (supplementary)
            {
                Advance();
            }
        }

        /// <summary>Checks Java identifier categories without depending on a JDK.</summary>
        /// <param name="text">The source text.</param>
        /// <param name="position">The code-point offset.</param>
        /// <param name="start">Whether this is the first code point.</param>
        /// <returns>Whether the code point is allowed in a Java identifier.</returns>
        private static bool IsIdentifierCharacter(string text, int position, bool start)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(text, position);
            if (category == UnicodeCategory.UppercaseLetter || category == UnicodeCategory.LowercaseLetter
                || category == UnicodeCategory.TitlecaseLetter || category == UnicodeCategory.ModifierLetter
                || category == UnicodeCategory.OtherLetter || category == UnicodeCategory.LetterNumber
                || category == UnicodeCategory.CurrencySymbol || category == UnicodeCategory.ConnectorPunctuation)
            {
                return true;
            }
            return !start && (category == UnicodeCategory.DecimalDigitNumber || category == UnicodeCategory.NonSpacingMark
                || category == UnicodeCategory.SpacingCombiningMark || IsIdentifierIgnorable(text[position], category));
        }

        /// <summary>Checks the Java identifier-ignorable control and format characters.</summary>
        /// <param name="character">The UTF-16 character.</param>
        /// <param name="category">The Unicode character category.</param>
        /// <returns>Whether Java ignores the character in identifier identity.</returns>
        private static bool IsIdentifierIgnorable(char character, UnicodeCategory category)
        {
            return category == UnicodeCategory.Format || character <= '\u0008'
                || (character >= '\u000E' && character <= '\u001B') || (character >= '\u007F' && character <= '\u009F');
        }

        /// <summary>Removes characters that Java ignores when comparing identifiers.</summary>
        /// <param name="value">The identifier token.</param>
        /// <returns>The JVM identifier spelling.</returns>
        private static string RemoveIdentifierIgnorables(string value)
        {
            StringBuilder? result = null;
            for (var index = 0; index < value.Length; index++)
            {
                var width = char.IsHighSurrogate(value[index]) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]) ? 2 : 1;
                if (IsIdentifierIgnorable(value[index], CharUnicodeInfo.GetUnicodeCategory(value, index)))
                {
                    if (result is null)
                    {
                        result = new StringBuilder(value.Substring(0, index));
                    }
                }
                else if (result is not null)
                {
                    result.Append(value, index, width);
                }
                index += width - 1;
            }
            return result?.ToString() ?? value;
        }

        /// <summary>Translates eligible Java Unicode escapes before comments or literals are recognized.</summary>
        /// <param name="path">The path used in malformed-escape diagnostics.</param>
        /// <param name="text">The original source text.</param>
        /// <param name="cancellationToken">The extraction cancellation token.</param>
        /// <returns>The translated input, without recursively translating escape output.</returns>
        /// <exception cref="InvalidDataException">An eligible Unicode escape is malformed.</exception>
        private static string TranslateUnicode(string path, string text, CancellationToken cancellationToken)
        {
            var result = new StringBuilder(text.Length);
            var backslashes = 0;
            var previousWasEscape = false;
            for (var index = 0; index < text.Length; index++)
            {
                if ((index & 4095) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                var character = text[index];
                var eligible = character == '\\' && (previousWasEscape || (backslashes & 1) == 0);
                if (eligible && index + 1 < text.Length && text[index + 1] == 'u')
                {
                    var start = index;
                    index++;
                    while (index < text.Length && text[index] == 'u')
                    {
                        index++;
                    }
                    if (index + 4 > text.Length)
                    {
                        throw new InvalidDataException($"Java source '{path}' at character {start + 1}: truncated Unicode escape.");
                    }
                    var value = 0;
                    for (var digit = 0; digit < 4; digit++)
                    {
                        var hex = HexDigit(text[index + digit]);
                        if (hex < 0)
                        {
                            throw new InvalidDataException($"Java source '{path}' at character {start + 1}: invalid Unicode escape.");
                        }
                        value = (value << 4) | hex;
                    }
                    character = (char)value;
                    index += 3;
                    previousWasEscape = true;
                }
                else
                {
                    previousWasEscape = false;
                }
                result.Append(character);
                backslashes = character == '\\' ? backslashes + 1 : 0;
            }
            return result.ToString();
        }

        /// <summary>Converts one ASCII hexadecimal digit.</summary>
        /// <param name="character">The source character.</param>
        /// <returns>The digit value, or minus one for a nondigit.</returns>
        private static int HexDigit(char character)
        {
            if (character >= '0' && character <= '9')
            {
                return character - '0';
            }
            if (character >= 'a' && character <= 'f')
            {
                return character - 'a' + 10;
            }
            if (character >= 'A' && character <= 'F')
            {
                return character - 'A' + 10;
            }
            return -1;
        }

        /// <summary>Creates a source-located lexical diagnostic.</summary>
        /// <param name="line">The translated line.</param>
        /// <param name="column">The translated column.</param>
        /// <param name="message">The diagnostic detail.</param>
        /// <returns>The extraction error.</returns>
        private InvalidDataException Invalid(int line, int column, string message)
        {
            return new InvalidDataException($"Java source '{_path}' ({line},{column}): {message}.");
        }
    }
}
