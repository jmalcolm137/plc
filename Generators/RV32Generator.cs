using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;

namespace PLC
{
    public class RV32Generator : IGenerator
    {
        public ParsedProgram Program { get; set; }
        Dictionary<string, int> _varOffsets = new();
        int _totalVars = 0;
        int _labelCounter = 0;
        List<string> _stringLiterals = new();

        public RV32Generator(ParsedProgram program)
        {
            Program = program;
        }

        public int Compile(string filename)
        {
            var lines = GenerateAssembly();
            using (var writer = new StreamWriter(filename))
            {
                foreach (string line in lines)
                    writer.WriteLine(line);
            }
            return 0;
        }

        public IEnumerable<string> Generate()
        {
            return GenerateAssembly();
        }

        List<string> GenerateAssembly()
        {
            _labelCounter = 0;
            _stringLiterals = new List<string>();
            _varOffsets = new Dictionary<string, int>();
            _totalVars = 0;

            AssignOffsets(Program.Block);
            foreach (var proc in Program.Block.Procedures)
                AssignOffsets(proc.Block!);

            var lines = new List<string>();

            lines.Add("\t.option norelax");
            lines.Add("\t.text");
            lines.Add("\t.align 2");
            lines.Add("\t.globl _start");
            lines.Add("");

            lines.Add("_start:");
            lines.Add("\tla s0, vars");
            lines.Add("\tjal ra, rv32_main");
            lines.Add("\tli a7, 93");
            lines.Add("\tli a0, 0");
            lines.Add("\tecall");
            lines.Add("");

            lines.AddRange(GeneratePrintStr());
            lines.AddRange(GeneratePrintInt());
            lines.AddRange(GenerateReadInt());
            if (Program.UsesRand)
                lines.AddRange(GenerateRand());

            foreach (var proc in Program.Block.Procedures)
            {
                lines.Add("");
                lines.Add("proc_" + proc.Name + ":");
                lines.Add("\taddi sp, sp, -4");
                lines.Add("\tsw ra, 0(sp)");
                lines.AddRange(GenerateBlock(proc.Block!));
                lines.Add("\tlw ra, 0(sp)");
                lines.Add("\taddi sp, sp, 4");
                lines.Add("\tret");
            }

            lines.Add("");
            lines.Add("rv32_main:");
            lines.Add("\taddi sp, sp, -4");
            lines.Add("\tsw ra, 0(sp)");
            lines.AddRange(GenerateStatement(Program.Block.Statement!));
            lines.Add("\tlw ra, 0(sp)");
            lines.Add("\taddi sp, sp, 4");
            lines.Add("\tret");

            lines.Add("");
            lines.Add("\t.data");
            lines.Add("\t.align 2");
            for (int i = 0; i < _stringLiterals.Count; i++)
            {
                lines.Add(".Lstr" + i + ":");
                lines.Add("\t.ascii \"" + _stringLiterals[i] + "\"");
            }
            if (_stringLiterals.Count > 0)
                lines.Add("");
            lines.Add("vars:");
            lines.Add("\t.zero " + (_totalVars * 4));

            return lines;
        }

        string NewLabel()
        {
            return ".L" + (_labelCounter++);
        }

        string GetStringLiteral(string message)
        {
            int index = _stringLiterals.Count;
            _stringLiterals.Add(message);
            return ".Lstr" + index;
        }

        void AssignOffsets(Block block)
        {
            foreach (var v in block.Variables)
            {
                if (!_varOffsets.ContainsKey(v.Name))
                {
                    _varOffsets[v.Name] = _totalVars * 4;
                    _totalVars++;
                }
            }
        }

        int GetOffset(string name)
        {
            if (_varOffsets.ContainsKey(name))
                return _varOffsets[name];
            throw new Exception("Unknown variable: " + name);
        }

