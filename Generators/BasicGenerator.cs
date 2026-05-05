#nullable enable
using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;

namespace PLC
{
    public class BasicGenerator : IGenerator
    {
        int lineNumber = 10;
        readonly Dictionary<string, int> GosubTable;
        public BasicGenerator(ParsedProgram program)
        {
            Program = program;
            GosubTable = new Dictionary<string, int>();
        }
        public ParsedProgram Program { get; set; }

        public int Compile(string filename)
        {
            return 1;
        }

        public IEnumerable<string> Generate()
        {
            // Generate constant declarations
            foreach (Identity i in Program.Block.Constants)
            {
                yield return lineNumber + " LET " + i.Name + " = " + i.Value;
                lineNumber += 10;
            }

            // Generate Procedures
            foreach (Procedure method in Program.Block.Procedures)
            {
                int gotoLine = lineNumber;
                lineNumber += 10;
                int startLine = lineNumber;
                GosubTable[method.Name] = startLine;
                lineNumber += 10;
                var lines = GenerateBlock(method.Block).ToList();
                yield return gotoLine + " GOTO " + (lineNumber + 10); // after RETURN
                yield return startLine + " REM " + method.Name;
                foreach (string line in lines)
                {
                    yield return line;
                }
                yield return lineNumber + " RETURN";
                lineNumber += 10;
            }
            
            // Generate the Main body of the program
            foreach (string s in GenerateStatement(Program.Block.Statement))
            {
                yield return s;
            }
            // Needed in case something tries to GOTO past the last line
            yield return lineNumber + " REM END";
        }
        IEnumerable<string> GenerateBlock(Block block)
        {
            string constants = GenerateConstantDeclarations(block.Constants);
            if (constants != String.Empty)
            {
                yield return constants;
            }
            foreach (string s in GenerateStatement(block.Statement))
            {
                yield return s;
            }
        }

        string GenerateConstantDeclarations(List<Identity> constants)
        {
            int c = constants.Count;
            if (c == 0)
            {
                return String.Empty;
            }

            StringBuilder sb = new();
            sb.Append("CONST ");
            for (int i = 0; i < c; i++)
            {
                Identity cc = constants.ElementAt(i);
                sb.Append(cc.Name);
                sb.Append(" = ");
                sb.Append(cc.Value);
                if (i < (c - 1))
                {
                    sb.Append(", ");
                }
            }

            sb.Append(";");
            return sb.ToString();
        }
        IEnumerable<string> GenerateStatement(Statement statement)
        {
            if (statement is WriteStatement)
            {
                var writeStatement = (WriteStatement) statement;
                if (writeStatement.Message == String.Empty)
                {
                    yield return lineNumber + " PRINT " + GenerateExpression(writeStatement.Expression);
                }
                else
                {
                    yield return lineNumber + " PRINT \"" + writeStatement.Message + "\"";
                }
                lineNumber += 10;
            }
            else if (statement is ReadStatement)
            {
                var readStatement = (ReadStatement) statement;
                if (readStatement.Message != String.Empty)
                {
                    yield return lineNumber + " INPUT \"" + readStatement.Message + "\"; " + readStatement.IdentityName;
                }
                else
                {
                    yield return lineNumber + " INPUT " + readStatement.IdentityName;
                }
                lineNumber += 10;
            }
            else if (statement is AssignmentStatement)
            {
                var assignmentStatement = (AssignmentStatement) statement;
                yield return lineNumber + " " + assignmentStatement.IdentityName + " = " +
                             GenerateExpression(assignmentStatement.Expression);
                lineNumber += 10;
            }
            else if (statement is CallStatement)
            {
                var cs = (CallStatement) statement;
                yield return lineNumber + " GOSUB " + GosubTable[cs.ProcedureName];
                lineNumber += 10;
            }
            else if (statement is CompoundStatement)
            {
                var cs = (CompoundStatement) statement;
                foreach (Statement stmt in cs.Statements.Where(x => x.SkipGeneration == false))
                {
                    foreach (string s in GenerateStatement(stmt))
                    {
                        yield return s;
                    }
                }
            }
            else if (statement is IfStatement)
            {
                var iff = (IfStatement) statement;
                int ifLineNumber = lineNumber;
                lineNumber += 10;
                var lines = GenerateStatement(iff.Statement).ToList();
                switch (iff.Condition.Type)
                {
                    case ConditionType.True:
                        break;
                    case ConditionType.False:
                        yield return ifLineNumber + " GOTO " + lineNumber;
                        break;
                    default:
                        yield return ifLineNumber + " IF " + GeneratePreCondition(iff.Condition) + " GOTO " +
                                     lineNumber;
                        break;
                }
                foreach (string line in lines)
                {
                    yield return line;
                }
            }
            else if (statement is DoWhileStatement)
            {
                var dw = (DoWhileStatement) statement;
                int returnNumber = lineNumber;
                foreach (string line in GenerateStatement(dw.Statement))
                {
                    yield return line;
                }
                switch (dw.Condition.Type)
                {
                    case ConditionType.True:
                        yield return lineNumber + " GOTO " + returnNumber;
                        lineNumber += 10;
                        break;
                    case ConditionType.False:
                        break;
                    default:
                        yield return lineNumber + " IF " + GeneratePostCondition(dw.Condition) + " GOTO " + returnNumber;
                        lineNumber += 10;
                        break;
                }
            }
            else if (statement is WhileStatement)
            {
                var ws = (WhileStatement) statement;
                int startLine = lineNumber;
                lineNumber += 10;
                var lines = GenerateStatement(ws.Statement).ToList();
                switch (ws.Condition.Type)
                {
                    case ConditionType.True:
                        break;
                    case ConditionType.False:
                        yield return startLine + " GOTO " + (lineNumber + 10);
                        break;
                    default:
                        yield return startLine + " IF " + GeneratePreCondition(ws.Condition) + " GOTO " +
                                     (lineNumber + 10);
                        break;
                }
                foreach (string line in lines)
                {
                    yield return line;
                }
                yield return lineNumber + " GOTO " + startLine;
                lineNumber += 10;
            }
            else // Must be empty statement
            {
                //yield return "";
            }
        }

