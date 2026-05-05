#nullable enable
using System;
using System.Collections.Generic;

namespace PLC
{
    public partial class Optimizer
    {
        ExpressionNode OptimizeExpressionNode(ExpressionNode node, bool countReferences = true)
        {
            if (node.Term != null)
            {
                node.Term = OptimizeTerm(node.Term);
            }

            // Try to get rid of multiplication by negative numbers
            if (node.RepresentsBinaryExpression)
            {
                Factor? secondFactor = node.Term?.TermNodes[0].Factor;
                if (secondFactor is ExpressionFactor)
                {
                    ExpressionFactor ef = (ExpressionFactor) secondFactor;
                    if (ef.Expression != null && ef.Expression.ExpressionNodes.Count > 0)
                    {
                        var firstNode = ef.Expression.ExpressionNodes[0];
                        if (!firstNode.IsPositive) // Is negative
                        {
                            firstNode.IsPositive = true;
                            node.IsPositive = !node.IsPositive;
                            if (ef.Expression != null)
                            {
                                ef.Expression = OptimizeExpression(ef.Expression);
                            }
                            if (node.Term != null)
                            {
                                node.Term = OptimizeTerm(node.Term);
                            }
                        }
                    }
                }
            }
            //Console.WriteLine("OptimizeExpressionNode: First factor of term is " + ((ConstantFactor) node.Term.FirstFactor).Value);
            return node;
        }

        Expression OptimizeExpression(Expression expression, bool countReferences = true)
        {
            // Do not try to optimize if already a simple constant
            if (expression.IsSingleConstantFactor)
            {
                return expression;
            }

            List<ExpressionNode> optimizedNodes = new();
            int constantResult = 0;
            foreach (var n in expression.ExpressionNodes)
            {
                var node = OptimizeExpressionNode(n);
                if (node.Term?.IsSingleConstantFactor ?? false)
                {
                    ConstantFactor? firstFactor = (node.Term?.TermNodes[0].Factor as ConstantFactor);
                    if (firstFactor != null)
                    {
                        int termValue = Int32.Parse(firstFactor.Value);
                        if (node.IsPositive)
                        {
                            constantResult += termValue;
                        }
                        else
                        {
                            constantResult -= termValue;
                        }
                    }
                }
                else
                {
                    optimizedNodes.Add(node);
                }
            }

            // If you remove the condition above to avoid optimizing single term constants
            // The following condition will cause single term constants of value zero to not be added
            if (constantResult != 0)
            {
                ExpressionNode en = new();
                TermNode tn = new();
                en.Term = new Term();
                en.Term.TermNodes.Add(tn);
                tn.Factor = new ConstantFactor() {Value = Math.Abs(constantResult).ToString()};
                tn.IsDivision = false;

                en.IsPositive = constantResult >= 0;
                optimizedNodes.Add(en);
            }

            expression.ExpressionNodes = optimizedNodes;
            return expression;
        }
    }
}