        IEnumerable<string> GeneratePrintStr()
        {
            yield return "rv32_print_str:";
            yield return "\taddi sp, sp, -4";
            yield return "\tsw ra, 0(sp)";
            yield return "\taddi a2, a1, 0";
            yield return "\taddi a1, a0, 0";
            yield return "\tli a0, 1";
            yield return "\tli a7, 64";
            yield return "\tecall";
            yield return "\tlw ra, 0(sp)";
            yield return "\taddi sp, sp, 4";
            yield return "\tret";
        }

        IEnumerable<string> GeneratePrintInt()
        {
            yield return "rv32_print_int:";
            yield return "\taddi sp, sp, -56";
            yield return "\tsw ra, 52(sp)";

            yield return "\tli t2, 0";
            yield return "\tbne a0, x0, 1f";
            yield return "\tli t0, 48";
            yield return "\tadd t1, sp, t2";
            yield return "\tsw t0, 0(t1)";
            yield return "\taddi t2, t2, 4";
            yield return "\tj 3f";

            yield return "1:";
            yield return "\tbge a0, x0, 2f";
            yield return "\tli t0, 45";
            yield return "\tadd t1, sp, t2";
            yield return "\tsw t0, 0(t1)";
            yield return "\taddi t2, t2, 4";
            yield return "\tsub a0, x0, a0";

            yield return "2:";
            yield return "\tli t3, 10";
            yield return "21:";
            yield return "\tdiv t4, a0, t3";
            yield return "\tmul t5, t4, t3";
            yield return "\tsub t5, a0, t5";
            yield return "\taddi t5, t5, 48";
            yield return "\tadd t1, sp, t2";
            yield return "\tsw t5, 0(t1)";
            yield return "\taddi t2, t2, 4";
            yield return "\taddi a0, t4, 0";
            yield return "\tbne a0, x0, 21b";

            yield return "3:";
            yield return "\taddi t1, sp, 0";
            yield return "\tadd t3, sp, t2";

            yield return "\tlw t0, 0(t1)";
            yield return "\tli t4, 45";
            yield return "\tbne t0, t4, 4f";
            yield return "\tsw t0, 48(sp)";
            yield return "\tli a7, 64";
            yield return "\tli a0, 1";
            yield return "\taddi a1, sp, 48";
            yield return "\tli a2, 1";
            yield return "\tecall";
            yield return "\taddi t1, t1, 4";

            yield return "4:";
            yield return "\taddi t3, t3, -4";
            yield return "41:";
            yield return "\tblt t3, t1, 5f";
            yield return "\tlw t0, 0(t3)";
            yield return "\tsw t0, 48(sp)";
            yield return "\tli a7, 64";
            yield return "\tli a0, 1";
            yield return "\taddi a1, sp, 48";
            yield return "\tli a2, 1";
            yield return "\tecall";
            yield return "\taddi t3, t3, -4";
            yield return "\tj 41b";

            yield return "5:";
            yield return "\tli t0, 10";
            yield return "\tsw t0, 48(sp)";
            yield return "\tli a7, 64";
            yield return "\tli a0, 1";
            yield return "\taddi a1, sp, 48";
            yield return "\tli a2, 1";
            yield return "\tecall";

            yield return "\tlw ra, 52(sp)";
            yield return "\taddi sp, sp, 56";
            yield return "\tret";
        }

