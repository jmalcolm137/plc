#nullable enable

namespace PLC
{
    public partial class Optimizer
    {
        Block OptimizeBlock(Block block)
        {
            if (block.Statement != null)
            {
                block.Statement = OptimizeStatement(block.Statement);
            }
            return block;
        }
    }
}