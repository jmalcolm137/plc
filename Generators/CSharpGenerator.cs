using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;

namespace PLC
{
    public class CSharpGenerator : IGenerator
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
        public CSharpGenerator(ParsedProgram program)
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
            yield return "using System;";
            yield return String.Empty; // Just makes it prettier
            yield return "public class Program {";
            
            // Generate constant declarations
            var constants = Program.Block.Constants;
            int c = constants.Count;
            if (c != 0)
            {
                StringBuilder sb = new();
                sb.Append("    const int ");
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
                yield return sb.ToString();
            }
            
            // Variable declarations
            var variables = Program.Block.Variables;
            c = variables.Count;
            if (c != 0)
            {
                StringBuilder sb = new();
                sb.Append("    static int ");
                for (int i = 0; i < c; i++)
                {
                    sb.Append(variables.ElementAt(i).Name);
                    if (i < (c - 1))
                    {
                        sb.Append(", ");
                    }
                }

                sb.Append(";");
                yield return sb.ToString();
                yield return String.Empty; // Just makes it look prettier
            }
            
            foreach (Procedure method in Program.Block.Procedures)
            {
                yield return "    static void " + method.Name + "() {";
                foreach (string s in GenerateBlock(method.Block))
                {
                    yield return "    " + s;
                }

                yield return "    }";
                yield return String.Empty;    // Just makes it prettier
            }
            
            // Generate the Main body of the program
            yield return "    public static void Main() {";

            foreach (string s in GenerateStatement(Program.Block.Statement))
            {
                yield return "    " + s;
            }
            yield return "    }";
            yield return "}";
        }
        IEnumerable<string> GenerateBlock(Block block)
        {
            string constants = GenerateConstantDeclarations(block.Constants);
            if (constants != String.Empty)
            {
                yield return constants;
            }

            string variables = GenerateVariableDeclarations(block.Variables);
            if (variables != String.Empty)
            {
                yield return variables;
            }
            
            foreach (string s in GenerateStatement(block.Statement))
            {
                yield return "    " + s;
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
            sb.Append("    const int ");
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

        string GenerateVariableDeclarations(List<Identity> variables)
        {
            int c = variables.Count;
            if (c == 0)
            {
                return String.Empty;
            }

            StringBuilder sb = new();
            sb.Append("    int ");
            var enumerator = variables.GetEnumerator();
            bool keepGoing = enumerator.MoveNext();
            while (keepGoing)
            {
                sb.Append(enumerator.Current.Name);
                if (enumerator.MoveNext())
                {
                    sb.Append(", ");
                }
                else
                {
                    keepGoing = false;
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
                    yield return "Console.WriteLine(" + GenerateExpression(writeStatement.Expression) + ");";
                }
                else
                {
                    yield return "Console.WriteLine(\"" + writeStatement.Message + "\");";
                }
            }
            else if (statement is ReadStatement)
            {
                var readStatement = (ReadStatement) statement;
                if (readStatement.Message != String.Empty)
                {
                    yield return "Console.Write(\"" + readStatement.Message + "\");";
                }
                yield return "Int32.TryParse(Console.ReadLine(), out " + readStatement.IdentityName + ");";
            }
            else if (statement is AssignmentStatement)
            {
                var assignmentStatement = (AssignmentStatement) statement;
                yield return assignmentStatement.IdentityName + " = " +
                             GenerateExpression(assignmentStatement.Expression) + ";";
            }
            else if (statement is CallStatement)
            {
                var cs = (CallStatement) statement;
                yield return cs.ProcedureName + "();";
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
                            yield return "    " + s;
                        }
                    }
                }
            }
            else if (statement is IfStatement)
            {
                var iff = (IfStatement) statement;
                yield return "if (" + GenerateCondition(iff.Condition) + ") {";
                foreach (string s in GenerateStatement(iff.Statement))
                {
                    yield return "    " + s;
                }

                yield return "}";
            }
            else if (statement is DoWhileStatement)
            {
                var dw = (DoWhileStatement) statement;
                if (dw.Condition.Type == ConditionType.True)
                {
                    yield return "for (;;) {";
                    foreach (string s in GenerateStatement(dw.Statement)) yield return s;
                    yield return "}";
                }
                else
                {
                    yield return "do {";
                    foreach (string s in GenerateStatement(dw.Statement)) yield return s;
                    yield return "} while (" + GenerateCondition(dw.Condition) + ");";
                }
            }
            else if (statement is WhileStatement)
            {
                var ws = (WhileStatement) statement; 
                yield return (ws.Condition.Type == ConditionType.True) ?
                    "for (;;) {" : 
                    "while (" + GenerateCondition(ws.Condition) + ") {";
                foreach (string s in GenerateStatement(ws.Statement))
                {
                    yield return s;
                }
                yield return "}";
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
            return "new Random().Next(" + low + "," + high + ")";
        }

        string GenerateFirstExpressionNode(ExpressionNode node)
        {
            /*
            if (node == null)
            {
                Console.WriteLine("Trying to generate null expression node ( first node )");
            }
            */
            return (node.IsPositive ? String.Empty : "-") + GenerateTerm(node.Term);
        }
        string GenerateExpressionNode(ExpressionNode node)
        {
            /*
            if (node == null)
            {
                Console.WriteLine("Trying to generate null expression node");
            }
            */
            return  (node.IsPositive ? "+" : "-") + GenerateTerm(node.Term);
        }
        
        string GenerateTerm(Term term)
        {
            /*
            if (term == null)
            {
                Console.WriteLine("Trying to generate null term");
            }
            */
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
            //Console.WriteLine("Trying to generate factor of type " + factor.GetType());
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

        string GenerateCondition(Condition condition)
        {
            switch (condition.Type)
            {
            case ConditionType.True:
                return "true";
            case ConditionType.False:
                return "false";
            case ConditionType.Odd:
                var oddCondition = (OddCondition) condition;
                // Switched from == 1 to > 0 ( used by Convert.ToBoolean as well )
                return "(" + GenerateExpression(oddCondition.Expression) + " & 1) > 0";
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