        IEnumerable<string> GenerateReadInt()
        {
            yield return "rv32_read_int:";
            yield return "\taddi sp, sp, -8";
            yield return "\tsw ra, 4(sp)";
            yield return "\tli t0, 0";
            yield return "\tsw t0, 0(sp)";

            yield return "\tli a7, 63";
            yield return "\tli a0, 0";
            yield return "\taddi a1, sp, 0";
            yield return "\tli a2, 1";
            yield return "\tecall";
            yield return "\tbeq a0, x0, 3f";

            yield return "\tlw t2, 0(sp)";
            yield return "\tandi t2, t2, 0xFF";

            yield return "\tli t1, 1";
            yield return "\tli t3, 45";
            yield return "\tbne t2, t3, 1f";
            yield return "\tli t1, -1";
            yield return "\tli a7, 63";
            yield return "\tli a0, 0";
            yield return "\taddi a1, sp, 0";
            yield return "\tli a2, 1";
            yield return "\tecall";
            yield return "\tbeq a0, x0, 3f";
            yield return "\tlw t2, 0(sp)";
            yield return "\tandi t2, t2, 0xFF";

            yield return "1:";
            yield return "\tli t0, 0";
            yield return "11:";
            yield return "\taddi t2, t2, -48";
            yield return "\tblt t2, x0, 2f";
            yield return "\tli t4, 9";
            yield return "\tblt t4, t2, 2f";
            yield return "\tli t4, 10";
            yield return "\tmul t0, t0, t4";
            yield return "\tadd t0, t0, t2";

            yield return "\tli a7, 63";
            yield return "\tli a0, 0";
            yield return "\taddi a1, sp, 0";
            yield return "\tli a2, 1";
            yield return "\tecall";
            yield return "\tbeq a0, x0, 2f";
            yield return "\tlw t2, 0(sp)";
            yield return "\tandi t2, t2, 0xFF";
            yield return "\tj 11b";

            yield return "2:";
            yield return "\tmul a0, t0, t1";

            yield return "3:";
            yield return "\tlw ra, 4(sp)";
            yield return "\taddi sp, sp, 8";
            yield return "\tret";
        }

        IEnumerable<string> GenerateRand()
        {
            yield return "rv32_rand:";
            yield return "\taddi sp, sp, -16";
            yield return "\tsw ra, 12(sp)";
            yield return "\tsw a1, 8(sp)";
            yield return "\tsw a0, 4(sp)";

            yield return "\tli a7, 278";
            yield return "\taddi a0, sp, 0";
            yield return "\tli a1, 4";
            yield return "\tli a2, 0";
            yield return "\tecall";

            yield return "\tlw t0, 0(sp)";
            yield return "\tblt t0, x0, 1f";
            yield return "\tj 2f";
            yield return "1:";
            yield return "\tsub t0, x0, t0";
            yield return "2:";
            yield return "\tlw a1, 8(sp)";
            yield return "\tlw a0, 4(sp)";
            yield return "\tsub a1, a1, a0";
            yield return "\taddi a1, a1, 1";
            yield return "\tbge x0, a1, 3f";
            yield return "\tdiv t2, t0, a1";
            yield return "\tmul t3, t2, a1";
            yield return "\tsub t2, t0, t3";
            yield return "\tadd a0, t2, a0";
            yield return "\tj 4f";
            yield return "3:";
            yield return "\taddi a0, a0, 0";
            yield return "4:";
            yield return "\tlw ra, 12(sp)";
            yield return "\taddi sp, sp, 16";
            yield return "\tret";
        }

        IEnumerable<string> GenerateBlock(Block block)
        {
            foreach (string s in GenerateStatement(block.Statement!))
                yield return s;
        }

