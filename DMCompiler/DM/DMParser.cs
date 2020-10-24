﻿using System;
using System.Collections.Generic;
using OpenDreamShared.Dream;

namespace DMCompiler.DM {
    class DMParser : Parser {
        public DMParser(DMLexer lexer) : base(lexer) {

        }

        public DMASTFile File() {
            DMASTBlockInner blockInner = BlockInner();
            Newline();
            Consume(TokenType.EndOfFile, "Expected EOF");

            return new DMASTFile(blockInner);
        }

        public DMASTBlockInner BlockInner() {
            DMASTStatement statement = Statement();

            if (statement != null) {
                List<DMASTStatement> statements = new List<DMASTStatement>() { statement };

                while (Delimiter()) {
                    statement = Statement();

                    if (statement != null) {
                        statements.Add(statement);
                    }
                }

                return new DMASTBlockInner(statements.ToArray());
            } else {
                return null;
            }
        }

        public DMASTStatement Statement() {
            DMASTPath path = Path();

            if (path != null) {
                if (Check(TokenType.DM_LeftParenthesis)) {
                    DMASTDefinitionParameter[] parameters = DefinitionParameters();
                    Consume(TokenType.DM_RightParenthesis, "Expected closing parenthesis");
                    DMASTProcBlockInner procBlock = ProcBlock();

                    return new DMASTProcDefinition(path, parameters);
                } else {
                    DMASTBlockInner block = Block();

                    if (block != null) {
                        return new DMASTObjectDefinition(path, block);
                    } else {
                        if (Check(TokenType.DM_Equals)) {
                            DMASTExpression value = Expression();

                            return new DMASTObjectVarDefinition(path, value);
                        } else {
                            return new DMASTObjectDefinition(path, null);
                        }
                    }
                }
            }

            return null;
        }

        public DMASTPath Path(bool expression = false) {
            Token firstToken = Current();
            DreamPath.PathType pathType = DreamPath.PathType.Relative;
            bool hasPathTypeToken = true;

            if (Check(TokenType.DM_Slash)) {
                pathType = DreamPath.PathType.Absolute;
            } else if (Check(TokenType.DM_Colon)) {
                pathType = DreamPath.PathType.DownwardSearch;
            } else if (Check(TokenType.DM_Period)) {
                pathType = DreamPath.PathType.UpwardSearch;
            } else {
                hasPathTypeToken = false;

                if (expression) return null;
            }

            DMASTPathElement pathElement = PathElement();
            if (pathElement != null) {
                List<DMASTPathElement> pathElements = new List<DMASTPathElement>() { pathElement };

                while (pathElement != null && Check(TokenType.DM_Slash)) {
                    pathElement = PathElement();

                    if (pathElement != null) {
                        pathElements.Add(pathElement);
                    }
                }

                return new DMASTPath(pathType, pathElements.ToArray());
            } else if (hasPathTypeToken) {
                if (expression) ReuseToken(firstToken);

                return null;
            }

            return null;
        }

        public DMASTPathElement PathElement() {
            Token elementToken = Current();

            if (Check(new TokenType[] { TokenType.DM_Identifier, TokenType.DM_Var, TokenType.DM_Proc })) {
                return new DMASTPathElement(elementToken.Text);
            } else {
                return null;
            }
        }

        public DMASTCallable Callable() {
            DMASTCallable callable = Dereference();

            if (callable == null) {
                Token firstToken = Current();

                if (Check(TokenType.DM_Identifier)) {
                    return new DMASTCallableIdentifier(firstToken.Text);
                }
            }
            
            return callable;
        }

        public DMASTCallableDereference Dereference() {
            Token leftToken = Current();

            if (Check(TokenType.DM_Identifier)) {
                TokenType[] separatorTypes = new TokenType[] { TokenType.DM_Period };

                if (Check(separatorTypes)) {
                    Token rightToken = Current();

                    Consume(TokenType.DM_Identifier, "Expected identifier");
                    return new DMASTCallableDereference(new DMASTCallableIdentifier(leftToken.Text), new DMASTCallableIdentifier(rightToken.Text));
                } else { 
                    ReuseToken(leftToken);

                    return null;
                }
            } else {
                return null;
            }
        }

