using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OpenDreamShared.Compiler.DMPreprocessor {
    public class DMPreprocessor {
        public List<string> IncludedMaps = new();
        public string IncludedInterface;

        //Every include pushes a new lexer that gets popped once the included file is finished
        private Stack<DMPreprocessorLexer> _lexerStack =  new();

        Stack<Token> _unprocessedTokens = new();
        private List<Token> _result = new();
        private List<Token> _currentLine = new();
        private bool _isCurrentLineWhitespaceOnly = true;
        private bool _enableDirectives;
        private Dictionary<string, DMMacro> _defines = new() {
            { "__LINE__", new DMMacroLine() },
            { "__FILE__", new DMMacroFile() }
        };

        public DMPreprocessor(bool enableDirectives) {
            _enableDirectives = enableDirectives;
        }

        public void IncludeFile(string includePath, string file) {
            file = file.Replace('\\', Path.DirectorySeparatorChar);
            string source = File.ReadAllText(Path.Combine(includePath, file));
            source = source.Replace("\r\n", "\n");
            source += '\n';

            _lexerStack.Push(new DMPreprocessorLexer(file, source));

            Token token = GetNextToken();
            while (token.Type != TokenType.EndOfFile) {
                switch (token.Type) {
                    case TokenType.DM_Preproc_Include: {
                        if (!_enableDirectives) throw new Exception("Cannot use preprocessor directives here");

                        Token includedFileToken = GetNextToken(true);
                        if (includedFileToken.Type != TokenType.DM_Preproc_ConstantString) throw new Exception("\"" + includedFileToken.Text + "\" is not a valid include path");

                        string includedFile = (string)includedFileToken.Value;
                        string includedFileExtension = Path.GetExtension(includedFile);
                        string fullIncludePath = Path.Combine(Path.GetDirectoryName(file), includedFile);

                        if (includedFileExtension == ".dm") {
                            IncludeFile(includePath, fullIncludePath);
                        } else if (includedFileExtension == ".dmm") {
                            string mapPath = Path.Combine(includePath, fullIncludePath);

                            IncludedMaps.Add(mapPath);
                        } else if (includedFileExtension == ".dmf") {
                            if (IncludedInterface != null) {
                                throw new Exception("Attempted to include a second interface file (" + fullIncludePath + ") while one was already included (" + IncludedInterface + ")");
                            }

                            IncludedInterface = fullIncludePath;
                        }
                        break;
                    }
                    case TokenType.DM_Preproc_Define: {
                        if (!_enableDirectives) throw new Exception("Cannot use preprocessor directives here");

                        Token defineIdentifier = GetNextToken(true);
                        if (defineIdentifier.Type != TokenType.DM_Preproc_Identifier) throw new Exception("Invalid define identifier");
                        List<string> parameters = null;
                        List<Token> defineTokens = new();

                        Token defineToken = GetNextToken();
                        if (defineToken.Type == TokenType.DM_Preproc_Punctuator_LeftParenthesis) {
                            parameters = new List<string>();

                            Token parameterToken;
                            do {
                                parameterToken = GetNextToken(true);
                                bool unnamed = parameterToken.Type == TokenType.DM_Preproc_Punctuator_Period;
                                if (!unnamed && parameterToken.Type != TokenType.DM_Preproc_Identifier) throw new Exception("Expected a macro parameter");

                                string parameterName = unnamed ? "" : parameterToken.Text;

                                parameterToken = GetNextToken(true);
                                if (parameterToken.Type == TokenType.DM_Preproc_Punctuator_Period) {
                                    if (!unnamed) parameterToken = GetNextToken();
                                    if (parameterToken.Type != TokenType.DM_Preproc_Punctuator_Period) throw new Exception("Expected a second period");
                                    parameterToken = GetNextToken();
                                    if (parameterToken.Type != TokenType.DM_Preproc_Punctuator_Period) throw new Exception("Expected a third period");

                                    parameters.Add(parameterName + "...");

                                    parameterToken = GetNextToken(true);
                                    if (unnamed) break;

                                } else {
                                    if (unnamed) throw new Exception("Expected a second period");
                                    parameters.Add(parameterName);
                                }
                            } while (parameterToken.Type == TokenType.DM_Preproc_Punctuator_Comma);

                            if (parameterToken.Type != TokenType.DM_Preproc_Punctuator_RightParenthesis) throw new Exception("Missing ')' in macro definition");
                            defineToken = GetNextToken(true);
                        } else if (defineToken.Type == TokenType.DM_Preproc_Whitespace) {
                            defineToken = GetNextToken(true);
                        }

                        while (defineToken.Type != TokenType.Newline && defineToken.Type != TokenType.EndOfFile) {
                            defineTokens.Add(defineToken);

                            defineToken = GetNextToken();
                        }

                        _defines[defineIdentifier.Text] = new DMMacro(parameters, defineTokens);
                        break;
                    }
                    case TokenType.DM_Preproc_Undefine: {
                        if (!_enableDirectives) throw new Exception("Cannot use preprocessor directives here");

                        Token defineIdentifier = GetNextToken(true);
                        if (defineIdentifier.Type != TokenType.DM_Preproc_Identifier) throw new Exception("Invalid define identifier");

                        _defines.Remove(defineIdentifier.Text);
                        break;
                    }
                    case TokenType.DM_Preproc_Identifier: {
                        if (_defines.TryGetValue(token.Text, out DMMacro macro)) {
                            List<List<Token>> parameters = null;

                            if (macro.HasParameters()) {
                                parameters = GetMacroParameters();

                                if (parameters == null) {
                                    _currentLine.Add(token);

                                    break;
                                }
                            }

                            List<Token> expandedTokens = macro.Expand(token, parameters);

                            //Put the tokens at the beginning of the macro on the top of the stack
                            //Can't use a queue because macros within a macro have to be processed before the rest of the macro
                            expandedTokens.Reverse();

                            foreach (Token expandedToken in expandedTokens) {
                                Token newToken = new Token(expandedToken.Type, expandedToken.Text, token.SourceFile, token.Line, token.Column, expandedToken.Value);

                                _unprocessedTokens.Push(newToken);
                            }
                        } else {
                            _isCurrentLineWhitespaceOnly = false;

                            _currentLine.Add(token);
                        }

                        break;
                    }
                    case TokenType.DM_Preproc_If:
                    {
                        //TODO Implement #if properly
                        SkipIfBody();
                        _currentLine.Add(new Token(TokenType.Warning, token.Text, token.SourceFile, token.Line, token.Column, "#if is not implemented"));
                        break;
                    }
                    case TokenType.DM_Preproc_Ifdef: {
                        if (!_enableDirectives) throw new Exception("Cannot use preprocessor directives here");

                        Token define = GetNextToken(true);
                        if (define.Type != TokenType.DM_Preproc_Identifier) throw new Exception("Expected a define identifier");

                        if (!_defines.ContainsKey(define.Text)) {
                            SkipIfBody();
                        }

                        break;
                    }
                    case TokenType.DM_Preproc_Ifndef: {
                        if (!_enableDirectives) throw new Exception("Cannot use preprocessor directives here");

                        Token define = GetNextToken(true);
                        if (define.Type != TokenType.DM_Preproc_Identifier) throw new Exception("Expected a define identifier");

                        if (_defines.ContainsKey(define.Text)) {
                            SkipIfBody();
                        }

                        break;
                    }
                    case TokenType.DM_Preproc_Else: { //If this is encountered outside of SkipIfBody, it needs skipped
                        if (!_enableDirectives) throw new Exception("Cannot use preprocessor directives here");

                        SkipIfBody();
                        break;
                    }
                    case TokenType.DM_Preproc_EndIf: break;
                    case TokenType.DM_Preproc_Error:
                    case TokenType.DM_Preproc_Warning: {
                        StringBuilder messageBuilder = new StringBuilder();

                        Token messageToken = GetNextToken(true);
                        while (messageToken.Type != TokenType.EndOfFile) {
                            if (messageToken.Type == TokenType.Newline) break;

                            messageBuilder.Append(messageToken.Text);
                            messageToken = GetNextToken();
                        }

                        string message = messageBuilder.ToString();
                        TokenType type = (token.Type == TokenType.DM_Preproc_Error) ? TokenType.Error : TokenType.Warning;

                        _isCurrentLineWhitespaceOnly = false;
                        _currentLine.Add(new Token(type, token.Text, token.SourceFile, token.Line, token.Column, message));
                        break;
                    }
                    case TokenType.Newline: {
                        if (!_isCurrentLineWhitespaceOnly) {
                            _result.AddRange(_currentLine);
                            _result.Add(token);

                            _isCurrentLineWhitespaceOnly = true;
                        }

                        _currentLine.Clear();
                        break;
                    }
                    case TokenType.Error: //Pass the error token on to the DM lexer
                    case TokenType.DM_Preproc_Number:
                    case TokenType.DM_Preproc_String:
                    case TokenType.DM_Preproc_ConstantString:
                    case TokenType.DM_Preproc_Punctuator:
                    case TokenType.DM_Preproc_Punctuator_Comma:
                    case TokenType.DM_Preproc_Punctuator_Period:
                    case TokenType.DM_Preproc_Punctuator_Colon:
                    case TokenType.DM_Preproc_Punctuator_Question:
                    case TokenType.DM_Preproc_Punctuator_LeftParenthesis:
                    case TokenType.DM_Preproc_Punctuator_LeftBracket:
                    case TokenType.DM_Preproc_Punctuator_RightBracket:
                    case TokenType.DM_Preproc_Punctuator_RightParenthesis: _isCurrentLineWhitespaceOnly = false; _currentLine.Add(token); break;
                    case TokenType.DM_Preproc_Whitespace: _currentLine.Add(token); break;
                    default: throw new Exception("Invalid token " + token);
                }

                token = GetNextToken();
            }

            _lexerStack.Pop();
        }

        public List<Token> GetResult() {
            return _result;
        }

        private Token GetNextToken(bool ignoreWhitespace = false) {
            if (_unprocessedTokens.Count > 0) {
                Token nextToken = _unprocessedTokens.Pop();

                if (ignoreWhitespace) {
                    while (nextToken.Type == TokenType.DM_Preproc_Whitespace) nextToken = _unprocessedTokens.Pop();
                }

                return nextToken;
            } else {
                return ignoreWhitespace ? _lexerStack.Peek().GetNextTokenIgnoringWhitespace() : _lexerStack.Peek().GetNextToken();
            }
        }

        private void SkipIfBody() {
            int ifdefCount = 1;

            Token token;
            while ((token = GetNextToken()).Type != TokenType.EndOfFile) {
                if (token.Type == TokenType.DM_Preproc_If || token.Type == TokenType.DM_Preproc_Ifdef || token.Type == TokenType.DM_Preproc_Ifndef) {
                    ifdefCount++;
                } else if (token.Type == TokenType.DM_Preproc_EndIf) {
                    ifdefCount--;
                }

                if (ifdefCount == 0 || (token.Type == TokenType.DM_Preproc_Else && ifdefCount == 1)) {
                    break;
                }
            }
        }

        private List<List<Token>> GetMacroParameters() {
            Token leftParenToken = GetNextToken();

            if (leftParenToken.Type == TokenType.DM_Preproc_Punctuator_LeftParenthesis) {
                List<List<Token>> parameters = new();
                List<Token> currentParameter = new();

                Token parameterToken = GetNextToken(true);
                int parenthesisNesting = 0;
                while (!(parenthesisNesting == 0 && parameterToken.Type == TokenType.DM_Preproc_Punctuator_RightParenthesis) &&
                        parameterToken.Type != TokenType.EndOfFile) {
                    if (parameterToken.Type == TokenType.DM_Preproc_Punctuator_Comma && parenthesisNesting == 0) {
                        parameters.Add(currentParameter);
                        currentParameter = new List<Token>();

                        parameterToken = GetNextToken(true);
                    } else {
                        if (parameterToken.Type != TokenType.Newline) currentParameter.Add(parameterToken);

                        if (parameterToken.Type == TokenType.DM_Preproc_Punctuator_LeftParenthesis) parenthesisNesting++;
                        else if (parameterToken.Type == TokenType.DM_Preproc_Punctuator_RightParenthesis) parenthesisNesting--;

                        parameterToken = GetNextToken();
                    }
                }

                parameters.Add(currentParameter);
                if (parameterToken.Type != TokenType.DM_Preproc_Punctuator_RightParenthesis) throw new Exception("Missing ')' in macro call");

                return parameters;
            }

            return null;
        }
    }
}
