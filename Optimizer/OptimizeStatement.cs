#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;

namespace PLC
{
    public partial class Optimizer
    {
        Statement OptimizeCompoundStatement(CompoundStatement cs)
        {
            List<Statement> optimizedStatements = new();

            foreach (Statement s in cs.Statements.Where(x => x.SkipGeneration == false))
            {
                if (s is CompoundStatement)
                {
                    CompoundStatement inner = (CompoundStatement) s;
                    foreach (Statement ss in inner.Statements.Where(x => x.SkipGeneration == false))
                    {
                        optimizedStatements.Add(OptimizeStatement(ss));
                    }
                }
                else
                {
                    optimizedStatements.Add(OptimizeStatement(s));
                }
            }
            PropagateAssignments(optimizedStatements);
            cs.Statements = optimizedStatements;

            // Replace single statement CompoundStatement with that statement
            if (cs.Statements.Count == 1)
            {
                return cs.Statements[0];
            }

            return cs;
        }

        Statement OptimizeAssignmentStatement(AssignmentStatement s)
        {
            s.Expression = OptimizeExpression(s.Expression);
            try
            {
                Identity identity = _block.Variables.Single(x => x.Name == s.IdentityName);
                identity.AssignmentStatements.Add(s);
            }
            catch
            {
            }

            return s;
        }

        Statement OptimizeIfStatement(IfStatement iff)
        {
            iff.Statement = OptimizeStatement(iff.Statement);
            iff.Condition = OptimizeCondition(iff.Condition);
            if (iff.Condition.Type == ConditionType.True)
            {
                return iff.Statement;
            }

            if (iff.Condition.Type == ConditionType.False)
            {
                return new EmptyStatement();
            }

            return iff;
        }

        Statement OptimizeWhileStatement(WhileStatement ws)
        {
            ws.Statement = OptimizeStatement(ws.Statement);
            ws.Condition = OptimizeCondition(ws.Condition);
            if (ws.Condition.Type == ConditionType.False)
            {
                return new EmptyStatement();
            }

            // Perform Loop Inversion
            // Saves two jumps when exiting the loop
            DoWhileStatement dw = new();
            dw.Condition = ws.Condition;
            dw.Statement = ws.Statement;
            // If condition is true, no need even for the IF
            if (dw.Condition.Type == ConditionType.True)
            {
                return dw;
            }

            IfStatement iff = new();
            iff.Condition = ws.Condition;
            iff.Statement = dw;
            return iff;
        }

        Statement OptimizeDoWhileStatement(DoWhileStatement dw)
        {
            dw.Statement = OptimizeStatement(dw.Statement);
            dw.Condition = OptimizeCondition(dw.Condition);
            if (dw.Condition.Type == ConditionType.False)
            {
                return dw.Statement;
            }

            return dw;
        }

        Statement OptimizeCallStatement(CallStatement cs)
        {
            string name = cs.ProcedureName;
            var procs = _block.Procedures;
            try
            {
                Procedure result = procs.Single(x => x.Name == name);
                if (result.Block.Constants.Count == 0 && result.Block.Variables.Count == 0 &&
                    !result.Block.Statement.CallsProcedure)
                {
                    return result.Block.Statement;
                }

                result.CallCount++;
            }
            catch
            {
                ;
            }

            return cs;
        }

        Statement OptimizeWriteStatement(WriteStatement ws)
        {
            if (ws.Message == String.Empty)
            {
                ws.Expression = OptimizeExpression(ws.Expression);
            }

            return ws;
        }

        Statement OptimizeReadStatement(ReadStatement rs)
        {
            try
            {
                Identity identity = _block.Variables.Single(x => x.Name == rs.IdentityName);
                identity.ReferenceCount++;
            }
            catch
            {
                Console.Error.WriteLine("Could not locate variable " + rs.IdentityName);
            }
            return rs;
        }

        Statement OptimizeStatement(Statement statement)
        {
            if (statement is CompoundStatement)
                return OptimizeCompoundStatement((CompoundStatement) statement);
            if (statement is AssignmentStatement)
                return OptimizeAssignmentStatement((AssignmentStatement) statement);
            if (statement is IfStatement)
                return OptimizeIfStatement((IfStatement) statement);
            if (statement is WhileStatement)
                return OptimizeWhileStatement((WhileStatement) statement);
            if (statement is DoWhileStatement)
                return OptimizeDoWhileStatement((DoWhileStatement) statement);
            if (statement is CallStatement)
                return OptimizeCallStatement((CallStatement) statement);
            if (statement is WriteStatement)
                return OptimizeWriteStatement((WriteStatement) statement);
            if (statement is ReadStatement)
                return OptimizeReadStatement((ReadStatement) statement);
            return statement;
        }

