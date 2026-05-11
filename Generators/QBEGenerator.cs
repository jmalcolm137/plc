using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace PLC
{
    public class QBEGenerator : IGenerator
    {
        public ParsedProgram Program { get; set; }
        int _labelCounter = 0;
        int _tempCounter = 0;
        readonly List<string> _stringLiterals = new();

        public QBEGenerator(ParsedProgram program)
        {
            Program = program;
        }

        public int Compile(string filename)
        {
            var lines = Generate();
            using (var writer = new StreamWriter(filename))
            {
                foreach (string line in lines)
                    writer.WriteLine(line);
            }
            return 0;
        }

        public IEnumerable<string> Generate()
        {
            _labelCounter = 0;
            _tempCounter = 0;
            _stringLiterals.Clear();

            CollectStringLiterals(Program.Block.Statement);

            yield return "data $fmt_d = { b \"%d\\n\", b 0 }";
            yield return "data $fmt_scan = { b \"%d\", b 0 }";
            yield return "";

            for (int i = 0; i < _stringLiterals.Count; i++)
            {
                yield return "data $str" + i + " = { b \"" + EscapeString(_stringLiterals[i]) + "\", b 0 }";
            }
            if (_stringLiterals.Count > 0)
                yield return "";

            foreach (var v in Program.Block.Variables)
                yield return "data $" + v.Name + " = { w 0 }";

            foreach (var proc in Program.Block.Procedures)
            {
                foreach (var v in proc.Block.Variables)
                    yield return "data $" + v.Name + " = { w 0 }";
            }

            if (Program.Block.Variables.Count > 0 ||
                Program.Block.Procedures.Any(p => p.Block.Variables.Count > 0))
                yield return "";

            foreach (var proc in Program.Block.Procedures)
            {
                yield return "function w $proc_" + proc.Name + "() {";
                yield return "@start";
                var procLines = new List<string>();
                GenerateStatement(proc.Block.Statement, procLines);
                foreach (string s in procLines)
                    yield return s;
                yield return "\tret 0";
                yield return "}";
                yield return "";
            }

            yield return "function w $main() {";
            yield return "@start";
            if (Program.UsesRand)
            {
                string t = NewTemp();
                yield return "\t" + t + " =l call $time(l 0)";
                yield return "\tcall $srand(w " + t + ")";
            }
            var mainLines = new List<string>();
            GenerateStatement(Program.Block.Statement, mainLines);
            foreach (string s in mainLines)
                yield return s;
            yield return "\tret 0";
            yield return "}";
        }

        string NewLabel()
        {
            return "@L" + (_labelCounter++);
        }

        string NewTemp()
        {
            return "%t" + (_tempCounter++);
        }

        void CollectStringLiterals(Statement stmt)
        {
            if (stmt is WriteStatement ws)
            {
                if (ws.Message != String.Empty && !_stringLiterals.Contains(ws.Message))
                    _stringLiterals.Add(ws.Message);
            }
            else if (stmt is ReadStatement rs)
            {
                if (rs.Message != String.Empty && !_stringLiterals.Contains(rs.Message))
                    _stringLiterals.Add(rs.Message);
            }
            else if (stmt is CompoundStatement cs)
            {
                foreach (var s in cs.Statements)
                    CollectStringLiterals(s);
            }
            else if (stmt is IfStatement ifs)
            {
                CollectStringLiterals(ifs.Statement);
            }
            else if (stmt is WhileStatement ws2)
            {
                CollectStringLiterals(ws2.Statement);
            }
            else if (stmt is DoWhileStatement dws)
            {
                CollectStringLiterals(dws.Statement);
            }
        }

        static string EscapeString(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        string GetStringLiteral(string message)
        {
            int index = _stringLiterals.IndexOf(message);
            if (index == -1)
            {
                index = _stringLiterals.Count;
                _stringLiterals.Add(message);
            }
            return "str" + index;
        }

        void GenerateStatement(Statement stmt, List<string> lines)
        {
            if (stmt is WriteStatement ws)
            {
                if (ws.Message == String.Empty && ws.Expression != null)
                {
                    string valReg = GenerateExpression(ws.Expression, lines);
                    string fmtPtr = NewTemp();
                    lines.Add("\t" + fmtPtr + " =l copy $fmt_d");
                    lines.Add("\tcall $printf(l " + fmtPtr + ", w " + valReg + ")");
                }
                else if (ws.Message != String.Empty)
                {
                    string strAddr = NewTemp();
                    lines.Add("\t" + strAddr + " =l copy $" + GetStringLiteral(ws.Message));
                    lines.Add("\tcall $puts(l " + strAddr + ")");
                }
            }
            else if (stmt is ReadStatement rs)
            {
                if (rs.Message != String.Empty)
                {
                    string msgAddr = NewTemp();
                    lines.Add("\t" + msgAddr + " =l copy $" + GetStringLiteral(rs.Message));
                    lines.Add("\tcall $puts(l " + msgAddr + ")");
                }
                string fmtPtr = NewTemp();
                string varAddr = NewTemp();
                lines.Add("\t" + fmtPtr + " =l copy $fmt_scan");
                lines.Add("\t" + varAddr + " =l copy $" + rs.IdentityName);
                lines.Add("\tcall $scanf(l " + fmtPtr + ", l " + varAddr + ")");
            }
            else if (stmt is AssignmentStatement ass)
            {
                string valReg = GenerateExpression(ass.Expression, lines);
                string varAddr = NewTemp();
                lines.Add("\t" + varAddr + " =l copy $" + ass.IdentityName);
                lines.Add("\tstorew " + valReg + ", " + varAddr);
            }
            else if (stmt is CallStatement cs)
            {
                lines.Add("\tcall $proc_" + cs.ProcedureName + "()");
            }
            else if (stmt is CompoundStatement comp)
            {
                foreach (var s in comp.Statements)
                {
                    if (!s.SkipGeneration)
                        GenerateStatement(s, lines);
                }
            }
            else if (stmt is IfStatement ifs)
            {
                string condReg = GenerateCondition(ifs.Condition, lines);
                string bodyLabel = NewLabel();
                string endLabel = NewLabel();
                lines.Add("\tjnz " + condReg + ", " + bodyLabel + ", " + endLabel);
                lines.Add(bodyLabel);
                GenerateStatement(ifs.Statement, lines);
                lines.Add("\tjmp " + endLabel);
                lines.Add(endLabel);
            }
            else if (stmt is WhileStatement wls)
            {
                string startLabel = NewLabel();
                string endLabel = NewLabel();
                lines.Add(startLabel);
                string condReg = GenerateCondition(wls.Condition, lines);
                string bodyLabel = NewLabel();
                lines.Add("\tjnz " + condReg + ", " + bodyLabel + ", " + endLabel);
                lines.Add(bodyLabel);
                GenerateStatement(wls.Statement, lines);
                lines.Add("\tjmp " + startLabel);
                lines.Add(endLabel);
            }
            else if (stmt is DoWhileStatement dws)
            {
                string startLabel = NewLabel();
                string endLabel = NewLabel();
                lines.Add(startLabel);
                GenerateStatement(dws.Statement, lines);
                string condReg = GenerateCondition(dws.Condition, lines);
                lines.Add("\tjnz " + condReg + ", " + startLabel + ", " + endLabel);
                lines.Add(endLabel);
            }
        }

        string GenerateExpression(Expression expr, List<string> lines)
        {
            if (expr is RandExpression rand)
            {
                string lowReg = GenerateExpression(rand.LowExpression, lines);
                string highReg = GenerateExpression(rand.HighExpression, lines);
                string result = NewTemp();
                string randVal = NewTemp();
                string diff = NewTemp();
                string diff1 = NewTemp();
                string divResult = NewTemp();
                string mulResult = NewTemp();
                string modVal = NewTemp();
                lines.Add("\t" + randVal + " =w call $rand()");
                lines.Add("\t" + diff + " =w sub " + highReg + ", " + lowReg);
                lines.Add("\t" + diff1 + " =w add " + diff + ", 1");
                lines.Add("\t" + divResult + " =w div " + randVal + ", " + diff1);
                lines.Add("\t" + mulResult + " =w mul " + divResult + ", " + diff1);
                lines.Add("\t" + modVal + " =w sub " + randVal + ", " + mulResult);
                lines.Add("\t" + result + " =w add " + modVal + ", " + lowReg);
                return result;
            }

            var nodes = expr.ExpressionNodes;
            if (nodes.Count == 0)
            {
                string z = NewTemp();
                lines.Add("\t" + z + " =w copy 0");
                return z;
            }

            string firstReg = GenerateTerm(nodes[0].Term, lines);
            if (!nodes[0].IsPositive)
            {
                string neg = NewTemp();
                lines.Add("\t" + neg + " =w sub 0, " + firstReg);
                firstReg = neg;
            }

            if (nodes.Count == 1)
                return firstReg;

            string accum = NewTemp();
            lines.Add("\t" + accum + " =w copy " + firstReg);

            for (int i = 1; i < nodes.Count; i++)
            {
                string termReg = GenerateTerm(nodes[i].Term, lines);
                string newAccum = NewTemp();
                if (nodes[i].IsPositive)
                    lines.Add("\t" + newAccum + " =w add " + accum + ", " + termReg);
                else
                    lines.Add("\t" + newAccum + " =w sub " + accum + ", " + termReg);
                accum = newAccum;
            }

            return accum;
        }

        string GenerateTerm(Term term, List<string> lines)
        {
            var nodes = term.TermNodes;
            if (nodes.Count == 0)
            {
                string z = NewTemp();
                lines.Add("\t" + z + " =w copy 0");
                return z;
            }

            string firstReg = GenerateFactor(nodes[0].Factor, lines);

            if (nodes.Count == 1)
                return firstReg;

            string accum = NewTemp();
            lines.Add("\t" + accum + " =w copy " + firstReg);

            for (int i = 1; i < nodes.Count; i++)
            {
                string factorReg = GenerateFactor(nodes[i].Factor, lines);
                string newAccum = NewTemp();
                if (nodes[i].IsDivision)
                    lines.Add("\t" + newAccum + " =w div " + accum + ", " + factorReg);
                else
                    lines.Add("\t" + newAccum + " =w mul " + accum + ", " + factorReg);
                accum = newAccum;
            }

            return accum;
        }

        string GenerateFactor(Factor factor, List<string> lines)
        {
            if (factor is ConstantFactor cf)
            {
                string r = NewTemp();
                lines.Add("\t" + r + " =w copy " + cf.Value);
                return r;
            }
            else if (factor is IdentityFactor idf)
            {
                string ptr = NewTemp();
                string val = NewTemp();
                lines.Add("\t" + ptr + " =l copy $" + idf.IdentityName);
                lines.Add("\t" + val + " =w loadw " + ptr);
                return val;
            }
            else if (factor is ExpressionFactor ef)
            {
                return GenerateExpression(ef.Expression, lines);
            }
            throw new Exception("Unknown factor type");
        }

        string GenerateCondition(Condition cond, List<string> lines)
        {
            if (cond is TrueCondition)
            {
                string r = NewTemp();
                lines.Add("\t" + r + " =w copy 1");
                return r;
            }
            if (cond is FalseCondition)
            {
                string r = NewTemp();
                lines.Add("\t" + r + " =w copy 0");
                return r;
            }
            if (cond is OddCondition odd)
            {
                string exprReg = GenerateExpression(odd.Expression, lines);
                string result = NewTemp();
                lines.Add("\t" + result + " =w and " + exprReg + ", 1");
                return result;
            }
            var bin = (BinaryCondition)cond;
            string leftReg = GenerateExpression(bin.FirstExpression, lines);
            string rightReg = GenerateExpression(bin.SecondExpression, lines);
            string res = NewTemp();
            string cmpOp = bin.Type switch
            {
                ConditionType.Equal => "ceqw",
                ConditionType.NotEqual => "cnew",
                ConditionType.LessThan => "csltw",
                ConditionType.LessThanOrEqual => "cslew",
                ConditionType.GreaterThan => "csgtw",
                ConditionType.GreaterThanOrEqual => "csgew",
                _ => throw new Exception("Unknown condition type")
            };
            lines.Add("\t" + res + " =w " + cmpOp + " " + leftReg + ", " + rightReg);
            return res;
        }
    }
}
