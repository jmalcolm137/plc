using System;
using System.Collections.Generic;

namespace PLC
{
    public enum ConditionType
    {
        Odd,
        True,
        False,
        Equal,
        NotEqual,
        LessThan,
        LessThanOrEqual,
        GreaterThan,
        GreaterThanOrEqual
    };

    public class ParsedProgram
    {
        public ParsedProgram()
        {
            Block = new Block();
            Globals = new List<Identity>();
        }

        public Block Block { get; set; } = new Block();
        public List<Identity> Globals { get; set; } = new List<Identity>();
        public bool UsesRand = false;
    }

    public class Identity
    {
        public string Name = String.Empty;
        public string Value = String.Empty;
        public int ReferenceCount = 0;
        public List<IdentityFactor> IdentityFactors;
        public List<AssignmentStatement> AssignmentStatements;

        public Identity()
        {
            IdentityFactors = new List<IdentityFactor>();
            AssignmentStatements = new List<AssignmentStatement>();
        }
    }

    public class Block
    {
        public Block()
        {
            Constants = new List<Identity>();
            Variables = new List<Identity>();
            Procedures = new List<Procedure>();
        }

        public List<Identity> Constants { get; set; } = new List<Identity>();
        public List<Identity> Variables { get; set; } = new List<Identity>();
        public List<Procedure> Procedures { get; set; } = new List<Procedure>();
        public Statement? Statement { get; set; }
    }

    public class Procedure
    {
        public string Name { get; set; } = String.Empty;
        public Block? Block { get; set; }
        public int CallCount = 0;
        public List<Identity> Locals { get; set; } = new List<Identity>();

        public Procedure()
        {
            Locals = new List<Identity>();
        }
    }

    public interface IStatement
    {
        public bool CallsProcedure { get; }
    }

    public class Statement : IStatement
    {
        public virtual bool CallsProcedure
        {
            get { return false; }
        }

        public bool SkipGeneration = false;
    }

    public class EmptyStatement : Statement
    {
        public EmptyStatement()
        {
            SkipGeneration = true;
        }
    }

    public class AssignmentStatement : Statement
    {
        public string IdentityName { get; set; } = String.Empty;
        public Expression? Expression { get; set; }
    }

    public class CallStatement : Statement
    {
        public string ProcedureName = String.Empty;

        public override bool CallsProcedure
        {
            get { return true; }
        }
    }

    public class ReadStatement : Statement
    {
        public string IdentityName { get; set; } = String.Empty;
        public string Message { get; set; } = String.Empty;
    }

    public class WriteStatement : Statement
    {
        public Expression? Expression { get; set; }
        public string Message { get; set; } = String.Empty;
    }

    public class CompoundStatement : Statement
    {
        public CompoundStatement()
        {
            Statements = new List<Statement>();
        }

        public List<Statement> Statements { get; set; } = new List<Statement>();

        public override bool CallsProcedure
        {
            get
            {
                foreach (Statement s in Statements)
                {
                    if (s.CallsProcedure == true) return true;
                }
                return false;
            }
        }
    }

    public class IfStatement : Statement
    {
        public Condition? Condition { get; set; }
        public Statement? Statement { get; set; }

        public override bool CallsProcedure
        {
            get { return Statement?.CallsProcedure ?? false; }
        }
    }

    public abstract class LoopStatement : Statement
    {
        public Statement? Statement { get; set; }
    }

    public class WhileStatement : LoopStatement
    {
        public Condition? Condition { get; set; }

        public override bool CallsProcedure
        {
            get { return Statement?.CallsProcedure ?? false; }
        }
    }

    public class DoWhileStatement : LoopStatement
    {
        public Condition? Condition { get; set; }

        public override bool CallsProcedure
        {
            get { return Statement?.CallsProcedure ?? false; }
        }
    }

    public class Condition
    {
        public ConditionType Type { get; set; }
    }

    public class OddCondition : Condition
    {
        public Expression? Expression { get; set; }

        public OddCondition()
        {
            Type = ConditionType.Odd;
        }
    }

    public class BinaryCondition : Condition
    {
        public Expression? FirstExpression { get; set; }
        public Expression? SecondExpression { get; set; }
    }

    public class TrueCondition : Condition
    {
        public TrueCondition()
        {
            Type = ConditionType.True;
        }
    }

    public class FalseCondition : Condition
    {
        public FalseCondition()
        {
            Type = ConditionType.False;
        }
    }

    public class Term
    {
        public Term()
        {
            TermNodes = new List<TermNode>();
        }

        public List<TermNode> TermNodes { get; set; } = new List<TermNode>();

        public Factor? FirstFactor
        {
            get { return TermNodes.Count > 0 ? TermNodes[0].Factor : null; }
        }

        public bool IsSingleFactor
        {
            get { return TermNodes.Count == 1; }
        }

        public bool IsSingleConstantFactor
        {
            get
            {
                if (IsSingleFactor)
                {
                    if (FirstFactor is ConstantFactor) return true;
                }
                return false;
            }
        }
    }

    public class TermNode
    {
        public bool IsDivision;
        public Factor? Factor { get; set; }

        public TermNode()
        {
            IsDivision = false;
        }
    }

    public class ExpressionNode
    {
        public ExpressionNode()
        {
            Term = new Term();
            IsPositive = true;
        }

        public bool IsPositive;
        public Term? Term { get; set; }

        public bool IsSingleConstantFactor
        {
            get
            {
                if (Term == null)
                {
                    return false;
                }
                return Term.IsSingleConstantFactor;
            }
        }

        public bool RepresentsBinaryExpression
        {
            get { return Term?.TermNodes.Count == 2; }
        }
    }

    public class Expression
    {
        public Expression()
        {
            ExpressionNodes = new List<ExpressionNode>();
        }

        public List<ExpressionNode> ExpressionNodes { get; set; } = new List<ExpressionNode>();

        public virtual bool IsSingleTerm
        {
            get
            {
                if (ExpressionNodes.Count == 1)
                {
                    if (ExpressionNodes[0].Term != null)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public virtual bool IsSingleConstantFactor
        {
            get
            {
                if (this.IsSingleTerm)
                {
                    if (ExpressionNodes[0].Term?.IsSingleConstantFactor ?? false) return true;
                }
                return false;
            }
        }

        public virtual bool IsSingleIdentity
        {
            get
            {
                if (this.IsSingleTerm)
                {
                    if (ExpressionNodes[0].Term?.IsSingleFactor ?? false)
                    {
                        if (ExpressionNodes[0].Term.FirstFactor is IdentityFactor)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
        }
    }

    public class RandExpression : Expression
    {
        public Expression? LowExpression { get; set; }
        public Expression? HighExpression { get; set; }
    }

    public abstract class Factor
    {
    }

    public class IdentityFactor : Factor
    {
        public string IdentityName { get; set; } = String.Empty;
    }

    public class ConstantFactor : Factor
    {
        public string Value { get; set; } = String.Empty;
    }

    public class ExpressionFactor : Factor
    {
        public ExpressionFactor()
        {
            Expression = new Expression();
        }

        public Expression? Expression { get; set; }
    }
}
