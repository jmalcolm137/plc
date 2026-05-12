using System;
using System.Text;
using System.Collections.Generic;

namespace PLC
{
    public class PythonGenerator : IGenerator
    {
        readonly Dictionary<ConditionType, string> _conditionDict = new()
        {
            {ConditionType.Equal, "=="},
            {ConditionType.NotEqual, "!="},
            {ConditionType.LessThan, "<"},
            {ConditionType.LessThanOrEqual, "<="},
            {ConditionType.GreaterThan, ">"},
            {ConditionType.GreaterThanOrEqual, ">="}
        };
        readonly Dictionary<ConditionType, string> _negatedConditionDict = new()
        {
            {ConditionType.Equal, "!="},
            {ConditionType.NotEqual, "=="},
            {ConditionType.LessThan, ">="},
            {ConditionType.LessThanOrEqual, ">"},
            {ConditionType.GreaterThan, "<="},
            {ConditionType.GreaterThanOrEqual, "<"}
        };
        public PythonGenerator(ParsedProgram program)
        {
            Program = program;
        }
        
        public ParsedProgram Program { get; set; }

        public int Compile(string filename)
        {
            return 1;
        }
        public IEnumerable<string> Generate()
        {
            if (Program.UsesRand)
            {
                yield return "import random";
                yield return String.Empty;
            }

            // Generate constant declarations
            var constants = Program.Block.Constants;
            foreach (Identity cc in constants)
            {
                yield return cc.Name + " = " + cc.Value;
            }
            
            foreach (Procedure method in Program.Block.Procedures)
            {
                yield return "def " + method.Name + "():";
                foreach (string s in GenerateBlock(method.Block))
                {
                    yield return s;
                }
                yield return String.Empty;
            }
            
            // Generate the Main body 
            foreach (string s in GenerateStatement(Program.Block.Statement))
            {
                yield return s;
            }
        }
        IEnumerable<string> GenerateBlock(Block block)
        {
            foreach (string s in GenerateConstantDeclarations(block.Constants))
            {
                yield return s;
            }
            
            foreach (string s in GenerateStatement(block.Statement))
            {
                yield return "    " + s;
            }
        }

        IEnumerable<string> GenerateConstantDeclarations(List<Identity> constants)
        {
            foreach (Identity cc in constants)
            {
                yield return "    " + cc.Name + " = " + cc.Value;
            }
        }

