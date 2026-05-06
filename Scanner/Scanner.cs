using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;

namespace PLC
{
    public class Scanner {
        IEnumerator<char>? enumerator;
        char current, next;

        bool MoveNext() {
            current = next;
            next = (enumerator!.MoveNext()) ? enumerator.Current : '\0';
            return (current != '\0');
        }

        public List<Token> Scan(string filename) {
            using (FileStream fileStream = new FileStream(filename, FileMode.Open)) {
               return new List<Token>(Scan(fileStream));
            }
        }

        public IEnumerable<Token> Scan(Stream stream) {
            return Scan(CharactersFromStream(stream));
        }
        IEnumerable<char> CharactersFromStream(Stream stream) {
            var reader = new StreamReader(stream);
            int integerValue;
            while ((integerValue = reader.Read()) != -1) {
                yield return (char) integerValue;
            }
        }

        public IEnumerable<Token> Scan(IEnumerable<string> lines) {
            return Scan(CharactersFromLines(lines));
        }

        private IEnumerable<char> CharactersFromLines(IEnumerable<string> lines) {
            foreach (string line in lines) {
                foreach (char c in line) {
                    yield return c;
                }
                yield return '\n';
            }
        }

        public IEnumerable<Token> Scan(IEnumerable<char> characters) {
            enumerator = characters.GetEnumerator();
            MoveNext(); // Prime the pump
            Token token = new();
            StringBuilder tb = new();
            token.LineNumber = 1;
            while (MoveNext()) {
                switch (current) {
                    case '"':
                        token.Type = TokenType.StringConstant;
                        bool stringTerminated = false;
                        while (next != '\0' && (next != '"' || current == '\\') && MoveNext()) {
                            if (current == '\\' && next == '"') {
                                tb.Append('"');
                                MoveNext(); // Skip the '"'
                            } else {
                                tb.Append(current);
                            }
                        }
                        if (next == '"') {
                            stringTerminated = true;
                            MoveNext(); // Skip the closing '"'
                        }
                        token.Text = tb.ToString();
                        tb.Clear();
                        if (!stringTerminated) {
                            throw new Exception("Unterminated string literal at line " + token.LineNumber);
                        }
                        yield return token;
                        break;
                    case ' ':
                    case '\n':
                    case '\r':
                    case '\t':
                        if (current == '\n') token.LineNumber++;
                        tb.Clear();
                        while (IsWhiteSpace(next) && MoveNext()) {
                            if (current == '\n') token.LineNumber++;
                        }
                        break;
                    case ';':
                        token.Type = TokenType.Terminator;
                        token.Text = ";";
                        tb.Clear();
                        yield return token;
                        break;
                    case ',':
                        token.Type = TokenType.Separator;
                        token.Text = ",";
                        tb.Clear();
                        yield return token;
                        break;
                    case '{':
                        token.Type = TokenType.Keyword;
                        token.Text = "BEGIN";
                        tb.Clear();
                        yield return token;
                        break;
                    case '}':
                        token.Type = TokenType.Keyword;
                        token.Text = "END";
                        tb.Clear();
                        yield return token;
                        break;
                    case '/':
                        tb.Append(current);
                        token.Type = TokenType.Operator;
                        if (next == '/') {
                            token.Type = TokenType.Comment;
                            while (next != '\n' && next != '\0' && MoveNext()) {
                                tb.Append(current);
                            }
                        } else if (next == '*') {
                            token.Type = TokenType.Comment;
                            MoveNext(); // Skip the asterisk
                            if (next == '/') {
                                MoveNext();
                                tb.Append("*/");
                            } else {
                                tb.Append('*');
                            }
                            int commentDepth = 1;
                            while (commentDepth > 0 && MoveNext()) {
                                if (current == '*' && next == '/') {
                                    commentDepth--;
                                } else {
                                    if (current == '/' && next == '*') {
                                        commentDepth++;
                                    }
                                }
                                if (current == '\n') token.LineNumber++;
                                tb.Append(current);
                            }
                            tb.Append('/');
                            MoveNext(); // Skip the /
                        }
                        token.Text = tb.ToString();
                        tb.Clear();
                        yield return token;
                        break;
                    case '!':
                        token.Type = TokenType.Keyword;
                        token.Text = "WRITE";
                        tb.Clear();
                        yield return token;
                        break;
                    case '?':
                        token.Type = TokenType.Keyword;
                        token.Text = "READ";
                        tb.Clear();
                        yield return token;
                        break;
                    case '.':
                        token.Type = TokenType.EndProgram;
                        tb.Clear();
                        yield return token;
                        break;
                    /*
                    case '\'':
                        token.Type = TokenType.CharacterContant;
                        while ((next != '\'') && MoveNext()) {
                            if (current == '\\' && next == '\'') {
                                tb.Append(current);
                                MoveNext();
                            }
                            tb.Append(current);
                        }
                        token.Text = tb.ToString();
                        tb.Clear();
                        MoveNext(); // Skip the '"'
                        yield return token;
                        break;   
                    */
                    default:
                        tb.Append(current);
                        if (Char.IsDigit(current)) {
                            token.Type = TokenType.IntegerConstant;
                            while (Char.IsDigit(next) && MoveNext())
                            {
                                tb.Append(current);
                            }
                        } else if (IsNonDigit(current)) {
                            token.Type = TokenType.Identifier;
                            while ((Char.IsDigit(next) || IsNonDigit(next)) && MoveNext()) {
                                tb.Append(current);
                            }
                        } else if (IsParens(current)) {
                            token.Type = TokenType.Parens;
                        } else if (IsSymbol(current)) {
                            token.Type = TokenType.Operator;
                            while (IsSymbol(next) && MoveNext()) {
                                tb.Append(current);
                            }
                        } else {
                            throw new Exception("Illegal character: " + current);
                        }
                        token.Text = tb.ToString();
                        tb.Clear();
                        yield return token;
                        break;
                }
            }
        }
        private bool IsNonDigit(char c) {
            return (Char.IsLetter(c) || c == '_');
        }

        private bool IsWhiteSpace(char c) {
            // Use Char.IsWhiteSpace(c)?
            return (new char[] { ' ', '\n', '\t', '\0' }).Contains(c);
        }

        private bool IsParens(char c) {
            return ((c == ')') || (c == '('));
        }

        private bool IsSymbol(char c) {
            char[] symbols = new char[] { '=', ':', '#', '<', '>', '+', '-', '*', '/', ',' };
            return symbols.Contains(c);
        }
    }
}