        string GenerateExpression(Expression expression)
        {
            if (expression is RandExpression)
            {
                return GenerateRandExpression((RandExpression) expression);
            }
            StringBuilder sb = new();
            if (expression.ExpressionNodes.Count == 0)
            {
                Console.WriteLine("Trying to generate an empty expression ( no nodes )");
            }

            var enumerator = expression.ExpressionNodes.GetEnumerator();
            if (enumerator.MoveNext())
            {
                sb.Append(GenerateFirstExpressionNode(enumerator.Current));
            }

            while (enumerator.MoveNext())
            {
                sb.Append(GenerateExpressionNode(enumerator.Current));
            }
            return sb.ToString();
        }

        string GenerateRandExpression(RandExpression r)
        {
            string low = GenerateExpression(r.LowExpression);
            string high = GenerateExpression(r.HighExpression);
            return "RAND " + low + " " + high;
        }

        public string GenerateFirstExpressionNode(ExpressionNode node)
        {
            return (node.IsPositive ? String.Empty : "-") + GenerateTerm(node.Term);
        }
        public string GenerateExpressionNode(ExpressionNode node)
        {
            return  (node.IsPositive ? "+" : "-") + GenerateTerm(node.Term);
        }
        
        string GenerateTerm(Term term)
        {
            StringBuilder sb = new();
            var te = term.TermNodes.GetEnumerator();
            te.MoveNext();
            sb.Append(GenerateFactor(te.Current.Factor));
            while (te.MoveNext())
            {
                sb.Append(te.Current.IsDivision ? "/" : "*");
                sb.Append(GenerateFactor(te.Current.Factor)); 
            }
            return sb.ToString();
        }

        string GenerateFactor(Factor factor)
        {
            if (factor == null)
            {
                Console.WriteLine("Trying to generate null factor");
            }
            if (factor is ConstantFactor)
            {
                ConstantFactor nf = (ConstantFactor) factor;
                return nf.Value;
            }
            else if (factor is IdentityFactor)
            {
                IdentityFactor nf = (IdentityFactor) factor;
                return nf.IdentityName;
            }
            else if (factor is ExpressionFactor)
            {
                ExpressionFactor ef = (ExpressionFactor) factor;
                string ex = GenerateExpression(ef.Expression);
                if (ef.Expression.IsSingleTerm)
                {
                    return ex;
                }
                return "(" + ex + ")";
            }
            throw new Exception("Could not generate factor");
        }

        string GeneratePreCondition(Condition condition)
        {
            Dictionary<ConditionType, string> conditionDict = new()
            {
                {ConditionType.Equal, "<>"},
                {ConditionType.NotEqual, "="},
                {ConditionType.LessThan, ">="},
                {ConditionType.LessThanOrEqual, ">"},
                {ConditionType.GreaterThan, "<="},
                {ConditionType.GreaterThanOrEqual, "<"}
            };
            if (condition is BinaryCondition)
            {
                var binaryCondition = (BinaryCondition) condition;
                return GenerateExpression(binaryCondition.FirstExpression) +
                       " " + conditionDict[binaryCondition.Type] + " " +
                       GenerateExpression(binaryCondition.SecondExpression);
            }
            else  // Odd condition
            {
                var oddCondition = (OddCondition) condition;
                return "(" + GenerateExpression(oddCondition.Expression) + " MOD 2) <> 0";
            }
        }
        
        string GeneratePostCondition(Condition condition)
        {
            Dictionary<ConditionType, string> conditionDict = new()
            {
                {ConditionType.Equal, "="},
                {ConditionType.NotEqual, "<>"},
                {ConditionType.LessThan, "<"},
                {ConditionType.LessThanOrEqual, "<="},
                {ConditionType.GreaterThan, ">"},
                {ConditionType.GreaterThanOrEqual, ">="}
            };
            if (condition is BinaryCondition)
            {
                var binaryCondition = (BinaryCondition) condition;
                return GenerateExpression(binaryCondition.FirstExpression) +
                       " " + conditionDict[binaryCondition.Type] + " " +
                       GenerateExpression(binaryCondition.SecondExpression);
            }
            else  // Odd condition
            {
                var oddCondition = (OddCondition) condition;
                return "(" + GenerateExpression(oddCondition.Expression) + " MOD 2) <> 0";
            }
        }
    }
}