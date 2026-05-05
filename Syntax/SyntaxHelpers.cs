namespace PLC
{
    public class ConstantExpression : Expression
    {
        public ConstantExpression(string constant, bool positive = true)
        {
            ConstantFactor factor = new() {Value = constant};
            TermNode tn = new() {Factor = factor, IsDivision = false};
            Term term = new();
            term.TermNodes.Add(tn);
            ExpressionNode en = new() {Term = term, IsPositive = positive};
            ExpressionNodes.Add(en);
        }
        public override bool IsSingleTerm
        {
            get
            {
                return true;
            }
        }

        public override bool IsSingleConstantFactor
        {
            get
            {
                return true;
            }
        }
    }
    public class SingleIdentityExpression : Expression
    {
        public SingleIdentityExpression(string identityName)
        {
            IdentityFactor factor = new() {IdentityName = identityName};
            TermNode tn = new() {Factor = factor, IsDivision = false};
            Term term = new();
            term.TermNodes.Add(tn);
            ExpressionNode en = new() {Term = term, IsPositive = true};
            ExpressionNodes.Add(en);
        }

        public override bool IsSingleTerm
        {
            get { return true; }
        }

        public override bool IsSingleConstantFactor
        {
            get { return false; }
        }
    }
}