        public DMASTBlockInner Block() {
            DMASTBlockInner block = BracedBlock();
            if (block == null) block = IndentedBlock();

            return block;
        }

        public DMASTBlockInner BracedBlock() {
            return null;
        }

        public DMASTBlockInner IndentedBlock() {
            if (Check(TokenType.DM_Indent)) {
                Newline();
                DMASTBlockInner blockInner = BlockInner();

                if (blockInner != null) {
                    Newline();

                    if (Check(TokenType.DM_Dedent)) {
                        return blockInner;
                    } else {
                        throw new Exception("Expected dedent");
                    }
                }
            }

            return null;
        }

        public DMASTProcBlockInner ProcBlock() {
            DMASTProcBlockInner procBlock = BracedProcBlock();
            if (procBlock == null) procBlock = IndentedProcBlock();

            return procBlock;
        }

        public DMASTProcBlockInner BracedProcBlock() {
            return null;
        }

        public DMASTProcBlockInner IndentedProcBlock() {
            if (Check(TokenType.DM_Indent)) {
                Newline();
                DMASTProcBlockInner procBlock = ProcBlockInner();
                Newline();
                Consume(TokenType.DM_Dedent, "Expected dedent");

                return procBlock;
            }

            return null;
        }

        public DMASTProcBlockInner ProcBlockInner() {
            DMASTProcStatement procStatement = ProcStatement();

            if (procStatement != null) {
                List<DMASTProcStatement> procStatements = new List<DMASTProcStatement>() { procStatement };

                while (Delimiter()) {
                    procStatement = ProcStatement();

                    if (procStatement != null) {
                        procStatements.Add(procStatement);
                    }
                }

                return new DMASTProcBlockInner(procStatements.ToArray());
            } else {
                return null;
            }
        }

        public DMASTProcStatement ProcStatement() {
            DMASTExpression expression = Expression();

            if (expression != null) {
                if (Check(TokenType.DM_Equals)) {
                    DMASTExpression value = Expression();

                    if (value != null) {
                        return new DMASTProcStatementAssign(expression, value);
                    } else {
                        throw new Exception("Expected an assignment value");
                    }
                } else {
                    return new DMASTProcStatementExpression(expression);
                }
            } else {
                DMASTProcStatement procStatement = If();

                return procStatement;
            }
        }

        public DMASTProcStatementIf If() {
            if (Check(TokenType.DM_If)) {
                Consume(TokenType.DM_LeftParenthesis, "Expected left parenthesis");
                DMASTExpression condition = Expression();
                if (condition == null) throw new Exception("Expected condition");
                Consume(TokenType.DM_RightParenthesis, "Expected right parenthesis");

                DMASTProcStatement procStatement = ProcStatement();
                DMASTProcBlockInner body;
                DMASTProcBlockInner elseBody = null;

                if (procStatement != null) {
                    body = new DMASTProcBlockInner(new DMASTProcStatement[] { procStatement });
                } else {
                    body = ProcBlock();
                }

                if (body == null) throw new Exception("If statement has no body");
                Token afterIfBody = Current();
                bool newLineAfterIf = Newline();
                if (Check(TokenType.DM_Else)) {
                    procStatement = ProcStatement();

                    if (procStatement != null) {
                        elseBody = new DMASTProcBlockInner(new DMASTProcStatement[] { procStatement });
                    } else {
                        elseBody = ProcBlock();
                    }

                    if (elseBody == null) throw new Exception("Else statement has no body");
                } else if (newLineAfterIf) {
                    ReuseToken(afterIfBody);
                }

                return new DMASTProcStatementIf(condition, body, elseBody);
            } else {
                return null;
            }
        }

        public DMASTCallParameter[] ProcCall() {
            if (Check(TokenType.DM_LeftParenthesis)) {
                DMASTCallParameter[] callParameters = CallParameters();

                Consume(TokenType.DM_RightParenthesis, "Expected closing parenthesis");
                return callParameters;
            }

            return null;
        }

        public DMASTCallParameter[] CallParameters() {
            List<DMASTCallParameter> parameters = new List<DMASTCallParameter>();

            do {
                DMASTCallParameter parameter = CallParameter();

                if (parameter != null) {
                    parameters.Add(parameter);
                } else if (Current().Type == TokenType.DM_Comma) {
                    parameters.Add(new DMASTCallParameter(new DMASTConstantNull()));
                }
            } while (Check(TokenType.DM_Comma));

            if (parameters.Count > 0) {
                return parameters.ToArray();
            } else {
                return null;
            }
        }