        IEnumerable<string> GenerateStatement(Statement statement)
        {
            if (statement is WriteStatement writeStmt)
            {
                if (writeStmt.Message == String.Empty && writeStmt.Expression != null)
                {
                    foreach (string s in GenerateExpression(writeStmt.Expression))
                        yield return s;
                    yield return "\tjal ra, rv32_print_int";
                }
                else if (writeStmt.Message != String.Empty)
                {
                    string label = GetStringLiteral(writeStmt.Message);
                    yield return "\tla a0, " + label;
                    yield return "\tli a1, " + writeStmt.Message.Length;
                    yield return "\tjal ra, rv32_print_str";
                    yield return "\taddi sp, sp, -4";
                    yield return "\tli t0, 10";
                    yield return "\tsw t0, 0(sp)";
                    yield return "\tli a7, 64";
                    yield return "\tli a0, 1";
                    yield return "\taddi a1, sp, 0";
                    yield return "\tli a2, 1";
                    yield return "\tecall";
                    yield return "\taddi sp, sp, 4";
                }
            }
            else if (statement is ReadStatement readStmt)
            {
                if (readStmt.Message != String.Empty)
                {
                    string label = GetStringLiteral(readStmt.Message);
                    yield return "\tla a0, " + label;
                    yield return "\tli a1, " + readStmt.Message.Length;
                    yield return "\tjal ra, rv32_print_str";
                }
                yield return "\tjal ra, rv32_read_int";
                yield return "\tsw a0, " + GetOffset(readStmt.IdentityName) + "(s0)";
            }
            else if (statement is AssignmentStatement assignStmt)
            {
                foreach (string s in GenerateExpression(assignStmt.Expression!))
                    yield return s;
                yield return "\tsw a0, " + GetOffset(assignStmt.IdentityName) + "(s0)";
            }
            else if (statement is CallStatement callStmt)
            {
                yield return "\tjal ra, proc_" + callStmt.ProcedureName;
            }
            else if (statement is CompoundStatement compoundStmt)
            {
                foreach (var stmt in compoundStmt.Statements)
                {
                    if (!stmt.SkipGeneration)
                    {
                        foreach (string s in GenerateStatement(stmt))
                            yield return s;
                    }
                }
            }
            else if (statement is IfStatement ifStmt)
            {
                string endLabel = NewLabel();
                foreach (string s in BranchWhenFalse(ifStmt.Condition!, endLabel))
                    yield return s;
                foreach (string s in GenerateStatement(ifStmt.Statement!))
                    yield return s;
                yield return endLabel + ":";
            }
            else if (statement is WhileStatement whileStmt)
            {
                string startLabel = NewLabel();
                string endLabel = NewLabel();
                yield return startLabel + ":";
                foreach (string s in BranchWhenFalse(whileStmt.Condition!, endLabel))
                    yield return s;
                foreach (string s in GenerateStatement(whileStmt.Statement!))
                    yield return s;
                yield return "\tbeq x0, x0, " + startLabel;
                yield return endLabel + ":";
            }
            else if (statement is DoWhileStatement doWhileStmt)
            {
                string startLabel = NewLabel();
                yield return startLabel + ":";
                foreach (string s in GenerateStatement(doWhileStmt.Statement!))
                    yield return s;
                foreach (string s in BranchWhenTrue(doWhileStmt.Condition!, startLabel))
                    yield return s;
            }
        }

        IEnumerable<string> BranchWhenFalse(Condition condition, string label)
        {
            if (condition is TrueCondition)
                yield break;
            if (condition is FalseCondition)
            {
                yield return "\tbeq x0, x0, " + label;
                yield break;
            }
            if (condition is OddCondition odd)
            {
                foreach (string s in GenerateExpression(odd.Expression!))
                    yield return s;
                yield return "\tandi a0, a0, 1";
                yield return "\tbeq a0, x0, " + label;
                yield break;
            }
            var bin = (BinaryCondition)condition;
            foreach (string s in GenerateExpression(bin.FirstExpression!))
                yield return s;
            yield return "\taddi t0, a0, 0";
            foreach (string s in GenerateExpression(bin.SecondExpression!))
                yield return s;
            switch (bin.Type)
            {
                case ConditionType.Equal:
                    yield return "\tbne t0, a0, " + label; break;
                case ConditionType.NotEqual:
                    yield return "\tbeq t0, a0, " + label; break;
                case ConditionType.LessThan:
                    yield return "\tbge t0, a0, " + label; break;
                case ConditionType.GreaterThan:
                    yield return "\tbge a0, t0, " + label; break;
                case ConditionType.LessThanOrEqual:
                    yield return "\tblt a0, t0, " + label; break;
                case ConditionType.GreaterThanOrEqual:
                    yield return "\tblt t0, a0, " + label; break;
            }
        }