        Expression DuplicateExpression(Expression original)
        {
            Expression ne = new();
            foreach (ExpressionNode enode in original.ExpressionNodes)
            {
                ExpressionNode n = new() {IsPositive = enode.IsPositive, Term = new Term()};
                foreach (TermNode tnode in enode.Term.TermNodes)
                {
                    n.Term.TermNodes.Add(new TermNode() { IsDivision = tnode.IsDivision, Factor = tnode.Factor });
                }
                ne.ExpressionNodes.Add(n);
            }
            return ne;
        }
        void PropagateAssignmentsIntoExpression(Expression original, List<AssignmentStatement> assignments)
        {
            if (original == null)
            {
                return;
            }
            foreach (ExpressionNode enode in original.ExpressionNodes)
            {
                foreach (TermNode tnode in enode.Term.TermNodes)
                {
                    Factor factor = tnode.Factor;
                    if (tnode.Factor is IdentityFactor)
                    {
                        IdentityFactor identityFactor = (IdentityFactor) factor;
                        foreach (AssignmentStatement a in assignments)
                        {
                            if (a.IdentityName == identityFactor.IdentityName && a.Expression.IsSingleConstantFactor)
                            {
                                tnode.Factor = a.Expression.ExpressionNodes[0].Term.FirstFactor;
                                try
                                {
                                    Identity identity =
                                        _block.Variables.Single(x => x.Name == identityFactor.IdentityName);
                                    identity.ReferenceCount--;
                                }
                                catch
                                {
                                    Console.Error.WriteLine("Could not locate variable " + identityFactor.IdentityName);
                                }
                            }
                        }
                    }
                }
            }
        }
        HashSet<string> IdentifiersCalled(Expression expresssion)
        {
            HashSet<string> hashset = new();
            foreach (ExpressionNode enode in expresssion.ExpressionNodes)
            {
                foreach (TermNode tnode in enode.Term.TermNodes)
                {
                    if (tnode.Factor is IdentityFactor)
                    {
                        IdentityFactor factor = (IdentityFactor) tnode.Factor;
                        hashset.Add(factor.IdentityName);
                    }
                }
            }
            return hashset;
        }
        void PropagateAssignments(List<Statement> statements, List<AssignmentStatement>? existingAssignments = null)
        {
            List<AssignmentStatement>? constantAssignments;
            if (existingAssignments == null)
            {
                constantAssignments = new List<AssignmentStatement>();
            }
            else
            {
                constantAssignments = existingAssignments;
            }
            /* Create phantom assignments for all variables setting them to zero
             * These will get overridden by any actual assignment statements later
             *
             * Cannot do this inside PROCEDURES - only inside Main
             */
            if (_inMain)
            {
                foreach (Identity variable in _block.Variables)
                {
                    constantAssignments.Add(new AssignmentStatement() { IdentityName = variable.Name, Expression = new ConstantExpression("0")});
                } 
            }
            for (int i = 0; i < statements.Count; i++)
            {
                Statement currentStatement = statements[i];
                if (currentStatement is AssignmentStatement)
                {
                    AssignmentStatement a = (AssignmentStatement) currentStatement;
                    constantAssignments.RemoveAll(x => x.IdentityName == a.IdentityName);
                    PropagateAssignmentsIntoExpression(a.Expression, constantAssignments);
                    a.Expression = OptimizeExpression(a.Expression, false);
                    if (a.Expression.IsSingleConstantFactor)
                    {
                        constantAssignments.Add(a);
                    }
                }
                else if (currentStatement is ReadStatement)
                {
                    ReadStatement r = (ReadStatement) currentStatement;
                    constantAssignments.RemoveAll(x => x.IdentityName == r.IdentityName);
                }
                else if (currentStatement is CompoundStatement)
                {
                    CompoundStatement s = (CompoundStatement) currentStatement;
                    PropagateAssignments(s.Statements, constantAssignments);
                }
                else if (currentStatement is WriteStatement)
                {
                    WriteStatement w = (WriteStatement) currentStatement;
                    PropagateAssignmentsIntoExpression(w.Expression, constantAssignments);
                }
                else if (currentStatement is IfStatement)
                {
                    IfStatement ifStatement = (IfStatement) currentStatement;
                    if (ifStatement.Statement is WriteStatement)
                    {
                        WriteStatement w = (WriteStatement) ifStatement.Statement;
                        if (!String.IsNullOrEmpty(w.Message))
                        {
                            PropagateAssignmentsIntoExpression(w.Expression, constantAssignments);  
                        }
                    }
                    bool keepAssignments = false;
                    if (ifStatement.Condition is BinaryCondition)
                    {
                        BinaryCondition bc1 = (BinaryCondition) ifStatement.Condition;
                        BinaryCondition bc2 = new()
                        {
                            FirstExpression = bc1.FirstExpression,
                            SecondExpression = bc1.SecondExpression,
                            Type = bc1.Type
                        };
                        for (int c = constantAssignments.Count - 1; c >= 0; c--)
                        {
                            AssignmentStatement currentAssignment = constantAssignments[c];
                            if (bc2.FirstExpression.IsSingleIdentity)
                            {
                                IdentityFactor identityFactor =
                                    (IdentityFactor) bc2.FirstExpression.ExpressionNodes[0].Term.FirstFactor;
                                if (currentAssignment.IdentityName == identityFactor.IdentityName)
                                {
                                    bc2.FirstExpression = currentAssignment.Expression;
                                }
                            }

                            if (bc2.SecondExpression.IsSingleIdentity)
                            {
                                IdentityFactor identityFactor =
                                    (IdentityFactor) bc2.SecondExpression.ExpressionNodes[0].Term.FirstFactor;
                                if (currentAssignment.IdentityName == identityFactor.IdentityName)
                                {
                                    bc2.SecondExpression = currentAssignment.Expression;
                                }
                            }

                            Condition cond = OptimizeCondition(bc2);
                            if (cond.Type == ConditionType.True || cond.Type == ConditionType.False)
                            {
                                try
                                {
                                    Identity identity = _block.Variables.Single(x => x.Name == currentAssignment.IdentityName);
                                    identity.ReferenceCount--;
                                }
                                catch
                                {
                                    Console.Error.WriteLine("Could not locate variable " + currentAssignment.IdentityName);
                                }
                                if (cond.Type == ConditionType.True)
                                {
                                    statements[i] = ifStatement.Statement;
                                    // Variables may have been changed after assignment
                                    // Unless there are only WRITE statements or empty statements in between
                                    keepAssignments = (ifStatement.Statement is EmptyStatement) || (ifStatement.Statement is WriteStatement);
                                    break;
                                }
                                if (cond.Type == ConditionType.False)
                                {
                                    statements[i] = new EmptyStatement();
                                    keepAssignments = true;
                                    break;
                                }
                            }
                        }
                    }
                    else if (ifStatement.Condition.Type == ConditionType.Odd)
                    {
                        var oc1 = (OddCondition) ifStatement.Condition;
                        OddCondition oc2 = new() {Expression = oc1.Expression};
                        if (oc2.Expression.IsSingleIdentity)
                        {
                            IdentityFactor identityFactor =
                                (IdentityFactor) oc2.Expression.ExpressionNodes[0].Term.FirstFactor;

                            for (int c = constantAssignments.Count - 1; c >= 0; c--)
                            {
                                AssignmentStatement currentAssignment = constantAssignments[c];
                                if (currentAssignment.IdentityName == identityFactor.IdentityName)
                                {
                                    oc2.Expression = currentAssignment.Expression;
                                    Condition cond = OptimizeCondition(oc2, false);
                                    if (cond.Type == ConditionType.True || cond.Type == ConditionType.False)
                                    {
                                        try
                                        {
                                            Identity identity = _block.Variables.Single(x =>
                                                x.Name == currentAssignment.IdentityName);
                                            identity.ReferenceCount--;
                                        }
                                        catch
                                        {
                                            Console.Error.WriteLine("Could not locate variable " +
                                                                    currentAssignment.IdentityName);
                                        }
                                        if (cond.Type == ConditionType.True)
                                        {
                                            statements[i] = ifStatement.Statement;
                                            // Variables may have been changed after assignment
                                            // Unless there are only WRITE statements or empty statements in between
                                            keepAssignments = (statements[i] is EmptyStatement) ||
                                                              (statements[i] is WriteStatement);
                                            break;
                                        }

                                        if (cond.Type == ConditionType.False)
                                        {
                                            statements[i] = new EmptyStatement();
                                            keepAssignments = true;
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    if (!keepAssignments)
                    {
                        constantAssignments.Clear();
                        // TODO: Instead of clearing them all, identify which variables may have changed
                    }
                }
                else
                {
                    // Clear all the previously registered assignments as they may no longer be valid
                    if (!(currentStatement is EmptyStatement))
                    {
                        constantAssignments.Clear();
                    }
                }
            }
        }

        void EliminateSingleAssignment(List<Statement> statements, string targetIdentity = "", List<AssignmentStatement>? existingAssignments = null)
        {
            //Console.WriteLine("Eliminating " + targetIdentity);
            List<AssignmentStatement>? assignments;
            if (existingAssignments == null)
            {
                assignments = new List<AssignmentStatement>();
            }
            else
            {
                assignments = existingAssignments;
            }
            /* Create phantom assignments for all variables setting them to zero
             * These will get overridden by any actual assignment statements later
             *
             * Cannot do this inside PROCEDURES - only inside Main
             */
            if (_inMain)
            {
                foreach (Identity variable in _block.Variables)
                {
                    assignments.Add(new AssignmentStatement() { IdentityName = variable.Name, Expression = new ConstantExpression("0")});
                } 
            }
            for (int i = 0; i < statements.Count; i++)
            {
                Statement currentStatement = statements[i];
                if (currentStatement is AssignmentStatement)
                {
                    AssignmentStatement a = (AssignmentStatement) currentStatement;
                    assignments.RemoveAll(x => x.IdentityName == a.IdentityName);
                    assignments.Add(a);
                    //Console.WriteLine("Added {0} =", a.IdentityName);
                }
                else if (currentStatement is ReadStatement)
                {
                    ReadStatement r = (ReadStatement) currentStatement;
                    assignments.RemoveAll(x => x.IdentityName == r.IdentityName);
                }
                else if (currentStatement is CompoundStatement)
                {
                    CompoundStatement s = (CompoundStatement) currentStatement;
                    EliminateSingleAssignment(s.Statements, targetIdentity, assignments);
                }
                else if (currentStatement is WriteStatement)
                {
                    WriteStatement w = (WriteStatement) currentStatement;
                    //Console.WriteLine("Found WRITE statement");
                    if (String.IsNullOrEmpty(w.Message) && w.Expression.IsSingleIdentity)
                    {
                        IdentityFactor identityFactor =
                            (IdentityFactor) w.Expression.ExpressionNodes[0].Term.FirstFactor;
                        if (identityFactor.IdentityName == targetIdentity)
                        {
                            AssignmentStatement a = assignments.First(x => x.IdentityName == targetIdentity);
                            w.Expression = a.Expression;
                            a.SkipGeneration = true;
                        }
                    }
                }
                else if (currentStatement is DoWhileStatement)
                {
                    DoWhileStatement dw = (DoWhileStatement) currentStatement;
                    if (dw.Statement is CompoundStatement)
                    {
                        EliminateSingleAssignment(((CompoundStatement) dw.Statement).Statements, targetIdentity);
                    }
                }
                else if (currentStatement is IfStatement)
                {
                    IfStatement ifStatement = (IfStatement) currentStatement;
                    if (ifStatement.Statement is WriteStatement)
                    {
                        WriteStatement w = (WriteStatement) currentStatement;
                        if (String.IsNullOrEmpty(w.Message) && w.Expression.IsSingleIdentity)
                        {
                            IdentityFactor identityFactor =
                                (IdentityFactor) w.Expression.ExpressionNodes[0].Term.FirstFactor;
                            if (identityFactor.IdentityName == targetIdentity)
                            {
                                AssignmentStatement a = assignments.First(x => x.IdentityName == targetIdentity);
                                w.Expression = a.Expression;
                                a.SkipGeneration = true;
                            }
                        }
                    } else if (ifStatement.Statement is DoWhileStatement)
                    {
                        DoWhileStatement dw = (DoWhileStatement) ifStatement.Statement;
                        if (dw.Statement is CompoundStatement)
                        {
                            EliminateSingleAssignment(((CompoundStatement) dw.Statement).Statements, targetIdentity);
                        }
                    }
                    assignments.Clear();
                }
                else
                {
                    // Clear all the previously registered assignments as they may no longer be valid
                    if (!(currentStatement is EmptyStatement))
                    {
                        assignments.Clear();
                    }
                }
            }
        }
    }
}