        public DMASTCallParameter CallParameter() {
            DMASTExpression expression = Expression();

            if (expression != null) {
                if (expression is DMASTCallableIdentifier && Check(TokenType.DM_Equals)) {
                    DMASTExpression value = Expression();

                    return new DMASTCallParameter(value, ((DMASTCallableIdentifier)expression).Identifier);
                } else {
                    return new DMASTCallParameter(expression);
                }
            }

            return null;
        }

        public DMASTDefinitionParameter[] DefinitionParameters() {
            List<DMASTDefinitionParameter> parameters = new List<DMASTDefinitionParameter>();

            do {
                DMASTDefinitionParameter parameter = DefinitionParameter();

                if (parameter != null) {
                    parameters.Add(parameter);
                } else if (Current().Type == TokenType.DM_Comma) {
                    throw new Exception("Expected parameter definition");
                }
            } while (Check(TokenType.DM_Comma));

            if (parameters.Count > 0) {
                return parameters.ToArray();
            } else {
                return null;
            }
        }

        public DMASTDefinitionParameter DefinitionParameter() {
            DMASTPath path = Path();

            if (path != null) {
                if (Check(TokenType.DM_Equals)) {
                    DMASTExpression value = Expression();

                    return new DMASTDefinitionParameter(path, value);
                } else {
                    return new DMASTDefinitionParameter(path);
                }

                //TODO: "as"
            }

            return null;
        }

        public DMASTExpression Expression() {
            return ExpressionComparison();
        }

        public DMASTExpression ExpressionComparison() {
            DMASTExpression expression = ExpressionNot();

            if (expression != null) {
                Token token = Current();

                if (Check(TokenType.DM_ExclamationEquals)) {
                    DMASTExpression b = ExpressionComparison();

                    if (b == null) throw new Exception("Expected an expression to compare to");
                    if (token.Type == TokenType.DM_ExclamationEquals) {
                        return new DMASTComparisonNotEqual(expression, b);
                    }
                }
            }

            return expression;
        }

        public DMASTExpression ExpressionNot() {
            if (Check(TokenType.DM_Exclamation)) {
                DMASTExpression expression = ExpressionNot();

                if (expression == null) throw new Exception("Expected an expression");
                return new DMASTExpressionNot(expression);
            } else {
                return ExpressionSign();
            }
        }

        public DMASTExpression ExpressionSign() {
            Token token = Current();

            if (Check(new TokenType[] { TokenType.DM_Plus, TokenType.DM_Minus })) {
                DMASTExpression expression = ExpressionPrimary();

                if (expression == null) throw new Exception("Expected an expression");
                if (token.Type == TokenType.DM_Minus) {
                    return new DMASTExpressionNegate(expression);
                } else {
                    return expression;
                }
            } else {
                return ExpressionPrimary();
            }
        }

        public DMASTExpression ExpressionPrimary() {
            DMASTExpression primary = Constant();

            if (primary == null) {
                DMASTCallable callable = Callable();

                if (callable != null) {
                    DMASTCallParameter[] callParameters = ProcCall();

                    if (callParameters != null) {
                        primary = new DMASTProcCall(callable, callParameters);
                    } else {
                        primary = callable;
                    }
                }
            }

            if (primary == null) {
                DMASTPath path = Path(true);

                if (path != null) primary = new DMASTConstantPath(path);
            }

            return primary;
        }

        public DMASTExpression Constant() {
            Token constantToken = Current();

            switch (constantToken.Type) {
                case TokenType.DM_Integer: Advance(); return new DMASTConstantInteger((int)constantToken.Value);
                case TokenType.DM_Float: Advance(); return new DMASTConstantFloat((float)constantToken.Value);
                case TokenType.DM_String: Advance(); return new DMASTConstantString((string)constantToken.Value);
                case TokenType.DM_Null: Advance(); return new DMASTConstantNull();
                default: return null;
            }
        }

        private bool Newline() {
            return Check(TokenType.Newline);
        }

        private bool Delimiter() {
            return Check(TokenType.DM_Semicolon) || Newline();
        }
    }
}