        IEnumerable<string> GenerateStatement(Statement statement)
        {
            if (statement is WriteStatement)
            {
                var writeStatement = (WriteStatement) statement;
                if (writeStatement.Message == String.Empty)
                {
                    yield return "print(" + GenerateExpression(writeStatement.Expression) + ")";
                }
                else
                {
                    yield return "print(\"" + writeStatement.Message + "\")";
                }
            }
            else if (statement is ReadStatement)
            {
                var readStatement = (ReadStatement) statement;
                if (readStatement.Message != String.Empty)
                {
                    yield return "print(\"" + readStatement.Message + "\", end=\"\")";
                }
                yield return readStatement.IdentityName + " = int(input())";
            }
            else if (statement is AssignmentStatement)
            {
                var assignmentStatement = (AssignmentStatement) statement;
                yield return assignmentStatement.IdentityName + " = " +
                             GenerateExpression(assignmentStatement.Expression);
            }
            else if (statement is CallStatement)
            {
                var cs = (CallStatement) statement;
                yield return cs.ProcedureName + "()";
            }
            else if (statement is CompoundStatement)
            {
                var bs = (CompoundStatement) statement;
                foreach (Statement st in bs.Statements)
                {
                    if (!st.SkipGeneration)
                    {
                        foreach (string s in GenerateStatement(st))
                        {
                            yield return s;
                        }
                    }
                }
            }
            else if (statement is IfStatement)
            {
                var iff = (IfStatement) statement;
		// For Python, we undo the pattern of if statement followed by DoWhile
		// since Python does not have a real DoWhileStatement
                if (iff.Statement is DoWhileStatement dws && !dws.SkipGeneration &&
                    GenerateCondition(iff.Condition) == GenerateCondition(dws.Condition))
                {
                    yield return "while " + GenerateCondition(iff.Condition) + ":";
                    foreach (string s in GenerateStatement(dws.Statement))
                    {
                        yield return "    " + s;
                    }
                }
                else
                {
                    yield return "if " + GenerateCondition(iff.Condition) + ":";
                    foreach (string s in GenerateStatement(iff.Statement))
                    {
                        yield return "    " + s;
                    }
                }
            }
            else if (statement is DoWhileStatement)
            {
                var dw = (DoWhileStatement) statement;
                if (dw.Condition.Type == ConditionType.True)
                {
                    yield return "while True:";
                    foreach (string s in GenerateStatement(dw.Statement)) yield return "    " + s;
                }
                else
                {
                    yield return "while True:";
                    foreach (string s in GenerateStatement(dw.Statement)) yield return "    " + s;
                    yield return "    if " + NegateCondition(dw.Condition) + ":";
                    yield return "        break";
                }
            }
            else if (statement is WhileStatement)
            {
                var ws = (WhileStatement) statement; 
                if (ws.Condition.Type == ConditionType.True)
                {
                    yield return "while True:";
                }
                else
                {
                    yield return "while " + GenerateCondition(ws.Condition) + ":";
                }
                foreach (string s in GenerateStatement(ws.Statement))
                {
                    yield return "    " + s;
                }
            }
            else   // Must be empty statement
            {
                ;
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
            return "random.randint(" + low + ", " + high + ")";
        }

        string GenerateFirstExpressionNode(ExpressionNode node)
        {
            return (node.IsPositive ? String.Empty : "-") + GenerateTerm(node.Term);
        }
        string GenerateExpressionNode(ExpressionNode node)
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
                sb.Append(te.Current.IsDivision ? "//" : "*");
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

        string NegateCondition(Condition condition)
        {
            switch (condition.Type)
            {
            case ConditionType.True:
                return "False";
            case ConditionType.False:
                return "True";
            case ConditionType.Odd:
                var oddCondition = (OddCondition) condition;
                return "(" + GenerateExpression(oddCondition.Expression) + " & 1) == 0";
            default:
                var binaryCondition = (BinaryCondition) condition;
                StringBuilder sb = new();
                if (binaryCondition.FirstExpression.ExpressionNodes.Count == 1)
                {
                    sb.Append(GenerateExpression(binaryCondition.FirstExpression));
                }
                else
                {
                    sb.Append("(");
                    sb.Append(GenerateExpression(binaryCondition.FirstExpression));
                    sb.Append(")");
                }

                sb.Append(_negatedConditionDict[binaryCondition.Type]);

                if (binaryCondition.SecondExpression.ExpressionNodes.Count == 1)
                {
                    sb.Append(GenerateExpression(binaryCondition.SecondExpression));
                }
                else
                {
                    sb.Append("(");
                    sb.Append(GenerateExpression(binaryCondition.SecondExpression));
                    sb.Append(")");
                }
                return sb.ToString();
            }
        }

        string GenerateCondition(Condition condition)
        {
            switch (condition.Type)
            {
            case ConditionType.True:
                return "True";
            case ConditionType.False:
                return "False";
            case ConditionType.Odd:
                var oddCondition = (OddCondition) condition;
                return GenerateExpression(oddCondition.Expression) + " & 1";
            default:
                var binaryCondition = (BinaryCondition) condition;
                StringBuilder sb = new();
                if (binaryCondition.FirstExpression.ExpressionNodes.Count == 1)
                {
                    sb.Append(GenerateExpression(binaryCondition.FirstExpression));
                }
                else
                {
                    sb.Append("(");
                    sb.Append(GenerateExpression(binaryCondition.FirstExpression));
                    sb.Append(")");
                }

                sb.Append(_conditionDict[binaryCondition.Type]);

                if (binaryCondition.SecondExpression.ExpressionNodes.Count == 1)
                {
                    sb.Append(GenerateExpression(binaryCondition.SecondExpression));
                }
                else
                {
                    sb.Append("(");
                    sb.Append(GenerateExpression(binaryCondition.SecondExpression));
                    sb.Append(")");
                }
                return sb.ToString();
            }
        }
    }
}
