#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;

namespace PLC
{
    public partial class Optimizer
    {
        Block _block;
        bool _inMain;

        public Optimizer()
        {
            _block = new Block();
            _inMain = false;
        }

        public ParsedProgram Optimize(ParsedProgram program)
        {
            _block = program.Block;
            
            List<Procedure> procsToRemove = new();
            foreach (var proc in _block.Procedures)
            {
                _inMain = false;
                proc.Block = OptimizeBlock(proc.Block);
                OptimizeProcedureTailCall(proc);
            }
            
            _block = OptimizeBlock(_block);

            foreach (var proc in _block.Procedures)
            {
                if (proc.CallCount == 0)
                {
                    procsToRemove.Add(proc);
                }
            }
            foreach (var procToRemove in procsToRemove)
            {
                _block.Procedures.Remove(procToRemove);
            }
            //PrintReferences();
            ClearReferences();
            foreach (var proc in _block.Procedures)
            {
                _inMain = false;
                proc.Block = OptimizeBlock(proc.Block);
                OptimizeProcedureTailCall(proc);
            }
            _inMain = false;
            _block = OptimizeBlock(_block);
            //PrintReferences();
            foreach (var variable in _block.Variables)
            {
                if (variable.ReferenceCount == 1 && variable.AssignmentStatements.Count == 1)
                {
                    EliminateSingleAssignment(((CompoundStatement) _block.Statement).Statements, variable.Name);
                    variable.ReferenceCount--;
                }
            }
            //PrintReferences();
            List<Identity> variablesToRemove = new();
            foreach (var variable in _block.Variables)
            {
                if (variable.ReferenceCount == 0)
                {
                    variablesToRemove.Add(variable);
                }
            }
            foreach (var variable in variablesToRemove)
            {
                foreach (AssignmentStatement s in variable.AssignmentStatements)
                {
                    s.SkipGeneration = true;
                }
                _block.Variables.Remove(variable);
            }
            //PrintReferences();
            _block.Constants.Clear();
            program.Block = _block;
            return program;
        }

        void PrintReferences()
        {
            foreach (var variable in _block.Variables)
            {
                System.Console.WriteLine("{0} is assigned {1} times and called {2} times", variable.Name,
                    variable.AssignmentStatements.Count, variable.ReferenceCount);
            }
        }

        void ClearReferences()
        {
            foreach (var variable in _block.Variables)
            {
                variable.AssignmentStatements.Clear();
                variable.ReferenceCount = 0;
            }
        }
        
        // Convert procedure call into a loop for a very specific case
        void OptimizeProcedureTailCall(Procedure proc)
        {
            if (proc.Block.Statement is IfStatement)
            {
                var iff = (IfStatement) proc.Block.Statement;
                if (iff.Statement is CompoundStatement)
                {
                    var bs = (CompoundStatement) iff.Statement;
                    if (bs.Statements.Last() is CallStatement)
                    {
                        CallStatement ls = (CallStatement) bs.Statements.Last();
                        if (ls.ProcedureName == proc.Name)
                        {
                            // Procedure calls itself
                            bs.Statements.Remove(ls);
                            DoWhileStatement dw = new();
                            dw.Condition = iff.Condition;
                            dw.Statement = iff.Statement;
                            iff.Statement = dw;
                            proc.CallCount--;
                        }
                    }
                }
            }
        }
    }
}