        IEnumerable<string> BranchWhenTrue(Condition condition, string label)
        {
            if (condition is TrueCondition)
            {
                yield return "\tbeq x0, x0, " + label;
                yield break;
            }
            if (condition is FalseCondition)
                yield break;
            if (condition is OddCondition odd)
            {
                foreach (string s in GenerateExpression(odd.Expression!))
                    yield return s;
                yield return "\tandi a0, a0, 1";
                yield return "\tbne a0, x0, " + label;
                yield break;
            }
            var bin = (BinaryCondition)condition;
            foreach (string s in GenerateExpression(bin.FirstExpression!))
                yield return s;
            yield return "\taddi t0, a0, 0";
            foreach (string s in GenerateExpression(bin.SecondExpression!))
                yield return s;
            switch (bin.Type)
            {
                case ConditionType.Equal:
                    yield return "\tbeq t0, a0, " + label; break;
                case ConditionType.NotEqual:
                    yield return "\tbne t0, a0, " + label; break;
                case ConditionType.LessThan:
                    yield return "\tblt t0, a0, " + label; break;
                case ConditionType.GreaterThan:
                    yield return "\tblt a0, t0, " + label; break;
                case ConditionType.LessThanOrEqual:
                    yield return "\tbge a0, t0, " + label; break;
                case ConditionType.GreaterThanOrEqual:
                    yield return "\tbge t0, a0, " + label; break;
            }
        }

        IEnumerable<string> GenerateExpression(Expression expression)
        {
            if (expression is RandExpression rand)
            {
                foreach (string s in GenerateExpression(rand.LowExpression!))
                    yield return s;
                yield return "\taddi t0, a0, 0";
                foreach (string s in GenerateExpression(rand.HighExpression!))
                    yield return s;
                yield return "\taddi a1, a0, 0";
                yield return "\taddi a0, t0, 0";
                yield return "\tjal ra, rv32_rand";
                yield break;
            }

            var nodes = expression.ExpressionNodes;
            if (nodes.Count == 0)
                yield break;

            foreach (string s in GenerateTerm(nodes[0].Term!))
                yield return s;
            if (!nodes[0].IsPositive)
                yield return "\tsub a0, x0, a0";
            if (nodes.Count == 1)
                yield break;

            yield return "\taddi t0, a0, 0";
            for (int i = 1; i < nodes.Count; i++)
            {
                foreach (string s in GenerateTerm(nodes[i].Term!))
                    yield return s;
                if (nodes[i].IsPositive)
                    yield return "\tadd t0, t0, a0";
                else
                    yield return "\tsub t0, t0, a0";
            }
            yield return "\taddi a0, t0, 0";
        }

        IEnumerable<string> GenerateTerm(Term term)
        {
            var nodes = term.TermNodes;
            if (nodes.Count == 0)
                yield break;

            foreach (string s in GenerateFactor(nodes[0].Factor!))
                yield return s;
            if (nodes.Count == 1)
                yield break;

            yield return "\taddi t0, a0, 0";
            for (int i = 1; i < nodes.Count; i++)
            {
                foreach (string s in GenerateFactor(nodes[i].Factor!))
                    yield return s;
                if (nodes[i].IsDivision)
                    yield return "\tdiv t0, t0, a0";
                else
                    yield return "\tmul t0, t0, a0";
            }
            yield return "\taddi a0, t0, 0";
        }

        IEnumerable<string> GenerateFactor(Factor factor)
        {
            if (factor is ConstantFactor cf)
            {
                yield return "\tli a0, " + cf.Value;
            }
            else if (factor is IdentityFactor idf)
            {
                int offset = GetOffset(idf.IdentityName);
                yield return "\tlw a0, " + offset + "(s0)";
            }
            else if (factor is ExpressionFactor ef)
            {
                foreach (string s in GenerateExpression(ef.Expression!))
                    yield return s;
            }
        }
    }
}
