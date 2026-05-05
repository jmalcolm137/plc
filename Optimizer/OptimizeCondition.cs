#nullable enable

namespace PLC
{
    public partial class Optimizer
    {
        Condition OptimizeCondition(Condition condition, bool countReferences = true)
        {
            if (condition is OddCondition)
            {
                var oc = (OddCondition) condition;
                if (oc.Expression != null)
                {
                    oc.Expression = OptimizeExpression(oc.Expression, countReferences);
                    if (oc.Expression.IsSingleConstantFactor && oc.Expression.ExpressionNodes.Count > 0)
                    {
                        ConstantFactor constant = (ConstantFactor) oc.Expression.ExpressionNodes[0].Term.FirstFactor!;
                        int c = System.Int32.Parse(constant.Value);
                        if ((c & 1) > 0)
                        {
                            return new FalseCondition();
                        }
                        else
                        {
                            return new TrueCondition();
                        }
                    }
                }
                return oc;
            }
            else if (condition is BinaryCondition)
            {
                var bc = (BinaryCondition) condition;
                if (bc.FirstExpression != null)
                {
                    bc.FirstExpression = OptimizeExpression(bc.FirstExpression, countReferences);
                }
                if (bc.SecondExpression != null)
                {
                    bc.SecondExpression = OptimizeExpression(bc.SecondExpression, countReferences);
                }
                if (bc.FirstExpression != null && bc.SecondExpression != null &&
                    bc.FirstExpression.IsSingleConstantFactor && bc.SecondExpression.IsSingleConstantFactor &&
                    bc.FirstExpression.ExpressionNodes.Count > 0 && bc.SecondExpression.ExpressionNodes.Count > 0)
                {
                    ConstantFactor first = (ConstantFactor) bc.FirstExpression.ExpressionNodes[0].Term.FirstFactor!;
                    ConstantFactor second  = (ConstantFactor) bc.SecondExpression.ExpressionNodes[0].Term.FirstFactor!;
                    int c1 = System.Int32.Parse(first.Value);
                    int c2 = System.Int32.Parse(second.Value);
                    switch (bc.Type)
                    {
                        case ConditionType.Equal:
                            return (c1 == c2) ? new TrueCondition() : new FalseCondition();
                        case ConditionType.NotEqual:
                            return (c1 != c2) ? new TrueCondition() : new FalseCondition();
                        case ConditionType.GreaterThan:
                            return (c1 > c2) ? new TrueCondition() : new FalseCondition();
                        case ConditionType.LessThan:
                            return (c1 < c2) ? new TrueCondition() : new FalseCondition();
                        case ConditionType.GreaterThanOrEqual:
                            return (c1 >= c2) ? new TrueCondition() : new FalseCondition();
                        case ConditionType.LessThanOrEqual:
                            return (c1 <= c2) ? new TrueCondition() : new FalseCondition();
                        default:
                            return bc;
                    }
                }
                return bc;
            }
            return condition;
        }
    }
}