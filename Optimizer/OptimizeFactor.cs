#nullable enable
using System.Linq;

namespace PLC
{
    public partial class Optimizer
    {
        Factor OptimizeFactor(Factor factor, bool countReferences = true)
        {
            if (factor is IdentityFactor)
            {
                var iff = (IdentityFactor) factor;
                string name = iff.IdentityName;
                
                // If it is a constant, return ConstantFactor instead
                try
                {
                    var identity = _block.Constants.Single(x => x.Name == name);
                    ConstantFactor f = new() { Value = identity.Value };
                    return f;
                }
                catch
                {
                    ;
                }
                if (countReferences)
                {
                    // If it is a variable, increment the number of times it has been referenced
                    try
                    {
                        var identity = _block.Variables.Single(x => x.Name == name);
                        identity.ReferenceCount++;
                        identity.IdentityFactors.Add(iff);
                    }
                    catch
                    {
                        ;
                    }
                }
            }

            if (factor is ExpressionFactor)
            {
                var ef = (ExpressionFactor) factor;
                if (ef.Expression != null)
                {
                    ef.Expression = OptimizeExpression(ef.Expression, countReferences);
                    // Convert ExpressionFactor to ConstantFactor constant
                    if (ef.Expression.IsSingleConstantFactor && ef.Expression.ExpressionNodes.Count > 0)
                    {
                        ExpressionNode? firstNode = ef.Expression.ExpressionNodes[0];
                        if (firstNode != null && firstNode.IsPositive)
                        {
                            return firstNode.Term?.FirstFactor!;
                        }
                    }
                }
                return ef;
            }
            return factor;
        }
    }
}